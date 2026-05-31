using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public sealed class PercentageGrowthMetricCalculator(
    MetricCode metricCode,
    MetricCode sourceMetricCode) : IFinancialMetricCalculator
{
    public MetricCode MetricCode { get; } = metricCode;

    public Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken)
    {
        var inputs = context.Inputs.Where(input => input.Code == sourceMetricCode).ToArray();
        var current = inputs.SingleOrDefault(input => input.Period == context.EffectivePeriod);
        var comparisonPeriod = context.Policy.Comparison is null
            ? null
            : new PeriodComparisonPolicy().GetComparisonPeriod(context.EffectivePeriod, context.Policy.Comparison.Value);
        var prior = inputs.SingleOrDefault(input => input.Period == comparisonPeriod);
        decimal? value = current?.Value is not null && prior?.Value is not null && prior.Value != 0
            ? (current.Value.Value - prior.Value.Value) / Math.Abs(prior.Value.Value) * 100m
            : null;

        return Task.FromResult(MetricCalculationResultFactory.Create(context, value, inputs));
    }
}

public sealed class TrailingTwelveMonthSumMetricCalculator(
    MetricCode metricCode,
    MetricCode sourceMetricCode,
    int requiredObservationCount) : IFinancialMetricCalculator
{
    public MetricCode MetricCode { get; } = metricCode;

    public Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken)
    {
        var inputs = context.Inputs
            .Where(input => input.Code == sourceMetricCode)
            .OrderBy(input => input.Period.EndDate)
            .ToArray();
        decimal? value = inputs.Length == requiredObservationCount && inputs.All(input => input.Value is not null)
            ? inputs.Sum(input => input.Value!.Value)
            : null;

        return Task.FromResult(MetricCalculationResultFactory.Create(context, value, inputs));
    }
}

public sealed class EarningsPerShareMetricCalculator(
    MetricCode metricCode,
    MetricCode earningsMetricCode,
    MetricCode sharesMetricCode) : IFinancialMetricCalculator
{
    public MetricCode MetricCode { get; } = metricCode;

    public Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken)
    {
        var earnings = context.Inputs.SingleOrDefault(input => input.Code == earningsMetricCode);
        var shares = context.Inputs.SingleOrDefault(input => input.Code == sharesMetricCode);
        decimal? value = earnings?.Value is not null && shares?.Value > 0
            ? earnings.Value.Value / shares.Value.Value
            : null;

        return Task.FromResult(MetricCalculationResultFactory.Create(context, value, context.Inputs));
    }
}

public sealed class ValuationRatioMetricCalculator(
    MetricCode metricCode,
    MetricCode numeratorMetricCode,
    MetricCode denominatorMetricCode) : IFinancialMetricCalculator
{
    public MetricCode MetricCode { get; } = metricCode;

    public Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken)
    {
        var numerator = context.Inputs.SingleOrDefault(input => input.Code == numeratorMetricCode);
        var denominator = context.Inputs.SingleOrDefault(input => input.Code == denominatorMetricCode);
        decimal? value = numerator?.Value is not null && denominator?.Value > 0
            ? numerator.Value.Value / denominator.Value.Value
            : null;

        return Task.FromResult(MetricCalculationResultFactory.Create(context, value, context.Inputs));
    }
}

/// <summary>
/// Calculates a metric as the sum of several component metrics for the <c>EffectivePeriod</c>.
/// All components must have a non-null value for the same period; otherwise the result is null
/// (MissingData warning). Typical use: EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX.
/// </summary>
public sealed class AdditiveCompositeMetricCalculator(
    MetricCode metricCode,
    IReadOnlyCollection<MetricCode> componentMetricCodes) : IFinancialMetricCalculator
{
    public MetricCode MetricCode { get; } = metricCode;

    public Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken)
    {
        var components = componentMetricCodes
            .Select(code => context.Inputs.SingleOrDefault(
                input => input.Code == code && input.Period == context.EffectivePeriod))
            .ToArray();

        decimal? value = components.All(c => c?.Value is not null)
            ? components.Sum(c => c!.Value!.Value)
            : null;

        var used = components.Where(c => c is not null).Cast<MetricInputObservation>().ToArray();
        return Task.FromResult(MetricCalculationResultFactory.Create(context, value, used));
    }
}

internal static class MetricCalculationResultFactory
{
    public static MetricCalculationResult Create(
        MetricCalculationContext context,
        decimal? value,
        IReadOnlyCollection<MetricInputObservation> dependencies)
    {
        var sourceEvidence = dependencies.SelectMany(dependency => dependency.SourceEvidence).ToArray();
        var observedAt = sourceEvidence.Length == 0
            ? DateTimeOffset.UnixEpoch
            : sourceEvidence.Max(evidence => evidence.SourceObservedAt);
        var synchronizedAt = sourceEvidence.Length == 0
            ? DateTimeOffset.UnixEpoch
            : sourceEvidence.Max(evidence => evidence.LastSynchronizedAt);
        var warnings = value is null
            ? [new FinancialDataWarning(FinancialDataWarningCode.MissingData, "Required metric inputs are missing or contain an invalid denominator.")]
            : Array.Empty<FinancialDataWarning>();

        return new MetricCalculationResult(
            context.Definition.Code,
            context.Definition.Version,
            context.Policy.Version,
            context.EffectivePeriod,
            value,
            context.Definition.Unit,
            new FinancialObservationQuality(observedAt, synchronizedAt, warnings),
            dependencies,
            sourceEvidence);
    }
}
