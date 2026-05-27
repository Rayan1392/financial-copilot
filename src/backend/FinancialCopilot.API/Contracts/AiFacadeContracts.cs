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
    ScannerTableResponse? ScannerTable = null,
    ExplainableAnswerResponse? ExplainableAnswer = null,
    UsageAccountingResponse? Usage = null,
    IReadOnlyCollection<MemoryDisclosureResponse>? MemoryDisclosures = null);

public sealed record UsageAccountingResponse(
    string OperationCode,
    string CompletionStatus,
    decimal CreditsCharged,
    decimal RemainingSpendingCapacity,
    string PricingPolicyVersion,
    bool Cached);

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

public sealed record ConditionFilterChipResponse(
    string MetricCode,
    string MetricDisplayName,
    string OperatorSymbol,
    string OperatorLabel,
    decimal Threshold,
    string ThresholdFormatted,
    string FilterOrigin,
    bool IsInferred,
    string? InferredReason);

public sealed record MetricEvidenceSummaryResponse(
    string MetricCode,
    string MetricVersion,
    string CalculationPolicyVersion,
    string MetricDisplayName,
    string Unit,
    decimal? ActualValue,
    string? FormattedValue,
    string PeriodType,
    DateTimeOffset? ObservedAt);

public sealed record DataCitationResponse(
    string SymbolCode,
    string MetricCode,
    DateTimeOffset? ObservedAt,
    string FreshnessStatus);

public sealed record ConfidenceFactorsResponse(
    double InterpretationCertainty,
    double EvidenceCompleteness,
    double SourceFreshness,
    double WarningPenalty);

public sealed record ConfidenceScoreResponse(
    double Score,
    ConfidenceFactorsResponse Factors,
    string PolicyVersion);

public sealed record ExplainableAnswerResponse(
    IReadOnlyCollection<ConditionFilterChipResponse> FilterChips,
    IReadOnlyCollection<MetricEvidenceSummaryResponse> MetricEvidence,
    IReadOnlyCollection<DataCitationResponse> DataCitations,
    ConfidenceScoreResponse Confidence,
    IReadOnlyCollection<string> SuggestedFollowUpQuestions,
    string? ExplanationText);

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
