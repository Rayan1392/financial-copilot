using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Application.Telegram;

public enum TelegramAssistantUpdateKind
{
    Message,
    CallbackQuery
}

public enum TelegramAssistantResultStatus
{
    Accepted,
    Replayed,
    Unlinked,
    Unsupported,
    ValidationError,
    TransientError
}

public sealed record TelegramAssistantUpdate(
    long TelegramUpdateId,
    TelegramAssistantUpdateKind Kind,
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

public sealed record TelegramAssistantRenderedMessage(
    int PartNumber,
    int TotalParts,
    string Text,
    string ParseMode = "MarkdownV2");

public sealed record TelegramAssistantResult(
    TelegramAssistantResultStatus Status,
    Guid? ActorId,
    Guid? TenantId,
    Guid? ConversationId,
    IReadOnlyList<TelegramAssistantRenderedMessage> Messages,
    string CorrelationId,
    AiQueryResponse? AiResponse = null);

public interface ITelegramAiAssistantAdapter
{
    Task<TelegramAssistantResult> HandleAsync(
        TelegramAssistantUpdate update,
        CancellationToken cancellationToken);
}
