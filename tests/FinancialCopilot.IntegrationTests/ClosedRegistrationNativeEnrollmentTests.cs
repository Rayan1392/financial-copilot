using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.IntegrationTests;

public sealed class ClosedRegistrationNativeEnrollmentTests(ClosedRegistrationNativeEnrollmentApiFactory factory)
    : IClassFixture<ClosedRegistrationNativeEnrollmentApiFactory>
{
    [Fact]
    public async Task ProvisionedUserCanEnrollVerifiedCredentialsAndLoginWhilePublicRegistrationIsClosed()
    {
        var launched = await NativeEnrollmentIntegrationTests.LaunchUserAsync(factory);
        var email = NativeEnrollmentIntegrationTests.NewEmail();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", launched.AccessToken);

        using var enroll = await client.PostAsJsonAsync("/api/auth/v1/credentials/email", new { email });
        Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        var token = factory.EmailSender.LatestToken!;
        using var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        using var password = await client.PostAsJsonAsync(
            "/api/auth/v1/credentials/password", new { newPassword = "NativePassword!123" });
        Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);

        using var registration = await client.PostAsJsonAsync(
            "/api/auth/v1/register", new { email = NativeEnrollmentIntegrationTests.NewEmail(), password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Forbidden, registration.StatusCode);
        using var login = await client.PostAsJsonAsync(
            "/api/auth/v1/login", new { email, password = "NativePassword!123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var session = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal(launched.UserId, session.RootElement.GetProperty("user").GetProperty("userId").GetGuid());
    }
}

public sealed class ClosedRegistrationNativeEnrollmentApiFactory : NativeEnrollmentApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:OwnedIdentity:PublicRegistrationEnabled"] = "false"
            }));
    }
}
