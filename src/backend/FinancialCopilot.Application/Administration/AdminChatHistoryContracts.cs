using System.Text.Json;

namespace FinancialCopilot.Application.Administration;

public sealed record AdminChatHistoryPage(
    IReadOnlyCollection<AdminChatHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminChatHistoryItem(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    string QuestionSummary,
    string? AnswerSummary,
    DateTimeOffset QuestionCreatedAtUtc,
    DateTimeOffset? AnswerCreatedAtUtc,
    bool HasAnswer);

public sealed record AdminChatHistoryTurn(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    string Question,
    DateTimeOffset QuestionCreatedAtUtc,
    IReadOnlyCollection<AdminChatHistoryAnswer> Answers);

public sealed record AdminChatHistoryAnswer(
    Guid MessageId,
    string Content,
    DateTimeOffset CreatedAtUtc,
    JsonElement? StructuredPayload);

public interface IAdminChatHistoryQueries
{
    Task<AdminChatHistoryPage> GetPageAsync(
        Guid tenantId,
        Guid userId,
        int page,
        CancellationToken cancellationToken);

    Task<AdminChatHistoryTurn?> GetTurnAsync(
        Guid tenantId,
        Guid userId,
        Guid administratorId,
        string administratorType,
        string correlationId,
        Guid turnId,
        CancellationToken cancellationToken);
}
