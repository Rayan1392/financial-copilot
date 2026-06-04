using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class OwnedIdentityEndpointTests : IClassFixture<OwnedIdentityApiFactory>
{
    private readonly OwnedIdentityApiFactory _factory;

    public OwnedIdentityEndpointTests(OwnedIdentityApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_IssuesOwnedJwtAndHttpOnlyRefreshCookie()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email = $"user-{Guid.NewGuid():N}@example.test", password = "StrongPassword!123" });
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("accessToken").GetString()));
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains("financial_copilot_refresh=", StringComparison.Ordinal) &&
                cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                cookie.Contains("path=/api/auth/v1", StringComparison.OrdinalIgnoreCase) &&
                cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "ai.query",
            json.RootElement.GetProperty("user").GetProperty("permissions")
                .EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Register_ProvisionsFreeBillingAccountForUsage()
    {
        using var client = _factory.CreateClient();

        using var register = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email = $"usage-{Guid.NewGuid():N}@example.test", password = "StrongPassword!123" });
        using var session = await ReadJsonAsync(register);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.RootElement.GetProperty("accessToken").GetString());

        using var usage = await client.GetAsync("/api/v1/usage/me");
        using var summary = await ReadJsonAsync(usage);

        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        Assert.Equal("Individual", summary.RootElement.GetProperty("customerType").GetString());
        Assert.Equal("Prepaid", summary.RootElement.GetProperty("billingMode").GetString());
        Assert.Equal(10000m, summary.RootElement.GetProperty("balance").GetDecimal());
        Assert.Equal(10000m, summary.RootElement.GetProperty("availableSpendingCapacity").GetDecimal());
    }

    [Fact]
    public async Task AuthPreflight_FromLocalFrontendOrigin_AllowsCredentials()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/v1/register");
        request.Headers.Add("Origin", "http://localhost:8080");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:8080",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(
            "true",
            response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        Assert.Contains(
            "POST",
            response.Headers.GetValues("Access-Control-Allow-Methods").Single(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_RepairsMissingBillingAccountForExistingUser()
    {
        using var client = _factory.CreateClient();
        var email = $"repair-{Guid.NewGuid():N}@example.test";
        using var registered = await RegisterAsync(client, email);
        using var registeredJson = await ReadJsonAsync(registered);
        var userId = Guid.Parse(
            registeredJson.RootElement.GetProperty("user").GetProperty("userId").GetString()!);
        await _factory.RemoveBillingAccountAsync(userId);

        using var login = await client.PostAsJsonAsync(
            "/api/auth/v1/login",
            new { email, password = "StrongPassword!123" });
        using var session = await ReadJsonAsync(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.RootElement.GetProperty("accessToken").GetString());
        using var usage = await client.GetAsync("/api/v1/usage/me");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
    }

    [Fact]
    public async Task LoginAndMe_ReturnPersistedIdentityProfile()
    {
        using var client = _factory.CreateClient();
        var email = $"login-{Guid.NewGuid():N}@example.test";
        await RegisterAsync(client, email);

        using var login = await client.PostAsJsonAsync(
            "/api/auth/v1/login",
            new { email, password = "StrongPassword!123" });
        using var json = await ReadJsonAsync(login);
        var accessToken = json.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var me = await client.GetAsync("/api/auth/v1/me");
        using var profile = await ReadJsonAsync(me);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, profile.RootElement.GetProperty("email").GetString());
        Assert.Contains(
            "User",
            profile.RootElement.GetProperty("roles").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Logout_ClearsScopedRefreshCookie()
    {
        using var client = _factory.CreateClient();
        await RegisterAsync(client, $"logout-{Guid.NewGuid():N}@example.test");

        using var logout = await client.PostAsync("/api/auth/v1/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(
            logout.Headers.GetValues("Set-Cookie"),
            cookie => cookie.Contains("financial_copilot_refresh=", StringComparison.Ordinal) &&
                cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase) &&
                cookie.Contains("path=/api/auth/v1", StringComparison.OrdinalIgnoreCase) &&
                cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_RotatesCookieAndRejectsReplayedToken()
    {
        using var client = _factory.CreateClient();
        var register = await RegisterAsync(client, $"refresh-{Guid.NewGuid():N}@example.test");
        var originalCookie = register.Headers.GetValues("Set-Cookie").Single();
        var originalToken = ReadCookieValue(originalCookie);

        using var refresh = await client.PostAsync("/api/auth/v1/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var replacementCookie = refresh.Headers.GetValues("Set-Cookie").Single();
        Assert.NotEqual(originalToken, ReadCookieValue(replacementCookie));

        using var replayClient = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        replayClient.DefaultRequestHeaders.Add("Cookie", $"financial_copilot_refresh={originalToken}");
        using var replay = await replayClient.PostAsync("/api/auth/v1/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email, password = "StrongPassword!123" });
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static string ReadCookieValue(string cookie) =>
        cookie.Split(';', 2)[0].Split('=', 2)[1];

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

public sealed class OwnedIdentityApiFactory : AuthenticationApiFactory
{
    private readonly string _authDatabaseName = $"owned-identity-{Guid.NewGuid():N}";
    private readonly string _billingDatabaseName = $"owned-identity-billing-{Guid.NewGuid():N}";

    public OwnedIdentityApiFactory()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        dbContext.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(_authDatabaseName));

            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options =>
                options.UseInMemoryDatabase(_billingDatabaseName));
        });
    }

    public async Task RemoveBillingAccountAsync(Guid userId)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await dbContext.WalletProjections
            .SingleAsync(row => row.CustomerAccountId == account.Id);
        dbContext.WalletProjections.Remove(wallet);
        dbContext.CustomerAccounts.Remove(account);
        await dbContext.SaveChangesAsync();
    }
}
