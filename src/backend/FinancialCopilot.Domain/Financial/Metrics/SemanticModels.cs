using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public enum MetricCategory
{
    Profitability,
    Growth,
    Valuation,
    SalesAndProduction,
    CashFlow,
    FinancialHealth
}

public sealed record MetricUnit(string Code, string DisplayLabel);

public sealed record MetricFormula(string Identifier, string Description);

public sealed record MetricCalculator(MetricCode MetricCode, string StrategyKey);

public sealed record MetricAlias(
    string Expression,
    string Language,
    MetricCode MetricCode,
    MetricVersion MetricVersion,
    GrowthComparison? ComparisonQualifier = null);

public sealed record MetricDependency(
    MetricCode MetricCode,
    MetricVersion? RequiredDefinitionVersion,
    bool Required = true);

public sealed record FinancialMetricDefinition(
    MetricCode Code,
    MetricVersion Version,
    string DisplayName,
    string Description,
    MetricCategory Category,
    MetricUnit Unit,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    IReadOnlyCollection<FiscalPeriodType> SupportedPeriodTypes,
    IReadOnlyCollection<MetricAlias> Aliases,
    IReadOnlyCollection<MetricDependency> Dependencies,
    IReadOnlyCollection<MetricDataRequirement> DataRequirements);

public sealed record MetricResolutionContext(
    FiscalPeriodType? PeriodType = null,
    GrowthComparison? Comparison = null);

public enum MetricResolutionStatus
{
    Resolved,
    Ambiguous,
    NotFound
}

public sealed record MetricResolutionResult(
    string UserExpression,
    string Language,
    MetricResolutionStatus Status,
    IReadOnlyCollection<FinancialMetricDefinition> Candidates,
    string? ClarificationMessage = null)
{
    public FinancialMetricDefinition? ResolvedDefinition =>
        Status == MetricResolutionStatus.Resolved ? Candidates.Single() : null;
}

public sealed record MetricInputObservation(
    MetricCode Code,
    MetricVersion DefinitionVersion,
    CalculationPolicyVersion CalculationPolicyVersion,
    FiscalPeriod Period,
    decimal? Value,
    IReadOnlyCollection<FinancialSourceEvidence> SourceEvidence);

public sealed record MetricCalculationContext(
    string ExternalCompanyId,
    FinancialMetricDefinition Definition,
    MetricCalculationPolicy Policy,
    FiscalPeriod EffectivePeriod,
    IReadOnlyCollection<MetricInputObservation> Inputs);

public sealed record MetricCalculationResult(
    MetricCode MetricCode,
    MetricVersion DefinitionVersion,
    CalculationPolicyVersion CalculationPolicyVersion,
    FiscalPeriod EffectivePeriod,
    decimal? Value,
    MetricUnit Unit,
    FinancialObservationQuality Quality,
    IReadOnlyCollection<MetricInputObservation> Dependencies,
    IReadOnlyCollection<FinancialSourceEvidence> SourceEvidence);
