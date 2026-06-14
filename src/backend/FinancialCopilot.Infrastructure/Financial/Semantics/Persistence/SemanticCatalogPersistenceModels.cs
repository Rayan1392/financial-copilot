namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;

public sealed class FinancialMetricDefinitionRow
{
    public string MetricCode { get; set; } = string.Empty;

    public string MetricVersion { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string UnitCode { get; set; } = string.Empty;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }
}

public sealed class MetricAliasRow
{
    public long Id { get; set; }

    public string Expression { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string MetricCode { get; set; } = string.Empty;

    public string MetricVersion { get; set; } = string.Empty;

    public string? ComparisonQualifier { get; set; }
}

public sealed class MetricCalculationPolicyRow
{
    public string MetricCode { get; set; } = string.Empty;

    public string PolicyVersion { get; set; } = string.Empty;

    public string DefinitionVersion { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string? Comparison { get; set; }

    public string MissingDataPolicy { get; set; } = string.Empty;

    public string? FormulaIdentifier { get; set; }

    public string? FormulaDescription { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }
}

public sealed class MetricDependencyRow
{
    public long Id { get; set; }

    public string MetricCode { get; set; } = string.Empty;

    public string MetricVersion { get; set; } = string.Empty;

    public string DependencyMetricCode { get; set; } = string.Empty;

    public string? RequiredDefinitionVersion { get; set; }

    public bool Required { get; set; }
}

public sealed class DynamicMetricAliasRow
{
    public Guid Id { get; set; }

    public string Expression { get; set; } = string.Empty;

    public string NormalizedExpression { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string MetricCode { get; set; } = string.Empty;

    public string MetricVersion { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal ConfidenceScore { get; set; }

    public int FrequencyCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? DisabledAt { get; set; }

    public string? DisabledBy { get; set; }

    public string? DisableReason { get; set; }
}

public sealed class MetricAliasCandidateRow
{
    public Guid Id { get; set; }

    public string Expression { get; set; } = string.Empty;

    public string NormalizedExpression { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string SuggestedMetricCode { get; set; } = string.Empty;

    public string? SuggestedMetricVersion { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal ConfidenceScore { get; set; }

    public int FrequencyCount { get; set; }

    public int DistinctActorCount { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public string? EvidenceExamplesJson { get; set; }

    public string? RejectionReason { get; set; }

    public Guid? PromotedAliasId { get; set; }
}
