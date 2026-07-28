using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramDisclosureListing112Tests
{
    [Theory]
    [InlineData(CompanyDisclosureType.MonthlyProductionSales, false)]
    [InlineData(CompanyDisclosureType.IncomeStatement, true)]
    [InlineData(CompanyDisclosureType.BalanceSheet, false)]
    [InlineData(CompanyDisclosureType.CashFlowStatement, true)]
    public void Render_UsesCompactRowsForEveryDisclosureTypeAndConsolidationScope(CompanyDisclosureType type, bool isComposing)
    {
        var message = Assert.Single(Render(CreateResult([CreateItem(type, isComposing: isComposing)])));

        Assert.Contains("ProviderA", message.Text);
        Assert.Contains("FOLAD", message.Text);
        Assert.Contains("عنوان", message.Text);
        if (isComposing) Assert.Contains("تلفیقی", message.Text);
        Assert.Equal("MarkdownV2", message.ParseMode);
    }

    [Fact]
    public void Render_UsesJalaliDatesForPublishedAndReceivedAndKeepsUnknownPublicationUnknown()
    {
        var known = Assert.Single(Render(CreateResult([CreateItem(CompanyDisclosureType.IncomeStatement)])));
        var unknown = Assert.Single(Render(CreateResult([CreateItem(CompanyDisclosureType.IncomeStatement, missingPublication: true)])));

        Assert.Contains("(تهران)", known.Text.Replace("\\", string.Empty));
        Assert.Contains("۱۴۰۵", known.Text);
        Assert.Contains("نامشخص", unknown.Text.Replace("\\", string.Empty));
        Assert.DoesNotContain("20:30", unknown.Text);
    }

    [Fact]
    public void Render_HandlesEmptyPartialAndStaleCoverageWithoutClaimingCompleteness()
    {
        var result = CreateResult([], coverage: DisclosureCoverageStatus.UnmappedCompany, freshness: "StalePersistedNormalizedData");
        var message = Assert.Single(Render(result));

        Assert.Contains("یافت نشد", message.Text);
        Assert.Contains("پوشش", message.Text);
        Assert.Contains("تازگی", message.Text);
    }

    [Fact]
    public void Render_EscapesMarkdownAndSplitsLongListingsWithinTelegramLimit()
    {
        var items = Enumerable.Range(1, 25)
            .Select(index => CreateItem(CompanyDisclosureType.MonthlyProductionSales, title: $"title _ * [ {index} ] ( safe ) " + new string('x', 220)))
            .ToArray();

        var messages = Render(CreateResult(items, page: 2, totalPages: 4, hasPrevious: true, hasNext: true));

        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.True(message.Text.Length <= 3900));
        Assert.Contains("\\_", messages[0].Text);
        Assert.Contains("\\*", messages[0].Text);
        Assert.All(messages, message => Assert.Equal("MarkdownV2", message.ParseMode));
    }

    private static IReadOnlyList<FinancialCopilot.Application.Telegram.TelegramAssistantRenderedMessage> Render(DisclosureListingResult result) =>
        new TelegramAssistantResponseRenderer(new TelegramMonthlyTrendChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance)
            .Render(new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.DisclosureListing,
                null, null, null, null, null, null, false, null, null, DisclosureListingResult: result), "fa-IR");

    private static DisclosureListingResult CreateResult(IReadOnlyList<CompanyDisclosureFeedItem> items,
        int page = 1, int totalPages = 1, bool hasPrevious = false, bool hasNext = false,
        DisclosureCoverageStatus coverage = DisclosureCoverageStatus.Complete, string freshness = "PersistedNormalizedData") =>
        new(items, new DisclosureListingAppliedFilters([CompanyDisclosureType.MonthlyProductionSales], "FOLAD", ["ProviderA"], null, null, null, null, DisclosureConsolidationScope.NonConsolidated),
            page, 8, hasPrevious, hasNext, totalPages * 8, totalPages, DateTimeOffset.UtcNow, coverage, freshness);

    private static CompanyDisclosureFeedItem CreateItem(CompanyDisclosureType type, bool isComposing = false,
        bool missingPublication = false, string title = "عنوان اطلاعیه") =>
        new($"d-{Guid.NewGuid():N}", "logical", type, "ProviderA", "company", null, "FOLAD", "شرکت فولاد", title,
            missingPublication ? null : new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30),
            new DateTimeOffset(2026, 7, 1, 20, 30, 0, TimeSpan.Zero), "source", 1, false,
            DisclosureCoverageStatus.Complete, "PersistedNormalizedData", IsComposing: isComposing);
}
