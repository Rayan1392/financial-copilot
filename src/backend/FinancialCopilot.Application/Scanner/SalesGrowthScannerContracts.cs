using System.Text.Json.Serialization;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

/// <summary>
/// Canonical identity of Feature 116. This is a semantic use-case identifier,
/// not an LLM label and not a provider-specific route.
/// </summary>
public static class SalesGrowthSymbolScanner
{
    public const string Intent = "SalesGrowthSymbolScanner";
    public const string MetricFamily = "MonthlySales";
    public const string Objective = "ListMatchingSymbols";
}

/// <summary>The monthly-sales observation used as the comparison baseline.</summary>
public enum SalesGrowthComparisonBaseline
{
    PreviousMonth,
    SameMonthPreviousYear,
    AveragePrevious12Months
}

/// <summary>
/// The threshold interpretation for a sales-growth condition.
/// </summary>
public enum SalesGrowthThresholdKind
{
    /// <summary>CurrentSales &gt; BaselineSales.</summary>
    Positive,

    /// <summary>
    /// GrowthPercent = ((CurrentSales - BaselineSales) / BaselineSales) * 100.
    /// The threshold value is expressed in percentage points.
    /// </summary>
    Percent,

    /// <summary>
    /// GrowthMultiple = CurrentSales / BaselineSales.
    /// The threshold value is expressed as a multiple (for example, 2.0).
    /// </summary>
    Multiple
}

/// <summary>
/// Versioned semantic policies required to reproduce a sales-growth result.
/// </summary>
public sealed record SalesGrowthPolicyVersions(
    CalculationPolicyVersion TargetPeriod,
    CalculationPolicyVersion Calculation)
{
    public static SalesGrowthPolicyVersions V1 { get; } = new(
        new CalculationPolicyVersion("sales-growth-target-period-v1"),
        new CalculationPolicyVersion("sales-growth-calculation-v1"));
}

/// <summary>
/// Fully governed semantics for a Feature 116 scanner request.
/// Contains only canonical application/domain values: no SQL, provider DTO, or
/// executable user expression is allowed to cross this boundary.
/// </summary>
public sealed record SalesGrowthScannerSemantics
{
    public SalesGrowthScannerSemantics(
        SalesGrowthComparisonBaseline baseline,
        SalesGrowthThresholdKind thresholdKind,
        ConditionOperator comparisonOperator,
        decimal? thresholdValue,
        FilterOrigin origin,
        SalesGrowthPolicyVersions policies)
        : this(
            baseline,
            thresholdKind,
            comparisonOperator,
            thresholdValue,
            origin,
            policies,
            origin,
            origin)
    {
    }

