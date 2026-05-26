using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

public sealed class SemanticMetadataEndpointTests : IClassFixture<BillingApiFactory>
{
    private readonly BillingApiFactory _factory;

    public SemanticMetadataEndpointTests(BillingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MetricsMetadata_RequiresAuthenticatedFacadeActor()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/ai/v1/metadata/metrics", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MetricsMetadata_ReturnsRegisteredVersionedSemanticDefinitions()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync("/api/ai/v1/metadata/metrics", CancellationToken.None);
        await using var body = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: CancellationToken.None);
        var pe = document.RootElement.GetProperty("metrics")
            .EnumerateArray()
            .Single(metric => metric.GetProperty("metricCode").GetString() == "PE_TTM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1", pe.GetProperty("metricVersion").GetString());
        Assert.Contains(
            pe.GetProperty("aliases").EnumerateArray(),
            alias => alias.GetProperty("language").GetString() == "fa-IR");
        Assert.Contains(
            pe.GetProperty("calculationPolicyVersions").EnumerateArray(),
            policy => policy.GetString() == "ttm-valuation-v1");
    }
}
