using System.Net;
using System.Net.Http.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature flag off (default configuration): the launch endpoint is absent and no exchange can occur.</summary>
public sealed class TahlilAppLaunchDisabledIntegrationTests(OwnedIdentityApiFactory factory) : IClassFixture<OwnedIdentityApiFactory>
{
    [Fact]
    public async Task WhenFeatureIsDisabled_LaunchReturnsNotFound()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/v1/tahlilapp/launch", new { code = "AbCdEfGhIjKlMnOpQrStUvWx" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
