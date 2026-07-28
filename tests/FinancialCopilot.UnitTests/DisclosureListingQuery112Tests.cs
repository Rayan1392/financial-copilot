using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class DisclosureListingQuery112Tests
{
    [Fact]
    public async Task ExecuteAsync_NormalizesProviderGroupAndUsesNonConsolidatedDefault()
    {
        var repository = new CapturingRepository();
        var useCase = new DisclosureListingUseCase(repository);

        await useCase.ExecuteAsync(new DisclosureListingQuery(
            ProviderNames: [" ProviderA ", "providera", "ProviderB"],
            SymbolOrCompany: " شغدیر "));

        Assert.Equal(["ProviderA", "ProviderB"], repository.Query!.ProviderNames);
        Assert.Equal("شغدیر", repository.Query.SymbolOrCompany);
        Assert.Equal(DisclosureConsolidationScope.NonConsolidated, repository.Query.ConsolidationScope);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task ExecuteAsync_RejectsInvalidPaging(int page, int pageSize)
    {
        var useCase = new DisclosureListingUseCase(new CapturingRepository());
        await Assert.ThrowsAsync<DisclosureListingValidationException>(() =>
            useCase.ExecuteAsync(new DisclosureListingQuery(Page: page, PageSize: pageSize)));
    }

    [Fact]
    public async Task ExecuteAsync_PreservesPublicationAndReceiptRangeBoundariesForCanonicalRepository()
    {
        var repository = new CapturingRepository();
        var useCase = new DisclosureListingUseCase(repository);
        var receivedFrom = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(3.5));
        var receivedTo = new DateTimeOffset(2026, 7, 31, 23, 59, 59, TimeSpan.FromHours(3.5));

        await useCase.ExecuteAsync(new DisclosureListingQuery(
            PublishedFrom: new DateOnly(2026, 7, 1),
            PublishedTo: new DateOnly(2026, 7, 31),
            ReceivedFrom: receivedFrom,
            ReceivedTo: receivedTo,
            Channel: "telegram"));

        Assert.Equal(new DateOnly(2026, 7, 1), repository.Query!.PublishedFrom);
        Assert.Equal(new DateOnly(2026, 7, 31), repository.Query.PublishedTo);
        Assert.Equal(receivedFrom.ToUniversalTime(), repository.Query.ReceivedFrom);
        Assert.Equal(receivedTo.ToUniversalTime(), repository.Query.ReceivedTo);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvertedDateRanges()
    {
        var useCase = new DisclosureListingUseCase(new CapturingRepository());

        await Assert.ThrowsAsync<DisclosureListingValidationException>(() => useCase.ExecuteAsync(
            new DisclosureListingQuery(PublishedFrom: new DateOnly(2026, 8, 2), PublishedTo: new DateOnly(2026, 8, 1))));
        await Assert.ThrowsAsync<DisclosureListingValidationException>(() => useCase.ExecuteAsync(
            new DisclosureListingQuery(ReceivedFrom: DateTimeOffset.UtcNow, ReceivedTo: DateTimeOffset.UtcNow.AddMinutes(-1))));
    }

    private sealed class CapturingRepository : ICompanyDisclosureFeedRepository
    {
        public CompanyDisclosureFeedQuery? Query { get; private set; }

        public Task<CompanyDisclosureFeedPage> QueryAsync(CompanyDisclosureFeedQuery query, CancellationToken cancellationToken = default)
        {
            Query = query;
            return Task.FromResult(new CompanyDisclosureFeedPage([], query.Page, query.PageSize, 0,
                DateTimeOffset.UtcNow, DisclosureCoverageStatus.Complete));
        }
    }
}
