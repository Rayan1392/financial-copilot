using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.IntegrationTests;

public sealed class FoundationEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FoundationEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ApplyMigrationsOnStartup"] = "false"
                }));
        });
    }

    [Fact]
    public async Task Health_ReturnsHealthyResponseAndCorrelationId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-Id", "integration-test-correlation");

        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("integration-test-correlation", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task OpenApi_IsAvailableInDevelopment()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", CancellationToken.None);
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
    }
}
