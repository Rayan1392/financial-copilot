using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.API.Contracts;

public sealed record AiQueryHttpRequest(
    string Message,
    Guid? ConversationId = null);

public sealed record AiQueryHttpResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid AssistantMessageId,
    string Intent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    string? TextAnswer,
    ScannerPlanResponse? ScannerPlan);

public sealed record ScannerPlanResponse(
    Guid PlanId,
    int ConditionCount,
    bool ClarificationRequired,
    string? ClarificationMessage,
    IReadOnlyCollection<string> ColumnOverflowWarnings);

public sealed record ConversationSummaryResponse(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ConversationDetailResponse(
    Guid ConversationId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<MessageResponse> Messages);

public sealed record MessageResponse(
    Guid MessageId,
    string Role,
    string Content,
    bool HasScannerPlan,
    DateTimeOffset CreatedAt);
