using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class TelegramGatewayPollingWorker(
    TelegramApiClient telegram,
    PrimaryApiClient primaryApi,
    GatewayIdempotencyStore idempotency,
    IOptions<TelegramGatewaySettings> options,
    ILogger<TelegramGatewayPollingWorker> logger) : BackgroundService
{
    private readonly TelegramGatewaySettings settings = options.Value;
    private long offset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled) { logger.LogInformation("Telegram Gateway polling is disabled."); return; }
        offset = await LoadOffsetAsync(stoppingToken);
        if (settings.DeleteWebhookOnStart) await telegram.DeleteWebhookAsync(stoppingToken);
        logger.LogInformation("Telegram Gateway polling started with persisted offset {Offset}.", offset);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates.OrderBy(item => item.UpdateId))
                {
                    await HandleAsync(update, stoppingToken);
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await SaveOffsetAsync(offset, stoppingToken);
                }
                if (updates.Count == 0) await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 0, 30)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning("Telegram Gateway polling cycle failed ({ExceptionType}); retrying with the persisted offset.", exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(TelegramGatewayUpdate update, CancellationToken cancellationToken)
    {
        if (update.Message is { From: { } from, Chat: { } chat } message)
        {
            if (string.IsNullOrWhiteSpace(message.Text)) return;
            if (message.Text.StartsWith("/start link_", StringComparison.Ordinal))
            {
                var success = await primaryApi.ConfirmLinkAsync(new TelegramLinkConfirmRequest(message.Text["/start ".Length..], from.Id, chat.Id, from.Username, update.UpdateId), cancellationToken);
                await telegram.SendMessageAsync(chat.Id, success ? "حساب تلگرام شما با موفقیت متصل شد." : "اتصال حساب تلگرام ناموفق بود.", null, null, cancellationToken);
                return;
            }
            await telegram.SendChatActionAsync(chat.Id, "typing", cancellationToken);
            var result = await primaryApi.HandleUpdateAsync(new TelegramAssistantUpdateRequest(update.UpdateId, TelegramAssistantUpdateKind.Message, from.Id, chat.Id, message.MessageThreadId, message.MessageId, null, null, message.Text.Trim(), from.LanguageCode ?? "fa-IR", DateTimeOffset.FromUnixTimeSeconds(message.Date), $"telegram:{update.UpdateId}"), cancellationToken);
            await SendMessagesAsync(chat.Id, update.UpdateId, result?.Messages, cancellationToken);
            return;
        }

        if (update.CallbackQuery is { } callback && callback.From is { } callbackFrom && callback.Message?.Chat is { } callbackChat)
        {
            await telegram.SendChatActionAsync(callbackChat.Id, "typing", cancellationToken);
            var result = await primaryApi.HandleUpdateAsync(new TelegramAssistantUpdateRequest(update.UpdateId, TelegramAssistantUpdateKind.CallbackQuery, callbackFrom.Id, callbackChat.Id, callback.Message!.MessageThreadId, callback.Message.MessageId, callback.Id, callback.Data, null, callbackFrom.LanguageCode ?? "fa-IR", DateTimeOffset.UtcNow, $"telegram:{update.UpdateId}"), cancellationToken);
            await telegram.AnswerCallbackQueryAsync(callback.Id, cancellationToken);
            await SendMessagesAsync(callbackChat.Id, update.UpdateId, result?.Messages, cancellationToken);
        }
    }

    private async Task SendMessagesAsync(long chatId, long updateId, IReadOnlyList<TelegramAssistantRenderedMessage>? messages, CancellationToken cancellationToken)
    {
        foreach (var message in (messages ?? []).OrderBy(item => item.PartNumber))
        {
            var key = $"update:{updateId}:part:{message.PartNumber}";
            if (idempotency.TryGet(key, out _)) continue;
            TelegramGatewayOperationResult result;
            if (message.Media is not null)
                result = await telegram.SendPhotoAsync(chatId, message.Text, message.ParseMode, message.Actions, message.Media, cancellationToken);
            else
                result = await telegram.SendMessageAsync(chatId, message.Text, message.ParseMode, message.Actions, cancellationToken);
            if (result.Succeeded || result.ErrorCode is not ("RateLimited" or "Timeout" or "GatewayUnavailable"))
                await idempotency.SetAsync(key, result, cancellationToken);
        }
    }

    private async Task<long> LoadOffsetAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settings.OffsetFilePath)) return 0;
        var text = await File.ReadAllTextAsync(settings.OffsetFilePath, cancellationToken);
        return long.TryParse(text, out var value) && value >= 0 ? value : 0;
    }

    private async Task SaveOffsetAsync(long value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(settings.OffsetFilePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(settings.OffsetFilePath, value.ToString(), cancellationToken);
    }
}
