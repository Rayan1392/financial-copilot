using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// End-to-end launch scenarios over the real API pipeline. Only the outbound TahlilApp gateway is replaced
/// by an in-test double (test project only; never a production fallback). Identity, billing, profile
/// encryption and session issuance are the production implementations.
/// </summary>
public sealed class TahlilAppLaunchIntegrationTests(TahlilAppLaunchApiFactory factory) : IClassFixture<TahlilAppLaunchApiFactory>
{
    private const string RefreshCookieName = "financial_copilot_refresh";

    [Fact]
    public async Task FirstLaunch_CreatesOneUserWithoutEmail_EncryptsPii_AndReturnsSession()
    {
        var externalId = NewExternalId();
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(externalId, firstName: "Ali", nationalId: "0012345678", mobile: "09121234567"));

        var response = await Launch(code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(string.Empty, body.RootElement.GetProperty("user").GetProperty("email").GetString());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims,
            claim => claim.Type == "amr" && claim.Value == "tahlilapp");
        Assert.NotNull(ReadRefreshCookie(response));

        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var identity = await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId);
        Assert.Equal("Active", identity.Status);
        var user = await auth.Users.SingleAsync(row => row.Id == identity.LocalUserId);
        Assert.Null(user.Email);

        var profile = await auth.UserProfiles.SingleAsync(row => row.UserId == user.Id);
        Assert.Equal("Ali", profile.FirstName);
        Assert.NotEqual("0012345678", profile.NationalIdEncrypted);
        Assert.NotEqual("+989121234567", profile.MobileEncrypted);
        Assert.Equal("5678", profile.NationalIdLast4);
        Assert.Equal("4567", profile.MobileLast4);
    }

    [Fact]
    public async Task RevokedIdentity_DeletesProfile_AndDeletingLocalUserCascadesProfile()
    {
        var externalId = NewExternalId();
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(externalId, firstName: "Private Name", nationalId: "0012345678", mobile: "09121234567"));
        await Launch(code);

        using (var scope = factory.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var identity = await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId);
            var store = new TahlilAppSyncStateStore(auth);
            await store.RecordIdentityStatusAsync(identity.LocalUserId, TahlilAppIdentityStatus.Revoked,
                DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.False(await auth.UserProfiles.AnyAsync(row => row.UserId == identity.LocalUserId));
            Assert.Equal(TahlilAppIdentityStatus.Revoked, identity.Status);
        }

        var secondExternalId = NewExternalId();
        var secondCode = NewCode();
        factory.Gateway.Respond(secondCode, Succeeded(secondExternalId, firstName: "Private Name", nationalId: "0098765432", mobile: "09129876543"));
        await Launch(secondCode);
        using var deleteScope = factory.Services.CreateScope();
        var deleteAuth = deleteScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var secondIdentity = await deleteAuth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == secondExternalId);
        var user = await deleteAuth.Users.SingleAsync(row => row.Id == secondIdentity.LocalUserId);
        deleteAuth.Users.Remove(user);
        await deleteAuth.SaveChangesAsync();
        Assert.False(await deleteAuth.UserProfiles.AnyAsync(row => row.UserId == secondIdentity.LocalUserId));
        Assert.False(await deleteAuth.TahlilAppExternalIdentities.AnyAsync(row => row.LocalUserId == secondIdentity.LocalUserId));
    }

    [Fact]
    public async Task LaunchLogsNeverContainProfilePiiValues()
    {
        const string firstName = "PrivacySentinelFirst";
        const string lastName = "PrivacySentinelLast";
        const string nationalId = "0012345678";
        const string mobile = "+989121234567";
        var code = NewCode();
        factory.Gateway.Respond(code, new TahlilAppExchangeResult(
            TahlilAppExchangeOutcome.Succeeded,
            new TahlilAppExchangeSnapshot(
                ExternalUserId: ExternalUserId.TryCreate(NewExternalId(), out var parsed) ? parsed!.Value : throw new InvalidOperationException(),
                IsSuspended: false,
                ExternalPlan: "Free",
                IsSubscriptionActive: false,
                SubscriptionExpiresAt: null,
                ExternalRevision: null,
                ExternalPermissionCount: 0,
                FirstName: IncomingProfileField.Valid(firstName),
                LastName: IncomingProfileField.Valid(lastName),
                NationalId: IncomingProfileField.Valid(nationalId),
                Mobile: IncomingProfileField.Valid(mobile))));

        var response = await Launch(code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var logs = factory.LogCapture.Messages;
        Assert.DoesNotContain(logs, message => message.Contains(firstName, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains(lastName, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains(nationalId, StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains(mobile, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondLaunch_ResolvesTheSameLocalUser()
    {
        var externalId = NewExternalId();
        var first = NewCode();
        var second = NewCode();
        factory.Gateway.Respond(first, Succeeded(externalId));
        factory.Gateway.Respond(second, Succeeded(externalId));

        var firstBody = await ReadAsync(await Launch(first));
        var secondBody = await ReadAsync(await Launch(second));

        Assert.Equal(
            firstBody.RootElement.GetProperty("user").GetProperty("userId").GetGuid(),
            secondBody.RootElement.GetProperty("user").GetProperty("userId").GetGuid());
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await auth.TahlilAppExternalIdentities.CountAsync(row => row.ExternalUserId == externalId));
    }

    [Fact]
    public async Task ConcurrentDifferentCodesForSameExternalIdentity_CreateOneLocalUser()
    {
        var externalId = NewExternalId();
        var first = NewCode();
        var second = NewCode();
        factory.Gateway.Respond(first, Succeeded(externalId));
        factory.Gateway.Respond(second, Succeeded(externalId));

        var responses = await Task.WhenAll(Launch(first), Launch(second));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var users = new List<Guid>();
        foreach (var response in responses)
        {
            using var body = await ReadAsync(response);
            users.Add(body.RootElement.GetProperty("user").GetProperty("userId").GetGuid());
        }

        Assert.Single(users.Distinct());
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await auth.TahlilAppExternalIdentities.CountAsync(row => row.ExternalUserId == externalId));
        Assert.Equal(1, await auth.Users.CountAsync(row => row.Id == users[0]));
    }

    [Fact]
    public async Task ReplayedCode_IsRejectedAndNeverExchangedTwice()
    {
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(NewExternalId()));

        var first = await Launch(code);
        var replay = await Launch(code);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(1, factory.Gateway.CallsFor(code));
        Assert.Contains("launch-invalid", ProblemType(await ReadAsync(replay)));
    }

    [Fact]
    public async Task InvalidCode_ReturnsLaunchInvalid()
    {
        var code = NewCode();
        factory.Gateway.Respond(code, new TahlilAppExchangeResult(TahlilAppExchangeOutcome.InvalidCode));

        var response = await Launch(code);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("launch-invalid", ProblemType(await ReadAsync(response)));
    }

    [Fact]
    public async Task TahlilAppUnavailable_ReturnsUnavailable_AndTheCodeIsNotRetried()
    {
        var code = NewCode();
        factory.Gateway.Respond(code, new TahlilAppExchangeResult(TahlilAppExchangeOutcome.Unavailable));

        var first = await Launch(code);
        var second = await Launch(code);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Contains("launch-unavailable", ProblemType(await ReadAsync(first)));
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(1, factory.Gateway.CallsFor(code));
        Assert.Null(ReadRefreshCookie(first));
    }

    [Fact]
    public async Task SuspendedExternalUser_IsDeniedWithoutSession()
    {
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(NewExternalId(), suspended: true));

        var response = await Launch(code);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("launch-account-disabled", ProblemType(await ReadAsync(response)));
        Assert.Null(ReadRefreshCookie(response));
    }

    [Fact]
    public async Task Launch_RevokesAPresentedRefreshToken_AndIssuesANewOne()
    {
        var externalId = NewExternalId();
        var first = NewCode();
        var second = NewCode();
        factory.Gateway.Respond(first, Succeeded(externalId));
        factory.Gateway.Respond(second, Succeeded(externalId));

        var firstResponse = await Launch(first);
        var oldCookie = ReadRefreshCookie(firstResponse)!;
        var secondResponse = await Launch(second, oldCookie);
        var newCookie = ReadRefreshCookie(secondResponse);

        Assert.NotEqual(oldCookie, newCookie);
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var revoked = await auth.RefreshTokens.SingleAsync(row => row.TokenHash == Hash(Uri.UnescapeDataString(oldCookie)));
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal("Replaced by TahlilApp launch.", revoked.RevocationReason);
        Assert.Equal("tahlilapp", (await auth.RefreshTokens.SingleAsync(row => row.TokenHash == Hash(Uri.UnescapeDataString(newCookie!)))).AuthenticationMethod);
    }

    [Fact]
    public async Task EmailLessSession_CanReadMe_WithAnEmptyEmail()
    {
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(NewExternalId()));
        var body = await ReadAsync(await Launch(code));
        var token = body.RootElement.GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var me = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await ReadAsync(me);
        Assert.Equal(string.Empty, profile.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SubscriptionSync_IsIdempotent_AndGrantsNoCredits()
    {
        var externalId = NewExternalId();
        var paidUntil = DateTimeOffset.UtcNow.AddDays(30);
        var first = NewCode();
        var second = NewCode();
        factory.Gateway.Respond(first, Succeeded(externalId, plan: "Plus", active: true, expiresAt: paidUntil, revision: 17));
        factory.Gateway.Respond(second, Succeeded(externalId, plan: "Plus", active: true, expiresAt: paidUntil, revision: 17));

        await Launch(first);
        var userId = await FindLocalUserAsync(externalId);
        var (balanceAfterFirst, revisionAfterFirst) = await ReadAccountAsync(userId);
        await Launch(second);
        var (balanceAfterSecond, revisionAfterSecond) = await ReadAccountAsync(userId);

        Assert.Equal(balanceAfterFirst, balanceAfterSecond);
        Assert.Equal(revisionAfterFirst, revisionAfterSecond);
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await billing.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.NotNull(account.SubscriptionEffectiveTo);
    }

    [Fact]
    public async Task ExpiredPaidSubscription_IsSyncedToFree()
    {
        var externalId = NewExternalId();
        var paid = NewCode();
        var lapsed = NewCode();
        factory.Gateway.Respond(paid, Succeeded(externalId, plan: "Pro", active: true, expiresAt: DateTimeOffset.UtcNow.AddDays(10)));
        factory.Gateway.Respond(lapsed, Succeeded(externalId, plan: "Pro", active: true, expiresAt: DateTimeOffset.UtcNow.AddDays(-1)));

        await Launch(paid);
        await Launch(lapsed);

        var userId = await FindLocalUserAsync(externalId);
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await billing.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Null(account.SubscriptionEffectiveTo);
    }

    [Fact]
    public async Task MalformedCode_IsRejectedWithoutAnExchange()
    {
        var response = await Launch("not a valid code!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Gateway.CallsFor("not a valid code!"));
    }

    [Fact]
    public async Task ConcurrentLaunches_WithOneCode_ExchangeOnceAndCreateOneUser()
    {
        var externalId = NewExternalId();
        var code = NewCode();
        factory.Gateway.Respond(code, Succeeded(externalId));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Launch(code)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, factory.Gateway.CallsFor(code));
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await auth.TahlilAppExternalIdentities.CountAsync(row => row.ExternalUserId == externalId));
    }

    [Fact]
    public async Task ReturningLaunch_KeepsAbsentFields_AndOverwritesAChangedValidValue()
    {
        var externalId = NewExternalId();
        var first = NewCode();
        var absent = NewCode();
        var changed = NewCode();
        factory.Gateway.Respond(first, Succeeded(externalId, firstName: "Ali", nationalId: "0012345678", mobile: "09121234567"));
        factory.Gateway.Respond(absent, Succeeded(externalId));
        factory.Gateway.Respond(changed, Succeeded(externalId, mobile: "09129999999"));

        await Launch(first);
        await Launch(absent);
        var afterAbsent = await ReadProfileAsync(externalId);
        await Launch(changed);
        var afterChanged = await ReadProfileAsync(externalId);

        // Absent fields never erase stored values (Design §10.2, AC-62).
        Assert.Equal("Ali", afterAbsent.FirstName);
        Assert.Equal("5678", afterAbsent.NationalIdLast4);
        Assert.Equal("4567", afterAbsent.MobileLast4);
        // A valid, changed value overwrites (AC-60); the unchanged national id is kept.
        Assert.Equal("9999", afterChanged.MobileLast4);
        Assert.Equal("5678", afterChanged.NationalIdLast4);
    }

    private async Task<UserProfileRow> ReadProfileAsync(string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var userId = (await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId)).LocalUserId;
        return await auth.UserProfiles.AsNoTracking().SingleAsync(row => row.UserId == userId);
    }

    private async Task<HttpResponseMessage> Launch(string code, string? refreshCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/v1/tahlilapp/launch")
        {
            Content = JsonContent.Create(new { code })
        };
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshCookie}");
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private async Task<Guid> FindLocalUserAsync(string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return (await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId)).LocalUserId;
    }

    private async Task<(decimal Balance, long Revision)> ReadAccountAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await billing.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await billing.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        return (wallet.Balance, account.SubscriptionRevision);
    }

    private static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        var cookie = values.FirstOrDefault(value => value.StartsWith(RefreshCookieName + "=", StringComparison.Ordinal));
        return cookie?.Split(';', 2)[0].Split('=', 2)[1];
    }

    private static string ProblemType(JsonDocument document) =>
        document.RootElement.TryGetProperty("type", out var type) ? type.GetString() ?? string.Empty : string.Empty;

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string NormaliseMobile(string raw)
    {
        IranianMobile.TryCreate(raw, out var mobile);
        return mobile!.Value;
    }

    private static string NewExternalId() => $"tahlil-{Guid.NewGuid():N}";

    private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static TahlilAppExchangeResult Succeeded(
        string externalId,
        bool suspended = false,
        string plan = "Free",
        bool active = false,
        DateTimeOffset? expiresAt = null,
        long? revision = null,
        string? firstName = null,
        string? nationalId = null,
        string? mobile = null) =>
        new(TahlilAppExchangeOutcome.Succeeded, new TahlilAppExchangeSnapshot(
            ExternalUserId: externalId,
            IsSuspended: suspended,
            ExternalPlan: plan,
            IsSubscriptionActive: active,
            SubscriptionExpiresAt: expiresAt,
            ExternalRevision: revision,
            ExternalPermissionCount: 0,
            FirstName: firstName is null ? IncomingProfileField.Absent : IncomingProfileField.Valid(firstName),
            LastName: IncomingProfileField.Absent,
            NationalId: nationalId is null ? IncomingProfileField.Absent : IncomingProfileField.Valid(nationalId),
            Mobile: mobile is null ? IncomingProfileField.Absent : IncomingProfileField.Valid(NormaliseMobile(mobile))));
}

