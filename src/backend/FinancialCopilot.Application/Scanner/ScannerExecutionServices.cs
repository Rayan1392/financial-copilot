namespace FinancialCopilot.Application.Scanner;

public sealed class ScannerResultColumnPolicy : IScannerResultColumnPolicy
{
    // Identity columns are always present and cannot be removed or reordered.
    private static readonly IReadOnlyCollection<ScannerTableColumn> IdentityColumns =
    [
        new ScannerTableColumn("SYMBOL",  "Symbol",  ScannerColumnType.Symbol),
        new ScannerTableColumn("COMPANY", "Company", ScannerColumnType.CompanyName)
    ];

    // Quote columns are only added when the user explicitly requested them or they
    // appear as a filter/sort condition. They are never automatic defaults for scanner results.
    private static readonly IReadOnlyDictionary<string, ScannerTableColumn> QuoteColumnDefinitions =
        new Dictionary<string, ScannerTableColumn>(StringComparer.OrdinalIgnoreCase)
        {
            ["LATEST_PRICE"]     = new("LATEST_PRICE",     "Latest Price", ScannerColumnType.LatestPrice),
            ["DAILY_CHANGE_PCT"] = new("DAILY_CHANGE_PCT", "Change %",     ScannerColumnType.DailyChangePercent),
            ["MARKET_CAP"]       = new("MARKET_CAP",       "Market Cap",   ScannerColumnType.MarketCap)
        };

    // Terms that must never become user-facing columns: identity columns already handled
    // above, and internal LLM output schema field names. Quote column synonyms are NOT
    // blocked here — they are resolved via QuoteColumnDefinitions when the user requests them.
    private static readonly HashSet<string> BlockedColumnTerms = new(
        [
            // identity — always present; must not be duplicated via RequestedColumns
            "symbol", "ticker", "company", "companyname",
            "نماد", "نامنماد", "شرکت", "نامشرکت",
            // internal LLM output schema field names — must never surface as columns
            "symbols", "universe", "conditions", "sort", "limit"
        ],
        StringComparer.OrdinalIgnoreCase);

