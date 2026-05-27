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
    ScannerPlanResponse? ScannerPlan,
    ScannerTableResponse? ScannerTable = null);

public sealed record ScannerPlanResponse(
    Guid PlanId,
    int ConditionCount,
    bool ClarificationRequired,
    string? ClarificationMessage,
    IReadOnlyCollection<string> ColumnOverflowWarnings);

public sealed record ScannerTableColumnResponse(
    string Identifier,
    string DisplayName,
    string ColumnType,
    string? MetricCode);

public sealed record ScannerTableCellResponse(
    decimal? Value,
    string? FormattedValue,
    string FreshnessStatus,
    DateTimeOffset? SourceTimestamp);

public sealed record ScannerTableRowResponse(
    string SymbolCode,
    string? CompanyName,
    IReadOnlyDictionary<string, ScannerTableCellResponse> Cells,
    double Score,
    IReadOnlyCollection<string> MatchedConditionMetrics);

public sealed record ScannerExecutionFactsResponse(
    DateTimeOffset ExecutedAt,
    TimeSpan Duration,
    int TotalSymbolsEvaluated,
    int MatchingSymbolCount,
    bool FromCache);

public sealed record ScannerTableResponse(
    Guid PlanId,
    IReadOnlyCollection<ScannerTableColumnResponse> Columns,
    IReadOnlyCollection<ScannerTableRowResponse> Rows,
    ScannerExecutionFactsResponse ExecutionFacts,
    IReadOnlyCollection<string> MissingDataWarnings);

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