/// <summary>Enabled-feature host. Only the outbound gateway is replaced, with an in-test double.</summary>
public class TahlilAppLaunchApiFactory : OwnedIdentityApiFactory
{
    private readonly string _keyRing = Directory.CreateTempSubdirectory("tahlilapp-keyring-").FullName;

    public FakeTahlilAppGateway Gateway { get; } = new();
    public TahlilAppLogCaptureProvider LogCapture { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(LogCapture));
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TahlilApp:Enabled"] = "true",
                ["Authentication:TahlilApp:BaseUrl"] = "https://tahlilapp.example.test",
                ["DataProtection:KeyRingPath"] = _keyRing,
                ["Feature141:Enabled"] = "false"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            foreach (var hostedService in services.Where(descriptor =>
                         descriptor.ServiceType == typeof(IHostedService) &&
                         descriptor.ImplementationType is { } implementationType &&
                         typeof(BackgroundService).IsAssignableFrom(implementationType)).ToArray())
            {
                services.Remove(hostedService);
            }

            services.RemoveAll<ITahlilAppIdentityGateway>();
            services.AddSingleton<ITahlilAppIdentityGateway>(Gateway);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Directory.Delete(_keyRing, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temporary key ring.
            }
        }
    }
}

public sealed class TahlilAppLaunchRateLimitFactory : TahlilAppLaunchApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RateLimiting:PublicAuthentication:PermitLimit"] = "1",
                ["RateLimiting:PublicAuthentication:WindowSeconds"] = "60"
            }));
    }
}

