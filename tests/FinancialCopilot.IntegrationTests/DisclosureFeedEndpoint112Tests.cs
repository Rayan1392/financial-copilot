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

public sealed class DisclosureFeedEndpoint112Tests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _authentication;
    private readonly WebApplicationFactory<Program> _factory;

    public DisclosureFeedEndpoint112Tests(AuthenticationApiFactory factory)
    {
        _authentication = factory;
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDisclosureListingUseCase>();
                services.AddSingleton<IDisclosureListingUseCase, FakeDisclosureListingUseCase>();
            });
        });
    }

    [Fact]
    public async Task GetDisclosures_RequiresAnAuthenticatedActor()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/v1/disclosures", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDisclosures_ReturnsTheCanonicalListingContractForAnAuthorizedActor()
    {
        using var client = AuthorizedClient();
        using var response = await client.GetAsync(
            "/api/v1/disclosures?types=MonthlyProductionSales&providerNames=ProviderA&page=2&pageSize=10", CancellationToken.None);
        using var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = body.RootElement;
        Assert.True(root.TryGetProperty("items", out var items));
        Assert.Single(items.EnumerateArray());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(10, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, root.GetProperty("totalPages").GetInt32());
        Assert.True(root.GetProperty("hasPreviousPage").GetBoolean());
        Assert.True(root.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal("UnmappedCompany", root.GetProperty("coverageStatus").GetString());
        Assert.Equal("PersistedNormalizedData", root.GetProperty("freshnessReasonCode").GetString());
        Assert.True(root.TryGetProperty("asOf", out _));
        var filters = root.GetProperty("appliedFilters");
        Assert.Equal("ProviderA", Assert.Single(filters.GetProperty("providerNames").EnumerateArray()).GetString());
        Assert.Equal("MonthlyProductionSales", Assert.Single(filters.GetProperty("types").EnumerateArray()).GetString());
    }

    [Theory]
    [InlineData("?types=NotAType")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?publishedFrom=2026-08-02&publishedTo=2026-08-01")]
    public async Task GetDisclosures_ReturnsProblemDetailsForInvalidInput(string query)
    {
        using var client = AuthorizedClient();
        using var response = await client.GetAsync($"/api/v1/disclosures{query}", CancellationToken.None);
        using var body = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", body.RootElement.GetProperty("type").GetString());
    }

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _authentication.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(stream, cancellationToken: CancellationToken.None);
    }

    private sealed class FakeDisclosureListingUseCase : IDisclosureListingUseCase
    {
        public Task<DisclosureListingResult> ExecuteAsync(DisclosureListingQuery query, CancellationToken cancellationToken = default)
        {
            if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.PublishedFrom > query.PublishedTo)
                throw new DisclosureListingValidationException("Invalid disclosure listing query.");

            var types = query.Types?.ToArray() ?? Enum.GetValues<CompanyDisclosureType>();
            var filters = new DisclosureListingAppliedFilters(types, query.SymbolOrCompany, query.ProviderNames?.ToArray() ?? [],
                query.PublishedFrom, query.PublishedTo, query.ReceivedFrom, query.ReceivedTo, query.ConsolidationScope);
            var item = new CompanyDisclosureFeedItem("monthly:ProviderA:1", "internal", CompanyDisclosureType.MonthlyProductionSales,
                "ProviderA", "external", null, "FOO", "Foo Company", "Long Persian title", new DateOnly(2026, 7, 1),
                new DateOnly(2026, 6, 30), new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.FromHours(3.5)), "source", 1,
                false, DisclosureCoverageStatus.UnmappedCompany, "PersistedNormalizedRecord");
            return Task.FromResult(new DisclosureListingResult([item], filters, query.Page, query.PageSize, query.Page > 1,
                true, 25, 3, DateTimeOffset.UtcNow, DisclosureCoverageStatus.UnmappedCompany, "PersistedNormalizedData"));
        }
    }
}
