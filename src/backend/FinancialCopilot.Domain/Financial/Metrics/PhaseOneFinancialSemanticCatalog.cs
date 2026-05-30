using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public static class PhaseOneFinancialSemanticCatalog
{
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 1);
    private static readonly MetricUnit Amount = new("amount", "Amount");
    private static readonly MetricUnit Percentage = new("percent", "Percent");
    private static readonly MetricUnit Ratio = new("ratio", "Ratio");
    private static readonly MetricUnit PerShare = new("amount-per-share", "Amount per share");

    public static IReadOnlyCollection<FinancialMetricDefinition> Definitions { get; } =
    [
        DefineSource("NET_PROFIT", "Net Profit", MetricCategory.Profitability, Amount, FiscalPeriodType.ThreeMonths),
        DefineSource("MONTHLY_SALES", "Monthly Sales", MetricCategory.SalesAndProduction, Amount, FiscalPeriodType.Monthly),
        // CodalDB income-statement source metrics (cumulative 3/6/9/12-month periods).
        Define("REVENUE", "Revenue", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("revenue", "en-US", "REVENUE"), Alias("sales", "en-US", "REVENUE"),
             Alias("درآمد", "fa-IR", "REVENUE"), Alias("فروش", "fa-IR", "REVENUE"), Alias("فروش خالص", "fa-IR", "REVENUE")],
            []),
        Define("TOTAL_REVENUE", "Total Revenue", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("total revenue", "en-US", "TOTAL_REVENUE"),
             Alias("کل درآمد", "fa-IR", "TOTAL_REVENUE"), Alias("جمع درآمد", "fa-IR", "TOTAL_REVENUE")],
            []),
        Define("GROSS_PROFIT", "Gross Profit", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("gross profit", "en-US", "GROSS_PROFIT"),
             Alias("سود ناخالص", "fa-IR", "GROSS_PROFIT")],
            []),
        Define("OPERATING_PROFIT", "Operating Profit", MetricCategory.Profitability, Amount,
            [FiscalPeriodType.ThreeMonths, FiscalPeriodType.SixMonths, FiscalPeriodType.NineMonths, FiscalPeriodType.TwelveMonths],
            [Alias("operating profit", "en-US", "OPERATING_PROFIT"), Alias("ebit proxy", "en-US", "OPERATING_PROFIT"),
             Alias("سود عملیاتی", "fa-IR", "OPERATING_PROFIT")],
            []),
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
        DefineSource("LATEST_PRICE", "Latest Observed Price", MetricCategory.Valuation, Amount, FiscalPeriodType.TrailingTwelveMonths),
        DefineSource("MARKET_CAP", "Market Capitalization", MetricCategory.Valuation, Amount, FiscalPeriodType.TrailingTwelveMonths),
        DefineSource("SHARES_OUTSTANDING", "Shares Outstanding", MetricCategory.FinancialHealth, Amount, FiscalPeriodType.TrailingTwelveMonths),
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
                Alias("رشد سود خالص آخرین فصل", "fa-IR", "NET_PROFIT_GROWTH_YOY", GrowthComparison.YearOverYear)
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
                Alias("رشد سود خالص آخرین فصل", "fa-IR", "NET_PROFIT_GROWTH_QOQ", GrowthComparison.QuarterOverQuarter)
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
                Alias("رشد فروش ماهانه سالانه", "fa-IR", "MONTHLY_SALES_GROWTH_YOY", GrowthComparison.YearOverYear)
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
        Define(
            "PE_TTM",
            "P/E (TTM)",
            MetricCategory.Valuation,
            Ratio,
            [FiscalPeriodType.TrailingTwelveMonths],
            [Alias("p/e", "en-US", "PE_TTM"), Alias("نسبت پی به ای", "fa-IR", "PE_TTM")],
            [Dependency("LATEST_PRICE"), Dependency("TTM_EPS")]),
        Define(
            "PS_TTM",
            "P/S (TTM)",
            MetricCategory.Valuation,
            Ratio,
            [FiscalPeriodType.TrailingTwelveMonths],
            [Alias("p/s", "en-US", "PS_TTM"), Alias("نسبت قیمت به فروش", "fa-IR", "PS_TTM")],
            [Dependency("MARKET_CAP"), Dependency("TTM_SALES")])
    ];

    public static IReadOnlyCollection<MetricCalculationPolicy> Policies { get; } =
    [
        GrowthPolicy("NET_PROFIT_GROWTH_YOY", "yoy-quarterly-v1", GrowthComparison.YearOverYear, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("NET_PROFIT_GROWTH_QOQ", "qoq-quarterly-v1", GrowthComparison.QuarterOverQuarter, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("MONTHLY_SALES_GROWTH_YOY", "yoy-monthly-sales-v1", GrowthComparison.YearOverYear, "MONTHLY_SALES", FiscalPeriodType.Monthly),
        GrowthPolicy("MONTHLY_SALES_GROWTH_MOM", "mom-monthly-sales-v1", GrowthComparison.MonthOverMonth, "MONTHLY_SALES", FiscalPeriodType.Monthly),
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
        new MetricCalculationPolicy(
            new MetricCode("PE_TTM"),
            new CalculationPolicyVersion("ttm-valuation-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("LATEST_PRICE"), FiscalPeriodType.TrailingTwelveMonths, true),
                new MetricDataRequirement(new MetricCode("TTM_EPS"), FiscalPeriodType.TrailingTwelveMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("price-divided-by-ttm-eps", "Latest observed price divided by TTM EPS."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("PS_TTM"),
            new CalculationPolicyVersion("ttm-sales-valuation-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [
                new MetricDataRequirement(new MetricCode("MARKET_CAP"), FiscalPeriodType.TrailingTwelveMonths, true),
                new MetricDataRequirement(new MetricCode("TTM_SALES"), FiscalPeriodType.TrailingTwelveMonths, true)
            ],
            new MetricVersion("v1"),
            new MetricFormula("market-cap-divided-by-ttm-sales", "Market capitalization divided by TTM sales."),
            EffectiveFrom)
    ];

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
                new MetricDataRequirement(dependency.MetricCode, periodTypes.Single(), dependency.Required)).ToArray());

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
