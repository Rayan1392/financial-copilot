using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Domain.Financial.Metrics;

public static class PhaseOneFinancialSemanticCatalog
{
    private static readonly DateOnly EffectiveFrom = new(2026, 1, 1);
    private static readonly MetricUnit Amount = new("amount", "Amount");
    private static readonly MetricUnit Percentage = new("percent", "Percent");
    private static readonly MetricUnit Ratio = new("ratio", "Ratio");

    public static IReadOnlyCollection<FinancialMetricDefinition> Definitions { get; } =
    [
        DefineSource("NET_PROFIT", "Net Profit", MetricCategory.Profitability, Amount, FiscalPeriodType.ThreeMonths),
        DefineSource("MONTHLY_SALES", "Monthly Sales", MetricCategory.SalesAndProduction, Amount, FiscalPeriodType.Monthly),
        DefineSource("TTM_EPS", "TTM EPS", MetricCategory.Profitability, Amount, FiscalPeriodType.TrailingTwelveMonths),
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
            [Dependency("TTM_EPS")]),
        Define(
            "PS_TTM",
            "P/S (TTM)",
            MetricCategory.Valuation,
            Ratio,
            [FiscalPeriodType.TrailingTwelveMonths],
            [Alias("p/s", "en-US", "PS_TTM"), Alias("نسبت قیمت به فروش", "fa-IR", "PS_TTM")],
            [])
    ];

    public static IReadOnlyCollection<MetricCalculationPolicy> Policies { get; } =
    [
        GrowthPolicy("NET_PROFIT_GROWTH_YOY", "yoy-quarterly-v1", GrowthComparison.YearOverYear, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("NET_PROFIT_GROWTH_QOQ", "qoq-quarterly-v1", GrowthComparison.QuarterOverQuarter, "NET_PROFIT", FiscalPeriodType.ThreeMonths),
        GrowthPolicy("MONTHLY_SALES_GROWTH_YOY", "yoy-monthly-sales-v1", GrowthComparison.YearOverYear, "MONTHLY_SALES", FiscalPeriodType.Monthly),
        GrowthPolicy("MONTHLY_SALES_GROWTH_MOM", "mom-monthly-sales-v1", GrowthComparison.MonthOverMonth, "MONTHLY_SALES", FiscalPeriodType.Monthly),
        new MetricCalculationPolicy(
            new MetricCode("PE_TTM"),
            new CalculationPolicyVersion("ttm-valuation-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [new MetricDataRequirement(new MetricCode("TTM_EPS"), FiscalPeriodType.TrailingTwelveMonths, true)],
            new MetricVersion("v1"),
            new MetricFormula("price-divided-by-ttm-eps", "Latest observed price divided by TTM EPS."),
            EffectiveFrom),
        new MetricCalculationPolicy(
            new MetricCode("PS_TTM"),
            new CalculationPolicyVersion("ttm-sales-valuation-v1"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [],
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
}
