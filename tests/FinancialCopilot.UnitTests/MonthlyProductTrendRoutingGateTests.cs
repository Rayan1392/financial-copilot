using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

/// <summary>Product trend needs credible product evidence, not just "monthly sales" wording.</summary>
public sealed class MonthlyProductTrendRoutingGateTests
{
    [Theory]
    [InlineData("روند فروش گندله کگهر")]
    [InlineData("فروش ماهانه کنسانتره کچاد")]
    [InlineData("روند فروش گندله کگهر در ۱۲ ماه اخیر")]
    [InlineData("روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟")]
    public void LegitimateProductTrendQueries_StillRouteToProductTrend(string query)
    {
        Assert.True(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        Assert.Equal("product_sales_trend", TopCapability(query));
    }

    [Theory]
    [InlineData("رتبه‌بندی کیفیت فروش ماهانه")]
    [InlineData("رتبه بندی کیفیت فروش ماهانه")]
    public void GenericRankingPhrases_AreNotProductTrends(string query)
    {
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        Assert.NotEqual("product_sales_trend", TopCapability(query));
    }

    [Fact]
    public void RankingPhrase_RoutesToRankingCapability() =>
        Assert.Equal("monthly_sales_quality_ranking", TopCapability("رتبه‌بندی کیفیت فروش ماهانه"));

    [Theory]
    [InlineData("روند فروش کچاد", "monthly_activity_trend")]
    [InlineData("روند فروش ماهانه کگهر", "monthly_activity_trend")]
    [InlineData("ترکیب فروش محصولات کچاد", "product_revenue_mix")]
    public void CompanyTrendAndRevenueMixRouting_Unchanged(string query, string expected)
    {
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(query));
        Assert.Equal(expected, TopCapability(query));
    }

    [Theory]
    [InlineData("گزارش")]
    [InlineData("بهترین گزارش فروش ماهانه")]
    [InlineData("گزارش فروش ماهانه")]
    [InlineData("رتبه‌بندی گزارش فروش")]
    public void ReportWords_AreNotProductEvidence(string text)
    {
        Assert.Null(ProductSemanticIntentRules.ExtractProductMention(text));
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesTrend(text));
        Assert.False(ProductSemanticIntentRules.LooksLikeProductSalesValue(text));
        Assert.False(MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(text));
        Assert.NotEqual("product_sales_trend", TopCapability(text));
        Assert.NotEqual("product_sales_value", TopCapability(text));
    }

    [Theory]
    [InlineData("کاتدش")]
    [InlineData("محصول گندله")]
    public void RealProductMentions_StillExtracted(string text) =>
        Assert.NotNull(ProductSemanticIntentRules.ExtractProductMention(text));

    [Fact]
    public void BestReportRankingPhrase_StillMatchesRankingRules() =>
        Assert.True(MonthlySalesQualityRankingIntentRules.LooksLikeMonthlySalesQualityRankingQuery("بهترین گزارش های ماهانه"));

    private static string? TopCapability(string query)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        return new DeterministicCapabilityInterpreter(registry).Interpret(query)
            .CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
    }
}
