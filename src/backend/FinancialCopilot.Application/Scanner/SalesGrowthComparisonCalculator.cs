namespace FinancialCopilot.Application.Scanner;

/// <summary>A normalized monthly-sales input for one company and period.</summary>
public sealed record SalesGrowthSalesObservation
{
    public SalesGrowthSalesObservation(
        string ExternalCompanyId,
        SalesGrowthEvaluationPeriod Period,
        decimal? SalesAmount,
        string SourceName,
        string EvidenceId,
        DateTimeOffset? ObservedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(ExternalCompanyId))
        {
            throw new ArgumentException("An external company identifier is required.", nameof(ExternalCompanyId));
        }

        if (string.IsNullOrWhiteSpace(SourceName))
        {
            throw new ArgumentException("A source name is required.", nameof(SourceName));
        }

        if (string.IsNullOrWhiteSpace(EvidenceId))
        {
            throw new ArgumentException("An evidence identifier is required.", nameof(EvidenceId));
        }

        this.ExternalCompanyId = ExternalCompanyId.Trim();
        this.Period = Period;
        this.SalesAmount = SalesAmount;
        this.SourceName = SourceName.Trim();
        this.EvidenceId = EvidenceId.Trim();
        this.ObservedAtUtc = ObservedAtUtc;
    }

    public string ExternalCompanyId { get; }

    public SalesGrowthEvaluationPeriod Period { get; }

    public decimal? SalesAmount { get; }

    public string SourceName { get; }

    public string EvidenceId { get; }

    public DateTimeOffset? ObservedAtUtc { get; }
}

public enum SalesGrowthValueState
{
    Available,
    Missing,
    Invalid,
    Unusable
}

public sealed record SalesGrowthInputEvidence(
    string ExternalCompanyId,
    SalesGrowthEvaluationPeriod Period,
    decimal? SalesAmount,
    string SourceName,
    string EvidenceId,
    DateTimeOffset? ObservedAtUtc);

public sealed record SalesGrowthValue(
    decimal? Amount,
    SalesGrowthValueState State,
    SalesGrowthEvaluationPeriod? Period,
    IReadOnlyCollection<SalesGrowthEvaluationPeriod> WindowPeriods);

public sealed record SalesGrowthComparisonCalculationResult(
    string ExternalCompanyId,
    SalesGrowthComparisonBaseline Baseline,
    SalesGrowthValue Current,
    SalesGrowthValue BaselineValue,
    decimal? GrowthDifference,
    decimal? GrowthPercent,
    decimal? GrowthMultiple,
    IReadOnlyCollection<SalesGrowthInputEvidence> Evidence,
    DateTimeOffset? LatestObservedAtUtc,
    string? FreshnessSource,
    SalesGrowthPolicyVersions Policies)
{
    public bool IsUsable =>
        Current.State == SalesGrowthValueState.Available &&
        BaselineValue.State == SalesGrowthValueState.Available &&
        GrowthPercent is not null &&
        GrowthMultiple is not null;
}

public interface ISalesGrowthComparisonCalculator
{
    SalesGrowthComparisonCalculationResult Calculate(
        string externalCompanyId,
        SalesGrowthEvaluationPeriod targetPeriod,
        SalesGrowthComparisonBaseline baseline,
        IReadOnlyCollection<SalesGrowthSalesObservation> observations);
}

/// <summary>
/// Calculates sales-growth comparisons from a fixed evidence snapshot. It has
/// no provider, database, clock, or rendering dependency, so equal inputs always
/// produce equal outputs.
/// </summary>
public sealed class SalesGrowthComparisonCalculator : ISalesGrowthComparisonCalculator
{
    public SalesGrowthComparisonCalculationResult Calculate(
        string externalCompanyId,
        SalesGrowthEvaluationPeriod targetPeriod,
        SalesGrowthComparisonBaseline baseline,
        IReadOnlyCollection<SalesGrowthSalesObservation> observations)
    {
        if (string.IsNullOrWhiteSpace(externalCompanyId))
        {
            throw new ArgumentException("An external company identifier is required.", nameof(externalCompanyId));
        }

        ArgumentNullException.ThrowIfNull(observations);

        var normalizedCompanyId = externalCompanyId.Trim();
        var companyObservations = observations
            .Where(observation => string.Equals(
                observation.ExternalCompanyId,
                normalizedCompanyId,
                StringComparison.Ordinal))
            .OrderBy(observation => observation.Period.Year)
            .ThenBy(observation => observation.Period.Month)
            .ThenBy(observation => observation.EvidenceId, StringComparer.Ordinal)
            .ToArray();

        var evidence = companyObservations
            .Select(ToEvidence)
            .ToArray();

        var currentPeriod = targetPeriod;
        var currentMatches = companyObservations
            .Where(observation => observation.Period == currentPeriod)
            .ToArray();
        var current = CreateValue(currentMatches, currentPeriod, [currentPeriod], currentMatches.Length > 1, requirePositive: false);

        var baselinePeriods = ResolveBaselinePeriods(targetPeriod, baseline);
        var baselineMatches = companyObservations
            .Where(observation => baselinePeriods.Contains(observation.Period))
            .ToArray();
        var duplicateBaselinePeriods = baselineMatches
            .GroupBy(observation => observation.Period)
            .Any(group => group.Count() > 1);
        var baselineValue = CreateBaselineValue(
            baselineMatches,
            baseline,
            baselinePeriods,
            duplicateBaselinePeriods);

        decimal? difference = current.State == SalesGrowthValueState.Available &&
                         baselineValue.State == SalesGrowthValueState.Available
            ? current.Amount!.Value - baselineValue.Amount!.Value
            : null;
        decimal? growthPercent = difference is not null && baselineValue.Amount > 0m
            ? difference.Value / baselineValue.Amount.Value * 100m
            : null;
        decimal? growthMultiple = current.State == SalesGrowthValueState.Available &&
                             baselineValue.State == SalesGrowthValueState.Available &&
                             baselineValue.Amount > 0m
            ? current.Amount!.Value / baselineValue.Amount.Value
            : null;

        var latestObservation = companyObservations
            .Where(observation => observation.ObservedAtUtc is not null)
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .ThenBy(observation => observation.EvidenceId, StringComparer.Ordinal)
            .FirstOrDefault();

        return new SalesGrowthComparisonCalculationResult(
            normalizedCompanyId,
            baseline,
            current,
            baselineValue,
            difference,
            growthPercent,
            growthMultiple,
            evidence,
            latestObservation?.ObservedAtUtc,
            latestObservation?.SourceName,
            SalesGrowthPolicyVersions.V1);
    }

