namespace FinancialCopilot.Application.Scanner;

// Applied filter display for one condition in the executed scanner plan.
public sealed record ConditionFilterChip(
    string MetricCode,
    string MetricDisplayName,
    string OperatorSymbol,
    string OperatorLabel,
    decimal Threshold,
    string ThresholdFormatted,
    string FilterOrigin,
    bool IsInferred,
    string? InferredReason);

// Canonical metric evidence with resolved semantic version and policy for one condition.
public sealed record MetricEvidenceSummary(
    string MetricCode,
    string MetricVersion,
    string CalculationPolicyVersion,
    string MetricDisplayName,
    string Unit,
    decimal? ActualValue,
    string? FormattedValue,
    string PeriodType,
    DateTimeOffset? ObservedAt);

// Source citation for a single cell: freshness status and observation timestamp.
public sealed record DataCitation(
    string SymbolCode,
    string MetricCode,
    DateTimeOffset? ObservedAt,
    string FreshnessStatus);

// Factor inputs used to calculate the confidence score — preserved for audit.
public sealed record ConfidenceFactors(
    double InterpretationCertainty,
    double EvidenceCompleteness,
    double SourceFreshness,
    double WarningPenalty);

// Backend-computed confidence score with factor breakdown and policy version.
// The AI model must not invent, estimate, or overwrite this value.
public sealed record ConfidenceScoreResult(
    double Score,
    ConfidenceFactors Factors,
    string PolicyVersion);

// Full explainable answer attached to a completed scanner result.
public sealed record ExplainableAnswer(
    IReadOnlyCollection<ConditionFilterChip> FilterChips,
    IReadOnlyCollection<MetricEvidenceSummary> MetricEvidence,
    IReadOnlyCollection<DataCitation> DataCitations,
    ConfidenceScoreResult Confidence,
    IReadOnlyCollection<string> SuggestedFollowUpQuestions,
    string? ExplanationText);

// Input to the explainable answer builder.
public sealed record ExplainableAnswerRequest(
    ScannerQueryPlan Plan,
    ScannerTableResult? ExecutionResult,
    Guid TenantId,
    string CorrelationId);

// Input to the AI-driven explanation and suggestions generator.
public sealed record ScannerExplanationRequest(
    string OriginalQuery,
    int MatchedSymbolCount,
    IReadOnlyCollection<string> MatchedSymbols,
    IReadOnlyCollection<ConditionFilterChip> FilterChips,
    Guid TenantId,
    string CorrelationId);

// Output from the AI-driven explanation generator. Both fields are optional.
public sealed record ScannerExplanationOutput(
    string? ExplanationText,
    IReadOnlyCollection<string> SuggestedFollowUpQuestions);

// Deterministic confidence score calculator. Never called by the LLM.
public interface IConfidenceScoreCalculator
{
    ConfidenceScoreResult Calculate(
        ScannerQueryPlan plan,
        ScannerTableResult? executionResult);
}

// Builds a complete ExplainableAnswer from a validated scanner plan and execution result.
public interface IExplainableAnswerBuilder
{
    Task<ExplainableAnswer> BuildAsync(
        ExplainableAnswerRequest request,
        CancellationToken cancellationToken);
}

// Generates optional prose summary and follow-up suggestions via the AI model.
public interface IScannerExplanationGenerator
{
    Task<ScannerExplanationOutput> GenerateAsync(
        ScannerExplanationRequest request,
        CancellationToken cancellationToken);
}
