using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

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
    string ParseMode = "MarkdownV2",
    IReadOnlyList<TelegramAssistantAction>? Actions = null,
    TelegramAssistantMediaAttachment? Media = null);

public sealed record TelegramAssistantMediaAttachment(
    string Kind,
    string ContentType,
    string FileName,
    string ContentBase64,
    string Sha256,
    string RenderVersion);

public sealed record TelegramAssistantAction(string Text, string CallbackData);

public sealed record TelegramAssistantResult(
    TelegramAssistantResultStatus Status,
    Guid? ActorId,
    Guid? TenantId,
    Guid? ConversationId,
    IReadOnlyList<TelegramAssistantRenderedMessage> Messages,
    string CorrelationId,
    AiQueryResponse? AiResponse = null,
    string RenderVersion = "telegram-render-v2");

public interface ITelegramAiAssistantAdapter
{
    Task<TelegramAssistantResult> HandleAsync(
        TelegramAssistantUpdate update,
        CancellationToken cancellationToken);
}

public interface ITelegramAssistantResponseRenderer
{
    string Version { get; }

    IReadOnlyList<TelegramAssistantRenderedMessage> Render(
        AiQueryResponse response,
        string locale);
}

public sealed record TelegramDisclosurePaginationState(
    Guid ActorId,
    Guid TenantId,
    long TelegramUserId,
    long TelegramChatId,
    int? MessageThreadId,
    Guid ConversationId,
    string OriginalQuery,
    int TotalPages,
    DateTimeOffset ExpiresAtUtc);

public interface ITelegramDisclosurePaginationStateStore
{
    string Create(TelegramDisclosurePaginationState state);
    bool TryGet(string token, out TelegramDisclosurePaginationState state);
}

public interface ITelegramMonthlyTrendChartRenderer
{
    TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend);

    // Kept on the existing renderer boundary so Telegram image delivery remains
    // an infrastructure concern and the web/API response contracts are unchanged.
    TelegramAssistantMediaAttachment Render(ProductRevenueMixResponse result) =>
        throw new NotSupportedException("Product revenue mix image rendering is not supported.");
}
