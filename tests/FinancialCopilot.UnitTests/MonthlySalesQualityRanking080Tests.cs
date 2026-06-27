using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlySalesQualityRanking080Tests
{
    private readonly MonthlySalesQualityScoreCalculator _calculator = new();

    [Fact]
    public void Calculator_StrongSalesGrowthWithQuantityGrowth_ProducesStrongScore()
    {
        var result = _calculator.Calculate(new MonthlySalesQualityScoreInput(
            MonthlySalesAmount: 180m,
            Avg12MonthSalesAmount: 100m,
            PreviousMonthSalesAmount: 150m,
            SameMonthPreviousYearSalesAmount: 120m,
            MonthlySalesQuantity: 120m,
            PreviousMonthSalesQuantity: 100m,
            MonthlyProductionQuantity: 130m,
            PreviousMonthProductionQuantity: 110m,
            MonthlyAverageSalesRate: 1.5m,
            PreviousMonthAverageSalesRate: 1.4m,
            ProductMixRows:
            [
                new MonthlySalesQualityProductMixInput("کنسانتره", 120m, 60m, 1, true, 130m, 120m, 1.5m),
                new MonthlySalesQualityProductMixInput("گندله", 60m, 30m, 2, false, 60m, 50m, 1.2m),
                new MonthlySalesQualityProductMixInput("آپاتیت", 20m, 10m, 3, false, 20m, 15m, 1.0m)
            ],
            LastThreeMonthlySalesAmounts: [120m, 150m, 180m],
            IndustryPercentile: 0.9m,
            IndustryPeerCount: 12,
            HistoryMonths: 12,
            HasProductLineItems: true));

        Assert.InRange(result.QualityScore, 75m, 100m);
        Assert.Equal("گزارش بسیار قوی", result.QualityLabel);
        Assert.InRange(result.ConfidenceScore, 80m, 100m);
        Assert.Contains(result.PositiveDrivers, driver => driver.Contains("روند ۳ ماهه", StringComparison.Ordinal));
    }

    [Fact]
    public void Calculator_SalesGrowthDrivenByRateWithQuantityCollapse_PenalizesQuality()
    {
        var result = _calculator.Calculate(new MonthlySalesQualityScoreInput(
            MonthlySalesAmount: 160m,
            Avg12MonthSalesAmount: 100m,
            PreviousMonthSalesAmount: 120m,
            SameMonthPreviousYearSalesAmount: 110m,
            MonthlySalesQuantity: 60m,
            PreviousMonthSalesQuantity: 100m,
            MonthlyProductionQuantity: 70m,
            PreviousMonthProductionQuantity: 100m,
            MonthlyAverageSalesRate: 2.6m,
            PreviousMonthAverageSalesRate: 1.2m,
            ProductMixRows:
            [
                new MonthlySalesQualityProductMixInput("محصول اصلی", 140m, 90m, 1, true, 70m, 60m, 2.6m)
            ],
            LastThreeMonthlySalesAmounts: [100m, 120m, 160m],
            IndustryPercentile: 0.45m,
            IndustryPeerCount: 8,
            HistoryMonths: 12,
            HasProductLineItems: true));

        Assert.True(result.DimensionScores.QuantityGrowthQuality <= 35m);
        Assert.True(result.DimensionScores.RateGrowthQuality <= 45m);
        Assert.Contains(result.NegativeDrivers, driver => driver.Contains("افت مقدار", StringComparison.Ordinal));
    }

    [Fact]
    public void Calculator_MissingProductMix_ReweightsInsteadOfZeroingScore()
    {
        var result = _calculator.Calculate(new MonthlySalesQualityScoreInput(
            MonthlySalesAmount: 120m,
            Avg12MonthSalesAmount: 100m,
            PreviousMonthSalesAmount: 110m,
            SameMonthPreviousYearSalesAmount: null,
            MonthlySalesQuantity: 105m,
            PreviousMonthSalesQuantity: 100m,
            MonthlyProductionQuantity: null,
            PreviousMonthProductionQuantity: null,
            MonthlyAverageSalesRate: 1.15m,
            PreviousMonthAverageSalesRate: 1.10m,
            ProductMixRows: [],
            LastThreeMonthlySalesAmounts: [95m, 110m, 120m],
            IndustryPercentile: null,
            IndustryPeerCount: 3,
            HistoryMonths: 7,
            HasProductLineItems: true));

        Assert.Null(result.DimensionScores.ProductMixStrength);
        Assert.True(result.QualityScore > 0m);
        Assert.Contains(result.NegativeDrivers, driver => driver.Contains("ترکیب فروش", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("بهترین گزارش‌های ماهانه بازار کدامند؟")]
    [InlineData("۱۰ گزارش برتر تولید و فروش این ماه را بگو")]
    [InlineData("گزارش\u200cهای فروش ضعیف این ماه کدامند؟")]
    public void IntentRules_DetectsRankingQueries(string query)
    {
        Assert.True(MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery(query));
    }

    [Theory]
    [InlineData("آخرین فروش کچاد")]
    [InlineData("پرفروش‌ترین محصول کچاد")]
    [InlineData("P/S کچاد")]
    [InlineData("آخرین قیمت کچاد")]
    public void IntentRules_DoesNotHijackOtherMonthlyRoutes(string query)
    {
        Assert.False(MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery(query));
    }

    [Fact]
    public void IntentRules_BuildQuery_ExtractsBottomDirectionAndLimit()
    {
        var query = MonthlySalesQualityRankingIntentRules.BuildQuery("۵ گزارش\u200cهای فروش ضعیف این ماه را بگو");

        Assert.Equal(MonthlySalesQualityDirection.Bottom, query.Direction);
        Assert.Equal(5, query.Limit);
        Assert.False(query.IncludeDimensionScores);
        Assert.True(query.IncludeExplanation);
    }

    [Fact]
    public void IntentRules_BuildQuery_ExtractsIndustryScopeFromPersianQuery()
    {
        var query = MonthlySalesQualityRankingIntentRules.BuildQuery("در صنعت فلزات اساسی کدام شرکت‌ها گزارش ماهانه بهتری داشتند؟");

        Assert.Equal(MonthlySalesQualityScope.Industry, query.Scope);
        Assert.Equal("فلزات اساسی", query.IndustryTitle);
    }
}