    // Normalized forms of quote column synonyms that the parser may pass in RequestedColumns.
    // These are resolved to the canonical QuoteColumnDefinitions entry so their ColumnType
    // is preserved. Without this map, "latest price" would become a generic Metric column.
    private static readonly IReadOnlyDictionary<string, string> QuoteColumnSynonyms =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["latestprice"]              = "LATEST_PRICE",
            ["price"]                    = "LATEST_PRICE",
            ["latestpricechangepercent"] = "DAILY_CHANGE_PCT",
            ["dailychangepct"]           = "DAILY_CHANGE_PCT",
            ["dailychangepercent"]       = "DAILY_CHANGE_PCT",
            ["changepercent"]            = "DAILY_CHANGE_PCT",
            ["percentchange"]            = "DAILY_CHANGE_PCT",
            ["marketcap"]                = "MARKET_CAP",
            ["marketcapitalization"]     = "MARKET_CAP",
            ["قیمت"]                     = "LATEST_PRICE",
            ["آخرینقیمت"]               = "LATEST_PRICE",
            ["درصدتغییر"]               = "DAILY_CHANGE_PCT",
            ["تغییرقیمت"]               = "DAILY_CHANGE_PCT",
            ["درصدتغییرآخرینقیمت"]     = "DAILY_CHANGE_PCT",
            ["ارزشبازار"]               = "MARKET_CAP"
        };

    public IReadOnlyCollection<ScannerTableColumn> BuildColumns(ScannerQueryPlan plan)
    {
        var usePersianLabels = IsPersianLanguage(plan.Language);
        var columns = IdentityColumns
            .Select(col => LocalizeDefaultColumn(col, usePersianLabels))
            .ToList();
        var seen = new HashSet<string>(
            IdentityColumns.Select(c => c.Identifier),
            StringComparer.OrdinalIgnoreCase);

        if (plan.SalesGrowth is not null)
        {
            var salesPlan = plan.SalesGrowth;
            columns.Add(new ScannerTableColumn(
                "MONTHLY_SALES",
                usePersianLabels ? "فروش آخرین دوره" : "Latest Monthly Sales",
                ScannerColumnType.Metric,
                "MONTHLY_SALES"));
            seen.Add("MONTHLY_SALES");

            var baselineColumn = salesPlan.Semantics.Baseline switch
            {
                SalesGrowthComparisonBaseline.PreviousMonth =>
                    new ScannerTableColumn(
                        "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH",
                        usePersianLabels ? "فروش ماه قبل" : "Previous Month Sales",
                        ScannerColumnType.Metric,
                        "MONTHLY_SALES_BASELINE_PREVIOUS_MONTH"),
                SalesGrowthComparisonBaseline.SameMonthPreviousYear =>
                    new ScannerTableColumn(
                        "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR",
                        usePersianLabels ? "فروش ماه مشابه سال قبل" : "Same Month Previous Year Sales",
                        ScannerColumnType.Metric,
                        "MONTHLY_SALES_BASELINE_SAME_MONTH_PREVIOUS_YEAR"),
                SalesGrowthComparisonBaseline.AveragePrevious12Months =>
                    new ScannerTableColumn(
                        "MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS",
                        usePersianLabels ? "میانگین فروش ۱۲ ماهه" : "Average Previous 12 Months Sales",
                        ScannerColumnType.Metric,
                        "MONTHLY_SALES_BASELINE_AVERAGE_PREVIOUS_12_MONTHS"),
                _ => throw new ArgumentOutOfRangeException()
            };
            columns.Add(baselineColumn);
            seen.Add(baselineColumn.Identifier);

            columns.Add(new ScannerTableColumn(
                "MONTHLY_SALES_GROWTH_PERCENT",
                usePersianLabels ? "درصد رشد" : "Growth Percent",
                ScannerColumnType.Metric,
                "MONTHLY_SALES_GROWTH_PERCENT"));
            seen.Add("MONTHLY_SALES_GROWTH_PERCENT");

            if (salesPlan.Semantics.ThresholdKind == SalesGrowthThresholdKind.Multiple ||
                IsSalesMultipleRequested(salesPlan))
            {
                columns.Add(new ScannerTableColumn(
                    "MONTHLY_SALES_GROWTH_MULTIPLE",
                    usePersianLabels ? "نسبت فروش" : "Sales Multiple",
                    ScannerColumnType.Metric,
                    "MONTHLY_SALES_GROWTH_MULTIPLE"));
                seen.Add("MONTHLY_SALES_GROWTH_MULTIPLE");
            }
        }

        // Add condition metrics. Quote-column conditions (e.g. MARKET_CAP filter) are added
        // via QuoteColumnDefinitions so their ColumnType is preserved correctly.
        foreach (var condition in plan.Conditions)
        {
            var code = condition.MetricReference.MetricCode.Value;
            if (plan.SalesGrowth is not null && IsSalesGrowthMetric(code)) continue;
            if (!seen.Add(code)) continue;

            if (QuoteColumnDefinitions.TryGetValue(code, out var quoteCol))
            {
                columns.Add(LocalizeDefaultColumn(quoteCol, usePersianLabels));
            }
            else
            {
                columns.Add(new ScannerTableColumn(
                    code,
                    FormatMetricDisplayName(code, usePersianLabels),
                    ScannerColumnType.Metric,
                    code));
            }
        }

        // Add explicitly requested columns. Blocked terms (identity, internal schema names)
        // are skipped. Quote column synonyms are resolved to their canonical identifier and
        // added via QuoteColumnDefinitions so their ColumnType is preserved correctly.
        var requestedMetricCount = 0;
        foreach (var col in plan.RequestedColumns)
        {
            if (IsBlockedColumnTerm(col.Identifier)) continue;
            if (requestedMetricCount >= ScannerQueryPlan.MaxDisplayColumns) break;

            // Resolve synonym → canonical quote column identifier (e.g. "latest price" → "LATEST_PRICE")
            var normalized = NormalizeColumnTerm(col.Identifier);
            var resolvedIdentifier = QuoteColumnSynonyms.TryGetValue(normalized, out var canonical)
                ? canonical
                : col.Identifier;

            if (!seen.Add(resolvedIdentifier)) continue;

            if (QuoteColumnDefinitions.TryGetValue(resolvedIdentifier, out var quoteCol))
            {
                columns.Add(LocalizeDefaultColumn(quoteCol, usePersianLabels));
            }
            else
            {
                columns.Add(new ScannerTableColumn(
                    resolvedIdentifier,
                    FormatMetricDisplayName(resolvedIdentifier, usePersianLabels),
                    ScannerColumnType.Metric,
                    resolvedIdentifier));
            }
            requestedMetricCount++;
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
            "NET PROFIT MARGIN" => "Net Profit Margin",
            "GROSS PROFIT MARGIN" => "Gross Profit Margin",
            "OPERATING PROFIT MARGIN" => "Operating Profit Margin",
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
            "NET PROFIT MARGIN" => "حاشیه سود خالص",
            "GROSS PROFIT MARGIN" => "حاشیه سود ناخالص",
            "OPERATING PROFIT MARGIN" => "حاشیه سود عملیاتی",
            _ => metricCode
        };

    private static bool IsSalesGrowthMetric(string code) =>
        code.Equals("MONTHLY_SALES_GROWTH", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("MONTHLY_SALES_GROWTH_MOM", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("MONTHLY_SALES_GROWTH_YOY", StringComparison.OrdinalIgnoreCase);

    private static bool IsSalesMultipleRequested(SalesGrowthScannerPlan plan) =>
        plan.EffectiveRequestedDisplayColumns.Any(column =>
            column.Identifier.Contains("multiple", StringComparison.OrdinalIgnoreCase) ||
            column.Identifier.Contains("ratio", StringComparison.OrdinalIgnoreCase) ||
            column.Identifier.Contains("نسبت", StringComparison.OrdinalIgnoreCase));

    private static bool IsPersianLanguage(string language) =>
        language.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedColumnTerm(string column) =>
        BlockedColumnTerms.Contains(NormalizeColumnTerm(column));

    private static string NormalizeColumnTerm(string term)
    {
        var chars = term.Trim().ToLowerInvariant()
            .Where(ch => !char.IsWhiteSpace(ch) && ch is not '_' and not '-' and not '/' and not '%' and not '.')
            .ToArray();
        return new string(chars);
    }
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
