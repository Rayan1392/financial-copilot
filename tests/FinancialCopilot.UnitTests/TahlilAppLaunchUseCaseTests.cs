using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppLaunchUseCaseTests
{
    private const string ValidCode = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_xyz";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocalUserId = Guid.NewGuid();

    [Fact]
    public async Task Disabled_ReturnsDisabledWithoutTouchingAnyPort()
    {
        var harness = new Harness(enabled: false);
        var result = await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Disabled, result.Outcome);
        Assert.Equal(0, harness.Gateway.Calls);
        Assert.Empty(harness.Attempts.Started);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in the code value!!")]
    public async Task MalformedCode_IsRejectedBeforeReplayGuardAndExchange(string code)
    {
        var harness = new Harness();
        var result = await harness.CreateUseCase().ExecuteAsync(Request(code), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Invalid, result.Outcome);
        Assert.Empty(harness.Attempts.Started);
        Assert.Equal(0, harness.Gateway.Calls);
    }

    [Fact]
    public async Task ReplayedCode_NeverReachesTheGateway()
    {
        var harness = new Harness();
        harness.Attempts.AlreadyExists.Add(ValidCode);

        var result = await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Invalid, result.Outcome);
        Assert.Equal(0, harness.Gateway.Calls);
    }

    [Fact]
    public async Task InvalidCode_IsRecordedAndReturnedAsInvalid()
    {
        var harness = new Harness { Exchange = new TahlilAppExchangeResult(TahlilAppExchangeOutcome.InvalidCode) };

        var result = await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Invalid, result.Outcome);
        Assert.Equal("invalid", harness.Attempts.Completed[ValidCode]);
        Assert.Equal(0, harness.Provisioner.Calls);
    }

    [Fact]
    public async Task UnavailableExchange_IsFinalForTheCode_AndNeverRetried()
    {
        var harness = new Harness { Exchange = new TahlilAppExchangeResult(TahlilAppExchangeOutcome.Unavailable) };
        var useCase = harness.CreateUseCase();

        var first = await useCase.ExecuteAsync(Request(), CancellationToken.None);
        var second = await useCase.ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Unavailable, first.Outcome);
        Assert.Equal(TahlilAppLaunchOutcome.Invalid, second.Outcome);
        Assert.Equal(1, harness.Gateway.Calls);
    }

    [Fact]
    public async Task SuspendedUser_IsDeniedBeforeProvisioning()
    {
        var harness = new Harness { Exchange = Succeeded(Snapshot(suspended: true)) };

        var result = await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.AccountDisabled, result.Outcome);
        Assert.Equal(0, harness.Provisioner.Calls);
        Assert.Equal(0, harness.Sessions.Issued);
    }

    [Fact]
    public async Task DisabledLocalAccount_IsDeniedWithoutSession()
    {
        var harness = new Harness
        {
            Exchange = Succeeded(Snapshot()),
            Provisioning = new TahlilAppProvisioningResult(TahlilAppProvisioningOutcome.AccountDisabled, LocalUserId, TenantId, false)
        };

        var result = await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.AccountDisabled, result.Outcome);
        Assert.Equal(0, harness.Subscriptions.Calls);
        Assert.Equal(0, harness.Sessions.Issued);
    }

    [Fact]
    public async Task Success_SynchronisesDecisionThenIssuesSession_WithPresentedTokenForRevocation()
    {
        var expiry = Now.AddDays(20);
        var harness = new Harness { Exchange = Succeeded(Snapshot(plan: "Plus", active: true, expiry: expiry, revision: 17)) };

        var result = await harness.CreateUseCase().ExecuteAsync(Request(presented: "old-refresh"), CancellationToken.None);

        Assert.Equal(TahlilAppLaunchOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Session);
        Assert.Equal(1, harness.Subscriptions.Calls);
        Assert.Equal("Plus", harness.Subscriptions.LastDecision!.PlanCode);
        Assert.Equal(expiry, harness.Subscriptions.LastDecision.EffectiveTo);
        Assert.Equal(17, harness.Subscriptions.LastRevision);
        Assert.Equal("old-refresh", harness.Sessions.LastPresentedRefreshToken);
        Assert.Equal("succeeded", harness.Attempts.Completed[ValidCode]);
    }

    [Fact]
    public async Task Success_RecordsSyncState_WithDueTimeOfTtlForAPaidPlan()
    {
        var expiry = Now.AddDays(20);
        var harness = new Harness { Exchange = Succeeded(Snapshot(plan: "Plus", active: true, expiry: expiry, revision: 17)) };

        await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        var success = Assert.Single(harness.SyncState.Successes);
        Assert.Equal(LocalUserId, harness.SyncState.LastLocalUserId);
        Assert.Equal("Plus", success.PlanCode);
        Assert.Equal(expiry, success.EffectiveTo);
        Assert.Equal(17, success.ExternalRevision);
        Assert.Equal(success.SyncedAtUtc.AddMinutes(15), success.NextDueAtUtc);
    }

    [Fact]
    public async Task Success_RecordsSyncState_CappingTheDueTimeAtAnEarlierExpiry()
    {
        var expiry = Now.AddMinutes(5);
        var harness = new Harness { Exchange = Succeeded(Snapshot(plan: "Pro", active: true, expiry: expiry)) };

        await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        var success = Assert.Single(harness.SyncState.Successes);
        Assert.Equal(expiry, success.NextDueAtUtc);
    }

    [Fact]
    public async Task Success_RecordsSyncState_WithNoDueTimeForFree()
    {
        var harness = new Harness { Exchange = Succeeded(Snapshot(plan: "Free", active: false)) };

        await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        var success = Assert.Single(harness.SyncState.Successes);
        Assert.Equal("Free", success.PlanCode);
        Assert.Null(success.NextDueAtUtc);
        Assert.Null(success.EffectiveTo);
    }

    [Fact]
    public async Task UnknownExternalPlan_SynchronisesFreeNeverAHigherPlan()
    {
        var harness = new Harness { Exchange = Succeeded(Snapshot(plan: "Enterprise", active: true, expiry: Now.AddDays(10))) };

        await harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None);

        Assert.Equal("Free", harness.Subscriptions.LastDecision!.PlanCode);
    }

    [Fact]
    public async Task SynchronisationFailure_RecordsErrorAndIssuesNoSession()
    {
        var harness = new Harness
        {
            Exchange = Succeeded(Snapshot(plan: "Pro", active: true, expiry: Now.AddDays(10))),
            SubscriptionFailure = new InvalidOperationException("billing unavailable")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.CreateUseCase().ExecuteAsync(Request(), CancellationToken.None));

        Assert.Equal("error", harness.Attempts.Completed[ValidCode]);
        Assert.Equal(0, harness.Sessions.Issued);
    }

    private static TahlilAppLaunchRequest Request(string code = ValidCode, string? presented = null) =>
        new(code, presented, "corr-1");

    private static TahlilAppExchangeResult Succeeded(TahlilAppExchangeSnapshot snapshot) =>
        new(TahlilAppExchangeOutcome.Succeeded, snapshot);

    private static TahlilAppExchangeSnapshot Snapshot(
        bool suspended = false,
        string? plan = "Free",
        bool active = false,
        DateTimeOffset? expiry = null,
        long? revision = null) =>
        new(
            ExternalUserId: "ext-user-1",
            IsSuspended: suspended,
            ExternalPlan: plan,
            IsSubscriptionActive: active,
            SubscriptionExpiresAt: expiry,
            ExternalRevision: revision,
            ExternalPermissionCount: 0,
            FirstName: IncomingProfileField.Valid("Ali"),
            LastName: IncomingProfileField.Absent,
            NationalId: IncomingProfileField.Invalid,
            Mobile: IncomingProfileField.Null);

    private sealed class Harness(bool enabled = true)
    {
        public FakeGateway Gateway { get; } = new();
        public FakeAttempts Attempts { get; } = new();
        public FakeProvisioner Provisioner { get; } = new();
        public FakeSubscriptions Subscriptions { get; } = new();
        public FakeSessions Sessions { get; } = new();
        public TahlilAppExchangeResult Exchange { get; set; } = new(TahlilAppExchangeOutcome.Unavailable);
        public TahlilAppProvisioningResult Provisioning { get; set; } = new(TahlilAppProvisioningOutcome.Resolved, LocalUserId, TenantId, true);
        public Exception? SubscriptionFailure { get; set; }

        public TahlilAppLaunchUseCase CreateUseCase()
        {
            Gateway.Result = Exchange;
            Provisioner.Result = Provisioning;
            Subscriptions.Failure = SubscriptionFailure;
            return new TahlilAppLaunchUseCase(
                Gateway,
                Attempts,
                Provisioner,
                Subscriptions,
                SyncState,
                Sessions,
                new TahlilAppLaunchPolicy(enabled, new Dictionary<string, string> { ["Free"] = "Free", ["Plus"] = "Plus", ["Pro"] = "Pro" }),
                TimeProvider.System);
        }

        public FakeSyncState SyncState { get; } = new();
    }

    private sealed class FakeSyncState : ITahlilAppSyncStateRecorder
    {
        public List<TahlilAppSyncSuccess> Successes { get; } = [];
        public Guid? LastLocalUserId { get; private set; }

        public Task RecordSuccessAsync(Guid localUserId, TahlilAppSyncSuccess success, CancellationToken cancellationToken)
        {
            LastLocalUserId = localUserId;
            Successes.Add(success);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeGateway : ITahlilAppIdentityGateway
    {
        public TahlilAppExchangeResult Result { get; set; } = new(TahlilAppExchangeOutcome.Unavailable);
        public int Calls { get; private set; }

        public Task<TahlilAppExchangeResult> ExchangeAsync(string code, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }

        public Task<TahlilAppStatusResult> GetStatusAsync(string externalUserId, string correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(new TahlilAppStatusResult(TahlilAppStatusOutcome.Unavailable));
    }

    private sealed class FakeAttempts : ITahlilAppLaunchAttemptStore
    {
        public List<string> Started { get; } = [];
        public HashSet<string> AlreadyExists { get; } = [];
        public Dictionary<string, string> Completed { get; } = new();

        public Task<bool> TryBeginAttemptAsync(string code, string correlationId, CancellationToken cancellationToken)
        {
            if (AlreadyExists.Contains(code) || Started.Contains(code))
            {
                return Task.FromResult(false);
            }

            Started.Add(code);
            return Task.FromResult(true);
        }

        public Task CompleteAttemptAsync(string code, string outcome, CancellationToken cancellationToken)
        {
            Completed[code] = outcome;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvisioner : ITahlilAppAccountProvisioner
    {
        public TahlilAppProvisioningResult Result { get; set; } = new(TahlilAppProvisioningOutcome.Resolved, LocalUserId, TenantId, true);
        public int Calls { get; private set; }

        public Task<TahlilAppProvisioningResult> ResolveAsync(TahlilAppExchangeSnapshot snapshot, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSubscriptions : ITahlilAppSubscriptionSynchronizer
    {
        public TahlilAppSubscriptionDecision? LastDecision { get; private set; }
        public long? LastRevision { get; private set; }
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }

        public Task SynchronizeAsync(Guid tenantId, Guid localUserId, TahlilAppSubscriptionDecision decision, long? externalRevision, CancellationToken cancellationToken)
        {
            Calls++;
            LastDecision = decision;
            LastRevision = externalRevision;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessions : ITahlilAppSessionIssuer
    {
        public int Issued { get; private set; }
        public string? LastPresentedRefreshToken { get; private set; }

        public Task<AuthenticatedSessionTokens> IssueAsync(Guid localUserId, Guid tenantId, string? presentedRefreshToken, CancellationToken cancellationToken)
        {
            Issued++;
            LastPresentedRefreshToken = presentedRefreshToken;
            return Task.FromResult(new AuthenticatedSessionTokens(
                "access", DateTimeOffset.UtcNow.AddMinutes(10), "refresh", DateTimeOffset.UtcNow.AddDays(1),
                localUserId, tenantId, null, ["User"], []));
        }
    }
}