public sealed class TahlilAppLaunchRateLimitIntegrationTests(TahlilAppLaunchRateLimitFactory factory)
    : IClassFixture<TahlilAppLaunchRateLimitFactory>
{
    [Fact]
    public async Task LaunchEndpoint_Returns429AfterConfiguredPublicLimit()
    {
        using var client = factory.CreateClient();
        var first = await client.PostAsJsonAsync("/api/auth/v1/tahlilapp/launch", new { code = NewCode() });
        var second = await client.PostAsJsonAsync("/api/auth/v1/tahlilapp/launch", new { code = NewCode() });

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal((HttpStatusCode)429, second.StatusCode);
    }

    private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

public sealed class TahlilAppLogCaptureProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();
    public IReadOnlyCollection<string> Messages => messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            messages.Enqueue(SerializeState(state));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Enqueue(formatter(state, exception));
            messages.Enqueue(SerializeState(state));
            if (exception is not null) messages.Enqueue(exception.ToString());
        }

        private static string SerializeState<TState>(TState state) => state is IEnumerable<KeyValuePair<string, object?>> properties
            ? string.Join(" ", properties.Select(property => $"{property.Key}={property.Value}"))
            : state?.ToString() ?? string.Empty;
    }
}

/// <summary>Test double for the TahlilApp exchange. Records calls per code; never used outside tests.</summary>
public sealed class FakeTahlilAppGateway : ITahlilAppIdentityGateway
{
    private readonly ConcurrentDictionary<string, TahlilAppExchangeResult> _responses = new();
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public void Respond(string code, TahlilAppExchangeResult result) => _responses[code] = result;

