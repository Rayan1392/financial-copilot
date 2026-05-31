namespace FinancialCopilot.Domain.Financial.MissingAnswer;

/// <summary>
/// Why a financial query produced no answer (or a partial one). Classified at query time using
/// signals already available to the scanner (registry membership, derived-metric presence, coverage
/// ratio, parser status). The enum is stable so admin dashboards and downstream tools can group on it.
/// </summary>
public enum MissingAnswerFeedbackClassification
{
    /// <summary>The requested metric is not registered in the semantic catalog.</summary>
    MetricGap,

    /// <summary>The metric is registered but no <c>DerivedMetrics</c> rows exist for any symbol.</summary>
    CalculationGap,

    /// <summary>The metric exists with rows, but the matched symbol count is sparse relative to the universe.</summary>
    DataCoverageGap,

    /// <summary>Required values are present but null/zero/implausible. Reserved for future classifier rules.</summary>
    DataQualityGap,

    /// <summary>Parser asked for clarification and the user did not resolve it (incomplete conversation).</summary>
    ParserLimitation,

    /// <summary>Catch-all when the miss does not match a more specific classification.</summary>
    UnknownGap
}

/// <summary>
/// Immutable record of a single missing-answer event. Persisted (with coalescing) by the feedback
/// repository so engineering can prioritize catalog expansion, data coverage, and parser fixes.
/// Contains no PII beyond the opaque <see cref="ActorId"/>.
/// </summary>
public sealed record MissingAnswerFeedback(
    Guid Id,
    string ActorId,
    string QueryText,
    string QueryHashSha256,
    MissingAnswerFeedbackClassification Classification,
    string? RequestedMetricCode,
    string? AffectedDataCodeOrName,
    int SymbolCountTotal,
    int SymbolCountMatched,
    DateTimeOffset SubmittedAt,
    DateOnly DateBucket,
    string? Context,
    int FrequencyCount,
    DateTimeOffset? ResolvedAt);
