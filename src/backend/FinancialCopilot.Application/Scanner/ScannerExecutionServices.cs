namespace FinancialCopilot.Application.Scanner;

public sealed class ScannerResultColumnPolicy : IScannerResultColumnPolicy
{
    private static readonly IReadOnlyCollection<ScannerTableColumn> DefaultColumns =
    [
        new ScannerTableColumn("SYMBOL", "Symbol", ScannerColumnType.Symbol),
        new ScannerTableColumn("COMPANY", "Company", ScannerColumnType.CompanyName),
        new ScannerTableColumn("LATEST_PRICE", "Latest Price", ScannerColumnType.LatestPrice),
        new ScannerTableColumn("DAILY_CHANGE_PCT", "Change %", ScannerColumnType.DailyChangePercent),
        new ScannerTableColumn("MARKET_CAP", "Market Cap", ScannerColumnType.MarketCap)
    ];

    public IReadOnlyCollection<ScannerTableColumn> BuildColumns(ScannerQueryPlan plan)
    {
        var usePersianLabels = IsPersianLanguage(plan.Language);
        var columns = DefaultColumns
            .Select(column => LocalizeDefaultColumn(column, usePersianLabels))
            .ToList();
        var seen = new HashSet<string>(
            DefaultColumns.Select(c => c.Identifier),
            StringComparer.OrdinalIgnoreCase);

        foreach (var condition in plan.Conditions)
        {
            var code = condition.MetricReference.MetricCode.Value;
            if (seen.Add(code) && columns.Count < ScannerQueryPlan.MaxDisplayColumns)
            {
                columns.Add(new ScannerTableColumn(
                    code,
                    FormatMetricDisplayName(code, usePersianLabels),
                    ScannerColumnType.Metric,
                    code));
            }
        }

        foreach (var col in plan.RequestedColumns)
        {
            if (seen.Add(col.Identifier) && columns.Count < ScannerQueryPlan.MaxDisplayColumns)
            {
                columns.Add(new ScannerTableColumn(
                    col.Identifier,
                    FormatMetricDisplayName(col.Identifier, usePersianLabels),
                    ScannerColumnType.Metric,
                    col.Identifier));
            }
        }

        return columns;
    }

    private static ScannerTableColumn LocalizeDefaultColumn(
        ScannerTableColumn column,
        bool usePersianLabels) =>
        usePersianLabels
            ? column with
            {
                DisplayName = column.Identifier switch
                {
                    "SYMBOL" => "نماد",
                    "COMPANY" => "شرکت",
                    "LATEST_PRICE" => "آخرین قیمت",
                    "DAILY_CHANGE_PCT" => "تغییر روزانه %",
                    "MARKET_CAP" => "ارزش بازار",
                    _ => column.DisplayName
                }
            }
            : column;

    private static string FormatMetricDisplayName(string metricCode, bool usePersianLabels) =>
        usePersianLabels
            ? FormatPersianMetricDisplayName(metricCode)
            : FormatEnglishMetricDisplayName(metricCode);

    private static string FormatEnglishMetricDisplayName(string metricCode) =>
        metricCode.Replace("_", " ").ToUpperInvariant() switch
        {
            "PE TTM" => "P/E (TTM)",
            "PS TTM" => "P/S (TTM)",
            "NET PROFIT GROWTH YOY" => "Net Profit Growth YoY",
            "NET PROFIT GROWTH QOQ" => "Net Profit Growth QoQ",
            "MONTHLY SALES GROWTH YOY" => "Sales Growth YoY",
            "MONTHLY SALES GROWTH MOM" => "Sales Growth MoM",
            "TTM EARNINGS" => "TTM Earnings",
            "TTM SALES" => "TTM Sales",
            "TTM EPS" => "EPS (TTM)",
            "MARKET CAP" => "Market Cap",
            "LATEST PRICE" => "Latest Price",
            "NET PROFIT" => "Net Profit",
            _ => metricCode
        };

    private static string FormatPersianMetricDisplayName(string metricCode) =>
        metricCode.Replace("_", " ").ToUpperInvariant() switch
        {
            "PE TTM" => "P/E دوازده‌ماهه",
            "PS TTM" => "P/S دوازده‌ماهه",
            "NET PROFIT GROWTH YOY" => "رشد سالانه سود خالص",
            "NET PROFIT GROWTH QOQ" => "رشد فصلی سود خالص",
            "MONTHLY SALES GROWTH YOY" => "رشد سالانه فروش",
            "MONTHLY SALES GROWTH MOM" => "رشد ماهانه فروش",
            "TTM EARNINGS" => "سود دوازده‌ماهه",
            "TTM SALES" => "فروش دوازده‌ماهه",
            "TTM EPS" => "EPS دوازده‌ماهه",
            "MARKET CAP" => "ارزش بازار",
            "LATEST PRICE" => "آخرین قیمت",
            "NET PROFIT" => "سود خالص",
            _ => metricCode
        };

    private static bool IsPersianLanguage(string language) =>
        language.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
}

public sealed class ScannerResultRanker : IScannerResultRanker
{
    public IReadOnlyCollection<ScannerTableRow> Rank(
        IReadOnlyCollection<ScannerTableRow> rows,
        ScannerQueryPlan plan)
    {
        return rows
            .Select(row => (row, score: ComputeScore(row, plan)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.row.SymbolCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.row with { Score = x.score })
            .ToList();
    }

    private static double ComputeScore(ScannerTableRow row, ScannerQueryPlan plan)
    {
        var total = 0.0;
        var count = 0;

        foreach (var condition in plan.Conditions)
        {
            var metricCode = condition.MetricReference.MetricCode.Value;
            if (!row.Cells.TryGetValue(metricCode, out var cell) || cell.Value is null)
                continue;

            var value = (double)cell.Value.Value;
            var threshold = (double)condition.Threshold;

            if (threshold == 0.0) continue;

            var contribution = condition.Operator switch
            {
                ConditionOperator.LessThan or ConditionOperator.LessThanOrEqual =>
                    Math.Max(0, (threshold - value) / Math.Abs(threshold)),
                ConditionOperator.GreaterThan or ConditionOperator.GreaterThanOrEqual =>
                    Math.Max(0, (value - threshold) / Math.Abs(threshold)),
                _ => 0.0
            };

            total += contribution;
            count++;
        }

        return count > 0 ? total / count : 0.0;
    }
}
