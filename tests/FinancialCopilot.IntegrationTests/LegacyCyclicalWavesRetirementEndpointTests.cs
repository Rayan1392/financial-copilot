using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 148 T148-05.3: legacy CyclicalWaves ingestion triggers are retired. A separate fixture instance keeps
/// these requests out of the per-actor rate-limit budget of <see cref="AdminDataOperationsEndpointTests"/>.
/// </summary>
public sealed class LegacyCyclicalWavesRetirementEndpointTests : IClassFixture<AdminDataOperationsApiFactory>
{
    private readonly AdminDataOperationsApiFactory _factory;

    public LegacyCyclicalWavesRetirementEndpointTests(AdminDataOperationsApiFactory factory)
    {
        _factory = factory;
        factory.Reset();
    }

    [Theory]
    [InlineData("/api/v1/admin/cyclicalwaves/full-sync")]
    [InlineData("/api/v1/admin/comprehensive-analysis/full-sync")]
    [InlineData("/api/v1/admin/comprehensive-analysis/daily-sync")]
    [InlineData("/api/v1/admin/cyclicalwaves/ps-visualization/sync")]
    [InlineData("/api/v1/admin/cyclicalwaves/ps-visualization/snapshot")]
    [InlineData("/api/v1/admin/cyclicalwaves/ps-visualization/history")]
    public async Task LegacyCyclicalWavesTriggers_ReturnGoneWithoutCallingAnyService(string path)
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(path, new { }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("ProviderRetired", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, _factory.LegacySpy.Calls);
    }

    [Fact]
    public async Task DataSync_ExplicitlyNamingCyclicalWaves_ReturnsGoneAndPublishesNothing()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { providerName = "CyclicalWaves", idempotencyKey = "legacy-cw" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Empty(_factory.PublishedRequests);
    }

    private HttpClient CreateDataAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateWebAppToken(includeTenant: true, dataAdmin: true));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}
