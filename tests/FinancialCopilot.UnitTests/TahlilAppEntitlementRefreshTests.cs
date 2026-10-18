using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Entitlement refresh (Design §11) through the production refresher and the real Billing synchroniser,
/// over an in-memory Billing context. TahlilApp and the sync-state store are test doubles.
/// Release gates: no revision bump or credit change on an identical refresh, no credits on upgrade or downgrade,
/// stale grace, lapsed windows, suspension and revocation.
/// </summary>
public sealed class TahlilAppEntitlementRefreshTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private const string ExternalId = "tahlil-refresh-1";

    [Fact]
    public async Task IdenticalPlan_OnRefresh_BumpsNoRevision_WritesNoAudit_AndLeavesTheWalletAlone()
    {
        var harness = new Harness();
        var expiry = Start.AddDays(30);
        await harness.RefreshAsync(Status("Plus", true, expiry, 17));
        var before = await harness.ReadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        harness.Gateway.Result = Status("Plus", true, expiry, 17);
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        var after = await harness.ReadAsync();

        Assert.Equal(2, harness.Gateway.Calls);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(1, before.Revision);
        Assert.Equal(before.Audits, after.Audits);
        Assert.Equal(1, after.Audits);
        Assert.Equal(before.Balance, after.Balance);
        Assert.Equal(harness.Clock.GetUtcNow().AddMinutes(15), harness.Store.Identity!.NextEntitlementSyncDueAtUtc);
    }

    [Fact]
    public async Task NotDue_MakesNoStatusCall()
    {
        var harness = new Harness();
        await harness.RefreshAsync(Status("Plus", true, Start.AddDays(30), 1));

        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);

        Assert.Equal(1, harness.Gateway.Calls);
    }

    [Fact]
    public async Task UpgradeThenDowngrade_GrantNoCredits_AndDowngradeToFreeClearsTheWindow()
    {
        var harness = new Harness();
        await harness.RefreshAsync(Status("Plus", true, Start.AddDays(30), 1));
        var balance = (await harness.ReadAsync()).Balance;

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        var proExpiry = harness.Clock.GetUtcNow().AddDays(30);
        await harness.RefreshAsync(Status("Pro", true, proExpiry, 2));
        var upgraded = await harness.ReadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        await harness.RefreshAsync(Status("Free", false, null, 3));
        var downgraded = await harness.ReadAsync();

        Assert.Equal("Pro", upgraded.PlanCode);
        Assert.Equal(proExpiry, upgraded.EffectiveTo);
        Assert.Equal(balance, upgraded.Balance);
        Assert.Equal(2, upgraded.Revision);

        Assert.Equal("Free", downgraded.PlanCode);
        Assert.Null(downgraded.EffectiveTo);
        Assert.Equal(balance, downgraded.Balance);
        Assert.Equal(3, downgraded.Revision);
        Assert.Equal("Free", harness.Store.Identity!.LastSyncedPlanCode);
        Assert.Null(harness.Store.Identity.NextEntitlementSyncDueAtUtc);
    }

    [Theory]
    [InlineData("Free", false, null, "Free")]
    [InlineData("Plus", true, 10, "Plus")]
    [InlineData("Pro", true, 10, "Pro")]
    [InlineData("Enterprise", true, 10, "Free")]
    [InlineData("Plus", true, null, "Free")]
    [InlineData("Pro", false, 10, "Free")]
    [InlineData("Pro", true, -1, "Free")]
    [InlineData(null, true, 10, "Free")]
    public async Task Mapping_FollowsTheFailClosedMatrix(string? plan, bool active, int? expiresInDays, string expectedPlan)
    {
        var harness = new Harness();
        var expiry = expiresInDays is { } days ? harness.Clock.GetUtcNow().AddDays(days) : (DateTimeOffset?)null;

        await harness.RefreshAsync(Status(plan, active, expiry, 1));
        var state = await harness.ReadAsync();

        Assert.Equal(expectedPlan, state.PlanCode);
        Assert.Equal(expectedPlan == "Free" ? null : expiry, state.EffectiveTo);
        Assert.Equal(expectedPlan, harness.Store.Identity!.LastSyncedPlanCode);
    }

    [Fact]
    public async Task StaleGrace_WhenUnavailableWithinTheGraceWindow_KeepsThePaidPlan()
    {
        var harness = new Harness();
        var expiry = Start.AddDays(30);
        await harness.RefreshAsync(Status("Plus", true, expiry, 1));
        var before = await harness.ReadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        harness.Gateway.Result = Unavailable();
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        var after = await harness.ReadAsync();

        Assert.Equal("Plus", after.PlanCode);
        Assert.Equal(expiry, after.EffectiveTo);
        Assert.Equal(before.Revision, after.Revision);
        var failure = Assert.Single(harness.Store.Failures);
        Assert.Equal("unavailable", failure.Category);
        Assert.False(failure.DowngradedToFree);
        Assert.Equal(harness.Clock.GetUtcNow().AddMinutes(15), failure.NextDueAtUtc);
    }

    [Fact]
    public async Task StaleGrace_WhenUnavailablePastTheGraceWindow_DowngradesToFree()
    {
        var harness = new Harness();
        await harness.RefreshAsync(Status("Plus", true, Start.AddDays(30), 1));
        var balance = (await harness.ReadAsync()).Balance;

        harness.Clock.Advance(TimeSpan.FromHours(7));
        harness.Gateway.Result = Unavailable();
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        var after = await harness.ReadAsync();

        Assert.Equal("Free", after.PlanCode);
        Assert.Null(after.EffectiveTo);
        Assert.Equal(2, after.Revision);
        Assert.Equal(balance, after.Balance);
        Assert.True(Assert.Single(harness.Store.Failures).DowngradedToFree);
        Assert.Equal("Free", harness.Store.Identity!.LastSyncedPlanCode);
    }

    [Fact]
    public async Task LapsedWindow_NeverGrantsPaidAccess_EvenWhenTahlilAppIsStillUnavailable()
    {
        var harness = new Harness();
        var expiry = Start.AddMinutes(20);
        await harness.RefreshAsync(Status("Pro", true, expiry, 1));

        // Still inside the window: the paid plan is kept, and the next due time is the window end.
        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        harness.Gateway.Result = Unavailable();
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        Assert.Equal("Pro", (await harness.ReadAsync()).PlanCode);
        Assert.Equal(expiry, harness.Store.Identity!.NextEntitlementSyncDueAtUtc);

        // Past the window: the next due refresh downgrades to Free even though TahlilApp is still unavailable.
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        var after = await harness.ReadAsync();

        Assert.Equal("Free", after.PlanCode);
        Assert.Null(after.EffectiveTo);
    }

    [Fact]
    public async Task Suspended_MarksTheIdentitySuspended_AndTouchesNoBillingState()
    {
        var harness = new Harness();
        await harness.RefreshAsync(Status("Plus", true, Start.AddDays(30), 1));
        var before = await harness.ReadAsync();

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        harness.Gateway.Result = Status("Plus", true, Start.AddDays(30), 2, suspended: true);
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        var after = await harness.ReadAsync();

        Assert.Equal(TahlilAppIdentityStatus.Suspended, harness.Store.Identity!.Status);
        Assert.Null(harness.Store.Identity.NextEntitlementSyncDueAtUtc);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal("Plus", after.PlanCode);

        // A suspended identity is no longer Active, so the refresher does not call TahlilApp again.
        harness.Clock.Advance(TimeSpan.FromMinutes(60));
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);
        Assert.Equal(2, harness.Gateway.Calls);
    }

    [Fact]
    public async Task NotFound_MarksTheIdentityRevoked()
    {
        var harness = new Harness();
        await harness.RefreshAsync(Status("Plus", true, Start.AddDays(30), 1));

        harness.Clock.Advance(TimeSpan.FromMinutes(16));
        harness.Gateway.Result = new TahlilAppStatusResult(TahlilAppStatusOutcome.NotFound);
        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);

        Assert.Equal(TahlilAppIdentityStatus.Revoked, harness.Store.Identity!.Status);
    }

    [Fact]
    public async Task UnknownOrInactiveIdentity_IsIgnored_WithoutCallingTahlilApp()
    {
        var unknown = new Harness { Identity = null };
        await unknown.Refresher.RefreshIfDueAsync(unknown.TenantId, unknown.UserId, CancellationToken.None);
        Assert.Equal(0, unknown.Gateway.Calls);

        var revoked = new Harness { Status = TahlilAppIdentityStatus.Revoked };
        await revoked.Refresher.RefreshIfDueAsync(revoked.TenantId, revoked.UserId, CancellationToken.None);
        Assert.Equal(0, revoked.Gateway.Calls);
    }

    [Fact]
    public async Task UnavailableOnAFreeUser_KeepsFree_AndNeverThrows()
    {
        var harness = new Harness();
        harness.Gateway.Result = Unavailable();

        await harness.Refresher.RefreshIfDueAsync(harness.TenantId, harness.UserId, CancellationToken.None);

        var state = await harness.ReadAsync();
        Assert.Equal("Free", state.PlanCode);
        Assert.Equal("unavailable", Assert.Single(harness.Store.Failures).Category);
    }

    [Fact]
    public void Schedule_CapsTheDueTimeAtAnEarlierExpiry_AndGivesFreeNoDueTime()
    {
        var expiry = Start.AddMinutes(5);
        var paid = TahlilAppEntitlementSchedule.CreateSuccess(
            new TahlilAppSubscriptionDecision("Pro", expiry, "active-paid"), 1, Start, TimeSpan.FromMinutes(15));
        var free = TahlilAppEntitlementSchedule.CreateSuccess(
            new TahlilAppSubscriptionDecision("Free", null, "free"), null, Start, TimeSpan.FromMinutes(15));

        Assert.Equal(expiry, paid.NextDueAtUtc);
        Assert.Null(free.NextDueAtUtc);
    }

    [Fact]
    public void Schedule_IsDueOnTheStoredDueTime_OrOnTheTtlWhenNoDueTimeIsStored()
    {
        var withDue = State(lastSync: Start, nextDue: Start.AddMinutes(15), plan: "Plus");
        var withoutDue = State(lastSync: Start, nextDue: null, plan: "Free");
        var never = State(lastSync: null, nextDue: null, plan: null);

        Assert.False(TahlilAppEntitlementSchedule.IsDue(withDue, Start.AddMinutes(14), TimeSpan.FromMinutes(15)));
        Assert.True(TahlilAppEntitlementSchedule.IsDue(withDue, Start.AddMinutes(15), TimeSpan.FromMinutes(15)));
        Assert.False(TahlilAppEntitlementSchedule.IsDue(withoutDue, Start.AddMinutes(14), TimeSpan.FromMinutes(15)));
        Assert.True(TahlilAppEntitlementSchedule.IsDue(withoutDue, Start.AddMinutes(15), TimeSpan.FromMinutes(15)));
        Assert.True(TahlilAppEntitlementSchedule.IsDue(never, Start, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public void Schedule_FreeNeverKeepsAPaidPlan()
    {
        var free = State(lastSync: Start, nextDue: null, plan: "Free", expiry: Start.AddDays(1));

        Assert.False(TahlilAppEntitlementSchedule.CanKeepLastKnownPlan(free, Start, TimeSpan.FromHours(6)));
    }

    private static TahlilAppIdentitySyncState State(
        DateTimeOffset? lastSync,
        DateTimeOffset? nextDue,
        string? plan,
        DateTimeOffset? expiry = null) =>
        new(Guid.Empty, Guid.Empty, ExternalId, TahlilAppIdentityStatus.Active, lastSync, nextDue, expiry, plan);

    private static TahlilAppStatusResult Status(string? plan, bool active, DateTimeOffset? expiry, long? revision, bool suspended = false) =>
        new(TahlilAppStatusOutcome.Succeeded, new TahlilAppStatusSnapshot(ExternalId, suspended, plan, active, expiry, revision));

    private static TahlilAppStatusResult Unavailable() => new(TahlilAppStatusOutcome.Unavailable);

    private sealed record BillingState(string PlanCode, DateTimeOffset? EffectiveTo, long Revision, decimal Balance, int Audits);

    private sealed class Harness
    {
        private static readonly Dictionary<string, string> PlanMap = new(StringComparer.Ordinal)
        {
            ["Free"] = "Free",
            ["Plus"] = "Plus",
            ["Pro"] = "Pro"
        };

        public Harness(TahlilAppIdentitySyncState? identity = null, string status = TahlilAppIdentityStatus.Active)
        {
            Clock = new ManualClock(Start);
            Billing = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
                .UseInMemoryDatabase($"tahlilapp-refresh-{Guid.NewGuid():N}")
                .Options);
            Billing.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25, PricingPolicyVersion = "v1" });
            Billing.SaveChanges();

            var synchronizer = new ExternalSubscriptionSynchronizer(
                Billing,
                new OwnedIdentityBillingProvisioner(Billing, Clock),
                Clock);
            Refresher = new TahlilAppEntitlementRefresher(
                Gateway,
                Store,
                synchronizer,
                Options.Create(new TahlilAppOptions { Enabled = true, BaseUrl = "https://tahlilapp.example.test" }),
                Clock,
                NullLogger<TahlilAppEntitlementRefresher>.Instance);

            Store.Identity = identity ?? new TahlilAppIdentitySyncState(
                TenantId, UserId, ExternalId, status, null, null, null, null);
        }

        public ManualClock Clock { get; }
        public BillingDbContext Billing { get; }
        public InMemoryStateStore Store { get; } = new();
        public FakeStatusGateway Gateway { get; } = new();
        public TahlilAppEntitlementRefresher Refresher { get; }
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public TahlilAppIdentitySyncState? Identity
        {
            get => Store.Identity;
            set => Store.Identity = value;
        }

        public string Status
        {
            set => Store.Identity = Store.Identity! with { Status = value };
        }

        /// <summary>Makes the identity due, answers with <paramref name="result"/>, and runs the refresher once.</summary>
        public async Task RefreshAsync(TahlilAppStatusResult result)
        {
            Gateway.Result = result;
            Store.Identity = Store.Identity! with
            {
                NextEntitlementSyncDueAtUtc = Clock.GetUtcNow().AddSeconds(-1)
            };
            await Refresher.RefreshIfDueAsync(TenantId, UserId, CancellationToken.None);
        }

        public async Task<BillingState> ReadAsync()
        {
            var account = await Billing.CustomerAccounts.AsNoTracking().SingleAsync(row => row.UserId == UserId);
            var wallet = await Billing.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == account.Id);
            var audits = await Billing.AdminAudits.CountAsync(row => row.TenantId == TenantId);
            return new BillingState(account.SubscriptionPlanCode!, account.SubscriptionEffectiveTo, account.SubscriptionRevision, wallet.Balance, audits);
        }
    }

    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class FakeStatusGateway : ITahlilAppIdentityGateway
    {
        public TahlilAppStatusResult Result { get; set; } = new(TahlilAppStatusOutcome.Unavailable);
        public int Calls { get; private set; }

        public Task<TahlilAppStatusResult> GetStatusAsync(string externalUserId, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }

        public Task<TahlilAppExchangeResult> ExchangeAsync(string code, string correlationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The refresher never exchanges a launch code.");
    }

    private sealed class InMemoryStateStore : ITahlilAppEntitlementStateStore
    {
        public TahlilAppIdentitySyncState? Identity { get; set; }
        public List<TahlilAppSyncSuccess> Successes { get; } = [];
        public List<TahlilAppSyncFailure> Failures { get; } = [];

        public Task<TahlilAppIdentitySyncState?> FindByLocalUserAsync(Guid localUserId, CancellationToken cancellationToken) =>
            Task.FromResult(Identity is not null && Identity.LocalUserId == localUserId ? Identity : null);

        public Task RecordSuccessAsync(Guid localUserId, TahlilAppSyncSuccess success, CancellationToken cancellationToken)
        {
            Successes.Add(success);
            Identity = Identity! with
            {
                LastEntitlementSyncAtUtc = success.SyncedAtUtc,
                NextEntitlementSyncDueAtUtc = success.NextDueAtUtc,
                LastKnownSubscriptionExpiresAtUtc = success.EffectiveTo,
                LastSyncedPlanCode = success.PlanCode
            };
            return Task.CompletedTask;
        }

        public Task RecordFailureAsync(Guid localUserId, TahlilAppSyncFailure failure, CancellationToken cancellationToken)
        {
            Failures.Add(failure);
            Identity = Identity! with
            {
                NextEntitlementSyncDueAtUtc = failure.NextDueAtUtc,
                LastSyncedPlanCode = failure.DowngradedToFree ? TahlilAppEntitlementSchedule.FreePlanCode : Identity.LastSyncedPlanCode,
                LastKnownSubscriptionExpiresAtUtc = failure.DowngradedToFree ? null : Identity.LastKnownSubscriptionExpiresAtUtc
            };
            return Task.CompletedTask;
        }

        public Task RecordIdentityStatusAsync(Guid localUserId, string status, DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
        {
            Identity = Identity! with
            {
                Status = status,
                LastEntitlementSyncAtUtc = checkedAtUtc,
                NextEntitlementSyncDueAtUtc = null
            };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TahlilAppDueIdentity>> ClaimDueAsync(string leaseOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, int batchSize, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Claims are exercised against the EF store in integration tests.");

        public Task ExtendLeaseAsync(Guid localUserId, string leaseOwner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ReleaseLeaseAsync(Guid localUserId, string leaseOwner, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
