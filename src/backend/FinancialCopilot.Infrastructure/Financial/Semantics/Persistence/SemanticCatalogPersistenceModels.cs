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
