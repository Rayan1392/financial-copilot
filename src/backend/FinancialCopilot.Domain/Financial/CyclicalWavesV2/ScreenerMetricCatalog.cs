namespace FinancialCopilot.Domain.Financial.CyclicalWavesV2;

public enum ScreenerMetricCategory
{
    MonthlySales,
    QuarterlySales,
    Profit,
    ProfitMargin,
    Valuation
}

public enum ScreenerMetricUnit
{
    Irr,
    Percent,
    Ratio
}

/// <summary>
/// One of the 29 financial fields of the CyclicalWaves v2 screener
/// (<c>GET /custom-filtering/ticker/{ticker}</c>, contract tahlilapp-api-client.pdf §3.2).
/// </summary>
public sealed record ScreenerMetricDefinition(
    short Id,
    string FieldKey,
    string NameEn,
    string NameFa,
    ScreenerMetricCategory Category,
    ScreenerMetricUnit Unit)
{
    /// <summary>The provider sends <c>pe = 0</c> when P/E cannot be computed (for example negative earnings).</summary>
    public bool ZeroMeansNotComputable => FieldKey == ScreenerMetricCatalog.PriceToEarnings;
}

/// <summary>
/// Single owner of the CyclicalWaves v2 screener field catalog. Ids are stable database keys
/// (seeded into <c>cyclicalwaves."MetricDefinitions"</c>) and must never be renumbered.
/// </summary>
public static class ScreenerMetricCatalog
{
    public const string LastQuarterSale = "lastQuarterSale";
    public const string PenultimateQuarterSale = "penultimateQuarterSale";
    public const string LastYearSameQuarterSale = "lastYearSameQuarterSale";
    public const string Average4QuarterSale = "average4QuarterSale";
    public const string LastQuarterNetProfit = "lastQuarterNetProfit";
    public const string PenultimateQuarterNetProfit = "penultimateQuarterNetProfit";
    public const string LastYearSameQuarterNetProfit = "lastYearSameQuarterNetProfit";
    public const string LastQuarterGrossProfit = "lastQuarterGrossProfit";
    public const string PenultimateQuarterGrossProfit = "penultimateQuarterGrossProfit";
    public const string LastYearSameQuarterGrossProfit = "lastYearSameQuarterGrossProfit";
    public const string LastQuarterOperatingProfit = "lastQuarterOperatingProfit";
    public const string PenultimateQuarterOperatingProfit = "penultimateQuarterOperatingProfit";
    public const string LastYearSameQuarterOperatingProfit = "lastYearSameQuarterOperatingProfit";
    public const string LastQuarterNetProfitMargin = "lastQuarterNetProfitMargin";
    public const string PenultimateQuarterNetProfitMargin = "penultimateQuarterNetProfitMargin";
    public const string LastYearSameQuarterNetProfitMargin = "lastYearSameQuarterNetProfitMargin";
    public const string LastQuarterGrossProfitMargin = "lastQuarterGrossProfitMargin";
    public const string PenultimateQuarterGrossProfitMargin = "penultimateQuarterGrossProfitMargin";
    public const string LastYearSameQuarterGrossProfitMargin = "lastYearSameQuarterGrossProfitMargin";
    public const string LastQuarterOperatingProfitMargin = "lastQuarterOperatingProfitMargin";
    public const string PenultimateQuarterOperatingProfitMargin = "penultimateQuarterOperatingProfitMargin";
    public const string LastYearSameQuarterOperatingProfitMargin = "lastYearSameQuarterOperatingProfitMargin";
    public const string Average12MonthSale = "average12MonthSale";
    public const string LastYearAverage12MonthSale = "lastYearAverage12MonthSale";
    public const string LastMonthSale = "lastMonthSale";
    public const string PenultimateMonthSale = "penultimateMonthSale";
    public const string LastYearSameMonthSale = "lastYearSameMonthSale";
    public const string PriceToEarnings = "pe";
    public const string PriceToSales = "ps";

