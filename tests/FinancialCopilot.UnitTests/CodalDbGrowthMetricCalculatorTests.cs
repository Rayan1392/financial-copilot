using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Tests for the EBIT additive composite calculator and the new engine-derived growth calculators
/// introduced in spec 026 (CodalDB derived growth metrics).
/// </summary>
public sealed class CodalDbGrowthMetricCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-04-30T09:00:00Z");

    // Iranian-style Q1: Apr 1, 2025 – Jun 30, 2025
    private static readonly FiscalPeriod Q1_2025 = FiscalPeriod.Closed(
        FiscalPeriodType.ThreeMonths, new DateOnly(2025, 4, 1), new DateOnly(2025, 6, 30));

    // Prior year Q1: Apr 1, 2024 – Jun 30, 2024
    private static readonly FiscalPeriod Q1_2024 = FiscalPeriod.Closed(
        FiscalPeriodType.ThreeMonths, new DateOnly(2024, 4, 1), new DateOnly(2024, 6, 30));

    // Prior quarter Q0: Jan 1, 2025 – Mar 31, 2025
    private static readonly FiscalPeriod Q0_2025 = FiscalPeriod.Closed(
        FiscalPeriodType.ThreeMonths, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31));

    // ── EBIT composite ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Ebit_AllComponentsPresent_SumsNetProfitFinanceCostsIncomeTax()
    {
        var calculator = new AdditiveCompositeMetricCalculator(
            new MetricCode("EBIT"),
            [new MetricCode("NET_PROFIT"), new MetricCode("FINANCE_COSTS"), new MetricCode("INCOME_TAX")]);
        var context = Context("EBIT", "ebit-composite-v1", Q1_2025,
        [
            Obs("NET_PROFIT",    Q1_2025, 500m),
            Obs("FINANCE_COSTS", Q1_2025, 80m),
            Obs("INCOME_TAX",    Q1_2025, 120m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(700m, result.Value); // 500 + 80 + 120
    }

    [Fact]
    public async Task Ebit_MissingComponent_ReturnsNullWithMissingDataWarning()
    {
        var calculator = new AdditiveCompositeMetricCalculator(
            new MetricCode("EBIT"),
            [new MetricCode("NET_PROFIT"), new MetricCode("FINANCE_COSTS"), new MetricCode("INCOME_TAX")]);
        var context = Context("EBIT", "ebit-composite-v1", Q1_2025,
        [
            Obs("NET_PROFIT", Q1_2025, 500m),
            // FINANCE_COSTS missing
            Obs("INCOME_TAX", Q1_2025, 120m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.True(result.Quality.HasMissingData);
    }

    [Fact]
    public async Task Ebit_NullComponentValue_ReturnsNull()
    {
        var calculator = new AdditiveCompositeMetricCalculator(
            new MetricCode("EBIT"),
            [new MetricCode("NET_PROFIT"), new MetricCode("FINANCE_COSTS"), new MetricCode("INCOME_TAX")]);
        var context = Context("EBIT", "ebit-composite-v1", Q1_2025,
        [
            Obs("NET_PROFIT",    Q1_2025, 500m),
            Obs("FINANCE_COSTS", Q1_2025, null),  // null value
            Obs("INCOME_TAX",    Q1_2025, 120m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Ebit_WrongPeriodObservations_IgnoredInSum()
    {
        // Components for a different period must not contaminate the EBIT for Q1_2025.
        var calculator = new AdditiveCompositeMetricCalculator(
            new MetricCode("EBIT"),
            [new MetricCode("NET_PROFIT"), new MetricCode("FINANCE_COSTS"), new MetricCode("INCOME_TAX")]);
        var context = Context("EBIT", "ebit-composite-v1", Q1_2025,
        [
            Obs("NET_PROFIT",    Q1_2025, 500m),
            Obs("FINANCE_COSTS", Q1_2025, 80m),
            Obs("INCOME_TAX",    Q1_2025, 120m),
            Obs("NET_PROFIT",    Q1_2024, 999m),  // prior year — must be ignored
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(700m, result.Value);
    }

    // ── Engine-derived YoY growth calculators ───────────────────────────────────

    [Fact]
    public async Task RevenueGrowthYoy_50PercentGrowth_CalculatesCorrectly()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("REVENUE_GROWTH_YOY"), new MetricCode("REVENUE"));
        var context = Context("REVENUE_GROWTH_YOY", "yoy-revenue-v1", Q1_2025,
        [
            Obs("REVENUE", Q1_2025, 300m),
            Obs("REVENUE", Q1_2024, 200m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(50m, result.Value); // (300-200)/200 × 100
    }

    [Fact]
    public async Task EpsGrowthYoy_DoubledEarnings_Returns100Percent()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("EPS_GROWTH_YOY"), new MetricCode("EPS"));
        var context = Context("EPS_GROWTH_YOY", "yoy-eps-v1", Q1_2025,
        [
            Obs("EPS", Q1_2025, 20m),
            Obs("EPS", Q1_2024, 10m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(100m, result.Value);
    }

    [Fact]
    public async Task EquityGrowthYoy_ZeroPriorDenominator_ReturnsNullWithWarning()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("EQUITY_GROWTH_YOY"), new MetricCode("TOTAL_EQUITY"));
        var context = Context("EQUITY_GROWTH_YOY", "yoy-equity-v1", Q1_2025,
        [
            Obs("TOTAL_EQUITY", Q1_2025, 1000m),
            Obs("TOTAL_EQUITY", Q1_2024, 0m)   // zero denominator
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.True(result.Quality.HasMissingData);
    }

    [Fact]
    public async Task GrossProfitGrowthYoy_MissingPriorPeriod_ReturnsNull()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("GROSS_PROFIT_GROWTH_YOY"), new MetricCode("GROSS_PROFIT"));
        var context = Context("GROSS_PROFIT_GROWTH_YOY", "yoy-gross-profit-v1", Q1_2025,
        [
            Obs("GROSS_PROFIT", Q1_2025, 500m)
            // prior period missing
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.True(result.Quality.HasMissingData);
    }

    // ── QoQ growth calculators (need discrete ThreeMonths input) ────────────────

    [Fact]
    public async Task OperatingProfitGrowthQoq_WithDiscreteQuarters_CalculatesCorrectly()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("OPERATING_PROFIT_GROWTH_QOQ"), new MetricCode("OPERATING_PROFIT"));
        // Effective period is Q1_2025; prior QoQ is Q0_2025 (shifted -3 months)
        var context = Context("OPERATING_PROFIT_GROWTH_QOQ", "qoq-operating-profit-v1", Q1_2025,
        [
            Obs("OPERATING_PROFIT", Q1_2025, 250m),
            Obs("OPERATING_PROFIT", Q0_2025, 200m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(25m, result.Value); // (250-200)/200 × 100
    }

    [Fact]
    public async Task EbitGrowthYoy_ComputedFromCompositeEbitInputs_CalculatesCorrectly()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("EBIT_GROWTH_YOY"), new MetricCode("EBIT"));
        var context = Context("EBIT_GROWTH_YOY", "yoy-ebit-v1", Q1_2025,
        [
            Obs("EBIT", Q1_2025, 800m),
            Obs("EBIT", Q1_2024, 500m)
        ]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(60m, result.Value); // (800-500)/500 × 100
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static MetricCalculationContext Context(
        string code, string policyVersion, FiscalPeriod period,
        IReadOnlyCollection<MetricInputObservation> inputs)
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        var definition = registry.ResolveDefinition(new MetricCode(code), AsOf);
        var policy = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies)
            .GetPolicy(definition.Code, new CalculationPolicyVersion(policyVersion));
        return new MetricCalculationContext(Guid.NewGuid(), definition, policy, period, inputs);
    }

    private static MetricInputObservation Obs(string code, FiscalPeriod period, decimal? value) =>
        new(new MetricCode(code), new MetricVersion("v1"),
            new CalculationPolicyVersion("src"), period, value,
            [new FinancialSourceEvidence("CodalDb", ObservedAt, ObservedAt)]);
}
