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
        if (!settings.Enabled)
        {
            logger.LogInformation("Telegram Gateway polling is disabled.");
            return;
        }

        offset = await LoadOffsetAsync(stoppingToken);
        if (settings.DeleteWebhookOnStart) await telegram.DeleteWebhookAsync(stoppingToken);
        logger.LogInformation("Telegram Gateway polling started with persisted offset {Offset}.", offset);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await telegram.GetUpdatesAsync(offset, stoppingToken);
                var completed = await ProcessUpdatesAsync(updates, stoppingToken);
                if (!completed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                else if (updates.Count == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 0, 30)),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Telegram Gateway polling cycle failed ({ExceptionType}); retrying with the persisted offset.",
                    exception.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<bool> ProcessUpdatesAsync(
        IReadOnlyList<TelegramGatewayUpdate> updates,
        CancellationToken cancellationToken)
    {
        foreach (var update in updates.OrderBy(item => item.UpdateId))
        {
            if (await ProcessUpdateAsync(update, cancellationToken) == UpdateCompletion.Retry)
            {
                return false;
            }

            offset = Math.Max(offset, update.UpdateId + 1);
            await SaveOffsetAsync(offset, cancellationToken);
        }

        return true;
    }

    internal async Task<UpdateCompletion> ProcessUpdateAsync(
        TelegramGatewayUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.Message is { From: { } from, Chat: { } chat } message)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                LogUnsupported(update.UpdateId, "message text is missing");
                return UpdateCompletion.Complete;
            }

            if (message.Text.StartsWith("/start link_", StringComparison.Ordinal))
            {
                var success = await primaryApi.ConfirmLinkAsync(
                    new TelegramLinkConfirmRequest(
                        message.Text["/start ".Length..],
                        from.Id,
                        chat.Id,
                        from.Username,
                        update.UpdateId),
                    cancellationToken);
                await telegram.SendMessageAsync(
                    chat.Id,
                    success
                        ? "حساب تلگرام شما با موفقیت متصل شد."
                        : "اتصال حساب تلگرام ناموفق بود.",
                    null,
                    null,
                    cancellationToken);
                return UpdateCompletion.Complete;
            }

            await telegram.SendChatActionAsync(chat.Id, "typing", cancellationToken);
            TelegramAssistantResult? result;
            try
            {
                result = await primaryApi.HandleUpdateAsync(
                    new TelegramAssistantUpdateRequest(
                        update.UpdateId,
                        TelegramAssistantUpdateKind.Message,
                        from.Id,
                        chat.Id,
                        message.MessageThreadId,
                        message.MessageId,
                        null,
                        null,
                        message.Text.Trim(),
                        from.LanguageCode ?? "fa-IR",
                        DateTimeOffset.FromUnixTimeSeconds(message.Date),
                        $"telegram:{update.UpdateId}"),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogPrimaryTransient(update.UpdateId, "Timeout");
                return UpdateCompletion.Retry;
            }
            catch (HttpRequestException exception) when (IsAuthenticationFailure(exception.StatusCode))
            {
                LogPrimaryAuthenticationFailure(update.UpdateId, exception.StatusCode!.Value, callback: false);
                await SendAuthenticationFailureAsync(chat.Id, update.UpdateId, cancellationToken);
                return UpdateCompletion.Complete;
            }
            catch (HttpRequestException exception) when (IsTransientPrimaryFailure(exception.StatusCode))
            {
                LogPrimaryTransient(update.UpdateId, exception.StatusCode?.ToString() ?? "NetworkError");
                return UpdateCompletion.Retry;
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(
                    "Primary API permanently rejected Telegram update {TelegramUpdateId} with status {StatusCode}.",
                    update.UpdateId,
                    exception.StatusCode is null ? "Unknown" : ((int)exception.StatusCode.Value).ToString());
                await SendAuthenticationFailureAsync(chat.Id, update.UpdateId, cancellationToken);
                return UpdateCompletion.Complete;
            }

            if (result is null)
            {
                LogPrimaryTransient(update.UpdateId, "EmptyResponse");
                return UpdateCompletion.Retry;
            }

            return await SendMessagesAsync(chat.Id, update.UpdateId, result.Messages, cancellationToken);
        }

        if (update.CallbackQuery is { } callback &&
            callback.From is { } callbackFrom &&
            callback.Message?.Chat is { } callbackChat)
        {
            await telegram.SendChatActionAsync(callbackChat.Id, "typing", cancellationToken);
            TelegramAssistantResult? result;
            try
            {
                result = await primaryApi.HandleUpdateAsync(
                    new TelegramAssistantUpdateRequest(
                        update.UpdateId,
                        TelegramAssistantUpdateKind.CallbackQuery,
                        callbackFrom.Id,
                        callbackChat.Id,
                        callback.Message!.MessageThreadId,
                        callback.Message.MessageId,
                        callback.Id,
                        callback.Data,
                        null,
                        callbackFrom.LanguageCode ?? "fa-IR",
                        DateTimeOffset.UtcNow,
                        $"telegram:{update.UpdateId}"),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogPrimaryTransient(update.UpdateId, "Timeout");
                return UpdateCompletion.Retry;
            }
            catch (HttpRequestException exception) when (IsAuthenticationFailure(exception.StatusCode))
            {
                LogPrimaryAuthenticationFailure(update.UpdateId, exception.StatusCode!.Value, callback: true);
                await SendAuthenticationFailureAsync(callbackChat.Id, update.UpdateId, cancellationToken);
                return UpdateCompletion.Complete;
            }
            catch (HttpRequestException exception) when (IsTransientPrimaryFailure(exception.StatusCode))
            {
                LogPrimaryTransient(update.UpdateId, exception.StatusCode?.ToString() ?? "NetworkError");
                return UpdateCompletion.Retry;
            }

            await telegram.AnswerCallbackQueryAsync(callback.Id, cancellationToken);
            return result is null
                ? UpdateCompletion.Retry
                : await SendMessagesAsync(callbackChat.Id, update.UpdateId, result.Messages, cancellationToken);
        }

        LogUnsupported(update.UpdateId, "unsupported or incomplete update shape");
        return UpdateCompletion.Complete;
    }

    internal async Task<UpdateCompletion> SendMessagesAsync(
        long chatId,
        long updateId,
        IReadOnlyList<TelegramAssistantRenderedMessage>? messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in (messages ?? []).OrderBy(item => item.PartNumber))
        {
            var key = $"update:{updateId}:part:{message.PartNumber}";
            if (idempotency.TryGet(key, out _)) continue;

            var result = message.Media is not null
                ? await telegram.SendPhotoAsync(
                    chatId,
                    message.Text,
                    message.ParseMode,
                    message.Actions,
                    message.Media,
                    cancellationToken)
                : await telegram.SendMessageAsync(
                    chatId,
                    message.Text,
                    message.ParseMode,
                    message.Actions,
                    cancellationToken);

            if (result.Succeeded)
            {
                try
                {
                    await idempotency.SetAsync(key, result, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        "Telegram response part persistence failed for update {TelegramUpdateId}, part {PartNumber} ({ExceptionType}).",
                        updateId,
                        message.PartNumber,
                        exception.GetType().Name);
                    // Telegram already confirmed the send. Retrying here causes
                    // duplicate messages when the state file is temporarily unavailable.
                    idempotency.Remember(key, result);
                }

                continue;
            }

            if (IsTransientTelegramFailure(result.ErrorCode))
            {
                logger.LogWarning(
                    "Telegram response delivery is transiently incomplete for update {TelegramUpdateId}, part {PartNumber}, code {ErrorCode}.",
                    updateId,
                    message.PartNumber,
                    result.ErrorCode);
                return UpdateCompletion.Retry;
            }

            logger.LogWarning(
                "Telegram permanently rejected response delivery for update {TelegramUpdateId}, chat {TelegramChatId}, part {PartNumber}, code {ErrorCode}.",
                updateId,
                chatId,
                message.PartNumber,
                result.ErrorCode ?? "TelegramError");
            return UpdateCompletion.Complete;
        }

        return UpdateCompletion.Complete;
    }

    private async Task SendAuthenticationFailureAsync(
        long chatId,
        long updateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await telegram.SendMessageAsync(
                chatId,
                "در حال حاضر سرویس موقتاً در دسترس نیست. لطفاً بعداً دوباره تلاش کنید.",
                null,
                null,
                cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogWarning(
                    "Telegram authentication-failure notice was not delivered for update {TelegramUpdateId}, chat {TelegramChatId}, code {ErrorCode}.",
                    updateId,
                    chatId,
                    result.ErrorCode ?? "TelegramError");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Telegram authentication-failure notice failed for update {TelegramUpdateId}, chat {TelegramChatId} ({ExceptionType}).",
                updateId,
                chatId,
                exception.GetType().Name);
        }
    }

    private static bool IsAuthenticationFailure(System.Net.HttpStatusCode? statusCode) =>
        statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;

    private static bool IsTransientPrimaryFailure(System.Net.HttpStatusCode? statusCode) =>
        statusCode is null or System.Net.HttpStatusCode.TooManyRequests || (int)statusCode.Value >= 500;

    private static bool IsTransientTelegramFailure(string? errorCode) =>
        errorCode is "RateLimited" or "Timeout" or "GatewayUnavailable";

    private void LogUnsupported(long updateId, string reason) =>
        logger.LogWarning("Telegram update {TelegramUpdateId} was skipped: {Reason}.", updateId, reason);

    private void LogPrimaryTransient(long updateId, string failure) =>
        logger.LogWarning(
            "Primary API handling is transiently incomplete for Telegram update {TelegramUpdateId}: {Failure}.",
            updateId,
            failure);

    private void LogPrimaryAuthenticationFailure(
        long updateId,
        System.Net.HttpStatusCode statusCode,
        bool callback) =>
        logger.LogError(
            "Primary API rejected Telegram {UpdateKind} {TelegramUpdateId} with service authentication status {StatusCode}.",
            callback ? "callback update" : "update",
            updateId,
            (int)statusCode);

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

internal enum UpdateCompletion
{
    Complete,
    Retry
}
