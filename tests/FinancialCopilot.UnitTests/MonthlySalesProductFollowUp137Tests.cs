using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class MonthlySalesProductFollowUp137Tests
{
    [Fact]
    public async Task Selector_ReturnsThreeDeterministicParserSafeActionsFromAnchorSales()
    {
        var resolver = new Resolver();
        var rows = new[]
        {
            Row("p1", "گندله", 30m),
            Row("p2", "فولاد", 20m),
            Row("p3", "آهن اسفنجی", 10m),
            Row("p4", "محصول چهارم", 5m)
        };
        var repository = new ProductCatalog(rows);
        var registry = new Registry(7);
        var service = new MonthlySalesProductFollowUpSuggestionService(
            resolver, repository, registry, NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);
        var trend = Trend();

        var first = await service.BuildAsync(trend);
        var repeated = await service.BuildAsync(trend);

        Assert.Equal(3, first.Count);
        Assert.Equal(first.Select(action => action.Id), repeated.Select(action => action.Id));
        Assert.Equal(first.Select(action => action.Message), repeated.Select(action => action.Message));
        Assert.Equal(new[] { "روند فروش گندله کچاد", "روند فروش فولاد کچاد", "روند فروش آهن اسفنجی کچاد" },
            first.Select(action => action.Message));
        Assert.All(first, action =>
        {
            Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind);
            Assert.Equal("monthly_product_trend", action.CapabilityCode);
            Assert.Equal(action.Message, action.LocalizedLabel);
            Assert.Equal("monthly_sales_product_follow_up", action.RelevanceReason);
            Assert.Equal(7, action.RegistryVersion);
            Assert.Equal("کچاد", action.PresetSlots["company"]);
        });
        Assert.Equal(2, repository.ReadCount);
        Assert.Equal(new[] { "گندله", "فولاد", "آهن اسفنجی" }, first.Select(action => action.PresetSlots["product"]));
        Assert.All(first, action => Assert.StartsWith("feature137:", action.Id));
    }

    [Fact]
    public async Task Selector_ExcludesDuplicateTitlesAndReturnsExplicitEmptyWhenNoSafeProductExists()
    {
        var duplicateRows = new[] { Row("p1", "گندله", 2m), Row("p2", "گندله", 1m) };
        var service = new MonthlySalesProductFollowUpSuggestionService(
            new Resolver(), new ProductCatalog(duplicateRows), new Registry(1),
            NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);

        Assert.Empty(await service.BuildAsync(Trend()));
    }

    [Fact]
    public async Task Selector_ZeroSalesIsValidButNullOnlySalesAreExcludedAndVersionChangesActionIdentity()
    {
        var rows = new[] { Row("zero", "صفر", 0m), Row("missing", "فاقد مقدار", null) };
        var resolver = new Resolver();
        var firstService = new MonthlySalesProductFollowUpSuggestionService(
            resolver, new ProductCatalog(rows), new Registry(1),
            NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);
        var changedVersionService = new MonthlySalesProductFollowUpSuggestionService(
            resolver, new ProductCatalog(rows), new Registry(2),
            NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);

        var action = Assert.Single(await firstService.BuildAsync(Trend()));
        var changedVersionAction = Assert.Single(await changedVersionService.BuildAsync(Trend()));

        Assert.Equal("صفر", action.PresetSlots["product"]);
        Assert.NotEqual(action.Id, changedVersionAction.Id);
        Assert.Equal(1, action.RegistryVersion);
        Assert.Equal(2, changedVersionAction.RegistryVersion);
    }

    [Fact]
    public async Task Selector_UsesSymbolPrecedenceAndNeverFallsBackToCompanyName()
    {
        var rows = new[] { Row("p1", "گندله", 10m) };
        var companyId = Guid.NewGuid();
        var tickerFallback = new MonthlySalesProductFollowUpSuggestionService(
            new Resolver(new ResolvedCompany(companyId, "company-1", "نماد", null, null, null, null, null, null)),
            new ProductCatalog(rows), new Registry(1), NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);
        var noSymbol = new MonthlySalesProductFollowUpSuggestionService(
            new Resolver(new ResolvedCompany(companyId, "company-1", null, null, null, null, null, null, null)),
            new ProductCatalog(rows), new Registry(1), NullLogger<MonthlySalesProductFollowUpSuggestionService>.Instance);

        var action = Assert.Single(await tickerFallback.BuildAsync(Trend()));
        Assert.Equal("روند فروش گندله نماد", action.Message);
        Assert.Empty(await noSymbol.BuildAsync(Trend()));
    }

    private static ProductSalesObservation Row(string key, string title, decimal? amount) => new(
        Guid.NewGuid(), Guid.NewGuid(), "company-1", new JalaliPeriod(1405, 3), "provider", "report",
        new DateOnly(2026, 5, 22), new DateOnly(2026, 6, 21), key, title, "تن",
        null, null, null, amount, ProviderProductCode: key, ProductKey: key);

    private static MonthlyActivityTrendResponse Trend() => new(
        "کچاد", "چادرملو", 1405, 3, "میلیارد تومان", 1m, null, null, null, null, null, null,
        [], [], [], "test", DateTimeOffset.UtcNow);

    private sealed class Resolver(ResolvedCompany? resolved = null) : ICompanyResolverService
    {
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult<ResolvedCompany?>(resolved ?? new ResolvedCompany(
                Guid.NewGuid(), "company-1", "ticker-fallback", null, null, null, null, "کچاد", "company-symbol"));
    }

    private sealed class ProductCatalog(IReadOnlyList<ProductSalesObservation> rows) : IMonthlyProductCatalogReadRepository
    {
        public int ReadCount { get; private set; }
        public Task<IReadOnlyList<ProductSalesObservation>> GetProductCatalogAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult(rows);
        public Task<IReadOnlyList<ProductSalesObservation>> GetAllProductSalesAsync(string externalCompanyId, CancellationToken ct = default) =>
            Task.FromResult(rows);
        public Task<MonthlyProductFollowUpReadResult> GetProductFollowUpReadAsync(
            string externalCompanyId, JalaliPeriod notAfter, CancellationToken ct = default)
        {
            ReadCount++;
            return Task.FromResult(new MonthlyProductFollowUpReadResult(
                rows, new JalaliPeriod(1405, 3), rows));
        }
    }

    private sealed class Registry(int version) : IConversationalCapabilityRegistry
    {
        public int Version => version;
        public IReadOnlyCollection<CapabilityDefinition> GetAll() => [];
        public IReadOnlyCollection<CapabilityDefinition> GetEnabled() => [];
        public CapabilityDefinition? Find(string code) => null;
    }
}