    [JsonConstructor]
    public SalesGrowthScannerSemantics(
        SalesGrowthComparisonBaseline baseline,
        SalesGrowthThresholdKind thresholdKind,
        ConditionOperator comparisonOperator,
        decimal? thresholdValue,
        FilterOrigin origin,
        SalesGrowthPolicyVersions policies,
        FilterOrigin baselineOrigin,
        FilterOrigin thresholdOrigin)
    {
        if (thresholdKind == SalesGrowthThresholdKind.Positive && thresholdValue is not null)
        {
            throw new ArgumentException(
                "Positive sales growth has no numeric threshold.",
                nameof(thresholdValue));
        }

        if (thresholdKind is SalesGrowthThresholdKind.Percent or SalesGrowthThresholdKind.Multiple
            && thresholdValue is null)
        {
            throw new ArgumentNullException(
                nameof(thresholdValue),
                "Percent and multiple sales-growth thresholds require a value.");
        }

        if (thresholdKind == SalesGrowthThresholdKind.Multiple && thresholdValue <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(thresholdValue),
                "A sales-growth multiple must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(policies);

        Baseline = baseline;
        ThresholdKind = thresholdKind;
        ComparisonOperator = comparisonOperator;
        ThresholdValue = thresholdValue;
        Origin = origin;
        BaselineOrigin = baselineOrigin;
        ThresholdOrigin = thresholdOrigin;
        Policies = policies;
    }

    public SalesGrowthComparisonBaseline Baseline { get; }

    public SalesGrowthThresholdKind ThresholdKind { get; }

    public ConditionOperator ComparisonOperator { get; }

    /// <summary>
    /// Percentage points for <see cref="SalesGrowthThresholdKind.Percent"/>;
    /// current-to-baseline ratio for <see cref="SalesGrowthThresholdKind.Multiple"/>;
    /// null for <see cref="SalesGrowthThresholdKind.Positive"/>.
    /// </summary>
    public decimal? ThresholdValue { get; }

    /// <summary>Whether the interpretation was explicit, inferred, or clarified.</summary>
    public FilterOrigin Origin { get; }

    public FilterOrigin BaselineOrigin { get; }

    public FilterOrigin ThresholdOrigin { get; }

    public SalesGrowthPolicyVersions Policies { get; }

    /// <summary>
    /// The canonical positive-growth rule. It is intentionally represented as
    /// semantic data and is evaluated by the scanner execution policy later.
    /// </summary>
    public const string PositiveRule = "CurrentSales > BaselineSales";

    /// <summary>
    /// The canonical percentage formula, where 30 means 30 percentage points.
    /// </summary>
    public const string PercentageFormula =
        "((CurrentSales - BaselineSales) / BaselineSales) * 100";

    /// <summary>
    /// The canonical multiple formula. A 2.0 multiple is 100% growth.
    /// </summary>
    public const string MultipleFormula = "CurrentSales / BaselineSales";
}

public enum SalesGrowthCurrentObservationSelector
{
    LatestEligibleCompleteMonthlySales
}

public enum SalesGrowthSortKey
{
    GrowthPercent
}

public enum SalesGrowthSortDirection
{
    Descending,
    Ascending
}

public sealed record SalesGrowthSort(
    SalesGrowthSortKey Key = SalesGrowthSortKey.GrowthPercent,
    SalesGrowthSortDirection Direction = SalesGrowthSortDirection.Descending);

/// <summary>
/// Feature 116-specific, provider-neutral additions to the generic scanner
/// plan. Target period selection is optional until the common-period policy
/// resolves it; execution must never silently invent one here.
/// </summary>
public sealed record SalesGrowthScannerPlan(
    SalesGrowthScannerSemantics Semantics,
    SalesGrowthCurrentObservationSelector CurrentObservationSelector = SalesGrowthCurrentObservationSelector.LatestEligibleCompleteMonthlySales,
    ScannerUniverseScope? MarketUniverse = null,
    DateOnly? TargetCommonPeriod = null,
    SalesGrowthSort? Sort = null,
    int Page = 1,
    int PageSize = 20,
    IReadOnlyCollection<ScannerColumnRequest>? RequestedDisplayColumns = null)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;
    public const int MaximumSymbols = 5_000;

    public ScannerUniverseScope EffectiveMarketUniverse =>
        MarketUniverse ?? new ScannerUniverseScope(MaximumSymbols: MaximumSymbols);

    public SalesGrowthSort EffectiveSort => Sort ?? new SalesGrowthSort();

    public IReadOnlyCollection<ScannerColumnRequest> EffectiveRequestedDisplayColumns =>
        RequestedDisplayColumns ?? [];

    /// <summary>
    /// Creates the governed generic "sales growth" interpretation. The
    /// default baseline is explicit policy data, not an LLM-selected formula.
    /// </summary>
    public static SalesGrowthScannerPlan CreateInferredDefault(
        ScannerUniverseScope? marketUniverse = null,
        int page = 1,
        int pageSize = DefaultPageSize) =>
        new(
            new SalesGrowthScannerSemantics(
                SalesGrowthComparisonBaseline.SameMonthPreviousYear,
                SalesGrowthThresholdKind.Positive,
                ConditionOperator.GreaterThan,
                null,
                FilterOrigin.InferredDefault,
                SalesGrowthPolicyVersions.V1),
            MarketUniverse: marketUniverse,
            Page: page,
            PageSize: pageSize);
}
