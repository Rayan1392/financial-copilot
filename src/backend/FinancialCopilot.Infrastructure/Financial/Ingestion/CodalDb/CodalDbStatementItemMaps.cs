namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Governed, data-driven mapping from CodalDB catalog ItemIds to canonical MetricCodes.
/// Adding a new metric is a dictionary entry here, not a code branch in the normalizer.
/// ItemIds are verified against the CodalDB IncomeItems / BalanceSheetItems catalogs.
/// </summary>
public static class CodalDbStatementItemMaps
{
    /// <summary>CodalDB IncomeItems.ItemId → canonical MetricCode (Phase 1 curated set).</summary>
    public static IReadOnlyDictionary<int, string> IncomeItemIdToMetricCode { get; } =
        new Dictionary<int, string>
        {
            [15]  = "REVENUE",
            [300] = "TOTAL_REVENUE",
            [143] = "NET_PROFIT",       // CodalDB "Net income" → reuses existing NET_PROFIT growth calculators
            [140] = "OPERATING_PROFIT",
            [139] = "GROSS_PROFIT",
            [160] = "EPS",
            [168] = "EPS_CONSOLIDATED",
            [12]  = "FINANCE_COSTS",    // EBIT input (026): EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX
            [13]  = "INCOME_TAX",       // EBIT input (026)
        };

    /// <summary>CodalDB BalanceSheetItems.ItemId → canonical MetricCode (Phase 1 curated set).</summary>
    public static IReadOnlyDictionary<int, string> BalanceItemIdToMetricCode { get; } =
        new Dictionary<int, string>
        {
            [147] = "TOTAL_EQUITY",
            [188] = "CAPITAL",          // "Paid capital"; richer capital data (Capitals table) is out of scope
        };
}
