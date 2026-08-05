using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public enum FilterOrigin
{
    Explicit,        // condition was directly stated by the user
    InferredDefault, // condition was added by documented parser default policy
    Clarified        // condition was originally ambiguous, resolved through clarification
}

public enum ConditionOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
    NotEqual
}

// A single resolved and validated screening condition.
// MetricReference carries the canonical MetricCode, version, and policy evidence
// so Story 009 (explainability) can state exactly which financial definition was used.
public sealed record ScannerCondition(
    ScannerMetricReference MetricReference,
    ConditionOperator Operator,
    decimal Threshold,
    FilterOrigin Origin,
    string? OriginReason = null);

public sealed record ScannerColumnRequest(
    string Identifier,
    bool IsUserRequested);

// A metric term the LLM proposed but the backend could not uniquely resolve.
// Populated when ClarificationRequired is true.
public sealed record ScannerClarificationItem(
    string UserTerminology,
    string Reason,
    IReadOnlyCollection<string> Candidates);

// Fully validated scanner query plan. Contains canonical MetricCode values,
// resolved definition/version references, and policy context.
// No SQL, no raw user expressions, no vendor-specific artifacts.
public sealed record ScannerQueryPlan(
    Guid PlanId,
    string OriginalUserQuery,
    string Language,
    IReadOnlyCollection<ScannerCondition> Conditions,
    IReadOnlyCollection<ScannerColumnRequest> RequestedColumns,
    bool ClarificationRequired,
    string? ClarificationMessage,
    IReadOnlyCollection<ScannerClarificationItem> ClarificationItems,
    IReadOnlyCollection<string> ColumnOverflowWarnings,
    DateTimeOffset ParsedAt,
    string PolicyVersion,
    SalesGrowthScannerPlan? SalesGrowth = null)
{
    public const int MaxDisplayColumns = 10;
}

public sealed record ScannerParseRequest(
    string UserQuery,
    string Language,
    string CorrelationId,
    Guid TenantId,
    DateOnly AsOf);

public sealed record ScannerParseResult(
    ScannerQueryPlan Plan,
    bool Succeeded,
    string? FailureReason = null);

// Raw JSON structure returned by the LLM before backend validation.
// Not exposed outside the parser layer.
public sealed record LlmConditionCandidate(
    string UserTerminology,
    string Language,
    string Operator,
    decimal Threshold,
    string? PeriodHint,
    string? GrowthComparison,
    bool InferredDefault,
    string? InferredReason = null);

public sealed record LlmScannerParseOutput(
    string DetectedLanguage,
    IReadOnlyCollection<LlmConditionCandidate> Conditions,
    IReadOnlyCollection<string> RequestedColumns,
    bool ClarificationRequired,
    string? ClarificationMessage);

public interface IScannerQueryParser
{
    Task<ScannerParseResult> ParseAsync(
        ScannerParseRequest request,
        CancellationToken cancellationToken);
}

public interface IScannerQueryPlanValidator
{
    // Validates a plan produced by the LLM parser. Returns a validation failure
    // message if the plan is invalid, or null if it is valid.
    string? Validate(ScannerQueryPlan plan);
}
