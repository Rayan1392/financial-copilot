using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.IntegrationTests;

public sealed class TahlilAppTopUpEndpointTests : IClassFixture<TahlilAppTopUpApiFactory>
{
    private readonly TahlilAppTopUpApiFactory factory;
    public TahlilAppTopUpEndpointTests(TahlilAppTopUpApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task TopUpEligibilityBlocksActiveMaxAndUsesLatestExpiringNonMaxPlanAfterMaxExpiry()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = DateTimeOffset.UtcNow;
        var max = new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_max:{now.AddDays(-1).ToUnixTimeMilliseconds()}:{now.AddDays(10).ToUnixTimeMilliseconds()}",
            PlanCode = "Max", StartsAtUtc = now.AddDays(-1), ExpiresAtUtc = now.AddDays(10)
        };
        billing.ExternalPlanSubscriptions.Add(max);
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_pro:{now.AddDays(-2).ToUnixTimeMilliseconds()}:{now.AddDays(30).ToUnixTimeMilliseconds()}",
            PlanCode = "Pro", StartsAtUtc = now.AddDays(-2), ExpiresAtUtc = now.AddDays(30)
        });
        await billing.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<TahlilAppTopUpService>();

        var whileMaxIsActive = await service.GetEligibilityAsync("external-topup-user", CancellationToken.None);
        Assert.NotNull(whileMaxIsActive);
        Assert.False(whileMaxIsActive!.Eligible);
        Assert.Null(whileMaxIsActive.Plan);

        max.ExpiresAtUtc = now.AddSeconds(-1);
        await billing.SaveChangesAsync();
        var afterMaxExpires = await service.GetEligibilityAsync("external-topup-user", CancellationToken.None);
        Assert.NotNull(afterMaxExpires);
        Assert.True(afterMaxExpires!.Eligible);
        Assert.Equal("Pro", afterMaxExpires.Plan);
        Assert.Equal(now.AddDays(30).Date, afterMaxExpires.Subscriptions.Max(window => window.ExpiresAtUtc).Date);
    }

    [Fact]
    public async Task Partner_auth_and_contract_quantities_are_enforced_and_grants_are_idempotent()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var windowStart = factory.WindowStart;
        var windowEnd = factory.WindowEnd;
        foreach (var credits in new[] { 100, 500, 1000 })
        {
            var package = $"TA-TOPUP-{credits}";
            var body = new
            {
                eventId = "evt-" + credits, purchaseId = "purchase-" + credits, paymentId = "payment-" + credits,
                userId = "external-topup-user", creditsPurchased = credits, packageId = package, packageVersion = "v1",
                subscription = new { slug = "sapio_plus", startsAtUtc = windowStart, expiresAtUtc = windowEnd },
                paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            var unauthorized = await PostAsync(client, body, null);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            var forbidden = await PostAsync(client, body, factory.CreatePartnerToken("wrong.scope"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var granted = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            using var document = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
            Assert.Equal("granted", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(credits, document.RootElement.GetProperty("creditsGranted").GetInt32());
            var duplicate = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        }

        var invalidQuantity = await PostAsync(client, new
        {
            eventId = "evt-invalid", purchaseId = "purchase-invalid", paymentId = "payment-invalid",
            userId = "external-topup-user", creditsPurchased = 200, packageId = "TA-TOPUP-100", packageVersion = "v1",
            subscription = new { slug = "sapio_plus", startsAtUtc = windowStart, expiresAtUtc = windowEnd },
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal((HttpStatusCode)422, invalidQuantity.StatusCode);
        Assert.Equal(1617m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Billing_admin_reconciliation_lists_purchase_outcomes_and_identity_conflicts()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var anonymous = await client.GetAsync("/api/v1/admin/billing/top-ups/reconciliation");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var body = new
        {
            eventId = "reconcile-event-1", purchaseId = "reconcile-purchase-1", paymentId = "reconcile-payment-1",
            userId = "external-topup-user", creditsPurchased = 100, packageId = "TA-TOPUP-100", packageVersion = "v1",
            subscription = new { slug = "sapio_plus", startsAtUtc = factory.WindowStart, expiresAtUtc = factory.WindowEnd },
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var granted = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var conflicting = await PostAsync(client, new
        {
            eventId = "reconcile-event-conflict", purchaseId = "reconcile-purchase-other", paymentId = "reconcile-payment-1",
            userId = "external-topup-user", creditsPurchased = 100, packageId = "TA-TOPUP-100", packageVersion = "v1",
            subscription = new { slug = "sapio_plus", startsAtUtc = factory.WindowStart, expiresAtUtc = factory.WindowEnd },
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/billing/top-ups/reconciliation?limit=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateBillingAdminToken());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.GetProperty("purchases").EnumerateArray());
        Assert.Equal("granted", document.RootElement.GetProperty("purchases")[0].GetProperty("status").GetString());
        Assert.Single(document.RootElement.GetProperty("identityConflicts").EnumerateArray());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/partners/tahlilapp/top-ups")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Contract-Version", "1");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}

public sealed class TahlilAppTopUpApiFactory : WebApplicationFactory<Program>
{
    private const string PartnerIssuer = "TahlilApp.Tests";
    private const string PartnerSigningKey = "integration-test-tahlilapp-topup-key-material-32-bytes";
    private const string PartnerKeyId = "partner-test-key";
    private readonly string billingDatabase = "topup-api-billing-" + Guid.NewGuid().ToString("N");
    private readonly string authDatabase = "topup-api-auth-" + Guid.NewGuid().ToString("N");
    public static readonly Guid UserId = Guid.Parse("43444444-4444-4444-8444-444444444444");
    public static readonly Guid TenantId = AuthenticationApiFactory.TenantId;
    public static readonly Guid AccountId = Guid.Parse("53555555-5555-4555-8555-555555555555");
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset WindowEnd { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:TahlilAppTopUps:Enabled"] = "true",
            ["Billing:TahlilAppTopUps:Authentication:Issuer"] = PartnerIssuer,
            ["Billing:TahlilAppTopUps:Authentication:Audience"] = "sapio-tahlilapp-topup",
            [$"Billing:TahlilAppTopUps:Authentication:SigningKeys:{PartnerKeyId}"] = PartnerSigningKey,
            ["Authentication:JwtBearer:Issuer"] = "FinancialCopilot.Tests",
            ["Authentication:JwtBearer:Audience"] = "FinancialCopilot.Tests.Web",
            ["Authentication:JwtBearer:SigningKey"] = "integration-test-signing-key-with-more-than-32-characters",
            ["Authentication:JwtBearer:RequireHttpsMetadata"] = "false",
            ["Database:ApplyMigrationsOnStartup"] = "false"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(billingDatabase));
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(authDatabase));
            services.RemoveAll<ITahlilAppEntitlementRefresher>();
            services.AddSingleton<ITahlilAppEntitlementRefresher, NoOpRefresher>();
        });
    }

    public string CreatePartnerToken(string scope)
    {
        var token = new JwtSecurityToken(PartnerIssuer, "sapio-tahlilapp-topup",
            [new Claim("sub", "tahlilapp-test"), new Claim("scope", scope)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PartnerSigningKey)), SecurityAlgorithms.HmacSha256));
        token.Header["kid"] = PartnerKeyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateBillingAdminToken()
    {
        var token = new JwtSecurityToken("FinancialCopilot.Tests", "FinancialCopilot.Tests.Web",
            [new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString()),
             new Claim(FinancialCopilotClaimTypes.TenantId, TenantId.ToString()),
             new Claim(FinancialCopilotClaimTypes.AuthenticationMode, "WebAppUser"),
             new Claim(FinancialCopilotClaimTypes.Permission, FinancialCopilotPermissions.AdminBillingAuditRead),
             new Claim("role", "BillingAdmin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-with-more-than-32-characters")), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await billing.Database.EnsureDeletedAsync(); await billing.Database.EnsureCreatedAsync();
        await auth.Database.EnsureDeletedAsync(); await auth.Database.EnsureCreatedAsync();
        var start = WindowStart = DateTimeOffset.UtcNow.AddDays(-2);
        var end = WindowEnd = DateTimeOffset.UtcNow.AddDays(30);
        var slug = "sapio_plus";
        billing.CustomerAccounts.Add(new CustomerAccountRow { Id = AccountId, TenantId = TenantId, UserId = UserId, AccountType = "Individual", BillingMode = "Prepaid", SubscriptionPlanCode = "Plus", SubscriptionActivationIsAuthoritative = true, SubscriptionEffectiveFrom = start, SubscriptionEffectiveTo = end });
        billing.WalletProjections.Add(new WalletProjectionRow { CustomerAccountId = AccountId, Balance = 17, ReservedAmount = 0, UpdatedAt = DateTimeOffset.UtcNow, Revision = 1 });
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow { Id = Guid.NewGuid(), CustomerAccountId = AccountId, IdentityKey = $"{slug}:{start.ToUnixTimeMilliseconds()}:{end.ToUnixTimeMilliseconds()}", PlanCode = "Plus", StartsAtUtc = start, ExpiresAtUtc = end });
        auth.Users.Add(new FinancialCopilotUser { Id = UserId, UserName = "topup-api-test", NormalizedUserName = "TOPUP-API-TEST", IsEnabled = true });
        auth.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow { Id = Guid.NewGuid(), Provider = "TahlilApp", ExternalUserId = "external-topup-user", LocalUserId = UserId, TenantId = TenantId, Status = "Active", LinkedAtUtc = DateTimeOffset.UtcNow });
        await auth.SaveChangesAsync();
        await billing.SaveChangesAsync();
    }

    public async Task<decimal> ReadBalanceAsync()
    {
        using var scope = Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await billing.WalletProjections.Where(row => row.CustomerAccountId == AccountId).Select(row => row.Balance).SingleAsync();
    }

    private sealed class NoOpRefresher : ITahlilAppEntitlementRefresher
    {
        public Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
