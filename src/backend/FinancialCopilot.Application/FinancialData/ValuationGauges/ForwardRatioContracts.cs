namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

// Sapio forward P/E and P/S estimate (Feature 159 change «valuation-gauge-forward-ratio», OQ-136). The estimate is
// Sapio's own, labelled «(برآورد ساپیو)»; it is never attributed to the gauge provider and never decides the label.

/// <summary>One month of sales from an accepted monthly activity report (the month itself), in rials.</summary>
public sealed record ForwardMonthlySales(DateOnly MonthEnd, decimal Sales);

/// <summary>
/// One stored income statement's discrete-quarter revenue and net profit, in rials. Either value is null when the
/// statement has no discrete-quarter figure (it is never filled from year-to-date values).
/// </summary>
public sealed record ForwardQuarter(DateOnly PeriodEnd, decimal? Revenue, decimal? NetProfit);

/// <summary>Estimator inputs for one security. Both series are ordered oldest first.</summary>
public sealed record ForwardRatioInputs(
    IReadOnlyList<ForwardMonthlySales> MonthlySales,
    IReadOnlyList<ForwardQuarter> Quarters,
    string? IndustryExternalId);

/// <summary>Owner-approved guard thresholds (OQ-145, OQ-148), taken from configuration.</summary>
public sealed record ForwardRatioThresholds(
    decimal MinGrowthFactor,
    decimal MaxGrowthFactor,
    int MaxMonthlyReportAgeDays,
    IReadOnlyCollection<string> ExcludedIndustryExternalIds);

/// <summary>
/// Estimator output. Each ratio is null with its own reason code when it cannot be estimated; the forward P/S can exist
/// while the forward P/E does not. The figures behind the ratios are present once the estimator has computed them.
/// </summary>
public sealed record ForwardRatioEstimate(
    decimal? ForwardPs,
    string PsReasonCode,
    decimal? ForwardPe,
    string PeReasonCode,
    decimal? GrowthFactor = null,
    decimal? ForecastSales = null,
    decimal? Margin = null,
    DateOnly? LatestSalesMonthEnd = null,
    DateOnly? LatestQuarterEnd = null)
{
    public static ForwardRatioEstimate None(string reasonCode) => new(null, reasonCode, null, reasonCode);
}

/// <summary>Low-cardinality reasons for "no forward estimate", used in telemetry and tests.</summary>
public static class ForwardRatioReasonCodes
{
    public const string None = "none";
    public const string ExcludedIndustry = "excluded_industry";
    public const string InsufficientMonthlySales = "insufficient_monthly_sales";
    public const string StaleMonthlySales = "stale_monthly_sales";
    public const string NoComparisonSales = "no_comparison_sales";
    public const string GrowthOutOfRange = "growth_out_of_range";
    public const string InsufficientQuarters = "insufficient_quarters";
    public const string NoCurrentValue = "no_current_value";
    public const string NoPositiveEarnings = "no_positive_earnings";
    public const string NonPositiveMargin = "non_positive_margin";

    // Reasons decided by the use case, before or around the estimator.
    public const string CardNotDisplayable = "card_not_displayable";
    public const string AxisNotRenderable = "axis_not_renderable";
    public const string CompanyUnmatched = "company_unmatched";
    public const string ReadTimedOut = "read_timed_out";
    public const string ReadFailed = "read_failed";
}

/// <summary>
/// Read-only estimator inputs for the security whose gauge was matched to <paramref name="vendorTicker"/>. Returns null
/// when the ticker or the monthly-report company cannot be matched uniquely. It translates rows and decides nothing.
/// </summary>
public interface IForwardRatioInputsReader
{
    Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity security, string vendorTicker, CancellationToken cancellationToken);
}
