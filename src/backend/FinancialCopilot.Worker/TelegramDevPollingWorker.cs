using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Worker;

public sealed class TelegramDevPollingWorker(
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramDevPollingOptions> options,
    ILogger<TelegramDevPollingWorker> logger) : BackgroundService
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private long _offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Telegram development polling worker is disabled.");
            return;
        }

        Validate(settings);
        logger.LogInformation("Telegram development polling worker started against backend {BackendBaseUrl}.", settings.BackendBaseUrl);

        using var telegram = httpClientFactory.CreateClient("TelegramDevPolling.Telegram");
        telegram.BaseAddress = new Uri($"https://api.telegram.org/bot{settings.BotToken}/", UriKind.Absolute);
        telegram.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);

        using var backend = httpClientFactory.CreateClient("TelegramDevPolling.Backend");
        backend.BaseAddress = new Uri(settings.BackendBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        backend.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        backend.DefaultRequestHeaders.TryAddWithoutValidation(ApiKeyHeaderName, settings.BackendApiKey);

        if (settings.DeleteWebhookOnStart)
        {
            await DeleteWebhookAsync(telegram, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        do
        {
            await PollOnceAsync(telegram, backend, settings, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(
        HttpClient telegram,
        HttpClient backend,
        TelegramDevPollingOptions settings,
        CancellationToken cancellationToken)
    {
        TelegramGetUpdatesResponse? response;
        try
        {
            var path = $"getUpdates?timeout=0&limit={settings.Limit}&offset={_offset}";
            response = await telegram.GetFromJsonAsync<TelegramGetUpdatesResponse>(path, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Telegram getUpdates failed.");
            return;
        }

        if (response?.Ok != true || response.Result.Count == 0)
        {
            return;
        }

        foreach (var update in response.Result.OrderBy(item => item.UpdateId))
        {
            var handled = false;
            try
            {
                await HandleUpdateAsync(update, telegram, backend, cancellationToken);
                handled = true;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Telegram update {UpdateId} failed in development poller.", update.UpdateId);
            }
            finally
            {
                if (handled)
                {
                    _offset = Math.Max(_offset, update.UpdateId + 1);
                }
            }
        }
    }

    private async Task HandleUpdateAsync(
        TelegramUpdate update,
        HttpClient telegram,
        HttpClient backend,
        CancellationToken cancellationToken)
    {
        if (update.Message is not null)
        {
            await HandleMessageAsync(update, update.Message, telegram, backend, cancellationToken);
            return;
        }

        if (update.CallbackQuery is not null)
        {
            await HandleCallbackAsync(update, update.CallbackQuery, telegram, backend, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(
        TelegramUpdate update,
        TelegramMessage message,
        HttpClient telegram,
        HttpClient backend,
        CancellationToken cancellationToken)
    {
        if (message.From is null || message.Chat is null)
        {
            return;
        }

        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.StartsWith("/start link_", StringComparison.Ordinal))
        {
            await ConfirmLinkAsync(update, message, text["/start ".Length..], telegram, backend, cancellationToken);
            return;
        }

        var result = await PostBackendAsync<TelegramAssistantResult>(
            backend,
            "api/v1/telegram/assistant/updates",
            new TelegramAssistantUpdateRequest(
                update.UpdateId,
                0,
                message.From.Id,
                message.Chat.Id,
                message.MessageThreadId,
                message.MessageId,
                null,
                null,
                text,
                message.From.LanguageCode ?? "fa-IR",
                DateTimeOffset.FromUnixTimeSeconds(message.Date),
                $"telegram-dev-{update.UpdateId}"),
            cancellationToken);

        await SendAssistantMessagesAsync(telegram, message.Chat.Id, result?.Messages ?? [], cancellationToken);
    }

    private async Task ConfirmLinkAsync(
        TelegramUpdate update,
        TelegramMessage message,
        string startParameter,
        HttpClient telegram,
        HttpClient backend,
        CancellationToken cancellationToken)
    {
        var response = await backend.PostAsJsonAsync(
            "api/v1/telegram/link/confirm",
            new TelegramLinkConfirmRequest(
                startParameter,
                message.From!.Id,
                message.Chat!.Id,
                message.From.Username,
                update.UpdateId),
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var text = response.IsSuccessStatusCode
            ? "حساب تلگرام شما با موفقیت متصل شد. اکنون می‌توانید سؤال مالی خود را همین‌جا ارسال کنید."
            : "اتصال حساب تلگرام ناموفق بود. پیوند ممکن است منقضی یا قبلاً مصرف شده باشد. لطفاً از وب‌اپ پیوند جدید بسازید.";

        logger.LogInformation(
            "Telegram link confirmation update {UpdateId} completed with status {StatusCode}. Body: {ResponseBody}",
            update.UpdateId,
            (int)response.StatusCode,
            responseBody);
        await SendMessageAsync(telegram, message.Chat.Id, text, null, cancellationToken);
    }

    private async Task HandleCallbackAsync(
        TelegramUpdate update,
        TelegramCallbackQuery callback,
        HttpClient telegram,
        HttpClient backend,
        CancellationToken cancellationToken)
    {
        if (callback.From is null || callback.Message?.Chat is null)
        {
            return;
        }

        var result = await PostBackendAsync<TelegramAssistantResult>(
            backend,
            "api/v1/telegram/assistant/updates",
            new TelegramAssistantUpdateRequest(
                update.UpdateId,
                1,
                callback.From.Id,
                callback.Message.Chat.Id,
                callback.Message.MessageThreadId,
                callback.Message.MessageId,
                callback.Id,
                callback.Data,
                null,
                callback.From.LanguageCode ?? "fa-IR",
                DateTimeOffset.UtcNow,
                $"telegram-dev-{update.UpdateId}"),
            cancellationToken);

        await AnswerCallbackAsync(telegram, callback.Id, cancellationToken);
        await SendAssistantMessagesAsync(telegram, callback.Message.Chat.Id, result?.Messages ?? [], cancellationToken);
    }

    private static async Task<T?> PostBackendAsync<T>(
        HttpClient backend,
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var response = await backend.PostAsJsonAsync(path, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Backend POST {path} failed with status {(int)response.StatusCode}: {body}");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static async Task SendAssistantMessagesAsync(
        HttpClient telegram,
        long chatId,
        IReadOnlyList<TelegramRenderedMessage> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages.OrderBy(item => item.PartNumber))
        {
            await SendMessageAsync(telegram, chatId, message.Text, message.ParseMode, cancellationToken);
        }
    }

    private static async Task SendMessageAsync(
        HttpClient telegram,
        long chatId,
        string text,
        string? parseMode,
        CancellationToken cancellationToken)
    {
        using var response = await telegram.PostAsJsonAsync(
            "sendMessage",
            TelegramSendMessageRequest.Create(chatId, text, parseMode),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Telegram sendMessage failed with status {(int)response.StatusCode}: {body}");
        }
    }

    private static async Task AnswerCallbackAsync(
        HttpClient telegram,
        string callbackQueryId,
        CancellationToken cancellationToken)
    {
        using var response = await telegram.PostAsJsonAsync(
            "answerCallbackQuery",
            new TelegramAnswerCallbackRequest(callbackQueryId),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task DeleteWebhookAsync(HttpClient telegram, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await telegram.PostAsJsonAsync("deleteWebhook", new { drop_pending_updates = false }, cancellationToken);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Telegram webhook cleared for development polling.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Telegram deleteWebhook failed; polling may not receive updates if a webhook remains configured.");
        }
    }

    private static void Validate(TelegramDevPollingOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BotToken))
        {
            throw new InvalidOperationException("Telegram:DevPolling:BotToken is required when development polling is enabled.");
        }

        if (string.IsNullOrWhiteSpace(settings.BackendApiKey))
        {
            throw new InvalidOperationException("Telegram:DevPolling:BackendApiKey is required when development polling is enabled.");
        }

        if (!Uri.TryCreate(settings.BackendBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Telegram:DevPolling:BackendBaseUrl must be an absolute URI.");
        }
    }

    private sealed record TelegramGetUpdatesResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] IReadOnlyList<TelegramUpdate> Result);

    private sealed record TelegramUpdate(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] TelegramMessage? Message = null,
        [property: JsonPropertyName("callback_query")] TelegramCallbackQuery? CallbackQuery = null);

    private sealed record TelegramMessage(
        [property: JsonPropertyName("message_id")] long MessageId,
        [property: JsonPropertyName("message_thread_id")] int? MessageThreadId,
        [property: JsonPropertyName("from")] TelegramUser? From,
        [property: JsonPropertyName("chat")] TelegramChat? Chat,
        [property: JsonPropertyName("date")] long Date,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record TelegramUser(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("language_code")] string? LanguageCode);

    private sealed record TelegramChat(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("type")] string? Type);

    private sealed record TelegramCallbackQuery(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("from")] TelegramUser? From,
        [property: JsonPropertyName("message")] TelegramMessage? Message,
        [property: JsonPropertyName("data")] string? Data);

    private sealed record TelegramLinkConfirmRequest(
        string StartParameter,
        long TelegramUserId,
        long TelegramChatId,
        string? Username,
        long TelegramUpdateId);

    private sealed record TelegramAssistantUpdateRequest(
        long TelegramUpdateId,
        int Kind,
        long TelegramUserId,
        long TelegramChatId,
        int? MessageThreadId,
        long? TelegramMessageId,
        string? CallbackQueryId,
        string? CallbackData,
        string? Text,
        string Locale,
        DateTimeOffset ReceivedAtUtc,
        string CorrelationId);

    private sealed record TelegramAssistantResult(
        string Status,
        Guid? ActorId,
        Guid? TenantId,
        Guid? ConversationId,
        IReadOnlyList<TelegramRenderedMessage> Messages,
        string CorrelationId);

    private sealed record TelegramRenderedMessage(
        int PartNumber,
        int TotalParts,
        string Text,
        string ParseMode);

    private sealed class TelegramSendMessageRequest
    {
        [JsonPropertyName("chat_id")]
        public required long ChatId { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }

        [JsonPropertyName("parse_mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ParseMode { get; init; }

        public static TelegramSendMessageRequest Create(long chatId, string text, string? parseMode) =>
            new()
            {
                ChatId = chatId,
                Text = text,
                ParseMode = string.IsNullOrWhiteSpace(parseMode) ? null : parseMode
            };
    }

    private sealed record TelegramAnswerCallbackRequest(
        [property: JsonPropertyName("callback_query_id")] string CallbackQueryId);
}
