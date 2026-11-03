using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using Catalog = FinancialCopilot.Domain.Financial.CyclicalWavesV2.ScreenerMetricCatalog;

namespace FinancialCopilot.Application.Scanner.CyclicalWavesV2;

public enum ScreenerOperandKind
{
    Direct,
    Growth,
    Unavailable
}

/// <summary>How one canonical metric code is answered from the CyclicalWaves v2 screener fields.</summary>
public sealed record ScreenerMetricBinding(
    string MetricCode,
    ScreenerOperandKind Kind,
    string? FieldKey,
    string? BaseFieldKey,
    ScreenerMetricUnit Unit,
    string? UnavailableReason = null)
{
    public bool IsAvailable => Kind != ScreenerOperandKind.Unavailable;

    /// <summary>Growth is always a percent; a direct binding takes the unit of its field.</summary>
    public ScreenerMetricUnit ComparisonUnit => Kind == ScreenerOperandKind.Growth ? ScreenerMetricUnit.Percent : Unit;

    /// <summary>Reads the operand from a ticker's latest values: the field itself, or the deterministic growth percent.</summary>
    public decimal? Evaluate(IReadOnlyDictionary<string, decimal?> values) => Kind switch
    {
        ScreenerOperandKind.Direct => values.GetValueOrDefault(FieldKey!),
        ScreenerOperandKind.Growth => ScreenerGrowth.Percent(values.GetValueOrDefault(FieldKey!), values.GetValueOrDefault(BaseFieldKey!)),
        _ => null
    };
}

/// <summary>
/// The one mapping table between the platform's canonical metric codes and the CyclicalWaves v2 screener
/// fields (design §10.3 and §10.5). Lookup and the scanner both read it; nothing is substituted from
/// another period or provider.
/// </summary>
public static class ScreenerMetricMapping
{
    private const string UnavailableReason = "metric_not_published_by_cyclicalwaves_v2";

    private static readonly IReadOnlyDictionary<string, (string Latest, string? Previous, string? SameLastYear)> QuarterlyFields =
        new Dictionary<string, (string, string?, string?)>(StringComparer.Ordinal)
        {
            ["REVENUE"] = (Catalog.LastQuarterSale, Catalog.PenultimateQuarterSale, Catalog.LastYearSameQuarterSale),
            ["NET_PROFIT"] = (Catalog.LastQuarterNetProfit, Catalog.PenultimateQuarterNetProfit, Catalog.LastYearSameQuarterNetProfit),
            ["GROSS_PROFIT"] = (Catalog.LastQuarterGrossProfit, Catalog.PenultimateQuarterGrossProfit, Catalog.LastYearSameQuarterGrossProfit),
            ["OPERATING_PROFIT"] = (Catalog.LastQuarterOperatingProfit, Catalog.PenultimateQuarterOperatingProfit, Catalog.LastYearSameQuarterOperatingProfit),
            ["NET_PROFIT_MARGIN"] = (Catalog.LastQuarterNetProfitMargin, Catalog.PenultimateQuarterNetProfitMargin, Catalog.LastYearSameQuarterNetProfitMargin),
            ["GROSS_PROFIT_MARGIN"] = (Catalog.LastQuarterGrossProfitMargin, Catalog.PenultimateQuarterGrossProfitMargin, Catalog.LastYearSameQuarterGrossProfitMargin),
            ["OPERATING_PROFIT_MARGIN"] = (Catalog.LastQuarterOperatingProfitMargin, Catalog.PenultimateQuarterOperatingProfitMargin, Catalog.LastYearSameQuarterOperatingProfitMargin)
        };

