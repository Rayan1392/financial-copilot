using System.Text.Json.Serialization;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

public sealed class SalesGrowthScannerOptions
{
    public const string SectionName = "SalesGrowthScanner";

    public bool Enabled { get; set; } = true;

    public SalesGrowthComparisonBaseline DefaultComparisonBaseline { get; set; } =
        SalesGrowthComparisonBaseline.SameMonthPreviousYear;

    public bool AllowDefaultComparison { get; set; } = true;

    public decimal MinimumCommonPeriodCoveragePercent { get; set; } = 70m;

    public int DefaultPageSize { get; set; } = SalesGrowthScannerPlan.DefaultPageSize;

    public int MaximumPageSize { get; set; } = SalesGrowthScannerPlan.MaximumPageSize;

    public bool AllowMixedLatestPeriods { get; set; }
}

public static class SalesGrowthScannerOptionsValidation
{
    public static IReadOnlyList<string> Validate(SalesGrowthScannerOptions options)
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(options.DefaultComparisonBaseline))
        {
            errors.Add("DefaultComparisonBaseline must be a defined sales-growth baseline.");
        }

        if (options.MinimumCommonPeriodCoveragePercent is < 0m or > 100m)
        {
            errors.Add("MinimumCommonPeriodCoveragePercent must be between 0 and 100.");
        }

        if (options.MaximumPageSize is < 1 or > SalesGrowthScannerPlan.MaximumPageSize)
        {
            errors.Add($"MaximumPageSize must be between 1 and {SalesGrowthScannerPlan.MaximumPageSize}.");
        }

        if (options.DefaultPageSize is < 1 || options.DefaultPageSize > options.MaximumPageSize)
        {
            errors.Add("DefaultPageSize must be between 1 and MaximumPageSize.");
        }

        return errors;
    }
}

/// <summary>A validated calendar month used as the single scanner evaluation period.</summary>
public readonly record struct SalesGrowthEvaluationPeriod
{
    [JsonConstructor]
    public SalesGrowthEvaluationPeriod(int year, int month)
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "The evaluation year must be positive.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "The evaluation month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    [JsonIgnore]
    public bool IsValid => Year >= 1 && Month is >= 1 and <= 12;

    [JsonIgnore]
    public DateOnly FirstDay => IsValid
        ? new(Year, Month, 1)
        : throw new InvalidOperationException(
            $"Cannot create the first day for an invalid sales-growth period ({Year}, {Month}).");
}

/// <summary>
/// A normalized monthly-sales period observation. Infrastructure supplies the
/// completeness decision from its persisted/read-model rules; the selector does
/// not infer completeness from arrival time or from a provider-specific payload.
/// </summary>
public sealed record SalesGrowthPeriodObservation(
    SalesGrowthEvaluationPeriod Period,
    string ExternalCompanyId,
    bool IsComplete);

public enum SalesGrowthCommonPeriodSelectionStatus
{
    Available,
    Partial,
    Unavailable
}

public sealed record SalesGrowthCommonPeriodSelectionResult(
    SalesGrowthCommonPeriodSelectionStatus Status,
    SalesGrowthEvaluationPeriod? TargetPeriod,
    int CoverageNumerator,
    int CoverageDenominator,
    decimal CoveragePercent,
    CalculationPolicyVersion PolicyVersion,
    bool MixedPeriodsAllowed,
    string? Reason)
{
    public bool IsUsable => Status == SalesGrowthCommonPeriodSelectionStatus.Available;
}

public interface ISalesGrowthCommonEvaluationPeriodSelector
{
    SalesGrowthCommonPeriodSelectionResult Select(
        IReadOnlyCollection<SalesGrowthPeriodObservation> observations,
        int eligibleUniverseSymbolCount);
}

/// <summary>
/// Selects the newest complete monthly period whose eligible-symbol coverage
/// satisfies policy. A below-policy result is reported as Partial rather than
/// being silently promoted to an executable scanner target.
/// </summary>
public sealed class SalesGrowthCommonEvaluationPeriodSelector : ISalesGrowthCommonEvaluationPeriodSelector
{
    private readonly SalesGrowthScannerOptions _options;

    public SalesGrowthCommonEvaluationPeriodSelector(SalesGrowthScannerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var validation = SalesGrowthScannerOptionsValidation.Validate(options);
        if (validation.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", validation), nameof(options));
        }
    }

    public SalesGrowthCommonPeriodSelectionResult Select(
        IReadOnlyCollection<SalesGrowthPeriodObservation> observations,
        int eligibleUniverseSymbolCount)
    {
        ArgumentNullException.ThrowIfNull(observations);

        if (eligibleUniverseSymbolCount <= 0)
        {
            return Unavailable("No eligible symbols are available for common-period coverage.");
        }

        var candidates = observations
            .Where(observation =>
                observation.Period.IsValid &&
                observation.IsComplete &&
                !string.IsNullOrWhiteSpace(observation.ExternalCompanyId))
            .GroupBy(observation => observation.Period)
            .Select(group => new PeriodCoverage(
                group.Key,
                group.Select(observation => observation.ExternalCompanyId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                eligibleUniverseSymbolCount))
            .OrderByDescending(candidate => candidate.Period.Year)
            .ThenByDescending(candidate => candidate.Period.Month)
            .ToArray();

        if (candidates.Length == 0)
        {
            return Unavailable("No complete monthly period observations are available.");
        }

        var qualifying = candidates.FirstOrDefault(candidate =>
            candidate.CoveragePercent >= _options.MinimumCommonPeriodCoveragePercent);

        if (qualifying is not null)
        {
            return CreateResult(
                SalesGrowthCommonPeriodSelectionStatus.Available,
                qualifying,
                "The newest complete period satisfying the configured common-period coverage policy was selected.");
        }

        var bestPartial = candidates
            .OrderByDescending(candidate => candidate.CoveragePercent)
            .ThenByDescending(candidate => candidate.Period.Year)
            .ThenByDescending(candidate => candidate.Period.Month)
            .First();

        return CreateResult(
            SalesGrowthCommonPeriodSelectionStatus.Partial,
            bestPartial,
            $"No common period reached the configured minimum coverage of {_options.MinimumCommonPeriodCoveragePercent:0.##}%.");
    }

    private SalesGrowthCommonPeriodSelectionResult CreateResult(
        SalesGrowthCommonPeriodSelectionStatus status,
        PeriodCoverage coverage,
        string reason) =>
        new(
            status,
            coverage.Period,
            coverage.Numerator,
            coverage.Denominator,
            coverage.CoveragePercent,
            SalesGrowthPolicyVersions.V1.TargetPeriod,
            _options.AllowMixedLatestPeriods,
            reason);

    private SalesGrowthCommonPeriodSelectionResult Unavailable(string reason) =>
        new(
            SalesGrowthCommonPeriodSelectionStatus.Unavailable,
            null,
            0,
            0,
            0m,
            SalesGrowthPolicyVersions.V1.TargetPeriod,
            _options.AllowMixedLatestPeriods,
            reason);

    private sealed record PeriodCoverage(
        SalesGrowthEvaluationPeriod Period,
        int Numerator,
        int Denominator)
    {
        public decimal CoveragePercent => Math.Round(
            Numerator * 100m / Denominator,
            2,
            MidpointRounding.AwayFromZero);
    }
}
