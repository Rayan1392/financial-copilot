using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.Notifications;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Notifications;

public sealed class TelegramNotificationTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramNotificationOptions> options) : ITelegramNotificationTransport
{
    public async Task<NotificationTransportResult> SendAsync(
        long chatId,
        string text,
        string deliveryPartIdempotencyKey,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BotToken))
            return Permanent("NotConfigured", "Telegram notification transport is not configured.");
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri))
            return Permanent("InvalidConfiguration", "Telegram notification transport base URL is invalid.");

        var client = httpClientFactory.CreateClient(nameof(TelegramNotificationTransport));
        client.BaseAddress = new Uri(baseUri, $"/bot{settings.BotToken.Trim()}/");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 1, 120));
        try
        {
            using var response = await client.PostAsJsonAsync("sendMessage", new
            {
                chat_id = chatId,
                text,
                disable_web_page_preview = true
            }, cancellationToken);
            var body = await response.Content.ReadFromJsonAsync<TelegramResponse>(cancellationToken: cancellationToken);
            if (response.IsSuccessStatusCode && body?.Ok == true && body.Result?.MessageId is not null)
                return new NotificationTransportResult(NotificationTransportOutcome.Delivered,
                    body.Result.MessageId.Value.ToString(CultureInfo.InvariantCulture), null, null);

            var retryAfter = body?.Parameters?.RetryAfter is > 0
                ? TimeSpan.FromSeconds(body.Parameters.RetryAfter.Value)
                : response.Headers.RetryAfter?.Delta;
            var code = $"Telegram{(int)response.StatusCode}";
            var redacted = Bounded(body?.Description ?? response.ReasonPhrase ?? "Telegram delivery failed.");
            return response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
                ? new NotificationTransportResult(NotificationTransportOutcome.RetryableFailure, null, code, redacted, retryAfter)
                : Permanent(code, redacted);
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

    private static NotificationTransportResult Permanent(string code, string message) =>
        new(NotificationTransportOutcome.PermanentFailure, null, code, Bounded(message));

    private static string Bounded(string value) => value.Length <= 512 ? value : value[..512];

    private sealed record TelegramResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] TelegramMessage? Result,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("parameters")] TelegramResponseParameters? Parameters);

    private sealed record TelegramMessage(
        [property: JsonPropertyName("message_id")] long? MessageId);

    private sealed record TelegramResponseParameters(
        [property: JsonPropertyName("retry_after")] int? RetryAfter);
}
