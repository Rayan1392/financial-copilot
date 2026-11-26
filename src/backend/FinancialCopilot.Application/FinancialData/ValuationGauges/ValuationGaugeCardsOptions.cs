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

    /// <summary>Decimals of the day-count percentages (largest-remainder rounding to exactly 100).</summary>
    public int PercentageDecimals { get; set; } = 1;

    public bool IsValid(out string error)
    {
        error = MaxFetchAgeHours is < 1 or > 720 ? "MaxFetchAgeHours must be 1-720."
            : ReadTimeoutMilliseconds is < 100 or > 30000 ? "ReadTimeoutMilliseconds must be 100-30000."
            : QuoteTimeoutMilliseconds is < 100 or > 30000 ? "QuoteTimeoutMilliseconds must be 100-30000."
            : PercentageDecimals is < 0 or > 3 ? "PercentageDecimals must be 0-3."
            : string.Empty;
        return error.Length == 0;
    }
}
