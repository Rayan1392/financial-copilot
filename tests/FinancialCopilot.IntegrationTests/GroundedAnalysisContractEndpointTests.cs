using System.Net;
using System.Net.Http.Json;
using FinancialCopilot.API.Contracts;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T05 (FR-4, PO-9): the query endpoint accepts only a depth, a web switch and a
/// pricing version; an unknown depth is rejected before anything runs; no amount exists on the request.
/// </summary>
public sealed class GroundedAnalysisContractEndpointTests(AuthenticationApiFactory factory)
    : IClassFixture<AuthenticationApiFactory>
{
    [Fact]
    public async Task UnknownDepth_IsRejectedWith400()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "سود خالص قمرو", analysisOptions = new { depth = "extreme", webSearch = true } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void RequestContracts_CarryNoAmount()
    {
        Assert.Equal(
            ["Depth", "WebSearch", "PricingVersion"],
            typeof(AnalysisOptionsHttpRequest).GetProperties().Select(property => property.Name).ToArray());
        Assert.DoesNotContain(
            typeof(AiQueryHttpRequest).GetProperties(),
            property => property.Name.Contains("Credit", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Price", StringComparison.OrdinalIgnoreCase));
    }
}
