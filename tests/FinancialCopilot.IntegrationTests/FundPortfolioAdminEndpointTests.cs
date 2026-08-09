using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.IntegrationTests;

public sealed class FundPortfolioAdminEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;
    public FundPortfolioAdminEndpointTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:ApplyMigrationsOnStartup"] = "false" })));

    [Theory]
    [InlineData("/api/v1/admin/fund-portfolio-reports/health")]
    [InlineData("/api/v1/admin/fund-portfolio-reports/source-status/ConfiguredLocalStorage")]
    [InlineData("/api/v1/admin/fund-portfolio-mapping-reviews")]
    public async Task FundPortfolioAdminEndpoints_WithoutCredentials_ReturnUnauthorized(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
