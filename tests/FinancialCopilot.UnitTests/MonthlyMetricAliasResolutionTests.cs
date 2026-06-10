using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 057: Persian monthly-activity questions must resolve to the monthly metrics backed by
/// the Noavaran monthly reports — while the bare ambiguous «فروش» stays on quarterly REVENUE.
/// </summary>
public sealed class MonthlyMetricAliasResolutionTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 10);

    private static readonly MetricAliasResolver Resolver = new(
        new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

    [Theory]
    [InlineData("آخرین فروش", "MONTHLY_SALES")]
    [InlineData("فروش ماهانه", "MONTHLY_SALES")]
    [InlineData("مقدار فروش", "MONTHLY_SALES_QUANTITY")]
    [InlineData("نرخ فروش", "MONTHLY_SALES_RATE")]
    [InlineData("تولید", "MONTHLY_PRODUCTION_QUANTITY")]
    [InlineData("مقدار تولید", "MONTHLY_PRODUCTION_QUANTITY")]
    public void PersianMonthlyTerms_ResolveToMonthlyMetrics(string term, string expectedCode)
    {
        var result = Resolver.ResolveAlias(term, "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedCode, Assert.Single(result.Candidates).Code.Value);
    }

    [Fact]
    public void BareSalesTerm_StaysOnQuarterlyRevenueByPolicy()
    {
        var result = Resolver.ResolveAlias("فروش", "fa-IR", new MetricResolutionContext(null, null), AsOf);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal("REVENUE", Assert.Single(result.Candidates).Code.Value);
    }
}
