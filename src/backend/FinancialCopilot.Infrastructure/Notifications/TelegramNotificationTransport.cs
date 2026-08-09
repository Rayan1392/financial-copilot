using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class TelegramNotificationTransport(
    ITelegramGatewayClient gatewayClient) : ITelegramNotificationTransport
{
    public async Task<NotificationTransportResult> SendAsync(
        long chatId,
        string text,
        string deliveryPartIdempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await gatewayClient.SendMessageAsync(chatId, text, null, deliveryPartIdempotencyKey, null, cancellationToken);
            return result.Succeeded
                ? new NotificationTransportResult(NotificationTransportOutcome.Delivered, result.ProviderMessageId, null, null)
                : new NotificationTransportResult(result.ErrorCode is "RateLimited" or "Timeout" or "GatewayUnavailable" ? NotificationTransportOutcome.RetryableFailure : NotificationTransportOutcome.PermanentFailure,
                    null, result.ErrorCode, result.RedactedError, result.RetryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NotificationTransportResult(NotificationTransportOutcome.RetryableFailure,
                null, "Timeout", "Telegram delivery timed out.");
        }
        catch (HttpRequestException)
        {
            return new NotificationTransportResult(NotificationTransportOutcome.RetryableFailure,
                null, "Network", "Telegram delivery failed because of a transient network error.");
        }
    }

}
