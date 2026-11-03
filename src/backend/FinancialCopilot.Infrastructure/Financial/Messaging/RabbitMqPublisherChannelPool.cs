using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace FinancialCopilot.Infrastructure.Financial.Messaging;

/// <summary>
/// Bounded set of publisher channels on one shared connection. Each operation leases a channel
/// exclusively, because the client does not guarantee safe concurrent publishing on one channel.
/// A channel that failed or closed is discarded, never returned to the pool.
/// </summary>
internal sealed class RabbitMqPublisherChannelPool(
    RabbitMqSharedConnection connection,
    CreateChannelOptions? channelOptions,
    RabbitMqConnectionPolicy policy,
    ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _leases = new(policy.PublisherChannelLimit, policy.PublisherChannelLimit);
    private readonly ConcurrentBag<IChannel> _idle = [];
    private volatile bool _disposed;

    /// <summary>
    /// Runs <paramref name="operation"/> with exclusive use of a channel. The whole lease, including
    /// waiting for a free channel and publisher confirms, is bounded by the publish timeout per message.
    /// </summary>
    public async Task ExecuteAsync(
        int messageCount,
        Func<IChannel, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var budget = policy.PublishTimeout * Math.Max(1, messageCount);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        var token = timeout.Token;
        var leased = false;
        IChannel? channel = null;
        try
        {
            await _leases.WaitAsync(token);
            leased = true;
            channel = await TakeIdleAsync() ??
                await (await connection.GetAsync(token)).CreateChannelAsync(channelOptions, token);
            await operation(channel, token);
            if (!_disposed && channel.IsOpen)
            {
                _idle.Add(channel);
                channel = null;
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Surface as a failure, not as cancellation, so callers record the publish as failed.
            throw new TimeoutException(
                $"RabbitMQ publish on '{connection.Name}' did not complete within {budget}.");
        }
        finally
        {
            if (channel is not null)
            {
                await DiscardAsync(channel);
            }

            if (leased)
            {
                _leases.Release();
            }
        }
    }

    private async Task<IChannel?> TakeIdleAsync()
    {
        while (_idle.TryTake(out var channel))
        {
            if (channel.IsOpen)
            {
                return channel;
            }

            await DiscardAsync(channel);
        }

        return null;
    }

    private async Task DiscardAsync(IChannel channel)
    {
        try
        {
            await channel.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Disposing a RabbitMQ publisher channel on {ConnectionName} failed.", connection.Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        while (_idle.TryTake(out var channel))
        {
            await DiscardAsync(channel);
        }
    }
}
