namespace FinancialCopilot.Domain.Financial.Metrics;

public enum MetricAliasSource
{
    ManualSeed,
    AutoLearned,
    AdminApproved
}

public enum MetricAliasStatus
{
    Active,
    Disabled
}

public enum MetricAliasCandidateStatus
{
    Pending,
    AutoApproved,
    NeedsReview,
    Approved,
    Rejected
}

public sealed record DynamicMetricAlias(
    Guid Id,
    string Expression,
    string NormalizedExpression,
    string Language,
    MetricCode MetricCode,
    string MetricVersion,
    MetricAliasSource Source,
    MetricAliasStatus Status,
    decimal ConfidenceScore,
    int FrequencyCount,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBy,
    DateTimeOffset? DisabledAt,
    string? DisabledBy,
    string? DisableReason);

public sealed record MetricAliasCandidate(
    Guid Id,
    string Expression,
    string NormalizedExpression,
    string Language,
    MetricCode SuggestedMetricCode,
    string? SuggestedMetricVersion,
    MetricAliasCandidateStatus Status,
    decimal ConfidenceScore,
    int FrequencyCount,
    int DistinctActorCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? EvidenceExamplesJson,
    string? RejectionReason,
    Guid? PromotedAliasId);
