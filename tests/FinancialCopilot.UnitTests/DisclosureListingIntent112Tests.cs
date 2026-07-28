using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class DisclosureListingIntent112Tests
{
    [Fact]
    public void CanonicalMonthlyListing_IsRecognizedAndMapped()
    {
        const string message = "فهرست آخرین تولید و فروش منتشر شده را بده";

        Assert.True(DisclosureListingIntentRules.LooksLikeDisclosureListingQuery(message));
        var query = DisclosureListingIntentRules.BuildQuery(message, new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(3.5)));

        Assert.Equal([CompanyDisclosureType.MonthlyProductionSales], query.Types);
        Assert.Equal(DisclosureConsolidationScope.NonConsolidated, query.ConsolidationScope);
    }

    [Theory]
    [InlineData("لیست صورت سود و زیان منتشر شده")]
    [InlineData("لیست ترازنامه های منتشر شده")]
    [InlineData("فهرست جریان وجه نقد منتشر شده")]
    public void FinancialStatementListingPhrases_AreRecognized(string message) =>
        Assert.True(DisclosureListingIntentRules.LooksLikeDisclosureListingQuery(message));

    [Theory]
    [InlineData("فهرست صورت سود و زیان منتشر شده", CompanyDisclosureType.IncomeStatement)]
    [InlineData("فهرست ترازنامه منتشر شده", CompanyDisclosureType.BalanceSheet)]
    [InlineData("فهرست جریان وجه نقد منتشر شده", CompanyDisclosureType.CashFlowStatement)]
    public void StatementListingPhrases_MapToTheirExactDisclosureType(string message, CompanyDisclosureType expectedType)
    {
        var query = DisclosureListingIntentRules.BuildQuery(message, DateTimeOffset.UtcNow);

        Assert.Equal([expectedType], query.Types);
    }

    [Fact]
    public void FinancialStatements_MapsToAllFinancialStatementTypes_AndConsolidatedScope()
    {
        var query = DisclosureListingIntentRules.BuildQuery("فهرست صورت های مالی تلفیقی منتشر شده", DateTimeOffset.UtcNow);

        Assert.Equal(
            [CompanyDisclosureType.IncomeStatement, CompanyDisclosureType.BalanceSheet, CompanyDisclosureType.CashFlowStatement],
            query.Types);
        Assert.Equal(DisclosureConsolidationScope.Consolidated, query.ConsolidationScope);
    }

    [Fact]
    public void SingleMetricQuestion_IsNotMisroutedAsListing()
    {
        Assert.False(DisclosureListingIntentRules.LooksLikeDisclosureListingQuery("آخرین فروش ماهانه فولاد چقدر است؟"));
    }

    [Fact]
    public void ListingQuery_ExtractsSymbolTodayWindowAndDefaultsToNonConsolidated()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(3.5));

        var query = DisclosureListingIntentRules.BuildQuery("فهرست تولید و فروش منتشر شده امروز فولاد", now);

        Assert.Equal("فولاد", query.SymbolOrCompany);
        Assert.Equal(new DateOnly(2026, 7, 27), query.PublishedFrom);
        Assert.Equal(DisclosureConsolidationScope.NonConsolidated, query.ConsolidationScope);
    }

    [Fact]
    public void ListingQuery_ExtractsThisWeekWindowAndHonorsExplicitConsolidatedScope()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.FromHours(3.5));

        var query = DisclosureListingIntentRules.BuildQuery("فهرست صورت های مالی تلفیقی این هفته شرکت فولاد", now);

        Assert.Equal(new DateOnly(2026, 7, 21), query.PublishedFrom);
        Assert.Equal(DisclosureConsolidationScope.Consolidated, query.ConsolidationScope);
        Assert.Equal("فولاد", query.SymbolOrCompany);
    }
}
