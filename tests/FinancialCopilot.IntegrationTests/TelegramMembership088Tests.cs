using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class TelegramMembership088Tests : IClassFixture<TelegramMembershipApiFactory>
{
    private readonly TelegramMembershipApiFactory _factory;

    public TelegramMembership088Tests(TelegramMembershipApiFactory factory) => _factory = factory;

    [Fact]
    public async Task VerifyMembership_PersistsEligibleStateAndReturnsEntitlement()
    {
        var telegramUserId = 881001L;
        FakeTelegramMembershipProvider.SetStatus(telegramUserId, TelegramChannelMembershipStatus.Member);
        using var web = _factory.CreateClient();
        await AuthenticateNewUserAsync(web);
        await LinkTelegramAsync(web, telegramUserId);

        using var verify = await web.PostAsync("/api/v1/telegram/membership/verify", null);
        using var verifyJson = await ReadJsonAsync(verify);
        using var entitlement = await web.GetAsync("/api/v1/telegram/entitlement/me");
        using var entitlementJson = await ReadJsonAsync(entitlement);

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.True(verifyJson.RootElement.GetProperty("isEligible").GetBoolean());
        Assert.Equal((int)TelegramChannelMembershipStatus.Member, verifyJson.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("بررسی دوباره عضویت", verifyJson.RootElement.GetProperty("actions")[0].GetProperty("label").GetString());
        Assert.Equal(HttpStatusCode.OK, entitlement.StatusCode);
        Assert.Equal("UsePaidEntitlement", entitlementJson.RootElement.GetProperty("nextAction").GetString());
        Assert.Equal(5m, entitlementJson.RootElement.GetProperty("freeDailyAllowance").GetProperty("totalCredits").GetDecimal());
        Assert.Equal("بررسی دوباره عضویت", entitlementJson.RootElement.GetProperty("actions")[0].GetProperty("label").GetString());

        using var scope = _factory.Services.CreateScope();
        var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await authDb.TelegramChannelMembershipVerifications.SingleAsync(
            candidate => candidate.TelegramUserId == telegramUserId && candidate.IsLatest);
        Assert.True(row.IsEligible);
        Assert.Equal("Member", row.Status);
    }

    [Fact]
    public async Task VerifyMembership_ProviderUnavailable_FailsClosedWithoutGrant()
    {
        var telegramUserId = 882001L;
        FakeTelegramMembershipProvider.SetStatus(telegramUserId, TelegramChannelMembershipStatus.UnknownProviderFailure);
        using var web = _factory.CreateClient();
        var user = await AuthenticateNewUserAsync(web);
        await LinkTelegramAsync(web, telegramUserId);
        using var verify = await web.PostAsync("/api/v1/telegram/membership/verify", null);

        using var scope = _factory.Services.CreateScope();
        var account = await GetAccountAsync(scope, user);
        var allowance = scope.ServiceProvider.GetRequiredService<IDailyFreeAllowanceService>();
        var result = await allowance.EnsureAsync(
            user.ToBillingActor(),
            account,
            "provider-failure",
            CancellationToken.None);
        var billingDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.False(result.Granted);
        Assert.DoesNotContain(billingDb.DailyFreeAllowanceGrants, row => row.ActorId == user.UserId);
        Assert.DoesNotContain(billingDb.UsageLedgerEntries, row => row.ActorId == user.UserId && row.OperationCode == "Telegram.DailyFreeAllowance");
    }

    [Fact]
    public async Task EnsureDailyFreeAllowance_GrantsOnlyOnceAndStoresLedgerMetadata()
    {
        var telegramUserId = 883001L;
        FakeTelegramMembershipProvider.SetStatus(telegramUserId, TelegramChannelMembershipStatus.Administrator);
        using var web = _factory.CreateClient();
        var user = await AuthenticateNewUserAsync(web);
        await LinkTelegramAsync(web, telegramUserId);
        using var verify = await web.PostAsync("/api/v1/telegram/membership/verify", null);
        verify.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var account = await GetAccountAsync(scope, user);
        var allowance = scope.ServiceProvider.GetRequiredService<IDailyFreeAllowanceService>();
        var actor = user.ToBillingActor();

        var first = await allowance.EnsureAsync(actor, account, "first-grant", CancellationToken.None);
        var replay = await allowance.EnsureAsync(actor, account, "replay-grant", CancellationToken.None);
        var bucket = await allowance.GetBucketAsync(actor, account, CancellationToken.None);
        var billingDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var grant = Assert.Single(billingDb.DailyFreeAllowanceGrants.Where(row => row.ActorId == user.UserId));
        var ledger = Assert.Single(billingDb.UsageLedgerEntries.Where(row => row.ActorId == user.UserId && row.OperationCode == "Telegram.DailyFreeAllowance"));

        Assert.True(first.Granted);
        Assert.False(replay.Granted);
        Assert.Equal(5m, bucket.RemainingCredits);
        Assert.Equal(grant.LedgerEntryId, ledger.Id);
        Assert.Equal("TelegramDailyFreeAllowance", ledger.AllocationSource);
        Assert.Equal(grant.AllowanceDateKey, ledger.AllowanceDateKey);
        Assert.True(billingDb.WalletProjections.Single(row => row.CustomerAccountId == account.Id).Balance >= grant.Amount);
    }

    [Fact]
    public async Task AiBillingReservation_UsesVerifiedTelegramAllowanceBeforePaidCapacity()
    {
        var telegramUserId = 884001L;
        FakeTelegramMembershipProvider.SetStatus(telegramUserId, TelegramChannelMembershipStatus.Member);
        using var web = _factory.CreateClient();
        var user = await AuthenticateNewUserAsync(web);
        await LinkTelegramAsync(web, telegramUserId);
        using var verify = await web.PostAsync("/api/v1/telegram/membership/verify", null);
        verify.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var hook = scope.ServiceProvider.GetRequiredService<FinancialCopilot.Application.AI.Orchestration.IBillingFacadeHook>();
        var handle = await hook.TryReserveAsync(
            new FinancialCopilot.Application.AI.Orchestration.BillingReservationRequest(
                "telegram-free-reservation",
                user.TenantId,
                user.UserId,
                "AiQuery.Scanner",
                user.UserId,
                null),
            CancellationToken.None);

        Assert.NotNull(handle);
        Assert.Equal("TelegramDailyFreeAllowance", handle.AllocationSource);

        using var billingDbScope = _factory.Services.CreateScope();
        var billingDb = billingDbScope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Assert.Single(billingDb.DailyFreeAllowanceGrants.Where(row => row.ActorId == user.UserId));
        Assert.Equal(1m, billingDb.WalletProjections.Single(row => row.CustomerAccountId == handle.CustomerAccountId).ReservedAmount);
    }

    private HttpClient CreateAdapterClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private async Task LinkTelegramAsync(HttpClient web, long telegramUserId)
    {
        using var challengeResponse = await web.PostAsync("/api/v1/telegram/link-token", null);
        using var challenge = await ReadJsonAsync(challengeResponse);
        challengeResponse.EnsureSuccessStatusCode();
        var startParameter = new Uri(challenge.RootElement.GetProperty("deepLink").GetString()!).Query.Split("start=", 2)[1];

        using var adapter = CreateAdapterClient();
        using var confirmation = await adapter.PostAsJsonAsync(
            "/api/v1/telegram/link/confirm",
            new
            {
                startParameter,
                telegramUserId,
                telegramChatId = telegramUserId,
                username = "member",
                telegramUpdateId = telegramUserId + 100000
            });
        confirmation.EnsureSuccessStatusCode();
    }

    private async Task<TestUser> AuthenticateNewUserAsync(HttpClient client)
    {
        var email = $"telegram-088-{Guid.NewGuid():N}@example.test";
        using var register = await client.PostAsJsonAsync(
            "/api/auth/v1/register",
            new { email, password = "StrongPassword!123" });
        using var session = await ReadJsonAsync(register);
        register.EnsureSuccessStatusCode();
        var accessToken = session.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var user = new TestUser(
            Guid.Parse(token.Subject),
            Guid.Parse(token.Claims.Single(claim => claim.Type == FinancialCopilotClaimTypes.TenantId).Value));
        await EnsureBillingAccountAsync(user);
        return user;
    }

    private async Task EnsureBillingAccountAsync(TestUser user)
    {
        using var scope = _factory.Services.CreateScope();
        var billingDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var exists = await billingDb.CustomerAccounts.AnyAsync(row =>
            row.TenantId == user.TenantId &&
            row.UserId == user.UserId);
        if (exists)
        {
            return;
        }

        var accountId = Guid.NewGuid();
        billingDb.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = user.TenantId,
            UserId = user.UserId,
            AccountType = "Individual",
            BillingMode = "Prepaid"
        });
        billingDb.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = accountId,
            Balance = 0,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await billingDb.SaveChangesAsync();
    }

    private static async Task<FinancialCopilot.Billing.Accounts.CustomerAccount> GetAccountAsync(IServiceScope scope, TestUser user)
    {
        var resolver = scope.ServiceProvider.GetRequiredService<IBillableAccountResolver>();
        return await resolver.ResolveAsync(
            user.ToBillingActor(),
            CancellationToken.None);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

internal sealed record TestUser(Guid UserId, Guid TenantId)
{
    public BillableActorContext ToBillingActor() => new(UserId, TenantId, UserId, null, null);
}

public sealed class TelegramMembershipApiFactory : OwnedIdentityApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:Membership:RequiredChannelId"] = "@test_channel",
                ["Telegram:Membership:DailyFreeCredits"] = "5",
                ["Telegram:Membership:VerificationCacheMinutes"] = "60",
                ["Telegram:Membership:ProviderFailureCacheMinutes"] = "5",
                ["Telegram:Membership:PolicyVersion"] = "telegram-free-daily-v1"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITelegramChannelMembershipProvider>();
            services.AddSingleton<ITelegramChannelMembershipProvider, FakeTelegramMembershipProvider>();
        });
    }
}

public sealed class FakeTelegramMembershipProvider : ITelegramChannelMembershipProvider
{
    private static readonly Dictionary<long, TelegramChannelMembershipStatus> Statuses = [];

    public static void SetStatus(long telegramUserId, TelegramChannelMembershipStatus status) =>
        Statuses[telegramUserId] = status;

    public Task<TelegramProviderMembershipObservation> GetMembershipAsync(
        long telegramUserId,
        string channelId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var status = Statuses.GetValueOrDefault(telegramUserId, TelegramChannelMembershipStatus.NotFound);
        var failure = status == TelegramChannelMembershipStatus.UnknownProviderFailure
            ? TelegramMembershipFailureCategory.ProviderUnavailable
            : TelegramMembershipFailureCategory.None;
        return Task.FromResult(new TelegramProviderMembershipObservation(status, DateTimeOffset.UtcNow, failure));
    }
}
