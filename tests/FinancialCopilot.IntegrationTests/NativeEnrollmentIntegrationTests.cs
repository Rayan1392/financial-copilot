using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Native email enrollment, first password and account state for TahlilApp-linked users (Feature 139, T-26/T-27).
/// The launch exchange is the only replaced collaborator (gateway double); enrollment mail goes to the recording sender.
/// </summary>
public sealed class NativeEnrollmentIntegrationTests(NativeEnrollmentApiFactory factory) : IClassFixture<NativeEnrollmentApiFactory>
{
    private const string Password = "NativePassword!123";

    [Fact]
    public async Task EnrollConfirmPassword_ThenDirectLogin_ReturnsTheLaunchUser_WithoutCreatingAUser()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        var usersBefore = await CountUsersAsync(factory);

        using (var client = ClientFor(factory, launched.AccessToken))
        {
            using var enroll = await EnrollAsync(client, email);
            Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
            var token = factory.EmailSender.TokenFor(email);

            using var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

            using var password = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = Password });
            Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);
        }

        using var anonymous = factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("/api/auth/v1/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var session = await ReadAsync(login);
        Assert.Equal(launched.UserId, session.RootElement.GetProperty("user").GetProperty("userId").GetGuid());
        Assert.Equal(usersBefore, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task AccountState_FollowsTheJourney_AFromLaunch_ThroughBAndC()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);

        var before = await ReadAccountAsync(client);
        Assert.Equal("A", before.GetProperty("accountAuthState").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("email").ValueKind);
        Assert.False(before.GetProperty("emailConfirmed").GetBoolean());
        Assert.False(before.GetProperty("hasPassword").GetBoolean());
        Assert.True(before.GetProperty("hasTahlilAppIdentity").GetBoolean());

        using (var enroll = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        using (var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token = factory.EmailSender.TokenFor(email) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }

        var afterConfirm = await ReadAccountAsync(client);
        Assert.Equal("B", afterConfirm.GetProperty("accountAuthState").GetString());
        Assert.Equal(email, afterConfirm.GetProperty("email").GetString());
        Assert.True(afterConfirm.GetProperty("emailConfirmed").GetBoolean());
        Assert.False(afterConfirm.GetProperty("hasPassword").GetBoolean());

        using (var password = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = Password }))
        {
            Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);
        }

        var afterPassword = await ReadAccountAsync(client);
        Assert.Equal("C", afterPassword.GetProperty("accountAuthState").GetString());
        Assert.True(afterPassword.GetProperty("hasPassword").GetBoolean());
        Assert.True(afterPassword.GetProperty("hasTahlilAppIdentity").GetBoolean());
    }

    [Fact]
    public async Task AccountState_ForNativeAccountWithoutTahlilAppIdentity_IsD()
    {
        var email = NewEmail();
        using var anonymous = factory.CreateClient();
        using (var registered = await anonymous.PostAsJsonAsync("/api/auth/v1/register", new { email, password = Password }))
        {
            Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        }
        using (var verified = await anonymous.PostAsJsonAsync("/api/auth/v1/email/verify", new { token = factory.EmailSender.TokenFor(email) }))
        {
            Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        }

        string accessToken;
        using (var login = await anonymous.PostAsJsonAsync("/api/auth/v1/login", new { email, password = Password }))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            using var session = await ReadAsync(login);
            accessToken = session.RootElement.GetProperty("accessToken").GetString()!;
        }

        using var client = ClientFor(factory, accessToken);
        var account = await ReadAccountAsync(client);
        Assert.Equal("D", account.GetProperty("accountAuthState").GetString());
        Assert.False(account.GetProperty("hasTahlilAppIdentity").GetBoolean());
        Assert.True(account.GetProperty("hasPassword").GetBoolean());
    }

    [Fact]
    public async Task Enrollment_TrimsAndLowercasesTheAddress_BeforeSending()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);

        using var response = await EnrollAsync(client, $"  {email.ToUpperInvariant()}  ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(email, factory.EmailSender.LatestMessage!.To);
    }

    [Fact]
    public async Task Enrollment_MalformedAddress_IsRejectedWith400_AndSendsNothing()
    {
        var launched = await LaunchUserAsync(factory);
        var sentBefore = factory.EmailSender.Count;
        using var client = ClientFor(factory, launched.AccessToken);

        using var response = await EnrollAsync(client, "Name <not-a-mailbox>");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https://financialcopilot/errors/invalid-email", await ProblemTypeAsync(response));
        Assert.Equal(sentBefore, factory.EmailSender.Count);
    }

    [Fact]
    public async Task Enrollment_WithoutBearer_IsUnauthorized()
    {
        using var anonymous = factory.CreateClient();

        using var response = await EnrollAsync(anonymous, NewEmail());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enrollment_ForAnotherAccountsAddress_ReturnsGenericAcceptance_AndStoresAndSendsNothing()
    {
        var owner = NewEmail();
        using (var anonymous = factory.CreateClient())
        {
            using var registered = await anonymous.PostAsJsonAsync("/api/auth/v1/register", new { email = owner, password = Password });
            Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        }
        var ownerBefore = await ReadUserAsync(factory, owner);
        var launched = await LaunchUserAsync(factory);
        var sentBefore = factory.EmailSender.Count;
        var tokensBefore = await CountEnrollmentTokensAsync(factory, owner);

        using var client = ClientFor(factory, launched.AccessToken);
        using var response = await EnrollAsync(client, owner);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sentBefore, factory.EmailSender.Count);
        Assert.Equal(tokensBefore, await CountEnrollmentTokensAsync(factory, owner));
        var ownerAfter = await ReadUserAsync(factory, owner);
        Assert.Equal(ownerBefore.Id, ownerAfter.Id);
        Assert.Equal(ownerBefore.EmailConfirmed, ownerAfter.EmailConfirmed);
        Assert.Equal(ownerBefore.EmailVerificationRequired, ownerAfter.EmailVerificationRequired);
        var launchedAccount = await ReadAccountAsync(client);
        Assert.Equal("A", launchedAccount.GetProperty("accountAuthState").GetString());
    }

    [Fact]
    public async Task Enrollment_RepeatedInsideCooldown_SendsNoSecondMessage()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);

        using (var first = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var sentAfterFirst = factory.EmailSender.Count;
        using (var second = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        Assert.Equal(sentAfterFirst, factory.EmailSender.Count);
        Assert.Equal(1, await CountEnrollmentTokensAsync(factory, email));
    }

    [Fact]
    public async Task Confirm_WhenAddressWasClaimedElsewhereSinceEnrollment_Returns409_AndLeavesEmailUnchanged()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        using (var enroll = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        // Read the token before another account sends its own message to the same address.
        var token = factory.EmailSender.TokenFor(email);
        using (var anonymous = factory.CreateClient())
        {
            using var registered = await anonymous.PostAsJsonAsync("/api/auth/v1/register", new { email, password = Password });
            Assert.Equal(HttpStatusCode.Accepted, registered.StatusCode);
        }
        var ownerId = (await ReadUserAsync(factory, email)).Id;

        using var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });

        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("https://financialcopilot/errors/email-already-registered", await ProblemTypeAsync(confirm));
        var account = await ReadAccountAsync(client);
        Assert.Equal(JsonValueKind.Null, account.GetProperty("email").ValueKind);
        Assert.Equal("A", account.GetProperty("accountAuthState").GetString());
        Assert.Equal(ownerId, (await ReadUserAsync(factory, email)).Id);
    }

    [Fact]
    public async Task Confirm_ReplayedToken_Returns400()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        using (var enroll = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        var token = factory.EmailSender.TokenFor(email);
        using (var first = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token }))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var replay = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("https://financialcopilot/errors/invalid-or-expired-token", await ProblemTypeAsync(replay));
    }

    [Fact]
    public async Task Confirm_TokenHeldByAnotherUser_Returns400_AndIsNotConsumed()
    {
        var owner = await LaunchUserAsync(factory);
        var intruder = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var ownerClient = ClientFor(factory, owner.AccessToken);
        using (var enroll = await EnrollAsync(ownerClient, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        var token = factory.EmailSender.TokenFor(email);

        using (var intruderClient = ClientFor(factory, intruder.AccessToken))
        using (var rejected = await intruderClient.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("https://financialcopilot/errors/invalid-or-expired-token", await ProblemTypeAsync(rejected));
        }

        using var confirmed = await ownerClient.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    [Fact]
    public async Task Confirm_ExpiredToken_Returns400()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        using (var enroll = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        var token = factory.EmailSender.TokenFor(email);
        // Expiry is moved into the past directly in the database; the token itself is unchanged.
        await ExpireEnrollmentTokenAsync(factory, email);

        using var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token });

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal("https://financialcopilot/errors/invalid-or-expired-token", await ProblemTypeAsync(confirm));
    }

    [Fact]
    public async Task Password_BeforeEmailConfirmed_Returns409_EmailNotConfirmed()
    {
        var launched = await LaunchUserAsync(factory);
        using var client = ClientFor(factory, launched.AccessToken);

        using var response = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = Password });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("https://financialcopilot/errors/email-not-confirmed", await ProblemTypeAsync(response));
    }

    [Fact]
    public async Task Password_WhenAlreadySet_Returns409_PasswordAlreadySet()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        await CompleteEnrollmentAsync(client, email);
        using (var first = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = Password }))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using var second = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = "AnotherPassword!456" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("https://financialcopilot/errors/password-already-set", await ProblemTypeAsync(second));
    }

    [Fact]
    public async Task Password_WeakPassword_Returns400_PasswordRejected_AndNoPasswordIsStored()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        await CompleteEnrollmentAsync(client, email);

        using var weak = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = "weak" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("https://financialcopilot/errors/password-rejected", await ProblemTypeAsync(weak));
        var account = await ReadAccountAsync(client);
        Assert.Equal("B", account.GetProperty("accountAuthState").GetString());
    }

    [Fact]
    public async Task Login_ForConfirmedAccountWithoutPassword_ReturnsInvalidCredentials()
    {
        var launched = await LaunchUserAsync(factory);
        var email = NewEmail();
        using var client = ClientFor(factory, launched.AccessToken);
        await CompleteEnrollmentAsync(client, email);

        using var anonymous = factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("/api/auth/v1/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("https://financialcopilot/errors/invalid-credentials", await ProblemTypeAsync(login));
    }

    [Fact]
    public async Task Login_WhenEntitlementRefreshFails_StillSignsInTheSameUser()
    {
        using var failing = new RefresherFailureTahlilAppApiFactory();
        var launched = await LaunchUserAsync(failing);
        var email = NewEmail();
        using (var client = ClientFor(failing, launched.AccessToken))
        {
            await CompleteEnrollmentAsync(client, email, failing);
            using var password = await client.PostAsJsonAsync("/api/auth/v1/credentials/password", new { newPassword = Password });
            Assert.Equal(HttpStatusCode.NoContent, password.StatusCode);
        }

        using var anonymous = failing.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("/api/auth/v1/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var session = await ReadAsync(login);
        Assert.Equal(launched.UserId, session.RootElement.GetProperty("user").GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Enrollment_WhenEmailDeliveryIsDisabled_Returns503_AndExposesNoToken()
    {
        using var disabled = new EmailDisabledTahlilAppApiFactory();
        var launched = await LaunchUserAsync(disabled);
        var email = NewEmail();
        var sentBefore = disabled.EmailSender.Count;
        using var client = ClientFor(disabled, launched.AccessToken);

        using var response = await EnrollAsync(client, email);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("https://financialcopilot/errors/email-delivery-unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("#token=", body, StringComparison.Ordinal);
        Assert.Equal(sentBefore, disabled.EmailSender.Count);
        Assert.Equal(0, await CountEnrollmentTokensAsync(disabled, email));
    }

    private async Task CompleteEnrollmentAsync(
        HttpClient client,
        string email,
        NativeEnrollmentApiFactory? target = null)
    {
        var source = target ?? factory;
        using (var enroll = await EnrollAsync(client, email)) Assert.Equal(HttpStatusCode.Accepted, enroll.StatusCode);
        var token = source.EmailSender.TokenFor(email);
        using (var confirm = await client.PostAsJsonAsync("/api/auth/v1/credentials/email/confirm", new { token }))
        {
            Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        }
    }

    private static HttpClient ClientFor(NativeEnrollmentApiFactory target, string accessToken)
    {
        var client = target.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static Task<HttpResponseMessage> EnrollAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/v1/credentials/email", new { email });

    private static async Task<JsonElement> ReadAccountAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/v1/account");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await ReadAsync(response);
        return json.RootElement.Clone();
    }

    private static async Task<LaunchedUser> LaunchUserAsync(NativeEnrollmentApiFactory target)
    {
        var code = NewCode();
        target.Gateway.Respond(code, Succeeded($"tahlil-{Guid.NewGuid():N}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/v1/tahlilapp/launch")
        {
            Content = JsonContent.Create(new { code })
        };
        using var response = await target.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadAsync(response);
        return new LaunchedUser(
            body.RootElement.GetProperty("user").GetProperty("userId").GetGuid(),
            body.RootElement.GetProperty("accessToken").GetString()!);
    }

    private static async Task<int> CountUsersAsync(NativeEnrollmentApiFactory target)
    {
        using var scope = target.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await auth.Users.CountAsync();
    }

    private static async Task<FinancialCopilotUser> ReadUserAsync(NativeEnrollmentApiFactory target, string email)
    {
        using var scope = target.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await auth.Users.AsNoTracking().SingleAsync(row => row.Email == email);
    }

    private static async Task<int> CountEnrollmentTokensAsync(NativeEnrollmentApiFactory target, string pendingEmail)
    {
        using var scope = target.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await auth.AuthEmailActionTokens.CountAsync(row =>
            row.PendingEmail == pendingEmail && row.Purpose == "email_enrollment");
    }

    private async Task ExpireEnrollmentTokenAsync(NativeEnrollmentApiFactory target, string pendingEmail)
    {
        using var scope = target.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await auth.AuthEmailActionTokens.SingleAsync(candidate =>
            candidate.PendingEmail == pendingEmail && candidate.Purpose == "email_enrollment");
        row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await auth.SaveChangesAsync();
    }

    private static async Task<string?> ProblemTypeAsync(HttpResponseMessage response)
    {
        using var json = await ReadAsync(response);
        return json.RootElement.TryGetProperty("type", out var type) ? type.GetString() : null;
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static string NewEmail() => $"enroll-{Guid.NewGuid():N}@example.test";

    private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static TahlilAppExchangeResult Succeeded(string externalId) =>
        new(TahlilAppExchangeOutcome.Succeeded, new TahlilAppExchangeSnapshot(
            ExternalUserId: externalId,
            IsSuspended: false,
            ExternalPlan: "Free",
            IsSubscriptionActive: false,
            SubscriptionExpiresAt: null,
            ExternalRevision: null,
            ExternalPermissionCount: 0,
            FirstName: IncomingProfileField.Absent,
            LastName: IncomingProfileField.Absent,
            NationalId: IncomingProfileField.Absent,
            Mobile: IncomingProfileField.Absent));

    private sealed record LaunchedUser(Guid UserId, string AccessToken);
}

/// <summary>
/// Enabled-feature host with the TahlilApp gateway replaced by <see cref="FakeTahlilAppGateway"/>. Mirrors the
/// configuration of TahlilAppLaunchApiFactory, which is sealed and therefore cannot be extended by the variant hosts.
/// Fields are created lazily because the base constructor builds the host before derived field initializers run.
/// </summary>
public class NativeEnrollmentApiFactory : OwnedIdentityApiFactory
{
    private string? _keyRing;
    private FakeTahlilAppGateway? _gateway;

    public FakeTahlilAppGateway Gateway => _gateway ??= new();

    private string KeyRing => _keyRing ??= Directory.CreateTempSubdirectory("native-enrollment-keyring-").FullName;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:TahlilApp:Enabled"] = "true",
                ["Authentication:TahlilApp:BaseUrl"] = "https://tahlilapp.example.test",
                ["DataProtection:KeyRingPath"] = KeyRing
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITahlilAppIdentityGateway>();
            services.AddSingleton<ITahlilAppIdentityGateway>(Gateway);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _keyRing is not null)
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

/// <summary>Same host as the launch factory, with outbound email delivery switched off.</summary>
public sealed class EmailDisabledTahlilAppApiFactory : NativeEnrollmentApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Email:Enabled"] = "false"
            });
        });
    }
}

/// <summary>Same host as the launch factory, with an entitlement refresher that always fails.</summary>
public sealed class RefresherFailureTahlilAppApiFactory : NativeEnrollmentApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITahlilAppEntitlementRefresher>();
            services.AddScoped<ITahlilAppEntitlementRefresher, FailingEntitlementRefresher>();
        });
    }
}

public sealed class FailingEntitlementRefresher : ITahlilAppEntitlementRefresher
{
    public Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated entitlement outage.");
}
