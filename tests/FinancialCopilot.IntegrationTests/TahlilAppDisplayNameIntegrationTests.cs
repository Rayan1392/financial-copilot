using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Official TahlilApp <c>name</c> → <c>auth_user_profiles.DisplayName</c> (Feature 139 Revision 9).
/// Every launch here goes through the production gateway and exchange parser over a stubbed HTTP body, then the
/// production provisioner, profile endpoint and admin directory query. Only the outbound HTTP response is faked.
/// </summary>
public sealed class TahlilAppDisplayNameIntegrationTests(TahlilAppLaunchApiFactory factory) : IClassFixture<TahlilAppLaunchApiFactory>
{
    private const string PersianName = "کامران عبدی";

    [Fact]
    public async Task OfficialExchange_FirstLaunch_PersistsThePersianNameAsDisplayName_AndLinksTheExternalId()
    {
        var externalId = NewExternalId();

        var response = await LaunchOfficialAsync(externalId, $"\"name\":\"{PersianName}\",");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var identity = await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId);
        Assert.Equal(ExternalUserId.Provider, identity.Provider);
        var user = await auth.Users.SingleAsync(row => row.Id == identity.LocalUserId);
        Assert.Null(user.Email);
        Assert.StartsWith("tahlilapp-", user.UserName);
        Assert.DoesNotContain(externalId, user.UserName);

