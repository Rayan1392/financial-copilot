using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.UnitTests;

public sealed class DerivedMetricCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 4, 30);
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-04-30T09:00:00Z");

    [Fact]
    public async Task PercentageGrowthCalculator_CalculatesQuarterlyNetProfitYearOverYear()
    {
        var context = Context(
            "NET_PROFIT_GROWTH_YOY",
            "yoy-quarterly-v1",
            Quarter(2026, 1),
            [
                Input("NET_PROFIT", Quarter(2026, 1), 150m),
                Input("NET_PROFIT", Quarter(2025, 1), 100m)
            ]);
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("NET_PROFIT_GROWTH_YOY"),
            new MetricCode("NET_PROFIT"));

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(50m, result.Value);
        Assert.Equal("yoy-quarterly-v1", result.CalculationPolicyVersion.Value);
    }

    [Theory]
    [InlineData("MONTHLY_SALES_GROWTH_YOY", "yoy-monthly-sales-v1", 2025, 4, 100)]
    [InlineData("MONTHLY_SALES_GROWTH_MOM", "mom-monthly-sales-v1", 2026, 3, 25)]
    public async Task PercentageGrowthCalculator_CalculatesMonthlySalesComparisons(
        string metricCode,
        string policyVersion,
        int comparisonYear,
        int comparisonMonth,
        decimal expected)
    {
        var context = Context(
            metricCode,
            policyVersion,
            Month(2026, 4),
            [
                Input("MONTHLY_SALES", Month(2026, 4), 200m),
                Input("MONTHLY_SALES", Month(comparisonYear, comparisonMonth), metricCode.EndsWith("YOY") ? 100m : 160m),
                Input("MONTHLY_SALES", Month(2026, 2), 1m)
            ]);
        var calculator = new PercentageGrowthMetricCalculator(new MetricCode(metricCode), new MetricCode("MONTHLY_SALES"));

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task PercentageGrowthCalculator_ReturnsMissingValueForZeroPriorDenominator()
    {
        var calculator = new PercentageGrowthMetricCalculator(
            new MetricCode("MONTHLY_SALES_GROWTH_MOM"),
            new MetricCode("MONTHLY_SALES"));
        var context = Context(
            "MONTHLY_SALES_GROWTH_MOM",
            "mom-monthly-sales-v1",
            Month(2026, 4),
            [Input("MONTHLY_SALES", Month(2026, 4), 100m), Input("MONTHLY_SALES", Month(2026, 3), 0m)]);

        var result = await calculator.CalculateAsync(context, CancellationToken.None);

        Assert.Null(result.Value);
        Assert.True(result.Quality.HasMissingData);
    }

    [Fact]
    public async Task TrailingTwelveMonthCalculators_SumSalesAndEarningsWhenAllPeriodsExist()
    {
        var salesCalculator = new TrailingTwelveMonthSumMetricCalculator(
            new MetricCode("TTM_SALES"),
            new MetricCode("MONTHLY_SALES"),
            12);
        var earningsCalculator = new TrailingTwelveMonthSumMetricCalculator(
            new MetricCode("TTM_EARNINGS"),
            new MetricCode("NET_PROFIT"),
            4);
        var ttm = Ttm();

        var sales = await salesCalculator.CalculateAsync(
            Context(
                "TTM_SALES",
                "ttm-sales-v1",
                ttm,
                Enumerable.Range(0, 12)
                    .Select(index => Input("MONTHLY_SALES", Month(2025 + (index + 4) / 12, (index + 4) % 12 + 1), 10m))
                    .ToArray()),
            CancellationToken.None);
        var earnings = await earningsCalculator.CalculateAsync(
            Context(
                "TTM_EARNINGS",
                "ttm-earnings-v1",
                ttm,
                [
                    Input("NET_PROFIT", Quarter(2025, 2), 10m),
                    Input("NET_PROFIT", Quarter(2025, 3), 20m),
                    Input("NET_PROFIT", Quarter(2025, 4), 30m),
                    Input("NET_PROFIT", Quarter(2026, 1), 40m)
                ]),
            CancellationToken.None);

        Assert.Equal(120m, sales.Value);
        Assert.Equal(100m, earnings.Value);
    }

    [Fact]
    public async Task EpsAndValuationCalculators_HandleValidAndInvalidDenominatorsWithQuoteEvidence()
    {
        var ttm = Ttm();
        var eps = await new EarningsPerShareMetricCalculator(
                new MetricCode("TTM_EPS"),
                new MetricCode("TTM_EARNINGS"),
                new MetricCode("SHARES_OUTSTANDING"))
            .CalculateAsync(
                Context(
                    "TTM_EPS",
                    "ttm-eps-v1",
                    ttm,
                    [Input("TTM_EARNINGS", ttm, 1_000m), Input("SHARES_OUTSTANDING", ttm, 100m)]),
                CancellationToken.None);
        var quoteEvidence = new FinancialSourceEvidence("QuoteProvider", ObservedAt, ObservedAt);
        var pe = await new ValuationRatioMetricCalculator(
                new MetricCode("PE_TTM"),
                new MetricCode("LATEST_PRICE"),
                new MetricCode("TTM_EPS"))
            .CalculateAsync(
                Context(
                    "PE_TTM",
                    "ttm-valuation-v1",
                    ttm,
                    [
                        Input("LATEST_PRICE", ttm, 50m, quoteEvidence),
                        Input("TTM_EPS", ttm, eps.Value)
                    ]),
                CancellationToken.None);
        var ps = await new ValuationRatioMetricCalculator(
                new MetricCode("PS_TTM"),
                new MetricCode("MARKET_CAP"),
                new MetricCode("TTM_SALES"))
            .CalculateAsync(
                Context(
                    "PS_TTM",
                    "ttm-sales-valuation-v1",
                    ttm,
                    [Input("MARKET_CAP", ttm, 500m, quoteEvidence), Input("TTM_SALES", ttm, 250m)]),
                CancellationToken.None);
        var invalid = await new ValuationRatioMetricCalculator(
                new MetricCode("PE_TTM"),
                new MetricCode("LATEST_PRICE"),
                new MetricCode("TTM_EPS"))
            .CalculateAsync(
                Context(
                    "PE_TTM",
                    "ttm-valuation-v1",
                    ttm,
                    [Input("LATEST_PRICE", ttm, 50m), Input("TTM_EPS", ttm, 0m)]),
                CancellationToken.None);

        Assert.Equal(10m, eps.Value);
        Assert.Equal(5m, pe.Value);
        Assert.Equal(2m, ps.Value);
        Assert.Contains(pe.SourceEvidence, evidence => evidence.SourceProvider == "QuoteProvider");
        Assert.Null(invalid.Value);
        Assert.True(invalid.Quality.HasMissingData);
    }

    [Fact]
    public async Task CalculationService_UsesRegisteredCalculatorAndPersistsVersionedEvidence()
    {
        var definition = new FinancialMetricDefinition(
            new MetricCode("CUSTOM_SCORE"),
            new MetricVersion("v3"),
            "Custom score",
            "Custom independent score.",
            MetricCategory.FinancialHealth,
            new MetricUnit("ratio", "Ratio"),
            new DateOnly(2026, 1, 1),
            null,
            [FiscalPeriodType.TrailingTwelveMonths],
            [],
            [],
            []);
        var policy = new MetricCalculationPolicy(
            definition.Code,
            new CalculationPolicyVersion("custom-policy-v2"),
            MetricValueUnit.Ratio,
            null,
            MissingDataPolicy.ReturnMissingValue,
            [],
            definition.Version);
        var store = new CapturingStore();
        var service = new DerivedMetricCalculationService(
            new FinancialMetricRegistry([definition], [new CustomCalculator()]),
            new MetricCalculationPolicyProvider([policy]),
            store);

        var result = await service.CalculateAsync(
            new CalculateDerivedMetricCommand(
                Guid.NewGuid(),
                definition.Code,
                policy.Version,
                Ttm(),
                []),
            CancellationToken.None);

        Assert.Equal(7m, result.Value);
        Assert.Equal("v3", result.MetricVersion.Value);
        Assert.Equal("custom-policy-v2", result.CalculationPolicyVersion.Value);
        Assert.Same(result, store.Stored);
    }

    private static MetricCalculationContext Context(
        string code,
        string policyVersion,
        FiscalPeriod period,
        IReadOnlyCollection<MetricInputObservation> inputs)
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        var definition = registry.ResolveDefinition(new MetricCode(code), AsOf);
        var policy = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies)
            .GetPolicy(definition.Code, new CalculationPolicyVersion(policyVersion));
        return new MetricCalculationContext(Guid.NewGuid(), definition, policy, period, inputs);
    }

    private static MetricInputObservation Input(
        string code,
        FiscalPeriod period,
        decimal? value,
        params FinancialSourceEvidence[] evidence) =>
        new(
            new MetricCode(code),
            new MetricVersion("v1"),
            new CalculationPolicyVersion("source-v1"),
            period,
            value,
            evidence.Length == 0
                ? [new FinancialSourceEvidence("NormalizedProvider", ObservedAt, ObservedAt)]
                : evidence);

    private static FiscalPeriod Month(int year, int month) =>
        FiscalPeriod.Closed(
            FiscalPeriodType.Monthly,
            new DateOnly(year, month, 1),
            new DateOnly(year, month, 1).AddMonths(1).AddDays(-1));

    private static FiscalPeriod Quarter(int year, int quarter)
    {
        var start = new DateOnly(year, (quarter - 1) * 3 + 1, 1);
        return FiscalPeriod.Closed(FiscalPeriodType.ThreeMonths, start, start.AddMonths(3).AddDays(-1));
    }

    private static FiscalPeriod Ttm() =>
        FiscalPeriod.Closed(
            FiscalPeriodType.TrailingTwelveMonths,
            new DateOnly(2025, 5, 1),
            new DateOnly(2026, 4, 30));

    private sealed class CapturingStore : IDerivedMetricResultStore
    {
        public DerivedMetric? Stored { get; private set; }

        public Task StoreAsync(DerivedMetric metric, CancellationToken cancellationToken)
        {
            Stored = metric;
            return Task.CompletedTask;
        }
    }

    private sealed class CustomCalculator : IFinancialMetricCalculator
    {
        public MetricCode MetricCode { get; } = new("CUSTOM_SCORE");

        public Task<MetricCalculationResult> CalculateAsync(
            MetricCalculationContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetricCalculationResult(
                MetricCode,
                context.Definition.Version,
                context.Policy.Version,
                context.EffectivePeriod,
                7m,
                context.Definition.Unit,
                FinancialCopilot.Domain.Financial.DataQuality.FinancialObservationQuality.Current(ObservedAt, ObservedAt),
                [],
                []));
    }
}