    public int CallsFor(string code) => _calls.TryGetValue(code, out var count) ? count : 0;

    public Task<TahlilAppExchangeResult> ExchangeAsync(string code, string correlationId, CancellationToken cancellationToken)
    {
        _calls.AddOrUpdate(code, 1, (_, count) => count + 1);
        return Task.FromResult(_responses.TryGetValue(code, out var result)
            ? result
            : new TahlilAppExchangeResult(TahlilAppExchangeOutcome.InvalidCode));
    }

    private readonly ConcurrentDictionary<string, TahlilAppStatusResult> _statuses = new();
    private readonly ConcurrentDictionary<string, int> _statusCalls = new();

    /// <summary>Sets the status answer for one external user. Unmapped users answer Unavailable, so a missing mapping never revokes.</summary>
    public void RespondStatus(string externalUserId, TahlilAppStatusResult result) => _statuses[externalUserId] = result;

    public int StatusCallsFor(string externalUserId) => _statusCalls.TryGetValue(externalUserId, out var count) ? count : 0;

    public Task<TahlilAppStatusResult> GetStatusAsync(string externalUserId, string correlationId, CancellationToken cancellationToken)
    {
        _statusCalls.AddOrUpdate(externalUserId, 1, (_, count) => count + 1);
        return Task.FromResult(_statuses.TryGetValue(externalUserId, out var result)
            ? result
            : new TahlilAppStatusResult(TahlilAppStatusOutcome.Unavailable));
    }
}