    private static readonly IReadOnlyDictionary<string, string> SingleFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PE_TTM"] = Catalog.PriceToEarnings,
        ["PS_TTM"] = Catalog.PriceToSales,
        ["AVG_4Q_REVENUE"] = Catalog.Average4QuarterSale,
        ["AVG_12M_MONTHLY_SALES"] = Catalog.Average12MonthSale,
        ["MONTHLY_SALES"] = Catalog.LastMonthSale
    };

    private static readonly IReadOnlyDictionary<string, (string Current, string Base)> GrowthFields =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["REVENUE_GROWTH_QOQ"] = (Catalog.LastQuarterSale, Catalog.PenultimateQuarterSale),
            ["NET_PROFIT_GROWTH_QOQ"] = (Catalog.LastQuarterNetProfit, Catalog.PenultimateQuarterNetProfit),
            ["GROSS_PROFIT_GROWTH_QOQ"] = (Catalog.LastQuarterGrossProfit, Catalog.PenultimateQuarterGrossProfit),
            ["OPERATING_PROFIT_GROWTH_QOQ"] = (Catalog.LastQuarterOperatingProfit, Catalog.PenultimateQuarterOperatingProfit),
            ["REVENUE_GROWTH_YOY"] = (Catalog.LastQuarterSale, Catalog.LastYearSameQuarterSale),
            ["NET_PROFIT_GROWTH_YOY"] = (Catalog.LastQuarterNetProfit, Catalog.LastYearSameQuarterNetProfit),
            ["GROSS_PROFIT_GROWTH_YOY"] = (Catalog.LastQuarterGrossProfit, Catalog.LastYearSameQuarterGrossProfit),
            ["OPERATING_PROFIT_GROWTH_YOY"] = (Catalog.LastQuarterOperatingProfit, Catalog.LastYearSameQuarterOperatingProfit),
            ["MONTHLY_SALES_GROWTH_MOM"] = (Catalog.LastMonthSale, Catalog.PenultimateMonthSale),
            ["MONTHLY_SALES_GROWTH_YOY"] = (Catalog.LastMonthSale, Catalog.LastYearSameMonthSale)
        };

    public static IReadOnlyCollection<string> DirectScannerCodes { get; } =
        SingleFields.Keys.Concat(QuarterlyFields.Keys).OrderBy(code => code, StringComparer.Ordinal).ToArray();

    public static IReadOnlyCollection<string> GrowthScannerCodes { get; } =
        GrowthFields.Keys.OrderBy(code => code, StringComparer.Ordinal).ToArray();

    /// <summary>Scanner binding for a condition's metric. A base code with a growth comparison means its growth.</summary>
    public static ScreenerMetricBinding ForScanner(MetricCode metricCode, GrowthComparison? comparison = null)
    {
        var code = EffectiveScannerCode(metricCode.Value, comparison);
        if (GrowthFields.TryGetValue(code, out var growth))
        {
            return new ScreenerMetricBinding(code, ScreenerOperandKind.Growth, growth.Current, growth.Base, ScreenerMetricUnit.Percent);
        }

        var field = SingleFields.GetValueOrDefault(code) ??
                    (QuarterlyFields.TryGetValue(code, out var quarterly) ? quarterly.Latest : null);
        return field is null
            ? Unavailable(code)
            : new ScreenerMetricBinding(code, ScreenerOperandKind.Direct, field, null, Catalog.Get(field).Unit);
    }

    /// <summary>Lookup binding for a metric and an optional period selector (design §10.3). EPS is answered from statements, not here.</summary>
    public static ScreenerMetricBinding ForLookup(MetricCode metricCode, SymbolLookupPeriodSelector? selector)
    {
        var code = metricCode.Value;
        string? field = null;
        if (QuarterlyFields.TryGetValue(code, out var quarterly))
        {
            field = selector switch
            {
                null or SymbolLookupPeriodSelector.LatestQuarter => quarterly.Latest,
                SymbolLookupPeriodSelector.PreviousQuarter => quarterly.Previous,
                SymbolLookupPeriodSelector.SameQuarterLastYear => quarterly.SameLastYear,
                _ => null
            };
        }
        else if (code == "MONTHLY_SALES")
        {
            field = selector switch
            {
                null or SymbolLookupPeriodSelector.LatestMonth => Catalog.LastMonthSale,
                SymbolLookupPeriodSelector.PreviousMonth => Catalog.PenultimateMonthSale,
                SymbolLookupPeriodSelector.SameMonthLastYear => Catalog.LastYearSameMonthSale,
                _ => null
            };
        }
        else if (code == "AVG_12M_MONTHLY_SALES")
        {
            field = selector switch
            {
                null or SymbolLookupPeriodSelector.LatestMonth => Catalog.Average12MonthSale,
                SymbolLookupPeriodSelector.LastYearAverage12Month => Catalog.LastYearAverage12MonthSale,
                _ => null
            };
        }
        else if (selector is null or SymbolLookupPeriodSelector.LatestQuarter or SymbolLookupPeriodSelector.LatestMonth &&
                 SingleFields.TryGetValue(code, out var single))
        {
            // P/E, P/S and the 4-quarter average have one latest value; a "latest" selector (for example one carried over
            // from the previous turn) asks for exactly that value. Any other period is unavailable.
            field = single;
        }
        else if (GrowthFields.ContainsKey(code))
        {
            return ForScanner(metricCode);
        }

        return field is null
            ? Unavailable(code)
            : new ScreenerMetricBinding(code, ScreenerOperandKind.Direct, field, null, Catalog.Get(field).Unit);
    }

    private static string EffectiveScannerCode(string code, GrowthComparison? comparison)
    {
        if (comparison is null || GrowthFields.ContainsKey(code))
        {
            return code;
        }

        var suffix = comparison switch
        {
            GrowthComparison.QuarterOverQuarter => "_GROWTH_QOQ",
            GrowthComparison.YearOverYear => "_GROWTH_YOY",
            GrowthComparison.MonthOverMonth => "_GROWTH_MOM",
            _ => string.Empty
        };
        // A growth comparison on a code without a v2 growth binding resolves to an unknown code,
        // so it is reported unavailable instead of silently comparing the level value.
        return code + suffix;
    }

    private static ScreenerMetricBinding Unavailable(string code) =>
        new(code, ScreenerOperandKind.Unavailable, null, null, ScreenerMetricUnit.Ratio, UnavailableReason);
}
