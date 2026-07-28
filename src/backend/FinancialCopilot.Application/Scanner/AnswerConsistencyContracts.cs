namespace FinancialCopilot.Application.Scanner;

// Outcome of a single consistency evaluation. The validator never trusts LLM-authored
// numbers: it either confirms the prose is safe, or returns deterministic replacement prose.
public enum AnswerConsistencyAction
{
    // Prose contained no numeric metric claims, or every claim matched the authoritative table.
    Unchanged,

    // Prose contained a numeric metric claim that conflicted with (or was absent from) the
    // deterministic table; the prose was replaced with backend-grounded deterministic prose.
    ReplacedWithDeterministic
}

// One authoritative (symbol, metric) value extracted from the deterministic structured table.
// Value is the raw decimal; FormattedValue is the exact string rendered in the table cell.
public sealed record AuthoritativeMetricValue(
    string SymbolCode,
    string MetricCode,
    string MetricDisplayName,
    decimal? Value,
    string? FormattedValue,
    bool IsAvailable);

// A detected conflict between an LLM prose number and the authoritative table value.
// Captured for structured logging so operators can audit hallucinated/stale numbers.
public sealed record AnswerConsistencyConflict(
    string SymbolCode,
    string MetricCode,
    string? ProseValue,
    string? TableValue);

public sealed record AnswerConsistencyResult(
    AnswerConsistencyAction Action,
    string Answer,
    IReadOnlyCollection<AnswerConsistencyConflict> Conflicts);

// Deterministic prose generator for symbol-lookup answers. Prose is built ONLY from the
// structured table cells produced by ISymbolMetricLookupService — never from LLM free text.
public interface ISymbolLookupProseBuilder
{
    string Build(SymbolLookupTableResult table);
}

// Validates a candidate prose answer against the authoritative structured result before the
// answer is persisted or returned. Applies to both V1 and the Microsoft Agent Framework V2 path.
// The LLM may author wording and follow-ups, but it must not author or alter metric values.
public interface IAnswerConsistencyValidator
{
    // Validates symbol-lookup prose. If the candidate prose states a number that conflicts with
    // (or is unsupported by) the deterministic table, the prose is replaced with deterministic prose.
    AnswerConsistencyResult ValidateSymbolLookup(
        SymbolLookupTableResult table,
        string? candidateProse,
        AnswerConsistencyContext context);

    // Validates scanner prose. Scanner prose must not state a metric value that conflicts with the
    // authoritative scanner table. Counts and the plan's thresholds are permitted; only an
    // unsupported metric figure triggers a safe deterministic replacement.
    AnswerConsistencyResult ValidateScanner(
        ScannerTableResult table,
        ScannerQueryPlan plan,
        string? candidateProse,
        AnswerConsistencyContext context);
}

// Diagnostic context for consistency-validation warnings. No PII; correlation/conversation only.
public sealed record AnswerConsistencyContext(
    string CorrelationId,
    Guid ConversationId,
    string OrchestrationMode,
    int WorkflowVersion);

// Application-owned port for emitting a structured warning when a numeric inconsistency is corrected.
// Infrastructure provides the concrete logging adapter; the validator stays free of logging frameworks.
public interface IAnswerConsistencyWarningSink
{
    void RecordCorrectedInconsistency(
        AnswerConsistencyContext context,
        AnswerConsistencyConflict conflict);
}