    public static IReadOnlyList<ScreenerMetricDefinition> All { get; } =
    [
        new(1, LastQuarterSale, "Last quarter sales", "فروش آخرین فصل مالی", ScreenerMetricCategory.QuarterlySales, ScreenerMetricUnit.Irr),
        new(2, PenultimateQuarterSale, "Penultimate quarter sales", "فروش فصل ماقبل آخر", ScreenerMetricCategory.QuarterlySales, ScreenerMetricUnit.Irr),
        new(3, LastYearSameQuarterSale, "Same quarter last year sales", "فروش فصل مشابه سال قبل", ScreenerMetricCategory.QuarterlySales, ScreenerMetricUnit.Irr),
        new(4, Average4QuarterSale, "Average sales of the four quarters before the last quarter", "میانگین فروش چهار فصل پیش از فصل آخر", ScreenerMetricCategory.QuarterlySales, ScreenerMetricUnit.Irr),
        new(5, LastQuarterNetProfit, "Last quarter net profit", "سود خالص آخرین فصل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(6, PenultimateQuarterNetProfit, "Penultimate quarter net profit", "سود خالص فصل ماقبل آخر", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(7, LastYearSameQuarterNetProfit, "Same quarter last year net profit", "سود خالص فصل مشابه سال قبل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(8, LastQuarterGrossProfit, "Last quarter gross profit", "سود ناخالص آخرین فصل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(9, PenultimateQuarterGrossProfit, "Penultimate quarter gross profit", "سود ناخالص فصل ماقبل آخر", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(10, LastYearSameQuarterGrossProfit, "Same quarter last year gross profit", "سود ناخالص فصل مشابه سال قبل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(11, LastQuarterOperatingProfit, "Last quarter operating profit", "سود عملیاتی آخرین فصل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(12, PenultimateQuarterOperatingProfit, "Penultimate quarter operating profit", "سود عملیاتی فصل ماقبل آخر", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(13, LastYearSameQuarterOperatingProfit, "Same quarter last year operating profit", "سود عملیاتی فصل مشابه سال قبل", ScreenerMetricCategory.Profit, ScreenerMetricUnit.Irr),
        new(14, LastQuarterNetProfitMargin, "Last quarter net profit margin", "حاشیه سود خالص آخرین فصل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(15, PenultimateQuarterNetProfitMargin, "Penultimate quarter net profit margin", "حاشیه سود خالص فصل ماقبل آخر", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(16, LastYearSameQuarterNetProfitMargin, "Same quarter last year net profit margin", "حاشیه سود خالص فصل مشابه سال قبل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(17, LastQuarterGrossProfitMargin, "Last quarter gross profit margin", "حاشیه سود ناخالص آخرین فصل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(18, PenultimateQuarterGrossProfitMargin, "Penultimate quarter gross profit margin", "حاشیه سود ناخالص فصل ماقبل آخر", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(19, LastYearSameQuarterGrossProfitMargin, "Same quarter last year gross profit margin", "حاشیه سود ناخالص فصل مشابه سال قبل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(20, LastQuarterOperatingProfitMargin, "Last quarter operating profit margin", "حاشیه سود عملیاتی آخرین فصل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(21, PenultimateQuarterOperatingProfitMargin, "Penultimate quarter operating profit margin", "حاشیه سود عملیاتی فصل ماقبل آخر", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(22, LastYearSameQuarterOperatingProfitMargin, "Same quarter last year operating profit margin", "حاشیه سود عملیاتی فصل مشابه سال قبل", ScreenerMetricCategory.ProfitMargin, ScreenerMetricUnit.Percent),
        new(23, Average12MonthSale, "Average monthly sales over 12 months, excluding the latest month", "میانگین فروش ۱۲ ماه، بدون احتساب تازه‌ترین ماه", ScreenerMetricCategory.MonthlySales, ScreenerMetricUnit.Irr),
        new(24, LastYearAverage12MonthSale, "Average monthly sales of the previous fiscal year", "میانگین فروش ماهانه سال مالی قبل", ScreenerMetricCategory.MonthlySales, ScreenerMetricUnit.Irr),
        new(25, LastMonthSale, "Last month sales", "فروش آخرین ماه", ScreenerMetricCategory.MonthlySales, ScreenerMetricUnit.Irr),
        new(26, PenultimateMonthSale, "Penultimate month sales", "فروش ماه ماقبل آخر", ScreenerMetricCategory.MonthlySales, ScreenerMetricUnit.Irr),
        new(27, LastYearSameMonthSale, "Same month last year sales", "فروش ماه مشابه سال قبل", ScreenerMetricCategory.MonthlySales, ScreenerMetricUnit.Irr),
        new(28, PriceToEarnings, "Price to earnings (P/E)", "نسبت قیمت به سود (P/E)", ScreenerMetricCategory.Valuation, ScreenerMetricUnit.Ratio),
        new(29, PriceToSales, "Price to sales (P/S)", "نسبت قیمت به فروش (P/S)", ScreenerMetricCategory.Valuation, ScreenerMetricUnit.Ratio)
    ];

    private static readonly IReadOnlyDictionary<string, ScreenerMetricDefinition> ByKey =
        All.ToDictionary(definition => definition.FieldKey, StringComparer.Ordinal);

    public static ScreenerMetricDefinition Get(string fieldKey) =>
        ByKey.TryGetValue(fieldKey, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(fieldKey), fieldKey, "Unknown CyclicalWaves v2 screener field.");

    public static bool TryGet(string fieldKey, out ScreenerMetricDefinition definition) =>
        ByKey.TryGetValue(fieldKey, out definition!);
}

/// <summary>Latest-state change detection for screener values: <c>null</c> equals <c>null</c>, decimals compare by value.</summary>
public static class ScreenerValueComparer
{
    public static bool AreEqual(decimal? stored, decimal? incoming) =>
        stored.HasValue == incoming.HasValue && (!stored.HasValue || stored.Value == incoming!.Value);
}
