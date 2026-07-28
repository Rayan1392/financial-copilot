using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public static class PhaseOneFinancialSemanticCatalog
{
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 1);
    private static readonly MetricUnit Amount = new("amount", "Amount");
    private static readonly MetricUnit Percentage = new("percent", "Percent");
    private static readonly MetricUnit Ratio = new("ratio", "Ratio");
    private static readonly MetricUnit PerShare = new("amount-per-share", "Amount per share");
    private static readonly MetricUnit Days = new("days", "Days");
    private static readonly MetricUnit Quantity = new("quantity", "Quantity");
    private static readonly MetricUnit AmountPerUnit = new("amount-per-unit", "Amount per unit");

    public static IReadOnlyCollection<FinancialMetricDefinition> Definitions { get; } =
    [
        Define("NET_PROFIT", "Net Profit", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths],
            [],
            [Dependency("NET_PROFIT")]),
        // Monthly-activity metrics (spec 057): sourced from normalized Noavaran monthly reports.
        // Each is queryable (aliases) AND self-sourced — the self-dependency makes the
        // recalculation processor persist one DerivedMetrics row per company-month so the symbol
        // lookup can answer «آخرین فروش …» from the latest month. The bare ambiguous «فروش» stays
        // on quarterly REVENUE by policy (statement sales); monthly asks use the explicit monthly
        // terms below.
        Define("MONTHLY_SALES", "Monthly Sales", MetricCategory.SalesAndProduction, Amount,
            [FiscalPeriodType.Monthly],
            [Alias("monthly sales", "en-US", "MONTHLY_SALES"),
             Alias("latest monthly sales", "en-US", "MONTHLY_SALES"),
             Alias("مبلغ فروش", "fa-IR", "MONTHLY_SALES"),
             Alias("مبلغ فروش ماهانه", "fa-IR", "MONTHLY_SALES"),
             Alias("فروش آخرین ماه", "fa-IR", "MONTHLY_SALES"),
             Alias("فروش ماهانه", "fa-IR", "MONTHLY_SALES"),
             Alias("فروش ماهیانه", "fa-IR", "MONTHLY_SALES"),
             Alias("فروش ماه مشابه سال قبل", "fa-IR", "MONTHLY_SALES"),
             Alias("فروش ماه مشابه دوره قبل", "fa-IR", "MONTHLY_SALES"),
             Alias("آخرین فروش", "fa-IR", "MONTHLY_SALES"),
             Alias("آخرین فروش ماهانه", "fa-IR", "MONTHLY_SALES")],
            [Dependency("MONTHLY_SALES")]),
        Define("MONTHLY_SALES_YTD", "Monthly Sales Year To Date", MetricCategory.SalesAndProduction, Amount,
            [FiscalPeriodType.Monthly],
            [Alias("monthly sales ytd", "en-US", "MONTHLY_SALES_YTD"),
             Alias("year to date sales", "en-US", "MONTHLY_SALES_YTD"),
             Alias("sales from fiscal year start", "en-US", "MONTHLY_SALES_YTD"),
             Alias("فروش YTD", "fa-IR", "MONTHLY_SALES_YTD"),
             Alias("فروش از ابتدای سال مالی", "fa-IR", "MONTHLY_SALES_YTD"),
             Alias("جمع فروش از ابتدای دوره", "fa-IR", "MONTHLY_SALES_YTD"),
             Alias("فروش از ابتدای دوره", "fa-IR", "MONTHLY_SALES_YTD")],
            [Dependency("MONTHLY_SALES_YTD")]),
        Define("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "Monthly Sales Year To Previous Month", MetricCategory.SalesAndProduction, Amount,
            [FiscalPeriodType.Monthly],
            [Alias("monthly sales ytd previous month", "en-US", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("sales from fiscal year start to previous month", "en-US", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("فروش YTD تا ماه قبل", "fa-IR", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("فروش YTD تا ماه گذشته", "fa-IR", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("فروش از ابتدای سال مالی تا ماه گذشته", "fa-IR", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("جمع فروش از ابتدای دوره تا ماه قبل", "fa-IR", "MONTHLY_SALES_YTD_PREVIOUS_MONTH"),
             Alias("فروش تا ماه گذشته", "fa-IR", "MONTHLY_SALES_YTD_PREVIOUS_MONTH")],
            [Dependency("MONTHLY_SALES_YTD_PREVIOUS_MONTH")]),
        Define("MONTHLY_SALES_QUANTITY", "Monthly Sales Quantity", MetricCategory.SalesAndProduction, Quantity,
            [FiscalPeriodType.Monthly],
            [Alias("monthly sales quantity", "en-US", "MONTHLY_SALES_QUANTITY"),
             Alias("sales quantity", "en-US", "MONTHLY_SALES_QUANTITY"),
             Alias("مقدار فروش", "fa-IR", "MONTHLY_SALES_QUANTITY"),
             Alias("مقدار فروش ماهانه", "fa-IR", "MONTHLY_SALES_QUANTITY")],
            [Dependency("MONTHLY_SALES_QUANTITY")]),
        Define("MONTHLY_PRODUCTION_QUANTITY", "Monthly Production Quantity", MetricCategory.SalesAndProduction, Quantity,
            [FiscalPeriodType.Monthly],
            [Alias("monthly production quantity", "en-US", "MONTHLY_PRODUCTION_QUANTITY"),
             Alias("production quantity", "en-US", "MONTHLY_PRODUCTION_QUANTITY"),
             Alias("تولید", "fa-IR", "MONTHLY_PRODUCTION_QUANTITY"),
             Alias("مقدار تولید", "fa-IR", "MONTHLY_PRODUCTION_QUANTITY"),
             Alias("تولید ماهانه", "fa-IR", "MONTHLY_PRODUCTION_QUANTITY"),
             Alias("آخرین تولید ماهانه", "fa-IR", "MONTHLY_PRODUCTION_QUANTITY")],
            [Dependency("MONTHLY_PRODUCTION_QUANTITY")]),
        Define("MONTHLY_SALES_RATE", "Monthly Sales Rate", MetricCategory.SalesAndProduction, AmountPerUnit,
            [FiscalPeriodType.Monthly],
            [Alias("monthly sales rate", "en-US", "MONTHLY_SALES_RATE"),
             Alias("sales rate", "en-US", "MONTHLY_SALES_RATE"),
             Alias("نرخ فروش", "fa-IR", "MONTHLY_SALES_RATE"),
             Alias("نرخ فروش ماهانه", "fa-IR", "MONTHLY_SALES_RATE")],
            [Dependency("MONTHLY_SALES_RATE")]),
        DefineRatio("MONTHLY_SALES_TO_PRODUCTION_RATIO", "Monthly Sales to Production Ratio", MetricCategory.SalesAndProduction, Ratio,
            [Alias("monthly sales to production ratio", "en-US", "MONTHLY_SALES_TO_PRODUCTION_RATIO"),
             Alias("sales to production ratio", "en-US", "MONTHLY_SALES_TO_PRODUCTION_RATIO"),
             Alias("نسبت فروش به تولید", "fa-IR", "MONTHLY_SALES_TO_PRODUCTION_RATIO"),
             Alias("نسبت مقدار فروش به تولید", "fa-IR", "MONTHLY_SALES_TO_PRODUCTION_RATIO")]),
        // CodalDB income-statement source metrics (cumulative 3/6/9/12-month periods).
        Define("REVENUE", "Revenue", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("revenue", "en-US", "REVENUE"), Alias("sales", "en-US", "REVENUE"),
             Alias("درآمد", "fa-IR", "REVENUE"), Alias("درآمد فصلی", "fa-IR", "REVENUE"),
             Alias("فروش", "fa-IR", "REVENUE"), Alias("فروش فصلی", "fa-IR", "REVENUE"),
             Alias("فروش خالص", "fa-IR", "REVENUE")],
            [Dependency("REVENUE")]),
        Define("TOTAL_REVENUE", "Total Revenue", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("total revenue", "en-US", "TOTAL_REVENUE"),
             Alias("کل درآمد", "fa-IR", "TOTAL_REVENUE"), Alias("جمع درآمد", "fa-IR", "TOTAL_REVENUE")],
            []),
        Define("GROSS_PROFIT", "Gross Profit", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("gross profit", "en-US", "GROSS_PROFIT"),
             Alias("سود ناخالص", "fa-IR", "GROSS_PROFIT")],
            [Dependency("GROSS_PROFIT")]),
        Define("OPERATING_PROFIT", "Operating Profit", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("operating profit", "en-US", "OPERATING_PROFIT"), Alias("ebit proxy", "en-US", "OPERATING_PROFIT"),
             Alias("سود عملیاتی", "fa-IR", "OPERATING_PROFIT")],
            [Dependency("OPERATING_PROFIT")]),
        Define("EPS", "Earnings per Share", MetricCategory.Profitability, PerShare,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("eps", "en-US", "EPS"), Alias("earnings per share", "en-US", "EPS"),
             Alias("سود هر سهم", "fa-IR", "EPS"), Alias("سود پایه هر سهم", "fa-IR", "EPS")],
            []),
        Define("EPS_CONSOLIDATED", "Earnings per Share (Consolidated)", MetricCategory.Profitability, PerShare,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("consolidated eps", "en-US", "EPS_CONSOLIDATED"),
             Alias("سود هر سهم تلفیقی", "fa-IR", "EPS_CONSOLIDATED")],
            []),
        Define("FINANCE_COSTS", "Finance Costs", MetricCategory.FinancialHealth, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("finance costs", "en-US", "FINANCE_COSTS"), Alias("financial costs", "en-US", "FINANCE_COSTS"),
             Alias("هزینه‌های مالی", "fa-IR", "FINANCE_COSTS"), Alias("هزینه مالی", "fa-IR", "FINANCE_COSTS")],
            []),
        Define("INCOME_TAX", "Income Tax", MetricCategory.FinancialHealth, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("income tax", "en-US", "INCOME_TAX"), Alias("taxes", "en-US", "INCOME_TAX"),
             Alias("مالیات بر درآمد", "fa-IR", "INCOME_TAX"), Alias("مالیات", "fa-IR", "INCOME_TAX")],
            []),
        // CodalDB balance-sheet source metrics.
        Define("TOTAL_EQUITY", "Total Equity", MetricCategory.FinancialHealth, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("total equity", "en-US", "TOTAL_EQUITY"), Alias("shareholders equity", "en-US", "TOTAL_EQUITY"),
             Alias("حقوق صاحبان سهام", "fa-IR", "TOTAL_EQUITY"), Alias("جمع حقوق صاحبان سهام", "fa-IR", "TOTAL_EQUITY")],
            []),
        Define("CAPITAL", "Capital", MetricCategory.FinancialHealth, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("capital", "en-US", "CAPITAL"), Alias("paid capital", "en-US", "CAPITAL"),
             Alias("سرمایه", "fa-IR", "CAPITAL")],
            []),
        Define("OPERATING_CASH_FLOW", "Operating Cash Flow", MetricCategory.FinancialHealth, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("operating cash flow", "en-US", "OPERATING_CASH_FLOW"),
             Alias("cash flow from operations", "en-US", "OPERATING_CASH_FLOW")],
            []),
        Define("LATEST_PRICE", "Latest Observed Price", MetricCategory.Valuation, Amount,
            [FiscalPeriodType.TrailingTwelveMonths],
            [Alias("latest price", "en-US", "LATEST_PRICE"),
             Alias("price", "en-US", "LATEST_PRICE"),
             Alias("last price", "en-US", "LATEST_PRICE"),
             Alias("closing price", "en-US", "LATEST_PRICE"),
             Alias("آخرین قیمت", "fa-IR", "LATEST_PRICE"),
             Alias("قیمت", "fa-IR", "LATEST_PRICE"),
             Alias("قیمت امروز", "fa-IR", "LATEST_PRICE"),
             Alias("قیمت پایانی", "fa-IR", "LATEST_PRICE")],
            []),
        Define("DAILY_CHANGE_PCT", "Daily Change Percent", MetricCategory.Valuation, Percentage,
            [FiscalPeriodType.TrailingTwelveMonths],
            [Alias("daily change", "en-US", "DAILY_CHANGE_PCT"),
             Alias("daily change percent", "en-US", "DAILY_CHANGE_PCT"),
             Alias("daily change pct", "en-US", "DAILY_CHANGE_PCT"),
             Alias("change percent", "en-US", "DAILY_CHANGE_PCT"),
             Alias("درصد تغییر قیمت", "fa-IR", "DAILY_CHANGE_PCT"),
             Alias("درصد تغییر روزانه", "fa-IR", "DAILY_CHANGE_PCT"),
             Alias("تغییر روزانه درصدی", "fa-IR", "DAILY_CHANGE_PCT"),
             Alias("درصد تغییر", "fa-IR", "DAILY_CHANGE_PCT"),
             Alias("تغییر قیمت", "fa-IR", "DAILY_CHANGE_PCT"),
             Alias("تغییر روزانه", "fa-IR", "DAILY_CHANGE_PCT")],
            []),
        DefineSource("MARKET_CAP", "Market Capitalization", MetricCategory.Valuation, Amount, FiscalPeriodType.TrailingTwelveMonths),
        DefineSource("SHARES_OUTSTANDING", "Shares Outstanding", MetricCategory.FinancialHealth, Amount, FiscalPeriodType.TrailingTwelveMonths),
        // CyclicalWaves pre-computed rolling averages (Q0 / M0 snapshots; no schema change required).
        // AVG_4Q_REVENUE: 4-quarter rolling average of quarterly revenue supplied by the vendor.
        // AVG_12M_MONTHLY_SALES: 12-month rolling average of monthly sales supplied by the vendor.
        Define("AVG_4Q_REVENUE", "Average 4-Quarter Revenue", MetricCategory.SalesAndProduction, Amount,
            [FiscalPeriodType.ThreeMonths],
            [Alias("average 4 quarter revenue", "en-US", "AVG_4Q_REVENUE"),
             Alias("avg 4q revenue", "en-US", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش ۴ فصل", "fa-IR", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش 4 فصل", "fa-IR", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش فصلی", "fa-IR", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش چهار فصل", "fa-IR", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش ۴ فصل اخیر", "fa-IR", "AVG_4Q_REVENUE"),
             Alias("میانگین فروش 4 فصل اخیر", "fa-IR", "AVG_4Q_REVENUE")],
            [Dependency("AVG_4Q_REVENUE")]),
        Define("AVG_12M_MONTHLY_SALES", "Average 12-Month Monthly Sales", MetricCategory.SalesAndProduction, Amount,
            [FiscalPeriodType.Monthly],
            [Alias("average 12 month sales", "en-US", "AVG_12M_MONTHLY_SALES"),
             Alias("avg 12m sales", "en-US", "AVG_12M_MONTHLY_SALES"),
             Alias("متوسط فروش ۱۲ ماهه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("متوسط فروش 12 ماهه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش ۱۲ ماهه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش 12 ماهه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش ۱۲ ماه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش 12 ماه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش ماهانه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش دوازده ماه", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش ۱۲ ماه اخیر", "fa-IR", "AVG_12M_MONTHLY_SALES"),
             Alias("میانگین فروش 12 ماه اخیر", "fa-IR", "AVG_12M_MONTHLY_SALES")],
            [Dependency("AVG_12M_MONTHLY_SALES")]),

        // PE_RATIO and PS_RATIO are intentionally NOT in Definitions — they are internal pipeline
        // source metrics fed by CyclicalWaves FinancialStatementLineItems and consumed only by
        // SourceLineItemPassthroughMetricCalculator for PE_TTM / PS_TTM.  Keeping them out of the
        // public catalog prevents the LLM from seeing them and avoids alias ambiguity with PE_TTM.
        Define(
            "TTM_SALES",
            "TTM Sales",
            MetricCategory.SalesAndProduction,
            Amount,
            [FiscalPeriodType.TrailingTwelveMonths],
            [],
            [Dependency("MONTHLY_SALES")]),
        Define(
            "TTM_EARNINGS",
            "TTM Earnings",
            MetricCategory.Profitability,
            Amount,
            [FiscalPeriodType.TrailingTwelveMonths],
            [],
            [Dependency("NET_PROFIT")]),
        Define(
            "TTM_EPS",
            "TTM EPS",
            MetricCategory.Profitability,
            Amount,
            [FiscalPeriodType.TrailingTwelveMonths],
            [],
            [Dependency("TTM_EARNINGS"), Dependency("SHARES_OUTSTANDING")]),
        Define(
            "NET_PROFIT_GROWTH_YOY",
            "Net Profit Growth YoY",
            MetricCategory.Growth,
            Percentage,
            [FiscalPeriodType.ThreeMonths],
            [
                Alias("net profit growth yoy", "en-US", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد سود خالص سالانه", "fa-IR", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد سود خالص آخرین فصل", "fa-IR", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد سود خالص نسبت به سال قبل", "fa-IR", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد سود نسبت به سال قبل", "fa-IR", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear)
            ],
            [Dependency("NET_PROFIT")]),
        Define(
            "NET_PROFIT_GROWTH_QOQ",
            "Net Profit Growth QoQ",
            MetricCategory.Growth,
            Percentage,
            [FiscalPeriodType.ThreeMonths],
            [
                Alias("net profit growth qoq", "en-US", "NET_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
                Alias("رشد سود خالص فصلی", "fa-IR", "NET_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
                Alias("رشد سود خالص آخرین فصل", "fa-IR", "NET_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
                Alias("رشد سود خالص نسبت به فصل قبل", "fa-IR", "NET_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)
            ],
            [Dependency("NET_PROFIT")]),
        Define(
            "MONTHLY_SALES_GROWTH_YOY",
            "Monthly Sales Growth YoY",
            MetricCategory.Growth,
            Percentage,
            [FiscalPeriodType.Monthly],
            [
                Alias("monthly sales growth yoy", "en-US", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد فروش ماهانه سالانه", "fa-IR", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("رشد فروش نسبت به سال قبل", "fa-IR", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear),
                Alias("میزان رشد فروش", "fa-IR", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear)
            ],
            [Dependency("MONTHLY_SALES")]),
        Define(
            "MONTHLY_SALES_GROWTH_MOM",
            "Monthly Sales Growth MoM",
            MetricCategory.Growth,
            Percentage,
            [FiscalPeriodType.Monthly],
            [
                Alias("monthly sales growth mom", "en-US", "MONTHLY_SALES_GROWTH_MOM", GrowthComparison.MonthOverMonth),
                Alias("رشد فروش نسبت به ماه قبل", "fa-IR", "MONTHLY_SALES_GROWTH_MOM", GrowthComparison.MonthOverMonth)
            ],
            [Dependency("MONTHLY_SALES")]),
        DefineGrowth("MONTHLY_PRODUCTION_GROWTH_YOY", "Monthly Production Growth YoY", "MONTHLY_PRODUCTION_QUANTITY",
            [Alias("monthly production growth yoy", "en-US", "MONTHLY_PRODUCTION_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("production growth yoy", "en-US", "MONTHLY_PRODUCTION_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد تولید نسبت به سال قبل", "fa-IR", "MONTHLY_PRODUCTION_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد تولید ماهانه سالانه", "fa-IR", "MONTHLY_PRODUCTION_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("میزان رشد تولید", "fa-IR", "MONTHLY_PRODUCTION_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("MONTHLY_SALES_QUANTITY_GROWTH_YOY", "Monthly Sales Quantity Growth YoY", "MONTHLY_SALES_QUANTITY",
            [Alias("monthly sales quantity growth yoy", "en-US", "MONTHLY_SALES_QUANTITY_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("sales quantity growth yoy", "en-US", "MONTHLY_SALES_QUANTITY_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد مقدار فروش نسبت به سال قبل", "fa-IR", "MONTHLY_SALES_QUANTITY_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        Define(
            "PE_TTM",
            "P/E (TTM)",
            MetricCategory.Valuation,
            Ratio,
            [FiscalPeriodType.ThreeMonths],
            [
                Alias("p/e", "en-US", "PE_TTM"),
                Alias("pe", "en-US", "PE_TTM"),
                // Persian queries often use Latin-script "P/E" — register for fa-IR so the resolver
                // finds it regardless of which language tag the LLM attaches to the term.
                Alias("p/e", "fa-IR", "PE_TTM"),
                Alias("pe", "fa-IR", "PE_TTM"),
                Alias("نسبت پی به ای", "fa-IR", "PE_TTM"),
                Alias("پی به ای", "fa-IR", "PE_TTM"),
                Alias("پی بر ای", "fa-IR", "PE_TTM"),
                Alias("پی‌ای", "fa-IR", "PE_TTM"),
                Alias("پی ای", "fa-IR", "PE_TTM"),
                Alias("نسبت قیمت به سود", "fa-IR", "PE_TTM"),
                Alias("قیمت به سود", "fa-IR", "PE_TTM"),
                Alias("price-to-earnings", "en-US", "PE_TTM"),
                Alias("price to earnings", "en-US", "PE_TTM"),
                Alias("price-to-earnings", "fa-IR", "PE_TTM"),
                Alias("price to earnings", "fa-IR", "PE_TTM"),
            ],
            [Dependency("PE_RATIO")]),
        Define(
            "PS_TTM",
            "P/S (TTM)",
            MetricCategory.Valuation,
            Ratio,
            [FiscalPeriodType.ThreeMonths],
            [
                Alias("p/s", "en-US", "PS_TTM"),
                Alias("ps", "en-US", "PS_TTM"),
                Alias("price-to-sales", "en-US", "PS_TTM"),
                Alias("price to sales", "en-US", "PS_TTM"),
                Alias("p/s", "fa-IR", "PS_TTM"),
                Alias("ps", "fa-IR", "PS_TTM"),
                Alias("نسبت قیمت به فروش", "fa-IR", "PS_TTM"),
                Alias("پی به اس", "fa-IR", "PS_TTM"),
                Alias("پی بر اس", "fa-IR", "PS_TTM"),
                Alias("پی‌اس", "fa-IR", "PS_TTM"),
                Alias("پی اس", "fa-IR", "PS_TTM"),
            ],
            [Dependency("PS_RATIO")]),

        // EBIT (engine-derived composite: NET_PROFIT + FINANCE_COSTS + INCOME_TAX).
        Define("EBIT", "EBIT", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths],
            [Alias("ebit", "en-US", "EBIT"), Alias("سود قبل از بهره و مالیات", "fa-IR", "EBIT"),
             Alias("سود عملیاتی پیش از بهره", "fa-IR", "EBIT")],
            [Dependency("NET_PROFIT"), Dependency("FINANCE_COSTS"), Dependency("INCOME_TAX")]),

        // Engine-derived YoY growth metrics (depend on Codal line items from spec 023).
        DefineGrowth("REVENUE_GROWTH_YOY", "Revenue Growth YoY", "REVENUE",
            [Alias("revenue growth yoy", "en-US", "REVENUE_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد درآمد سالانه", "fa-IR", "REVENUE_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد فروش نسبت به سال قبل", "fa-IR", "REVENUE_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد فروش سالانه", "fa-IR", "REVENUE_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("GROSS_PROFIT_GROWTH_YOY", "Gross Profit Growth YoY", "GROSS_PROFIT",
            [Alias("gross profit growth yoy", "en-US", "GROSS_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد سود ناخالص سالانه", "fa-IR", "GROSS_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("OPERATING_PROFIT_GROWTH_YOY", "Operating Profit Growth YoY", "OPERATING_PROFIT",
            [Alias("operating profit growth yoy", "en-US", "OPERATING_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد سود عملیاتی سالانه", "fa-IR", "OPERATING_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("EPS_GROWTH_YOY", "EPS Growth YoY", "EPS",
            [Alias("eps growth yoy", "en-US", "EPS_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد سود هر سهم سالانه", "fa-IR", "EPS_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("EBIT_GROWTH_YOY", "EBIT Growth YoY", "EBIT",
            [Alias("ebit growth yoy", "en-US", "EBIT_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد سود قبل از بهره و مالیات سالانه", "fa-IR", "EBIT_GROWTH_YOY", GrowthComparison.YearOverYear)]),
        DefineGrowth("EQUITY_GROWTH_YOY", "Equity Growth YoY", "TOTAL_EQUITY",
            [Alias("equity growth yoy", "en-US", "EQUITY_GROWTH_YOY", GrowthComparison.YearOverYear),
             Alias("رشد حقوق صاحبان سهام سالانه", "fa-IR", "EQUITY_GROWTH_YOY", GrowthComparison.YearOverYear)]),

        // Engine-derived QoQ growth metrics (use CodalDiscreteQuarterDeriver to convert cumulative first).
        DefineGrowth("REVENUE_GROWTH_QOQ", "Revenue Growth QoQ", "REVENUE",
            [Alias("revenue growth qoq", "en-US", "REVENUE_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد درآمد فصلی", "fa-IR", "REVENUE_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد فروش فصلی", "fa-IR", "REVENUE_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد فروش نسبت به فصل قبل", "fa-IR", "REVENUE_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد فروش", "fa-IR", "REVENUE_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),
        DefineGrowth("GROSS_PROFIT_GROWTH_QOQ", "Gross Profit Growth QoQ", "GROSS_PROFIT",
            [Alias("gross profit growth qoq", "en-US", "GROSS_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود ناخالص فصلی", "fa-IR", "GROSS_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),
        DefineGrowth("OPERATING_PROFIT_GROWTH_QOQ", "Operating Profit Growth QoQ", "OPERATING_PROFIT",
            [Alias("operating profit growth qoq", "en-US", "OPERATING_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود عملیاتی فصلی", "fa-IR", "OPERATING_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود عملیاتی نسبت به فصل قبل", "fa-IR", "OPERATING_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),
        DefineGrowth("EPS_GROWTH_QOQ", "EPS Growth QoQ", "EPS",
            [Alias("eps growth qoq", "en-US", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود هر سهم فصلی", "fa-IR", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود فصلی", "fa-IR", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود نسبت به فصل قبل", "fa-IR", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود", "fa-IR", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد فصلی", "fa-IR", "EPS_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),
        DefineGrowth("EBIT_GROWTH_QOQ", "EBIT Growth QoQ", "EBIT",
            [Alias("ebit growth qoq", "en-US", "EBIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد سود قبل از بهره و مالیات فصلی", "fa-IR", "EBIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),
        DefineGrowth("EQUITY_GROWTH_QOQ", "Equity Growth QoQ", "TOTAL_EQUITY",
            [Alias("equity growth qoq", "en-US", "EQUITY_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter),
             Alias("رشد حقوق صاحبان سهام فصلی", "fa-IR", "EQUITY_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)]),

        // Vendor-precomputed growth rates (CodalDB FinancialRatios; CalculationPolicyVersion = "codal-ratio-source-v1").
        // Codes are distinct from engine-derived codes so they never overwrite each other.
        DefineRatio("SALES_GROWTH_RATE", "Sales Growth Rate (Vendor)", MetricCategory.Growth, Percentage,
            [Alias("sales growth rate", "en-US", "SALES_GROWTH_RATE"),
             Alias("نرخ رشد فروش", "fa-IR", "SALES_GROWTH_RATE")]),
        DefineRatio("NET_PROFIT_GROWTH_RATE", "Net Profit Growth Rate (Vendor)", MetricCategory.Growth, Percentage,
            [Alias("net profit growth rate", "en-US", "NET_PROFIT_GROWTH_RATE"),
             Alias("نرخ رشد سود خالص", "fa-IR", "NET_PROFIT_GROWTH_RATE")]),
        DefineRatio("EQUITY_GROWTH_RATE", "Equity Growth Rate (Vendor)", MetricCategory.Growth, Percentage,
            [Alias("equity growth rate", "en-US", "EQUITY_GROWTH_RATE"),
             Alias("نرخ رشد حقوق صاحبان سهام", "fa-IR", "EQUITY_GROWTH_RATE")]),
        DefineRatio("TOTAL_ASSETS_GROWTH_RATE", "Total Assets Growth Rate (Vendor)", MetricCategory.Growth, Percentage,
            [Alias("total assets growth rate", "en-US", "TOTAL_ASSETS_GROWTH_RATE"),
             Alias("نرخ رشد کل دارایی‌ها", "fa-IR", "TOTAL_ASSETS_GROWTH_RATE")]),
        DefineRatio("TOTAL_DEBT_GROWTH_RATE", "Total Debt Growth Rate (Vendor)", MetricCategory.Growth, Percentage,
            [Alias("total debt growth rate", "en-US", "TOTAL_DEBT_GROWTH_RATE"),
             Alias("نرخ رشد کل بدهی‌ها", "fa-IR", "TOTAL_DEBT_GROWTH_RATE")]),
        DefineRatio("TANGIBLE_FIXED_ASSETS_GROWTH_RATE", "Tangible Fixed Assets Growth Rate (Vendor)",
            MetricCategory.Growth, Percentage,
            [Alias("tangible fixed assets growth rate", "en-US", "TANGIBLE_FIXED_ASSETS_GROWTH_RATE"),
             Alias("نرخ رشد دارایی‌های ثابت مشهود", "fa-IR", "TANGIBLE_FIXED_ASSETS_GROWTH_RATE")]),

        // CodalDB vendor-precomputed ratios (CalculationPolicyVersion = "codal-ratio-source-v1").
        // Period types: 3/6/9/12-month cumulative, matching FinancialRatios.PeriodType values.
        DefineRatio("CURRENT_RATIO", "Current Ratio", MetricCategory.FinancialHealth, Ratio,
            [Alias("current ratio", "en-US", "CURRENT_RATIO"), Alias("نسبت جاری", "fa-IR", "CURRENT_RATIO")]),
        DefineRatio("QUICK_RATIO", "Quick Ratio", MetricCategory.FinancialHealth, Ratio,
            [Alias("quick ratio", "en-US", "QUICK_RATIO"), Alias("نسبت آنی", "fa-IR", "QUICK_RATIO"),
             Alias("نسبت سریع", "fa-IR", "QUICK_RATIO")]),
        DefineRatio("NET_WORKING_CAPITAL", "Net Working Capital", MetricCategory.FinancialHealth, Amount,
            [Alias("net working capital", "en-US", "NET_WORKING_CAPITAL"),
             Alias("سرمایه در گردش خالص", "fa-IR", "NET_WORKING_CAPITAL")]),
        DefineRatio("COMPREHENSIVE_LIQUIDITY_INDEX", "Comprehensive Liquidity Index",
            MetricCategory.FinancialHealth, Ratio,
            [Alias("comprehensive liquidity index", "en-US", "COMPREHENSIVE_LIQUIDITY_INDEX"),
             Alias("شاخص نقدینگی جامع", "fa-IR", "COMPREHENSIVE_LIQUIDITY_INDEX")]),
        DefineRatio("CURRENT_ASSETS_TO_TOTAL_ASSETS", "Current Assets to Total Assets",
            MetricCategory.FinancialHealth, Ratio,
            [Alias("current assets to total assets", "en-US", "CURRENT_ASSETS_TO_TOTAL_ASSETS"),
             Alias("نسبت دارایی‌های جاری به کل دارایی‌ها", "fa-IR", "CURRENT_ASSETS_TO_TOTAL_ASSETS")]),
        DefineRatio("CURRENT_DEBT_TO_TOTAL_ASSETS", "Current Debt to Total Assets",
            MetricCategory.FinancialHealth, Ratio,
            [Alias("current debt to total assets", "en-US", "CURRENT_DEBT_TO_TOTAL_ASSETS"),
             Alias("نسبت بدهی جاری به کل دارایی‌ها", "fa-IR", "CURRENT_DEBT_TO_TOTAL_ASSETS")]),
        DefineRatio("ASSET_TURNOVER", "Asset Turnover", MetricCategory.FinancialHealth, Ratio,
            [Alias("asset turnover", "en-US", "ASSET_TURNOVER"),
             Alias("گردش دارایی‌ها", "fa-IR", "ASSET_TURNOVER")]),
        DefineRatio("TANGIBLE_FIXED_ASSETS_TURNOVER", "Tangible Fixed Assets Turnover",
            MetricCategory.FinancialHealth, Ratio,
            [Alias("tangible fixed assets turnover", "en-US", "TANGIBLE_FIXED_ASSETS_TURNOVER"),
             Alias("گردش دارایی‌های ثابت مشهود", "fa-IR", "TANGIBLE_FIXED_ASSETS_TURNOVER")]),
        DefineRatio("OPERATING_ASSETS_RATIO", "Operating Assets Ratio", MetricCategory.FinancialHealth, Ratio,
            [Alias("operating assets ratio", "en-US", "OPERATING_ASSETS_RATIO"),
             Alias("نسبت دارایی‌های عملیاتی", "fa-IR", "OPERATING_ASSETS_RATIO")]),
        DefineRatio("AVERAGE_COLLECTION_PERIOD", "Average Collection Period",
            MetricCategory.FinancialHealth, Days,
            [Alias("average collection period", "en-US", "AVERAGE_COLLECTION_PERIOD"),
             Alias("دوره وصول مطالبات", "fa-IR", "AVERAGE_COLLECTION_PERIOD")]),
        DefineRatio("RETURN_ON_ASSETS", "Return on Assets", MetricCategory.Profitability, Percentage,
            [Alias("return on assets", "en-US", "RETURN_ON_ASSETS"), Alias("roa", "en-US", "RETURN_ON_ASSETS"),
             Alias("بازده دارایی‌ها", "fa-IR", "RETURN_ON_ASSETS")]),
        DefineRatio("RETURN_ON_EQUITY", "Return on Equity", MetricCategory.Profitability, Percentage,
            [Alias("return on equity", "en-US", "RETURN_ON_EQUITY"), Alias("roe", "en-US", "RETURN_ON_EQUITY"),
             Alias("بازده حقوق صاحبان سهام", "fa-IR", "RETURN_ON_EQUITY")]),
        DefineRatio("RETURN_ON_INVESTMENT", "Return on Investment", MetricCategory.Profitability, Percentage,
            [Alias("return on investment", "en-US", "RETURN_ON_INVESTMENT"), Alias("roi", "en-US", "RETURN_ON_INVESTMENT"),
             Alias("بازده سرمایه‌گذاری", "fa-IR", "RETURN_ON_INVESTMENT")]),
        DefineRatio("NET_RETURN_ON_WORKING_CAPITAL", "Net Return on Working Capital",
            MetricCategory.Profitability, Percentage,
            [Alias("net return on working capital", "en-US", "NET_RETURN_ON_WORKING_CAPITAL"),
             Alias("بازده خالص سرمایه در گردش", "fa-IR", "NET_RETURN_ON_WORKING_CAPITAL")]),
        // Margin metrics sourced from CyclicalWaves quarterly income statement line items
        // (GROSS_PROFIT_MARGIN, OPERATING_PROFIT_MARGIN, NET_PROFIT_MARGIN in FinancialStatementLineItems).
        // Data is already persisted by CyclicalWavesFinancialStatementNormalizer; these definitions
        // make them queryable by the semantic alias resolver.
        Define("NET_PROFIT_MARGIN", "Net Profit Margin", MetricCategory.Profitability, Percentage,
            [FiscalPeriodType.ThreeMonths],
            [Alias("net profit margin", "en-US", "NET_PROFIT_MARGIN"),
             Alias("حاشیه سود خالص", "fa-IR", "NET_PROFIT_MARGIN"),
             Alias("حاشیه سود", "fa-IR", "NET_PROFIT_MARGIN"),
             Alias("مارجین خالص", "fa-IR", "NET_PROFIT_MARGIN"),
             Alias("مارجین سود خالص", "fa-IR", "NET_PROFIT_MARGIN")],
            [Dependency("NET_PROFIT_MARGIN")]),
        Define("GROSS_PROFIT_MARGIN", "Gross Profit Margin", MetricCategory.Profitability, Percentage,
            [FiscalPeriodType.ThreeMonths],
            [Alias("gross profit margin", "en-US", "GROSS_PROFIT_MARGIN"),
             Alias("حاشیه سود ناخالص", "fa-IR", "GROSS_PROFIT_MARGIN"),
             Alias("مارجین ناخالص", "fa-IR", "GROSS_PROFIT_MARGIN"),
             Alias("مارجین سود ناخالص", "fa-IR", "GROSS_PROFIT_MARGIN")],
            [Dependency("GROSS_PROFIT_MARGIN")]),
        Define("OPERATING_PROFIT_MARGIN", "Operating Profit Margin", MetricCategory.Profitability, Percentage,
            [FiscalPeriodType.ThreeMonths],
            [Alias("operating profit margin", "en-US", "OPERATING_PROFIT_MARGIN"),
             Alias("حاشیه سود عملیاتی", "fa-IR", "OPERATING_PROFIT_MARGIN"),
             Alias("مارجین عملیاتی", "fa-IR", "OPERATING_PROFIT_MARGIN"),
             Alias("مارجین سود عملیاتی", "fa-IR", "OPERATING_PROFIT_MARGIN")],
            [Dependency("OPERATING_PROFIT_MARGIN")]),
        DefineRatio("DEBT_TO_EQUITY", "Debt to Equity", MetricCategory.FinancialHealth, Ratio,
            [Alias("debt to equity", "en-US", "DEBT_TO_EQUITY"), Alias("d/e ratio", "en-US", "DEBT_TO_EQUITY"),
             Alias("نسبت بدهی به حقوق صاحبان سهام", "fa-IR", "DEBT_TO_EQUITY")])
    ];

    public static IReadOnlyCollection<MetricCalculationPolicy> Policies { get; } =
    [
        GrowthPolicy("NET_PROFIT_GROWTH_YOY", "yoy-quarterly-v1", GrowthComparison.YearOverYear, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("NET_PROFIT_GROWTH_QOQ", "qoq-quarterly-v1", GrowthComparison.QuarterOverQuarter, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("MONTHLY_SALES_GROWTH_YOY", "yoy-monthly-sales-v1", GrowthComparison.YearOverYear, "MONTHLY_SALES", FiscalPeriodType.Monthly),
        GrowthPolicy("MONTHLY_SALES_GROWTH_MOM", "mom-monthly-sales-v1", GrowthComparison.MonthOverMonth, "MONTHLY_SALES", FiscalPeriodType.Monthly),
        GrowthPolicy("MONTHLY_PRODUCTION_GROWTH_YOY", "yoy-monthly-production-v1", GrowthComparison.YearOverYear, "MONTHLY_PRODUCTION_QUANTITY", FiscalPeriodType.Monthly),
        GrowthPolicy("MONTHLY_SALES_QUANTITY_GROWTH_YOY", "yoy-monthly-sales-quantity-v1", GrowthComparison.YearOverYear, "MONTHLY_SALES_QUANTITY", FiscalPeriodType.Monthly),
        // Spec 057: per-month identity persistence of the normalized monthly-activity aggregates so
        // the symbol lookup reads them from DerivedMetrics. MONTHLY_SALES_RATE is the
        // quantity-weighted average rate (Σ sales amount ÷ Σ sales quantity over the month's line
        // items where both are present), computed in its normalized input source.
        MonthlySourcePolicy("MONTHLY_SALES", "monthly-sales-source-v1"),
        MonthlySourcePolicy("MONTHLY_SALES_YTD", "monthly-sales-ytd-source-v1"),
        MonthlySourcePolicy("MONTHLY_SALES_YTD_PREVIOUS_MONTH", "monthly-sales-ytd-previous-month-source-v1"),
        MonthlySourcePolicy("MONTHLY_SALES_QUANTITY", "monthly-sales-quantity-source-v1"),
        MonthlySourcePolicy("MONTHLY_PRODUCTION_QUANTITY", "monthly-production-quantity-source-v1"),
        MonthlySourcePolicy("MONTHLY_SALES_RATE", "monthly-sales-rate-source-v1"),
        SumPolicy("TTM_SALES", "ttm-sales-v1", "MONTHLY_SALES", FiscalPeriodType.Monthly),
        SumPolicy("TTM_EARNINGS", "ttm-earnings-v1", "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        new MetricCalculationPolicy(
            new MetricCode("TTM_EPS"),
            new CalculationPolicyVersion("ttm-eps-v1"),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("TTM_EARNINGS"), FiscalPeriodType.TrailingTwelveMonths, true),
                new MetricDataRequirement(new MetricCode("SHARES_OUTSTANDING"), FiscalPeriodType.TrailingTwelveMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("ttm-earnings-divided-by-shares", "TTM earnings divided by shares outstanding."),
            EffectiveFrom),
        // EBIT composite policy (NET_PROFIT + FINANCE_COSTS + INCOME_TAX).
        // MissingDataPolicy: OPERATING_PROFIT (ItemId 140) is documented as a proxy fallback when
        // component items are absent; the proxy substitution is deferred to the orchestration layer.
        new MetricCalculationPolicy(
            new MetricCode("EBIT"),
            new CalculationPolicyVersion("ebit-composite-v1"),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("NET_PROFIT"), FiscalPeriodType.ThreeMonths, true),
                new MetricDataRequirement(new MetricCode("FINANCE_COSTS"), FiscalPeriodType.ThreeMonths, true),
                new MetricDataRequirement(new MetricCode("INCOME_TAX"), FiscalPeriodType.ThreeMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("additive-composite",
                "EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX. Proxy: OPERATING_PROFIT when components missing (deferred)."),
            EffectiveFrom),

        // CyclicalWaves statement snapshots — passthrough from line items → DerivedMetrics.
        QuarterlySourcePolicy("REVENUE", "cw-revenue-passthrough-v1"),
        QuarterlySourcePolicy("NET_PROFIT", "cw-net-profit-passthrough-v1"),
        QuarterlySourcePolicy("GROSS_PROFIT", "cw-gross-profit-passthrough-v1"),
        QuarterlySourcePolicy("OPERATING_PROFIT", "cw-operating-profit-passthrough-v1"),

        // CyclicalWaves pre-computed average metrics — passthrough from line items → DerivedMetrics.
        new MetricCalculationPolicy(
            new MetricCode("AVG_4Q_REVENUE"),
            new CalculationPolicyVersion("cw-avg-4q-revenue-passthrough-v1"),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("AVG_4Q_REVENUE"), FiscalPeriodType.ThreeMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula("vendor-line-item-passthrough", "Vendor-supplied 4-quarter rolling average of quarterly revenue from CyclicalWaves."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("AVG_12M_MONTHLY_SALES"),
            new CalculationPolicyVersion("cw-avg-12m-monthly-sales-passthrough-v1"),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("AVG_12M_MONTHLY_SALES"), FiscalPeriodType.Monthly, true)],
            new MetricVersion("v1"),
            new MetricFormula("vendor-line-item-passthrough", "Vendor-supplied 12-month rolling average of monthly sales from CyclicalWaves."),
            EffectiveFrom),

        // CyclicalWaves margin snapshots — passthrough from FinancialStatementLineItems → DerivedMetrics.
        new MetricCalculationPolicy(
            new MetricCode("NET_PROFIT_MARGIN"),
            new CalculationPolicyVersion("cw-net-profit-margin-passthrough-v1"),
            MetricValueUnit.Percentage,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("NET_PROFIT_MARGIN"), FiscalPeriodType.ThreeMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula("vendor-line-item-passthrough", "Vendor-supplied net profit margin from CyclicalWaves quarterly income statement snapshot."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("GROSS_PROFIT_MARGIN"),
            new CalculationPolicyVersion("cw-gross-profit-margin-passthrough-v1"),
            MetricValueUnit.Percentage,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("GROSS_PROFIT_MARGIN"), FiscalPeriodType.ThreeMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula("vendor-line-item-passthrough", "Vendor-supplied gross profit margin from CyclicalWaves quarterly income statement snapshot."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("OPERATING_PROFIT_MARGIN"),
            new CalculationPolicyVersion("cw-operating-profit-margin-passthrough-v1"),
            MetricValueUnit.Percentage,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("OPERATING_PROFIT_MARGIN"), FiscalPeriodType.ThreeMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula("vendor-line-item-passthrough", "Vendor-supplied operating profit margin from CyclicalWaves quarterly income statement snapshot."),
            EffectiveFrom),

        // Engine-derived YoY growth policies (same cumulative period, shifted −12 months).
        GrowthPolicy("REVENUE_GROWTH_YOY",         "yoy-revenue-v1",          GrowthComparison.YearOverYear,          "REVENUE",           FiscalPeriodType.ThreeMonths),
        GrowthPolicy("GROSS_PROFIT_GROWTH_YOY",    "yoy-gross-profit-v1",     GrowthComparison.YearOverYear,          "GROSS_PROFIT",      FiscalPeriodType.ThreeMonths),
        GrowthPolicy("OPERATING_PROFIT_GROWTH_YOY","yoy-operating-profit-v1", GrowthComparison.YearOverYear,          "OPERATING_PROFIT",  FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EPS_GROWTH_YOY",             "yoy-eps-v1",              GrowthComparison.YearOverYear,          "EPS",               FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EBIT_GROWTH_YOY",            "yoy-ebit-v1",             GrowthComparison.YearOverYear,          "EBIT",              FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EQUITY_GROWTH_YOY",          "yoy-equity-v1",           GrowthComparison.YearOverYear,          "TOTAL_EQUITY",      FiscalPeriodType.ThreeMonths),

        // Engine-derived QoQ growth policies (discrete ThreeMonths via CodalDiscreteQuarterDeriver).
        GrowthPolicy("REVENUE_GROWTH_QOQ",         "qoq-revenue-v1",          GrowthComparison.QuarterOverQuarter,    "REVENUE",           FiscalPeriodType.ThreeMonths),
        GrowthPolicy("GROSS_PROFIT_GROWTH_QOQ",    "qoq-gross-profit-v1",     GrowthComparison.QuarterOverQuarter,    "GROSS_PROFIT",      FiscalPeriodType.ThreeMonths),
        GrowthPolicy("OPERATING_PROFIT_GROWTH_QOQ","qoq-operating-profit-v1", GrowthComparison.QuarterOverQuarter,    "OPERATING_PROFIT",  FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EPS_GROWTH_QOQ",             "qoq-eps-v1",              GrowthComparison.QuarterOverQuarter,    "EPS",               FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EBIT_GROWTH_QOQ",            "qoq-ebit-v1",             GrowthComparison.QuarterOverQuarter,    "EBIT",              FiscalPeriodType.ThreeMonths),
        GrowthPolicy("EQUITY_GROWTH_QOQ",          "qoq-equity-v1",           GrowthComparison.QuarterOverQuarter,    "TOTAL_EQUITY",      FiscalPeriodType.ThreeMonths),

        new MetricCalculationPolicy(
            new MetricCode("PE_TTM"),
            new CalculationPolicyVersion("vendor-pe-ratio-passthrough-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("PE_RATIO"), FiscalPeriodType.ThreeMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("vendor-pe-ratio-passthrough", "Vendor-supplied P/E ratio from CyclicalWaves quarterly income statement snapshot."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("PS_TTM"),
            new CalculationPolicyVersion("vendor-ps-ratio-passthrough-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("PS_RATIO"), FiscalPeriodType.ThreeMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("vendor-ps-ratio-passthrough", "Vendor-supplied P/S ratio from CyclicalWaves quarterly income statement snapshot."),
            EffectiveFrom)
    ];

    // Growth metrics are computed over ThreeMonths periods (Q1 cumulative = Q1 discrete).
    // YoY uses the same cumulative period shifted one year back; QoQ uses discrete quarters
    // produced by CodalDiscreteQuarterDeriver before being passed to the growth calculator.
    private static FinancialMetricDefinition DefineGrowth(
        string code,
        string name,
        string dependencyCode,
        IReadOnlyCollection<MetricAlias> aliases) =>
        Define(code, name, MetricCategory.Growth, Percentage,
            [FiscalPeriodType.ThreeMonths],
            aliases,
            [Dependency(dependencyCode)]);

    // Vendor-precomputed ratios support cumulative quarterly periods (3/6/9/12-month) matching
    // CodalDB FinancialRatios.PeriodType values; no dependencies (values come from the vendor).
    private static FinancialMetricDefinition DefineRatio(
        string code,
        string name,
        MetricCategory category,
        MetricUnit unit,
        IReadOnlyCollection<MetricAlias> aliases) =>
        Define(code, name, category, unit,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths,
             FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            aliases, []);

    private static FinancialMetricDefinition DefineSource(
        string code,
        string name,
        MetricCategory category,
        MetricUnit unit,
        FiscalPeriodType periodType) =>
        Define(code, name, category, unit, [periodType], [], []);

    private static FinancialMetricDefinition Define(
        string code,
        string name,
        MetricCategory category,
        MetricUnit unit,
        IReadOnlyCollection<FiscalPeriodType> periodTypes,
        IReadOnlyCollection<MetricAlias> aliases,
        IReadOnlyCollection<MetricDependency> dependencies) =>
        new(
            new MetricCode(code),
            new MetricVersion("v1"),
            name,
            $"Governed semantic definition for {name}.",
            category,
            unit,
            EffectiveFrom,
            null,
            periodTypes,
            aliases,
            dependencies,
            dependencies.Select(dependency =>
                new MetricDataRequirement(dependency.MetricCode, DependencyRequirementPeriod(periodTypes), dependency.Required)).ToArray());

    private static FiscalPeriodType DependencyRequirementPeriod(IReadOnlyCollection<FiscalPeriodType> periodTypes) =>
        periodTypes.Count == 1
            ? periodTypes.Single()
            : periodTypes.Contains(FiscalPeriodType.ThreeMonths)
                ? FiscalPeriodType.ThreeMonths
                : periodTypes.First();

    private static MetricAlias Alias(
        string expression,
        string language,
        string code,
        GrowthComparison? comparison = null) =>
        new(expression, language, new MetricCode(code), new MetricVersion("v1"), comparison);

    private static MetricDependency Dependency(string code) =>
        new(new MetricCode(code), new MetricVersion("v1"));

    private static MetricCalculationPolicy GrowthPolicy(
        string code,
        string policyVersion,
        GrowthComparison? comparison,
        string dependencyCode,
        FiscalPeriodType periodType) =>
        new(
            new MetricCode(code),
            new CalculationPolicyVersion(policyVersion),
            MetricValueUnit.Percentage,
            comparison,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode(dependencyCode), periodType, true)],
            new MetricVersion("v1"),
            new MetricFormula("percent-change", "Percentage change between governed current and comparison observations."),
            EffectiveFrom);

    // Spec 057: identity persistence of a normalized monthly-activity source aggregate, one
    // DerivedMetrics row per company-month (the calculator selects the observation matching the
    // effective monthly period).
    private static MetricCalculationPolicy MonthlySourcePolicy(string code, string policyVersion) =>
        new(
            new MetricCode(code),
            new CalculationPolicyVersion(policyVersion),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode(code), FiscalPeriodType.Monthly, true)],
            new MetricVersion("v1"),
            new MetricFormula(
                "monthly-source-identity",
                "Normalized Noavaran monthly-activity aggregate for the Shamsi month, persisted per month."),
            EffectiveFrom);

    private static MetricCalculationPolicy QuarterlySourcePolicy(string code, string policyVersion) =>
        new(
            new MetricCode(code),
            new CalculationPolicyVersion(policyVersion),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode(code), FiscalPeriodType.ThreeMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula(
                "quarterly-source-identity",
                "CyclicalWaves provider-precomputed quarterly snapshot, persisted without arithmetic transformation."),
            EffectiveFrom);

    private static MetricCalculationPolicy SumPolicy(
        string code,
        string policyVersion,
        string dependencyCode,
        FiscalPeriodType dependencyPeriodType) =>
        new(
            new MetricCode(code),
            new CalculationPolicyVersion(policyVersion),
            MetricValueUnit.Amount,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode(dependencyCode), dependencyPeriodType, true)],
            new MetricVersion("v1"),
            new MetricFormula("trailing-twelve-month-sum", "Sum of required observations within the trailing twelve month period."),
            EffectiveFrom);
}
