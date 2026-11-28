namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Standalone valuation gauge cards. <see cref="Enabled"/> is off by default; turning it off stops new cards and the
/// symbol-menu shortcut while stored cards stay readable from history.
/// </summary>
public sealed class ValuationGaugeCardsOptions
{
    public const string SectionName = "ValuationGaugeCards";

    public bool Enabled { get; set; }

    /// <summary>A vendor snapshot older than this is stale: its values are suppressed in new answers.</summary>
    public int MaxFetchAgeHours { get; set; } = 48;

    /// <summary>Budget for the Feature 148 snapshot read.</summary>
    public int ReadTimeoutMilliseconds { get; set; } = 2000;

    /// <summary>Budget for the canonical market quote used by the equilibrium needle.</summary>
    public int QuoteTimeoutMilliseconds { get; set; } = 1500;

    /// <summary>
    /// Tehran time ("HH:mm") from which the equilibrium price is read from today's daily trade row when it exists; before
    /// it, today's latest intraday trade is used (change «equilibrium-intraday-price», OQ-187).
    /// </summary>
    public string DailyPriceReadyTehran { get; set; } = "19:00";

    /// <summary>Decimals of the day-count percentages (largest-remainder rounding to exactly 100).</summary>
    public int PercentageDecimals { get; set; } = 1;

    /// <summary>Sapio's forward P/E and P/S estimate on the ratio cards; off by default.</summary>
    public ValuationGaugeForwardOptions ForwardEstimate { get; set; } = new();

    /// <summary>The provider's own forward P/E and P/S on the ratio cards; on by default (OQ-163).</summary>
    public ValuationGaugeProviderForwardOptions ProviderForward { get; set; } = new();

    public bool IsValid(out string error)
    {
        error = MaxFetchAgeHours is < 1 or > 720 ? "MaxFetchAgeHours must be 1-720."
            : ReadTimeoutMilliseconds is < 100 or > 30000 ? "ReadTimeoutMilliseconds must be 100-30000."
            : QuoteTimeoutMilliseconds is < 100 or > 30000 ? "QuoteTimeoutMilliseconds must be 100-30000."
            : !ValuationGaugeQuoteSelection.TryParseTehranTime(DailyPriceReadyTehran, out _)
                ? "DailyPriceReadyTehran must be a time HH:mm."
            : PercentageDecimals is < 0 or > 3 ? "PercentageDecimals must be 0-3."
            : ProviderForward is null ? "ProviderForward is required."
            : ForwardEstimate is null ? "ForwardEstimate is required."
            : ForwardEstimate.Error();
        return error.Length == 0;
    }
}

/// <summary>
/// Configuration <c>ValuationGaugeCards:ProviderForward</c> (change «valuation-gauge-provider-forward»). The value comes
/// with the gauge snapshot, so there is nothing to tune; <see cref="Enabled"/> is the kill switch and turning it off
/// restores the cards as they were before the provider value was shown.
/// </summary>
public sealed class ValuationGaugeProviderForwardOptions
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration <c>ValuationGaugeCards:ForwardEstimate</c> (change «valuation-gauge-forward-ratio»). Turning
/// <see cref="Enabled"/> off restores today's cards and is the rollback path. The defaults are the owner-approved guards
/// (OQ-145, OQ-148).
/// </summary>
public sealed class ValuationGaugeForwardOptions
{
    public bool Enabled { get; set; }

    public decimal MinGrowthFactor { get; set; } = 0.5m;

    public decimal MaxGrowthFactor { get; set; } = 4m;

    public int MaxMonthlyReportAgeDays { get; set; } = 75;

    /// <summary>
    /// Industry external ids that get no estimate. The default is set here, not in appsettings.json: the configuration
    /// binder appends to a collection, so a value in configuration adds to this default.
    /// </summary>
    public List<string> ExcludedIndustryExternalIds { get; set; } = ["31"];

    /// <summary>Own budget for the estimator input read; on overrun only the estimate is dropped.</summary>
    public int ReadTimeoutMilliseconds { get; set; } = 1000;

    public ForwardRatioThresholds Thresholds() =>
        new(MinGrowthFactor, MaxGrowthFactor, MaxMonthlyReportAgeDays, ExcludedIndustryExternalIds.ToHashSet(StringComparer.Ordinal));

    internal string Error() =>
        MinGrowthFactor <= 0m || MinGrowthFactor >= MaxGrowthFactor || MaxGrowthFactor > 20m
            ? "ForwardEstimate growth factors must satisfy 0 < MinGrowthFactor < MaxGrowthFactor <= 20."
            : MaxMonthlyReportAgeDays is < 1 or > 365 ? "ForwardEstimate:MaxMonthlyReportAgeDays must be 1-365."
            : ReadTimeoutMilliseconds is < 100 or > 30000 ? "ForwardEstimate:ReadTimeoutMilliseconds must be 100-30000."
            : ExcludedIndustryExternalIds is null || ExcludedIndustryExternalIds.Any(string.IsNullOrWhiteSpace)
                ? "ForwardEstimate:ExcludedIndustryExternalIds must not contain empty values."
            : string.Empty;
}
