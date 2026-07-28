using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace FinancialCopilot.Infrastructure.Financial.Messaging;

internal static class RabbitMqConsumerAcknowledgement
{
    public static async Task<bool> TryAckAsync(
        IChannel channel,
        ulong deliveryTag,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsAcknowledgementFailure(exception, cancellationToken))
        {
            logger.LogWarning(
                exception,
                "Could not acknowledge RabbitMQ {OperationName} message because the channel is closed.",
                operationName);
            return false;
        }
    }

    public static async Task<bool> TryNackAsync(
        IChannel channel,
        ulong deliveryTag,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken,
        bool requeue = false)
    {
        try
        {
            await channel.BasicNackAsync(
                deliveryTag,
                multiple: false,
                requeue,
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsAcknowledgementFailure(exception, cancellationToken))
        {
            logger.LogWarning(
                exception,
                "Could not reject RabbitMQ {OperationName} message because the channel is closed.",
                operationName);
            return false;
        }
    }

    private static bool IsAcknowledgementFailure(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is AlreadyClosedException ||
            exception is OperationInterruptedException ||
            exception is IOException);
}
