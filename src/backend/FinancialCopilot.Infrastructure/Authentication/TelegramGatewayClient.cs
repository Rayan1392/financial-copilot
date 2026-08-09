using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramGatewayClient(
    HttpClient httpClient,
    IOptions<TelegramGatewayOptions> options,
    ILogger<TelegramGatewayClient> logger) : ITelegramGatewayClient
{
    private readonly TelegramGatewayOptions settings = options.Value;

    public Task<TelegramGatewayOperationResult> SendMessageAsync(long chatId, string text, string? parseMode, string idempotencyKey, IReadOnlyList<TelegramAssistantAction>? actions, CancellationToken cancellationToken) =>
        PostAsync<TelegramGatewayOperationResult>("v1/gateway/telegram/send-message", new { chatId, text, parseMode, idempotencyKey, actions }, cancellationToken);

    public Task<TelegramGatewayOperationResult> SendPhotoAsync(long chatId, string caption, string? parseMode, string idempotencyKey, IReadOnlyList<TelegramAssistantAction>? actions, TelegramAssistantMediaAttachment media, CancellationToken cancellationToken) =>
        PostAsync<TelegramGatewayOperationResult>("v1/gateway/telegram/send-photo", new { chatId, caption, parseMode, idempotencyKey, actions, media }, cancellationToken);

    public Task<TelegramGatewayOperationResult> SendChatActionAsync(long chatId, string action, string idempotencyKey, CancellationToken cancellationToken) =>
        PostAsync<TelegramGatewayOperationResult>("v1/gateway/telegram/send-chat-action", new { chatId, action, idempotencyKey }, cancellationToken);

    public Task<TelegramGatewayOperationResult> AnswerCallbackQueryAsync(string callbackQueryId, string idempotencyKey, CancellationToken cancellationToken) =>
        PostAsync<TelegramGatewayOperationResult>("v1/gateway/telegram/answer-callback-query", new { callbackQueryId, idempotencyKey }, cancellationToken);

    public Task<TelegramGatewayMembershipResult> GetChatMemberAsync(long telegramUserId, string channelId, string correlationId, CancellationToken cancellationToken) =>
        PostAsync<TelegramGatewayMembershipResult>("v1/gateway/telegram/get-chat-member", new { telegramUserId, channelId, correlationId }, cancellationToken);

    private async Task<T> PostAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        if (!settings.Enabled || !Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(settings.ServiceId) || string.IsNullOrWhiteSpace(settings.ServiceSecret))
            return Failure<T>("NotConfigured", "Telegram Gateway is not configured.");

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N");
        request.Headers.TryAddWithoutValidation("X-Gateway-Id", settings.ServiceId);
        request.Headers.TryAddWithoutValidation("X-Gateway-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Gateway-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("X-Gateway-Signature", Sign("POST", "/" + path, timestamp, nonce, json));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Telegram Gateway returned {StatusCode} for {Path}.", (int)response.StatusCode, path);
                return Failure<T>(response.StatusCode == HttpStatusCode.TooManyRequests ? "RateLimited" : "GatewayUnavailable", "Telegram Gateway request failed.");
            }
            return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidOperationException("Telegram Gateway returned an empty response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure<T>("Timeout", "Telegram Gateway request timed out.");
        }
        catch (HttpRequestException)
        {
            return Failure<T>("GatewayUnavailable", "Telegram Gateway is unavailable.");
        }
    }

    private static T Failure<T>(string code, string message) => typeof(T) == typeof(TelegramGatewayMembershipResult)
        ? (T)(object)new TelegramGatewayMembershipResult(false, ErrorCode: code, RedactedError: message)
        : (T)(object)new TelegramGatewayOperationResult(false, ErrorCode: code, RedactedError: message);

    public static string Sign(string method, string path, string timestamp, string nonce, string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{method}\n{path}\n{timestamp}\n{nonce}\n{body}")));

    private string Sign(string method, string path, string timestamp, string nonce, string body) => Sign(method, path, timestamp, nonce, body, settings.ServiceSecret);
}
