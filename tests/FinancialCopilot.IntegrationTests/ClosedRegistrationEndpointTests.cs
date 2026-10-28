using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class ClosedRegistrationEndpointTests(ClosedRegistrationApiFactory factory)
    : IClassFixture<ClosedRegistrationApiFactory>
{
    [Fact]
    public async Task AnonymousRegistrationIsForbiddenWithoutDisclosingOrChangingAccountAndBillingState()
    {
        using var client = factory.CreateClient();
        var email = $"closed-{Guid.NewGuid():N}@example.test";
        int usersBefore;
        int accountsBefore;
        decimal? walletBefore;
        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            usersBefore = auth.Users.Count();
            accountsBefore = billing.CustomerAccounts.Count();
            walletBefore = billing.WalletProjections.Sum(wallet => (decimal?)wallet.Balance);
        }

        using var response = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email, password = "StrongPassword!123" });
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.EndsWith("/REGISTRATION_DISABLED", problem.RootElement.GetProperty("type").GetString());
        Assert.DoesNotContain(email, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email-already-registered", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            Assert.Equal(usersBefore, auth.Users.Count());
            Assert.Null(await auth.Users.SingleOrDefaultAsync(row => row.Email == email));
            Assert.Equal(accountsBefore, billing.CustomerAccounts.Count());
            Assert.Equal(walletBefore, billing.WalletProjections.Sum(wallet => (decimal?)wallet.Balance));
        }

        using var login = await client.PostAsJsonAsync(
            "/api/auth/v1/login",
            new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        using var forgot = await client.PostAsJsonAsync(
            "/api/auth/v1/password/forgot",
            new { email });
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        using var invalidReset = await client.PostAsJsonAsync(
            "/api/auth/v1/password/reset",
            new { token = "invalid-reset-token", newPassword = "AnotherStrongPassword!123" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReset.StatusCode);
    }
}

public sealed class ClosedRegistrationApiFactory : OwnedIdentityApiFactory
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
