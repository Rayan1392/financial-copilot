namespace FinancialCopilot.Application.Telegram;

public sealed record TelegramGatewayOperationResult(
    bool Succeeded,
    string? ProviderMessageId = null,
    string? ErrorCode = null,
    string? RedactedError = null,
    TimeSpan? RetryAfter = null);

public sealed record TelegramGatewayMembershipResult(
    bool Succeeded,
    string? Status = null,
    bool? IsMember = null,
    string? ErrorCode = null,
    string? RedactedError = null);

public interface ITelegramGatewayClient
{
    Task<TelegramGatewayOperationResult> SendMessageAsync(
        long chatId,
        string text,
        string? parseMode,
        string idempotencyKey,
        IReadOnlyList<TelegramAssistantAction>? actions,
        CancellationToken cancellationToken);

    Task<TelegramGatewayOperationResult> SendPhotoAsync(
        long chatId,
        string caption,
        string? parseMode,
        string idempotencyKey,
        IReadOnlyList<TelegramAssistantAction>? actions,
        TelegramAssistantMediaAttachment media,
        CancellationToken cancellationToken);

    Task<TelegramGatewayOperationResult> SendChatActionAsync(
        long chatId,
        string action,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<TelegramGatewayOperationResult> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<TelegramGatewayMembershipResult> GetChatMemberAsync(
        long telegramUserId,
        string channelId,
        string correlationId,
        CancellationToken cancellationToken);
}
