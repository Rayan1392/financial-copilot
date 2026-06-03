namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public static class NadpcoApiStatementItemMaps
{
    /// <summary>
    /// NADPCO income-statement itemID -> canonical MetricCode. Source amounts are retained as
    /// provided by NADPCO; current samples report amountUnit = "N/A", documented as million Rials.
    /// </summary>
    public static IReadOnlyDictionary<int, string> IncomeItemIdToMetricCode { get; } =
        new Dictionary<int, string>
        {
            [15]  = "REVENUE",
            [300] = "TOTAL_REVENUE",
            [143] = "NET_PROFIT",
            [140] = "OPERATING_PROFIT",
            [139] = "GROSS_PROFIT",
            [160] = "EPS",
            [168] = "EPS_CONSOLIDATED",
            [12]  = "FINANCE_COSTS",
            [336] = "INCOME_TAX"
        };

    /// <summary>NADPCO balance-sheet itemID -> canonical MetricCode.</summary>
    public static IReadOnlyDictionary<int, string> BalanceSheetItemIdToMetricCode { get; } =
        new Dictionary<int, string>
        {
            // Verified against NADPCO/Codal-style balance mappings used by the product. The
            // attached sample demonstrates endpoint shape but not the equity/capital rows.
            [147] = "TOTAL_EQUITY",
            [188] = "CAPITAL"
        };

    /// <summary>NADPCO cash-flow itemID -> canonical MetricCode.</summary>
    public static IReadOnlyDictionary<int, string> CashFlowItemIdToMetricCode { get; } =
        new Dictionary<int, string>
        {
            // The attached sample identifies item 1 as operating cash flow.
            [1] = "OPERATING_CASH_FLOW"
        };
}
