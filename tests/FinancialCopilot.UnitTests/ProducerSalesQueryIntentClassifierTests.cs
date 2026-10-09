using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class ProducerSalesQueryIntentClassifierTests
{
    private readonly ProducerSalesQueryIntentClassifier _classifier = new();

    [Fact]
    public void UnqualifiedProducerSalesRankingIsAmbiguous()
    {
        var intent = _classifier.Classify("کدام شرکت‌های تولیدکننده گندله بیشترین فروش را داشته‌اند؟", "گندله");

        Assert.True(intent.IsProducerSalesRanking);
        Assert.True(intent.RequiresClarification);
        Assert.Null(intent.Scope);
        Assert.Null(intent.MeasureCatalogId);
    }

    [Fact]
    public void ExplicitWholeCompanySalesSelectsTotalSalesScope()
    {
        var intent = _classifier.Classify("کدام شرکت‌های تولیدکننده گندله بیشترین مبلغ کل فروش شرکت را داشته‌اند؟", "گندله");

        Assert.Equal(PopulationSalesScope.TotalCompanySales, intent.Scope);
        Assert.Equal("metric_monthly_sales", intent.MeasureCatalogId);
        Assert.False(intent.RequiresClarification);
    }

    [Fact]
    public void ExplicitTotalMonthlySalesSelectsWholeCompanySalesScope()
    {
        var intent = _classifier.Classify("کدام شرکت‌های تولیدکننده گندله بیشترین فروش کل ماهانه را داشته‌اند؟", "گندله");

        Assert.Equal(PopulationSalesScope.TotalCompanySales, intent.Scope);
        Assert.Equal("metric_monthly_sales", intent.MeasureCatalogId);
        Assert.False(intent.RequiresClarification);
    }

    [Fact]
    public void ExplicitPelletSalesQuantitySelectsRequestedProductScope()
    {
        var intent = _classifier.Classify("کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش گندله را داشته‌اند؟", "گندله");

        Assert.Equal(PopulationSalesScope.RequestedProductSales, intent.Scope);
        Assert.Equal("metric_monthly_sales_quantity", intent.MeasureCatalogId);
        Assert.False(intent.RequiresClarification);
    }

    [Fact]
    public void NonRankingProductQuestionDoesNotBecomeProducerSalesComposition()
    {
        var intent = _classifier.Classify("کدام شرکت‌ها گندله تولید می‌کنند؟", "گندله");

        Assert.False(intent.IsProducerSalesRanking);
        Assert.False(intent.RequiresClarification);
    }
}