    private static SalesGrowthValue CreateBaselineValue(
        IReadOnlyCollection<SalesGrowthSalesObservation> matches,
        SalesGrowthComparisonBaseline baseline,
        IReadOnlyCollection<SalesGrowthEvaluationPeriod> baselinePeriods,
        bool duplicatePeriods)
    {
        if (duplicatePeriods)
        {
            return new SalesGrowthValue(null, SalesGrowthValueState.Invalid, baselinePeriods.FirstOrDefault(), baselinePeriods);
        }

        if (baseline == SalesGrowthComparisonBaseline.AveragePrevious12Months)
        {
            if (matches.Count != 12)
            {
                return new SalesGrowthValue(null, SalesGrowthValueState.Missing, null, baselinePeriods);
            }

            if (matches.Any(match => match.SalesAmount is null))
            {
                return new SalesGrowthValue(null, SalesGrowthValueState.Missing, null, baselinePeriods);
            }

            if (matches.Any(match => match.SalesAmount < 0m))
            {
                return new SalesGrowthValue(null, SalesGrowthValueState.Invalid, null, baselinePeriods);
            }

            var average = matches.Sum(match => match.SalesAmount!.Value) / 12m;
            return average <= 0m
                ? new SalesGrowthValue(average, SalesGrowthValueState.Unusable, null, baselinePeriods)
                : new SalesGrowthValue(average, SalesGrowthValueState.Available, null, baselinePeriods);
        }

        var period = baselinePeriods.Single();
        return CreateValue(matches, period, baselinePeriods, duplicatePeriods, requirePositive: true);
    }

    private static SalesGrowthValue CreateValue(
        IReadOnlyCollection<SalesGrowthSalesObservation> matches,
        SalesGrowthEvaluationPeriod period,
        IReadOnlyCollection<SalesGrowthEvaluationPeriod> windowPeriods,
        bool duplicatePeriods,
        bool requirePositive)
    {
        if (duplicatePeriods)
        {
            return new SalesGrowthValue(null, SalesGrowthValueState.Invalid, period, windowPeriods);
        }

        var match = matches.SingleOrDefault();
        if (match is null || match.SalesAmount is null)
        {
            return new SalesGrowthValue(null, SalesGrowthValueState.Missing, period, windowPeriods);
        }

        if (match.SalesAmount < 0m)
        {
            return new SalesGrowthValue(match.SalesAmount, SalesGrowthValueState.Invalid, period, windowPeriods);
        }

        return requirePositive && match.SalesAmount <= 0m
            ? new SalesGrowthValue(match.SalesAmount, SalesGrowthValueState.Unusable, period, windowPeriods)
            : new SalesGrowthValue(match.SalesAmount, SalesGrowthValueState.Available, period, windowPeriods);
    }

    private static IReadOnlyCollection<SalesGrowthEvaluationPeriod> ResolveBaselinePeriods(
        SalesGrowthEvaluationPeriod targetPeriod,
        SalesGrowthComparisonBaseline baseline) =>
        baseline switch
        {
            SalesGrowthComparisonBaseline.PreviousMonth => [Shift(targetPeriod, -1)],
            SalesGrowthComparisonBaseline.SameMonthPreviousYear => [Shift(targetPeriod, -12)],
            SalesGrowthComparisonBaseline.AveragePrevious12Months => Enumerable.Range(1, 12)
                .Select(offset => Shift(targetPeriod, -offset))
                .OrderBy(period => period.Year)
                .ThenBy(period => period.Month)
                .ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(baseline), baseline, "Unsupported sales-growth baseline.")
        };

    private static SalesGrowthEvaluationPeriod Shift(SalesGrowthEvaluationPeriod period, int months)
    {
        var shifted = period.FirstDay.AddMonths(months);
        return new SalesGrowthEvaluationPeriod(shifted.Year, shifted.Month);
    }

    private static SalesGrowthInputEvidence ToEvidence(SalesGrowthSalesObservation observation) =>
        new(
            observation.ExternalCompanyId,
            observation.Period,
            observation.SalesAmount,
            observation.SourceName,
            observation.EvidenceId,
            observation.ObservedAtUtc);
}
