using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
    public async Task Register_CreatesUnverifiedAccountWithoutIssuingSession()
    {
        using var client = _factory.CreateClient();
        var email = $"user-{Guid.NewGuid():N}@example.test";

        using var response = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email, password = "StrongPassword!123" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(response.Headers, header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
        using var login = await client.PostAsJsonAsync(
            "/api/auth/v1/login", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await db.Users.SingleAsync(row => row.Email == email);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.EmailVerificationRequired);
        var tokenRow = await db.AuthEmailActionTokens.SingleAsync(row => row.UserId == user.Id);
        Assert.NotEqual(_factory.EmailSender.LatestToken, tokenRow.TokenHash);
        Assert.Contains("lang=\"fa\" dir=\"rtl\"", _factory.EmailSender.LatestMessage!.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("https://app.example.test/verify-email#token=", _factory.EmailSender.LatestMessage.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_ProvisionsFreeBillingAccountForUsage()
    {
        using var client = _factory.CreateClient();

        using var session = await ReadJsonAsync(await RegisterAsync(client, $"usage-{Guid.NewGuid():N}@example.test"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            session.RootElement.GetProperty("accessToken").GetString());

        using var usage = await client.GetAsync("/api/v1/usage/me");
        using var summary = await ReadJsonAsync(usage);

        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        Assert.Equal("Individual", summary.RootElement.GetProperty("customerType").GetString());
        Assert.Equal("Prepaid", summary.RootElement.GetProperty("billingMode").GetString());
        Assert.Equal(25m, summary.RootElement.GetProperty("balance").GetDecimal());
        Assert.Equal(25m, summary.RootElement.GetProperty("availableSpendingCapacity").GetDecimal());
        Assert.Equal("Free", summary.RootElement.GetProperty("planCode").GetString());
        Assert.Equal(25m, summary.RootElement.GetProperty("planIncludedCredits").GetDecimal());
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

    [Fact]
    public async Task PasswordReset_RevokesRefreshAndNewOwnedAccessTokens()
    {
        using var client = _factory.CreateClient();
        var registered = await RegisterAsync(client, $"reset-{Guid.NewGuid():N}@example.test");
        var cookie = registered.Headers.GetValues("Set-Cookie").Single();
        using var session = await ReadJsonAsync(registered);
        var accessToken = session.RootElement.GetProperty("accessToken").GetString();
        var email = session.RootElement.GetProperty("user").GetProperty("email").GetString();
        using var secondLogin = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.OK, secondLogin.StatusCode);
        var secondCookie = secondLogin.Headers.GetValues("Set-Cookie").Single();
        var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/v1/me");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var beforeReset = await client.SendAsync(profileRequest);
        Assert.Equal(HttpStatusCode.OK, beforeReset.StatusCode);

        using var request = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var resetToken = _factory.EmailSender.LatestToken;
        using var reset = await client.PostAsJsonAsync("/api/auth/v1/password/reset", new { token = resetToken, newPassword = "NewStrongPassword!123" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var replay = await client.PostAsJsonAsync("/api/auth/v1/password/reset", new { token = resetToken, newPassword = "AnotherStrongPassword!123" });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var oldJwtClient = _factory.CreateClient();
        oldJwtClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var afterReset = await oldJwtClient.GetAsync("/api/auth/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterReset.StatusCode);

        using var refreshClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        refreshClient.DefaultRequestHeaders.Add("Cookie", "financial_copilot_refresh=" + ReadCookieValue(cookie));
        using var refresh = await refreshClient.PostAsync("/api/auth/v1/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var secondRefreshClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        secondRefreshClient.DefaultRequestHeaders.Add("Cookie", "financial_copilot_refresh=" + ReadCookieValue(secondCookie));
        using var secondRefresh = await secondRefreshClient.PostAsync("/api/auth/v1/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRefresh.StatusCode);

        using var oldPassword = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" });
        using var newPassword = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "NewStrongPassword!123" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task UnknownAndRegisteredForgotRequestsHaveSamePublicResponse()
    {
        using var client = _factory.CreateClient();
        var knownEmail = $"known-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email = knownEmail, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        using var unknown = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email = $"missing-{Guid.NewGuid():N}@example.test" });
        using var known = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email = knownEmail });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());
        using var unknownResend = await client.PostAsJsonAsync("/api/auth/v1/email/resend", new { email = $"missing-{Guid.NewGuid():N}@example.test" });
        using var knownResend = await client.PostAsJsonAsync("/api/auth/v1/email/resend", new { email = knownEmail });
        Assert.Equal(HttpStatusCode.Accepted, unknownResend.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, knownResend.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknownResend.Content.ReadAsStringAsync());
        Assert.Equal(await unknownResend.Content.ReadAsStringAsync(), await knownResend.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LegacyUnconfirmedAccount_RemainsLoginEligibleAfterPublicResend()
    {
        using var client = _factory.CreateClient();
        var email = $"legacy-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.SingleAsync(row => row.Email == email);
            user.EmailVerificationRequired = false;
            var firstToken = await db.AuthEmailActionTokens.SingleAsync(row => row.UserId == user.Id);
            firstToken.CreatedAtUtc = DateTime.UtcNow.AddHours(-2);
            await db.SaveChangesAsync();
        }

        using var before = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" });
        using var resend = await client.PostAsJsonAsync("/api/auth/v1/email/resend", new { email });
        using var after = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        using var scopeAfter = _factory.Services.CreateScope();
        var authDb = scopeAfter.ServiceProvider.GetRequiredService<AuthDbContext>();
        var legacyUser = await authDb.Users.SingleAsync(row => row.Email == email);
        Assert.False(legacyUser.EmailConfirmed);
        Assert.False(legacyUser.EmailVerificationRequired);
    }

    [Fact]
    public async Task VerificationToken_IsSingleUse()
    {
        using var client = _factory.CreateClient();
        var email = $"verify-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        var token = _factory.EmailSender.LatestToken;

        using var first = await client.PostAsJsonAsync("/api/auth/v1/email/verify", new { token });
        using var replay = await client.PostAsJsonAsync("/api/auth/v1/email/verify", new { token });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ExpiredVerificationToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var email = $"expired-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        var token = _factory.EmailSender.LatestToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            var row = await db.AuthEmailActionTokens.SingleAsync(candidate => candidate.UserId == user.Id && candidate.Purpose == "email_verification");
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        using var response = await client.PostAsJsonAsync("/api/auth/v1/email/verify", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredPasswordResetToken_IsRejected()
    {
        using var client = _factory.CreateClient();
        var email = $"expired-reset-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        using var request = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = _factory.EmailSender.TokenFor(email);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            var row = await db.AuthEmailActionTokens.SingleAsync(candidate => candidate.Purpose == "password_reset" && candidate.UserId == user.Id);
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var reset = await client.PostAsJsonAsync("/api/auth/v1/password/reset", new { token, newPassword = "NewStrongPassword!123" });
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task ConcurrentPasswordReset_AllowsAtMostOneSuccess()
    {
        using var client = _factory.CreateClient();
        var email = $"concurrent-reset-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        using var request = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        var token = _factory.EmailSender.TokenFor(email);

        using var firstClient = _factory.CreateClient();
        using var secondClient = _factory.CreateClient();
        var firstTask = firstClient.PostAsJsonAsync("/api/auth/v1/password/reset", new { token, newPassword = "FirstStrongPassword!123" });
        var secondTask = secondClient.PostAsJsonAsync("/api/auth/v1/password/reset", new { token, newPassword = "SecondStrongPassword!123" });
        using var first = await firstTask;
        using var second = await secondTask;
        Assert.Equal(1, new[] { first.StatusCode, second.StatusCode }.Count(status => status == HttpStatusCode.NoContent));
        Assert.Contains(HttpStatusCode.BadRequest, new[] { first.StatusCode, second.StatusCode });
    }

    [Fact]
    public async Task PasswordReset_DoesNotVerifyAnUnverifiedAccount()
    {
        using var client = _factory.CreateClient();
        var email = $"unverified-reset-{Guid.NewGuid():N}@example.test";
        using var registration = await client.PostAsJsonAsync("/api/auth/v1/register", new { email, password = "StrongPassword!123" });
        Assert.Equal(HttpStatusCode.Accepted, registration.StatusCode);
        using var request = await client.PostAsJsonAsync("/api/auth/v1/password/forgot", new { email });
        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        using var reset = await client.PostAsJsonAsync("/api/auth/v1/password/reset", new { token = _factory.EmailSender.LatestToken, newPassword = "NewStrongPassword!123" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "NewStrongPassword!123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = await db.Users.SingleAsync(row => row.Email == email);
        Assert.False(user.EmailConfirmed);
        Assert.True(user.EmailVerificationRequired);
    }

    [Fact]
    public async Task RegistrationRemainsAcceptedWhenEmailDeliveryFails()
    {
        using var client = _factory.CreateClient();
        _factory.EmailSender.ShouldFail = true;
        try
        {
            using var response = await client.PostAsJsonAsync("/api/auth/v1/register", new { email = $"delivery-failure-{Guid.NewGuid():N}@example.test", password = "StrongPassword!123" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        finally
        {
            _factory.EmailSender.ShouldFail = false;
        }
    }

    private async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email, password = "StrongPassword!123" });
        response.EnsureSuccessStatusCode();
        response.Dispose();
        var token = _factory.EmailSender.LatestToken;
        using var verification = await client.PostAsJsonAsync("/api/auth/v1/email/verify", new { token });
        verification.EnsureSuccessStatusCode();
        return await client.PostAsJsonAsync("/api/auth/v1/login", new { email, password = "StrongPassword!123" });
    }

    private static string ReadCookieValue(string cookie) =>
        cookie.Split(';', 2)[0].Split('=', 2)[1];

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

public class OwnedIdentityApiFactory : AuthenticationApiFactory
{
    private readonly SqliteConnection _authConnection = new($"Data Source=owned-identity-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private readonly string _billingDatabaseName = $"owned-identity-billing-{Guid.NewGuid():N}";
    public RecordingEmailSender EmailSender { get; } = new();

    public OwnedIdentityApiFactory()
    {
        _authConnection.Open();
        using var scope = Services.CreateScope();
        var authDbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        authDbContext.Database.EnsureCreated();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        dbContext.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ApiKeys:Clients:0:TenantId"] = "11111111-1111-1111-1111-111111111111",
                ["Telegram:AccountLinking:BotUsername"] = "financial_copilot_test_bot",
                ["Telegram:AccountLinking:WebConfirmationBaseUrl"] = "https://app.example.test/telegram/link/confirm",
                ["Telegram:AccountLinking:TokenLifetimeMinutes"] = "10",
                ["Authentication:Email:Enabled"] = "true",
                ["Authentication:Email:FrontendBaseUrl"] = "https://app.example.test",
                ["Authentication:Email:TokenHashSecret"] = "integration-test-email-hash-secret-more-than-32-characters",
                ["Authentication:Smtp:Host"] = "smtp.example.test",
                ["Authentication:Smtp:SenderAddress"] = "noreply@example.test",
                ["RateLimiting:PublicAuthentication:PermitLimit"] = "1000"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseSqlite(_authConnection.ConnectionString));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _authConnection.Dispose();
    }
}

public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();
    public bool ShouldFail { get; set; }
    public int Count => _messages.Count;
    public EmailMessage? LatestMessage => _messages.TryPeek(out _) ? _messages.LastOrDefault() : null;
    public string LatestToken
    {
        get => ExtractToken(LatestMessage ?? throw new InvalidOperationException("No email has been recorded."));
    }

    public string TokenFor(string email) => ExtractToken(_messages.LastOrDefault(message =>
        string.Equals(message.To, email, StringComparison.OrdinalIgnoreCase)) ??
        throw new InvalidOperationException($"No email has been recorded for {email}."));

    private static string ExtractToken(EmailMessage message)
    {
        var marker = "#token=";
        var start = message.TextBody.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("The email did not contain an action token.");
        var value = message.TextBody[(start + marker.Length)..].Split([' ', '\r', '\n'], 2)[0];
        return Uri.UnescapeDataString(value);
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        if (ShouldFail) throw new EmailDeliveryException("Test delivery failure.");
        return Task.CompletedTask;
    }
}
