using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinancialCopilot.Infrastructure.Financial.Messaging;

internal static class RabbitMqResilientConsumer
{
    /// <summary>
    /// Keeps one consumer registered on its own channel of the shared consumer connection until
    /// cancellation. A channel closed by the broker (for example a delivery-acknowledgement timeout)
    /// is disposed and replaced after a jittered backoff; permanent failures stop the consumer.
    /// </summary>
    public static async Task RunAsync(
        RabbitMqSharedConnection connection,
        Func<IChannel, CancellationToken, Task> startConsumingAsync,
        string operationName,
        RabbitMqConnectionPolicy policy,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IChannel? channel = null;
            try
            {
                var open = await connection.GetAsync(cancellationToken);
                channel = await open.CreateChannelAsync(cancellationToken: cancellationToken);
                var closed = new TaskCompletionSource<ShutdownEventArgs>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                channel.ChannelShutdownAsync += (_, args) =>
                {
                    closed.TrySetResult(args);
                    return Task.CompletedTask;
                };

                await startConsumingAsync(channel, cancellationToken);
                if (channel.IsClosed)
                {
                    closed.TrySetResult(channel.CloseReason ??
                        new ShutdownEventArgs(ShutdownInitiator.Library, 0, "Channel closed before the consumer started."));
                }

                var started = Stopwatch.GetTimestamp();
                logger.LogInformation(
                    "RabbitMQ {OperationName} consumer registered on {ConnectionName}.",
                    operationName,
                    connection.Name);
                var reason = await closed.Task.WaitAsync(cancellationToken);
                if (Stopwatch.GetElapsedTime(started) >= policy.MaxRetryDelay)
                {
                    consecutiveFailures = 0;
                }

                logger.LogWarning(
                    "RabbitMQ {OperationName} consumer channel was closed by {Initiator} ({ReplyCode} {ReplyText}); replacing it.",
                    operationName,
                    reason.Initiator,
                    reason.ReplyCode,
                    reason.ReplyText);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (RabbitMqFailure.IsPermanent(exception))
            {
                logger.LogCritical(
                    exception,
                    "RabbitMQ {OperationName} consumer stopped because the broker rejected its credentials, permissions or queue declaration.",
                    operationName);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ {OperationName} consumer could not be started.", operationName);
            }
            finally
            {
                if (channel is not null)
                {
                    await DisposeQuietlyAsync(channel, operationName, logger);
                }
            }

            consecutiveFailures++;
            await Task.Delay(policy.RetryDelay(consecutiveFailures), cancellationToken);
        }
    }

    private static async Task DisposeQuietlyAsync(IChannel channel, string operationName, ILogger logger)
    {
        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Disposing the RabbitMQ {OperationName} consumer channel failed.", operationName);
        }
    }
}
