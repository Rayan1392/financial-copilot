using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class TelegramApiClient(IHttpClientFactory factory, IOptions<TelegramGatewaySettings> options)
{
    private readonly TelegramGatewaySettings settings = options.Value;

    public async Task<IReadOnlyList<TelegramGatewayUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        using var client = CreateClient(settings.LongPollTimeoutSeconds + 10);
        var response = await client.GetFromJsonAsync<TelegramEnvelope<IReadOnlyList<TelegramGatewayUpdate>>>(
            $"getUpdates?timeout={settings.LongPollTimeoutSeconds}&limit={Math.Clamp(settings.Limit, 1, 100)}&offset={offset}", cancellationToken);
        return response?.Ok == true ? response.Result ?? [] : [];
    }

    public async Task DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        await client.PostAsJsonAsync("deleteWebhook", new { drop_pending_updates = false }, cancellationToken);
    }

    public async Task<TelegramGatewayOperationResult> SendMessageAsync(long chatId, string text, string? parseMode, IReadOnlyList<TelegramAssistantAction>? actions, CancellationToken cancellationToken)
    {
        var payload = SendMessagePayload(chatId, text, parseMode, actions);
        var result = await PostOperationAsync<TelegramMessageResult>("sendMessage", payload, value => value?.MessageId?.ToString(CultureInfo.InvariantCulture), cancellationToken);

        // Telegram may reject otherwise valid MarkdownV2 when model-produced content contains
        // an entity edge case. Retry once as plain text so a valid assistant response is not lost.
        if (!result.Succeeded && result.ErrorCode == "TelegramError" && !string.IsNullOrWhiteSpace(parseMode))
        {
            var plainPayload = SendMessagePayload(chatId, text, null, actions);
            result = await PostOperationAsync<TelegramMessageResult>("sendMessage", plainPayload, value => value?.MessageId?.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }

        return result;
    }

    public async Task<TelegramGatewayOperationResult> SendPhotoAsync(long chatId, string caption, string? parseMode, IReadOnlyList<TelegramAssistantAction>? actions, TelegramAssistantMediaAttachment media, CancellationToken cancellationToken)
    {
        if (!string.Equals(media.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            return new(false, ErrorCode: "InvalidMedia", RedactedError: "Only PNG photo attachments are supported.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(media.ContentBase64); }
        catch (FormatException) { return new(false, ErrorCode: "InvalidMedia", RedactedError: "Photo attachment validation failed."); }
        if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024 || !string.Equals(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), media.Sha256, StringComparison.OrdinalIgnoreCase))
            return new(false, ErrorCode: "InvalidMedia", RedactedError: "Photo attachment validation failed.");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString(CultureInfo.InvariantCulture)), "chat_id");
        form.Add(new StringContent(caption), "caption");
        if (!string.IsNullOrWhiteSpace(parseMode)) form.Add(new StringContent(parseMode), "parse_mode");
        if (Markup(actions) is { } markup) form.Add(new StringContent(JsonSerializer.Serialize(markup)), "reply_markup");
        var photo = new ByteArrayContent(bytes); photo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(photo, "photo", media.FileName);
        return await SendOperationAsync<TelegramMessageResult>("sendPhoto", form, response => response?.MessageId?.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    public Task<TelegramGatewayOperationResult> SendChatActionAsync(long chatId, string action, CancellationToken cancellationToken) =>
        PostOperationAsync<TelegramMessageResult>("sendChatAction", new { chat_id = chatId, action }, _ => null, cancellationToken);

    public Task<TelegramGatewayOperationResult> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken) =>
        PostOperationAsync<TelegramMessageResult>("answerCallbackQuery", new { callback_query_id = callbackQueryId }, _ => null, cancellationToken);

    public async Task<TelegramGatewayMembershipResult> GetChatMemberAsync(long userId, string channelId, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new { chat_id = channelId, user_id = userId });
        using var client = CreateClient();
        using var response = await client.PostAsync("getChatMember", content, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<TelegramEnvelope<TelegramChatMember>>(cancellationToken: cancellationToken);
        return response.IsSuccessStatusCode && envelope?.Ok == true && envelope.Result is { } member
            ? new TelegramGatewayMembershipResult(true, member.Status, member.IsMember)
            : new TelegramGatewayMembershipResult(false, ErrorCode: "TelegramError", RedactedError: "Telegram membership operation failed.");
    }

    private async Task<TelegramGatewayOperationResult> PostOperationAsync<TResult>(string method, object payload, Func<TResult?, string?> id, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(payload);
        return await SendOperationAsync(method, content, id, cancellationToken);
    }

    private async Task<TelegramGatewayOperationResult> SendOperationAsync<TResult>(string method, HttpContent content, Func<TResult?, string?> id, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient();
            using var response = await client.PostAsync(method, content, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return new(false, ErrorCode: "RateLimited", RedactedError: "Telegram operation was rate limited.", RetryAfter: response.Headers.RetryAfter?.Delta);
            if (response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.GatewayTimeout)
                return new(false, ErrorCode: "Timeout", RedactedError: "Telegram operation timed out.");
            if ((int)response.StatusCode >= 500)
                return new(false, ErrorCode: "GatewayUnavailable", RedactedError: "Telegram operation is temporarily unavailable.");
            if (!response.IsSuccessStatusCode)
                return new(false, ErrorCode: "TelegramError", RedactedError: "Telegram permanently rejected the operation.");

            var envelope = await response.Content.ReadFromJsonAsync<TelegramEnvelope<TResult>>(cancellationToken: cancellationToken);
            return envelope?.Ok == true
                ? new(true, envelope.Result is null ? null : id(envelope.Result))
                : new(false, ErrorCode: "TelegramError", RedactedError: "Telegram permanently rejected the operation.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, ErrorCode: "Timeout", RedactedError: "Telegram operation timed out.");
        }
        catch (HttpRequestException)
        {
            return new(false, ErrorCode: "GatewayUnavailable", RedactedError: "Telegram operation is temporarily unavailable.");
        }
        catch (JsonException)
        {
            return new(false, ErrorCode: "GatewayUnavailable", RedactedError: "Telegram returned an unreadable response.");
        }
    }

    private HttpClient CreateClient(int? timeoutSeconds = null)
    {
        var client = factory.CreateClient("TelegramGateway.Telegram");
        client.BaseAddress = new Uri($"https://api.telegram.org/bot{settings.BotToken}/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds ?? settings.RequestTimeoutSeconds, 5, 180));
        return client;
    }

    private static object? Markup(IReadOnlyList<TelegramAssistantAction>? actions) => actions is { Count: > 0 }
        ? new { inline_keyboard = actions.Select(action => new[] { new { text = action.Text, callback_data = action.CallbackData } }).ToArray() }
        : null;

    private static Dictionary<string, object> SendMessagePayload(long chatId, string text, string? parseMode, IReadOnlyList<TelegramAssistantAction>? actions)
    {
        var payload = new Dictionary<string, object>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["disable_web_page_preview"] = true
        };

        if (!string.IsNullOrWhiteSpace(parseMode)) payload["parse_mode"] = parseMode;
        if (Markup(actions) is { } markup) payload["reply_markup"] = markup;
        return payload;
    }

    private sealed record TelegramEnvelope<T>([property: JsonPropertyName("ok")] bool Ok, [property: JsonPropertyName("result")] T? Result);
    private sealed record TelegramMessageResult([property: JsonPropertyName("message_id")] long? MessageId);
    private sealed record TelegramChatMember([property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("is_member")] bool? IsMember);
}

public sealed record TelegramGatewayUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramGatewayMessage? Message = null,
    [property: JsonPropertyName("callback_query")] TelegramGatewayCallbackQuery? CallbackQuery = null);

public sealed record TelegramGatewayMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("message_thread_id")] int? MessageThreadId,
    [property: JsonPropertyName("from")] TelegramGatewayUser? From,
    [property: JsonPropertyName("chat")] TelegramGatewayChat? Chat,
    [property: JsonPropertyName("date")] long Date,
    [property: JsonPropertyName("text")] string? Text);

public sealed record TelegramGatewayUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("language_code")] string? LanguageCode);

public sealed record TelegramGatewayChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type);

public sealed record TelegramGatewayCallbackQuery(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("from")] TelegramGatewayUser? From,
    [property: JsonPropertyName("message")] TelegramGatewayMessage? Message,
    [property: JsonPropertyName("data")] string? Data);
