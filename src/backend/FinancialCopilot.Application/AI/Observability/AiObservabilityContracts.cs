using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.Application.AI.Observability;

// Classifies AI workflow failures by operational domain for alerting and root-cause analysis.
public enum AiErrorCategory
{
    Validation,
    Clarification,
    ProviderFailure,
    Timeout,
    ToolFailure,
    DataInsufficiency,
    BillingRejection,
    PersistenceFailure
}

// Controls whether prompt/response content is retained and under what privacy constraints.
// Sensitive content is never captured unless CapturePromptContent or CaptureResponseContent
// is explicitly set to true; even then, PII redaction must remain enabled.
public sealed record PromptRedactionPolicy(
    bool CapturePromptContent,
    bool CaptureResponseContent,
    bool RedactPii,
    string? RetentionCategory,
    string PolicyVersion)
{
    // Safe default: no content capture, PII redaction on.
    public static readonly PromptRedactionPolicy DefaultSafe = new(
        CapturePromptContent: false,
        CaptureResponseContent: false,
        RedactPii: true,
        RetentionCategory: null,
        PolicyVersion: "v1");
}

// Prompt/completion capture record. PromptContentSummary and ResponseContentSummary are null
// unless the redaction policy explicitly enables capture; the caller must populate them only
// after applying the policy.
public sealed record PromptTrace(
    string CorrelationId,
    Guid TenantId,
    AiWorkloadKind Workload,
    string? PromptContentSummary,
    string? ResponseContentSummary,
    bool Redacted,
    DateTimeOffset CapturedAt);

// Per-tool invocation trace. Correlates to the workflow via CorrelationId.
public sealed record ToolExecutionTrace(
    string CorrelationId,
    string ToolName,
    bool Succeeded,
    TimeSpan Duration,
    AiErrorCategory? ErrorCategory,
    string? ErrorCode,
    DateTimeOffset ExecutedAt);

// Per-provider attempt latency. Multiple records per workflow when fallback occurs.
public sealed record ProviderLatency(
    string CorrelationId,
    string ProviderKey,
    string ModelKey,
    int AttemptNumber,
    TimeSpan Duration,
    AiExecutionStatus Status,
    string? FailureCode,
    DateTimeOffset AttemptedAt);

// Token accounting facts for reconciliation with Billing and provider cost reporting.
public sealed record TokenUsage(
    string CorrelationId,
    string ProviderKey,
    string ModelKey,
    int? InputTokens,
    int? OutputTokens,
    bool CacheHit);

// Observability-only cost record. Billing ledger remains authoritative; this enables
// reconciliation checks and provider-cost vs. billed-credit comparisons.
public sealed record CostTelemetry(
    string CorrelationId,
    Guid TenantId,
    string OperationCode,
    decimal? ProviderReportedCost,
    string? ProviderReportedCurrency,
    decimal? BillingChargedCredits,
    string BillingPolicyVersion,
    bool IsCachedResponse);

// Per-AI-model-execution summary. Captures all provider attempts made for a single
// IAiModelExecutionService.ExecuteAsync call, plus aggregated token usage.
public sealed record AiExecutionTrace(
    string CorrelationId,
    Guid TenantId,
    AiWorkloadKind Workload,
    IReadOnlyCollection<ProviderLatency> ProviderAttempts,
    AiExecutionStatus FinalStatus,
    int TotalAttempts,
    TimeSpan TotalDuration,
    TokenUsage? FinalTokenUsage);

// Top-level workflow summary. Connects all sub-traces for a single AI facade request:
// provider attempts, tool executions, token usage, and billing cost outcome.
public sealed record WorkflowTelemetry(
    string CorrelationId,
    Guid TenantId,
    string WorkflowName,
    bool Succeeded,
    TimeSpan TotalDuration,
    string? DetectedIntent,
    string? SelectedTool,
    AiErrorCategory? ErrorCategory,
    string? ErrorCode,
    IReadOnlyCollection<ProviderLatency> ProviderAttempts,
    IReadOnlyCollection<ToolExecutionTrace> ToolExecutions,
    TokenUsage? AggregatedTokenUsage,
    CostTelemetry? CostOutcome,
    DateTimeOffset StartedAt);

// Higher-level telemetry sink for workflow-level observability.
// IAiExecutionTelemetrySink handles per-provider-attempt recording inside the model
// execution service; this interface handles workflow-level and tool-level recording
// from the AI facade orchestration layer (delivered by story 007).
public interface IAiWorkflowTelemetrySink
{
    Task RecordWorkflowAsync(WorkflowTelemetry telemetry, CancellationToken cancellationToken);

    Task RecordToolExecutionAsync(ToolExecutionTrace trace, CancellationToken cancellationToken);

    // Policy is evaluated by the caller before invoking this method. The sink applies
    // an additional defensive check: content fields must be null when policy disallows capture.
    Task RecordPromptAsync(PromptTrace trace, PromptRedactionPolicy policy, CancellationToken cancellationToken);

    Task RecordAiExecutionAsync(AiExecutionTrace trace, CancellationToken cancellationToken);
}