        var profile = await auth.UserProfiles.AsNoTracking().SingleAsync(row => row.UserId == identity.LocalUserId);
        Assert.Equal(PersianName, profile.DisplayName);
        // The full name is never split into first/last name.
        Assert.Null(profile.FirstName);
        Assert.Null(profile.LastName);
    }

    [Fact]
    public async Task OfficialExchange_RepeatLaunchWithAChangedName_OverwritesDisplayName_AndReusesTheSameLink()
    {
        var externalId = NewExternalId();
        var firstBody = await ReadAsync(await LaunchOfficialAsync(externalId, $"\"name\":\"{PersianName}\","));
        var before = await ReadProfileAsync(externalId);

        var secondBody = await ReadAsync(await LaunchOfficialAsync(externalId, "\"name\":\"کامران عبدی‌زاده\","));
        var after = await ReadProfileAsync(externalId);

        Assert.Equal(UserIdOf(firstBody), UserIdOf(secondBody));
        Assert.Equal("کامران عبدی‌زاده", after.DisplayName);
        Assert.True(after.UpdatedAtUtc > before.UpdatedAtUtc);
        Assert.True(after.ProfileSyncedAtUtc > before.ProfileSyncedAtUtc);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await auth.TahlilAppExternalIdentities.CountAsync(row => row.ExternalUserId == externalId));
        Assert.Equal(1, await auth.UserProfiles.CountAsync(row => row.UserId == UserIdOf(firstBody)));
    }

    [Fact]
    public async Task OfficialExchange_RepeatLaunchWithTheIdenticalName_DoesNotTouchUpdatedAtUtc()
    {
        var externalId = NewExternalId();
        await LaunchOfficialAsync(externalId, $"\"name\":\"{PersianName}\",");
        var before = await ReadProfileAsync(externalId);

        await LaunchOfficialAsync(externalId, $"\"name\":\" {PersianName} \",");
        var after = await ReadProfileAsync(externalId);

        Assert.Equal(PersianName, after.DisplayName);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.True(after.ProfileSyncedAtUtc > before.ProfileSyncedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"name\":null,")]
    [InlineData("\"name\":42,")]
    [InlineData("\"name\":\"   \",")]
    [InlineData("\"name\":\"bad\\u0007name\",")]
    public async Task OfficialExchange_MissingNullOrInvalidName_StillLaunches_AndKeepsTheStoredName(string nameProperty)
    {
        var externalId = NewExternalId();
        await LaunchOfficialAsync(externalId, $"\"name\":\"{PersianName}\",");
        var before = await ReadProfileAsync(externalId);

        var response = await LaunchOfficialAsync(externalId, nameProperty);
        var after = await ReadProfileAsync(externalId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PersianName, after.DisplayName);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"name\":null,")]
    [InlineData("\"name\":{\"first\":\"x\"},")]
    public async Task OfficialExchange_FirstLaunchWithoutAUsableName_SucceedsWithANullDisplayName(string nameProperty)
    {
        var externalId = NewExternalId();

        var response = await LaunchOfficialAsync(externalId, nameProperty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await ReadProfileAsync(externalId);
        Assert.Null(profile.DisplayName);
    }

    [Fact]
    public async Task ProfileApi_ReturnsDisplayName_AndNullNamesAsJsonNull()
    {
        var named = await ReadAsync(await LaunchOfficialAsync(NewExternalId(), $"\"name\":\"{PersianName}\","));
        var unnamed = await ReadAsync(await LaunchOfficialAsync(NewExternalId(), "\"name\":null,"));

        using var namedProfile = await GetProfileAsync(AccessTokenOf(named));
        using var unnamedProfile = await GetProfileAsync(AccessTokenOf(unnamed));

        Assert.Equal(PersianName, namedProfile.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, namedProfile.RootElement.GetProperty("firstName").ValueKind);
        Assert.Equal(JsonValueKind.Null, unnamedProfile.RootElement.GetProperty("displayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, unnamedProfile.RootElement.GetProperty("firstName").ValueKind);
        Assert.Equal(JsonValueKind.Null, unnamedProfile.RootElement.GetProperty("lastName").ValueKind);
    }

    [Fact]
    public async Task OfficialExchange_LogsNeverContainTheNameValue()
    {
        const string validSentinel = "نام‌محرمانه PrivacyDisplayNameSentinel";
        const string invalidSentinel = "PrivacyInvalidNameSentinel";
        var externalId = NewExternalId();

        var first = await LaunchOfficialAsync(externalId, $"\"name\":\"{validSentinel}\",");
        var rejected = await LaunchOfficialAsync(externalId, $"\"name\":\"{invalidSentinel}\\u0007\",");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var logs = factory.LogCapture.Messages;
        Assert.Contains(logs, message => message.Contains("ChangedFields: displayName", StringComparison.Ordinal));
        Assert.Contains(logs, message => message.Contains("TahlilAppProfileFieldRejected", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains("PrivacyDisplayNameSentinel", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains("نام‌محرمانه", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, message => message.Contains(invalidSentinel, StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> LaunchOfficialAsync(string externalId, string nameProperty)
    {
        var code = NewCode();
        factory.Gateway.RespondWithOfficialBody(code, OfficialBody(externalId, nameProperty));
        return await PostLaunchAsync(code);
    }

    private async Task<HttpResponseMessage> PostLaunchAsync(string code)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync("/api/auth/v1/tahlilapp/launch", new { code });
    }

    private async Task<UserProfileRow> ReadProfileAsync(string externalId)
    {
        var userId = await FindLocalUserAsync(externalId);
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await auth.UserProfiles.AsNoTracking().SingleAsync(row => row.UserId == userId);
    }

    private async Task<Guid> FindLocalUserAsync(string externalId)
    {
        using var scope = factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return (await auth.TahlilAppExternalIdentities.SingleAsync(row => row.ExternalUserId == externalId)).LocalUserId;
    }

    private async Task<JsonDocument> GetProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/v1/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static Guid UserIdOf(JsonDocument launch) =>
        launch.RootElement.GetProperty("user").GetProperty("userId").GetGuid();

    private static string AccessTokenOf(JsonDocument launch) =>
        launch.RootElement.GetProperty("accessToken").GetString()!;

    /// <summary>The official exchange response shape, with the stable id and the <c>name</c> property supplied per test.</summary>
    private static string OfficialBody(string externalId, string nameProperty) =>
        $$$"""{"user_id":"{{{externalId}}}",{{{nameProperty}}}"subscription":{"active":true,"expires_at":"2026-11-27T08:22:04.000Z","plans":[{"slug":"sapio_pro","starts_at":"2026-10-28T08:22:04.000Z","expires_at":"2026-11-27T08:22:04.000Z"}]}}""";

    private static string NewExternalId() => $"tahlil-name-{Guid.NewGuid():N}";

    private static string NewCode() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>
/// Admin directory display and search over the production query. EF InMemory checks the query logic; SQLite cannot
/// order by DateTimeOffset, and the PostgreSQL translation is covered by <see cref="Feature139DisplayNamePostgreSqlTests"/>.
/// </summary>
public sealed class AdminDirectoryDisplayNameTests
{
    [Fact]
    public async Task Directory_ShowsAndSearchesDisplayName_AndKeepsTheFirstLastFallbackForOlderRecords()
    {
        var tenantId = Guid.NewGuid();
        var official = Guid.NewGuid();
        var legacy = Guid.NewGuid();
        var unnamed = Guid.NewGuid();
        var database = $"feature139-directory-{Guid.NewGuid():N}";
        await using (var seed = CreateAuth(database))
        {
            var now = DateTimeOffset.UtcNow;
            seed.Tenants.Add(new TenantRow { Id = tenantId, Name = "Directory tenant" });
            foreach (var userId in new[] { official, legacy, unnamed })
            {
                seed.Users.Add(new FinancialCopilotUser { Id = userId, UserName = $"tahlilapp-{userId:N}", IsEnabled = true });
                seed.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = userId });
            }

            seed.UserProfiles.AddRange(
                new UserProfileRow { UserId = official, DisplayName = "کامران عبدی", FirstName = "Stale", CreatedAtUtc = now, UpdatedAtUtc = now, ProfileSyncedAtUtc = now },
                // An older record: names arrived as first/last fields before the official `name` was persisted.
                new UserProfileRow { UserId = legacy, FirstName = "Ali", LastName = "Rezaei", CreatedAtUtc = now, UpdatedAtUtc = now, ProfileSyncedAtUtc = now },
                new UserProfileRow { UserId = unnamed, CreatedAtUtc = now, UpdatedAtUtc = now, ProfileSyncedAtUtc = now });
            await seed.SaveChangesAsync();
        }

        var byDisplayName = Assert.Single(await SearchAsync(database, tenantId, "عبدی"));
        Assert.Equal(official, byDisplayName.UserId);
        Assert.Equal("کامران عبدی", byDisplayName.DisplayName);

        var byLegacyName = Assert.Single(await SearchAsync(database, tenantId, "ali rezaei"));
        Assert.Equal(legacy, byLegacyName.UserId);
        Assert.Equal("Ali Rezaei", byLegacyName.DisplayName);

        var everyone = await SearchAsync(database, tenantId, search: null);
        Assert.Equal(3, everyone.Count);
        Assert.Null(everyone.Single(item => item.UserId == unnamed).DisplayName);
        // Existing first/last name search is preserved, but the official name is what is displayed.
        var byStoredFirstName = Assert.Single(await SearchAsync(database, tenantId, "Stale"));
        Assert.Equal("کامران عبدی", byStoredFirstName.DisplayName);
    }

    private static async Task<IReadOnlyCollection<AdminUserDirectoryItem>> SearchAsync(string database, Guid tenantId, string? search)
    {
        await using var auth = CreateAuth(database);
        await using var billing = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"{database}-billing").Options);
        var queries = new EfCoreAdminUserQueries(auth, billing, TimeProvider.System, new EphemeralDataProtectionProvider(),
            NullLogger<EfCoreAdminUserQueries>.Instance);
        var page = await queries.GetDirectoryAsync(
            tenantId, new AdminUserDirectoryQuery(PageSize: 100, Search: search), false, false, false, CancellationToken.None);
        return page.Items;
    }

    private static AuthDbContext CreateAuth(string database) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(database).Options);
}
