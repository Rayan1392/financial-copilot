namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Governed mapping from <c>FinancialRatioItems.Id</c> to the canonical <c>MetricCode</c> and
/// display unit for that ratio. Only curated entries are present; unmapped ids are ignored.
/// <para>
/// <b>Current ratio duplicate:</b> CodalDB contains two catalog entries for Current ratio —
/// <c>Id=65</c> (used here) and <c>Id=41066</c> (unused). Id 65 was selected for better row
/// coverage; if a future data audit shows 41066 is preferred, swap the key.
/// </para>
/// <para>
/// <b>Percentage encoding:</b> CodalDB stores percentage-type ratios (ROE, ROA, margins) in
/// percent scale (e.g. 18.5 = 18.5%), not fraction scale (0.185). Values are persisted as-is
/// because the platform's <c>Percentage</c> <see cref="MetricUnit"/> represents percent-scale
/// values throughout the domain. No normalization multiplier is applied.
/// </para>
/// </summary>
public static class CodalDbRatioItemMap
{
    /// <summary>Maps <c>FinancialRatioItems.Id</c> → <c>(MetricCode, UnitKey)</c>.</summary>
    public static readonly IReadOnlyDictionary<int, (string MetricCode, string UnitKey)> RatioIdToMetric =
        new Dictionary<int, (string, string)>
        {
            [65]    = ("CURRENT_RATIO",                 "Ratio"),
            [8191]  = ("QUICK_RATIO",                   "Ratio"),
            [4069]  = ("NET_WORKING_CAPITAL",            "Amount"),
            [6901]  = ("COMPREHENSIVE_LIQUIDITY_INDEX",  "Ratio"),
            [4071]  = ("CURRENT_ASSETS_TO_TOTAL_ASSETS", "Ratio"),
            [41006] = ("CURRENT_DEBT_TO_TOTAL_ASSETS",   "Ratio"),
            [4100]  = ("ASSET_TURNOVER",                 "Ratio"),
            [41067] = ("TANGIBLE_FIXED_ASSETS_TURNOVER", "Ratio"),
            [20706] = ("OPERATING_ASSETS_RATIO",         "Ratio"),
            [4106]  = ("AVERAGE_COLLECTION_PERIOD",      "Days"),
            [4136]  = ("RETURN_ON_ASSETS",               "Percentage"),
            [4138]  = ("RETURN_ON_EQUITY",               "Percentage"),
            [4139]  = ("RETURN_ON_INVESTMENT",           "Percentage"),
            [4140]  = ("NET_RETURN_ON_WORKING_CAPITAL",  "Percentage"),
            [4135]  = ("NET_PROFIT_MARGIN",              "Percentage"),
            [4117]  = ("DEBT_TO_EQUITY",                         "Ratio"),

            // Vendor-precomputed growth rates (spec 026). Distinct codes from engine-derived growth.
            [6902]  = ("SALES_GROWTH_RATE",                      "Percentage"),
            [6903]  = ("NET_PROFIT_GROWTH_RATE",                 "Percentage"),
            [8092]  = ("EQUITY_GROWTH_RATE",                     "Percentage"),
            [6904]  = ("TOTAL_ASSETS_GROWTH_RATE",               "Percentage"),
            [8091]  = ("TOTAL_DEBT_GROWTH_RATE",                 "Percentage"),
            [6905]  = ("TANGIBLE_FIXED_ASSETS_GROWTH_RATE",      "Percentage"),
        };

    /// <summary>The complete set of mapped ratio item ids, used to filter SQL queries.</summary>
    public static readonly IReadOnlyCollection<int> MappedItemIds = RatioIdToMetric.Keys.ToArray();

    public const string CalculationPolicyVersion = "codal-ratio-source-v1";
}
