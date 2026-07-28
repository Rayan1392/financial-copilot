using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>HTTP-level scenarios shared by the web disclosure feed and its canonical API contract.</summary>
public sealed class DisclosureFeedEndToEnd112Tests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _authentication;
    private readonly WebApplicationFactory<Program> _factory;

    public DisclosureFeedEndToEnd112Tests(AuthenticationApiFactory factory)
    {
        _authentication = factory;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDisclosureListingUseCase>();
                services.AddSingleton<IDisclosureListingUseCase, ScenarioDisclosureListingUseCase>();
            });
        });
    }

    [Fact]
    public async Task WebFeed_MonthlyFilterPaginationHasStableOrderingWithoutDuplicates()
    {
        using var client = AuthorizedClient();
        var first = await GetJsonAsync(client, "/api/v1/disclosures?types=MonthlyProductionSales&page=1&pageSize=2");
        var second = await GetJsonAsync(client, "/api/v1/disclosures?types=MonthlyProductionSales&page=2&pageSize=2");
        using var firstResponse = first.Response;
        using var firstBody = first.Body;
        using var secondResponse = second.Response;
        using var secondBody = second.Body;

        Assert.Equal(HttpStatusCode.OK, first.Response.StatusCode);
        Assert.Equal(["monthly-3", "monthly-2"], Ids(first.Body));
        Assert.Equal(["monthly-1"], Ids(second.Body));
        Assert.Equal(3, Ids(first.Body).Concat(Ids(second.Body)).Distinct().Count());
    }

    [Fact]
    public async Task WebFeed_NonConsolidatedIncomeFilterExcludesConsolidatedRows()
    {
        using var client = AuthorizedClient();
        var result = await GetJsonAsync(client,
            "/api/v1/disclosures?types=IncomeStatement&consolidationScope=NonConsolidated");
        using var response = result.Response;
        using var body = result.Body;

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        var item = Assert.Single(result.Body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("income-standalone", item.GetProperty("disclosureId").GetString());
        Assert.False(item.GetProperty("isComposing").GetBoolean());
    }

    [Fact]
    public async Task CoverageScenario_ExposesOneCanonicalStalePartialStateToAllClients()
    {
        using var client = AuthorizedClient();
        var result = await GetJsonAsync(client, "/api/v1/disclosures?types=BalanceSheet");
        using var response = result.Response;
        using var body = result.Body;

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Equal("UnmappedCompany", result.Body.RootElement.GetProperty("coverageStatus").GetString());
        Assert.Equal("StalePersistedNormalizedData", result.Body.RootElement.GetProperty("freshnessReasonCode").GetString());
    }

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authentication.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private static async Task<(HttpResponseMessage Response, JsonDocument Body)> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, CancellationToken.None);
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return (response, await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None));
    }

    private static string[] Ids(JsonDocument body) => body.RootElement.GetProperty("items").EnumerateArray()
        .Select(item => item.GetProperty("disclosureId").GetString()!).ToArray();

    private sealed class ScenarioDisclosureListingUseCase : IDisclosureListingUseCase
    {
        private static readonly CompanyDisclosureFeedItem[] Records =
        [
            Item("monthly-3", CompanyDisclosureType.MonthlyProductionSales),
            Item("monthly-2", CompanyDisclosureType.MonthlyProductionSales),
            Item("monthly-1", CompanyDisclosureType.MonthlyProductionSales),
            Item("income-standalone", CompanyDisclosureType.IncomeStatement),
            Item("income-consolidated", CompanyDisclosureType.IncomeStatement, isComposing: true),
            Item("partial-1", CompanyDisclosureType.BalanceSheet, provider: "PartialProvider", coverage: DisclosureCoverageStatus.UnmappedCompany)
        ];

        public Task<DisclosureListingResult> ExecuteAsync(DisclosureListingQuery query, CancellationToken cancellationToken = default)
        {
            var types = query.Types?.ToArray() ?? Enum.GetValues<CompanyDisclosureType>();
            var filtered = Records.Where(item => types.Contains(item.Type))
                .Where(item => query.ProviderNames is not { Count: > 0 } || query.ProviderNames.Contains(item.ProviderName, StringComparer.OrdinalIgnoreCase))
                .Where(item => query.ConsolidationScope != DisclosureConsolidationScope.NonConsolidated || !item.IsComposing)
                .Where(item => query.ConsolidationScope != DisclosureConsolidationScope.Consolidated || item.IsComposing)
                .ToArray();
            var partial = filtered.Any(item => item.CoverageStatus != DisclosureCoverageStatus.Complete);
            var total = filtered.Length;
            var items = filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
            var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize);
            return Task.FromResult(new DisclosureListingResult(items,
                new DisclosureListingAppliedFilters(types, query.SymbolOrCompany, query.ProviderNames?.ToArray() ?? [], query.PublishedFrom, query.PublishedTo, query.ReceivedFrom, query.ReceivedTo, query.ConsolidationScope),
                query.Page, query.PageSize, query.Page > 1, query.Page < pages, total, pages, DateTimeOffset.UtcNow,
                partial ? DisclosureCoverageStatus.UnmappedCompany : DisclosureCoverageStatus.Complete,
                partial ? "StalePersistedNormalizedData" : "PersistedNormalizedData"));
        }

        private static CompanyDisclosureFeedItem Item(string id, CompanyDisclosureType type, bool isComposing = false,
            string provider = "ProviderA", DisclosureCoverageStatus coverage = DisclosureCoverageStatus.Complete) =>
            new(id, $"logical-{id}", type, provider, "company", null, "FOLAD", "Foolad", "Disclosure", null, null,
                DateTimeOffset.UtcNow, id, 1, false, coverage, "PersistedNormalizedData", IsComposing: isComposing);
    }
}
