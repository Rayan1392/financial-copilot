using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Radar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class PersonalMarketRadar093Tests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 7, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Profile_lifecycle_is_versioned_and_rejects_stale_updates()
    {
        var profile = CreateProfile();

        profile.Update(1, [InsightType.PriceMovement], InsightSeverity.Notice, 55,
            RadarSensitivity.Focused, RadarDeliveryMode.Digest, RadarState.Paused, Now.AddMinutes(1));

        Assert.Equal(RadarState.Paused, profile.State);
        Assert.Equal(2, profile.Version);
        Assert.Throws<RadarValidationException>(() => profile.Remove(1, Now.AddMinutes(2)));
        profile.Remove(2, Now.AddMinutes(2));
        Assert.Equal(RadarState.Removed, profile.State);
        Assert.Equal(3, profile.Version);
        profile.Update(3, [InsightType.PriceMovement], InsightSeverity.Notice, 50,
            RadarSensitivity.Balanced, RadarDeliveryMode.Immediate, RadarState.Active, Now.AddMinutes(3));
        Assert.Equal(RadarState.Active, profile.State);
        Assert.Null(profile.RemovedAtUtc);
        Assert.Equal(4, profile.Version);
    }

    [Fact]
    public void Symbol_override_precedes_profile_and_sensitivity_policy_is_deterministic()
    {
        var profile = CreateProfile();
        var symbolOverride = RadarSymbolOverride.Create(profile.Id, "company-1", RadarState.Active,
            [InsightType.LargeTradeDetected], InsightSeverity.Important, 80, RadarSensitivity.Focused, Now);
        var fact = Fact(InsightType.PriceMovement, InsightSeverity.Critical, 95, Now);

        var categorySuppressed = RadarSelectionPolicy.Evaluate(profile, symbolOverride, fact, [10, 20, 90], Now);
        var matchingFact = fact with { InsightType = InsightType.LargeTradeDetected };
        var matched = RadarSelectionPolicy.Evaluate(profile, symbolOverride, matchingFact, [10, 20, 90], Now);

        Assert.Equal(RadarSuppressionReason.EventTypeDisabled, categorySuppressed.SuppressionReason);
        Assert.Equal(RadarMatchDecision.Matched, matched.Decision);
        Assert.Equal(RadarSensitivity.Focused, matched.EffectiveSensitivity);
        Assert.Equal(80, matched.EffectiveMinimumImportance);
        Assert.Equal("radar-selection-v1/radar-sensitivity-v1", matched.SensitivityPolicyVersion);
        Assert.Equal(100, matched.HistoricalPercentile);
    }

    [Fact]
    public void Stale_evidence_is_suppressed_and_composite_score_is_bounded()
    {
        var profile = CreateProfile();
        var stale = Fact(InsightType.PriceMovement, InsightSeverity.Critical, 95, Now.AddHours(-2));

        var result = RadarSelectionPolicy.Evaluate(profile, null, stale, [], Now);
        var composite = RadarSelectionPolicy.CompositeScore([
            stale with { Importance = 90 }, stale with { InsightEventId = Guid.NewGuid(), Importance = 100 }
        ]);

        Assert.Equal(RadarSuppressionReason.StaleSource, result.SuppressionReason);
        Assert.Equal(100, composite);
    }

    [Fact]
    public async Task Billing_capability_governs_followed_symbol_limit_without_plan_name_checks()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var account = new CustomerAccount(Guid.NewGuid(), tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var capabilities = new FixedPlanCapabilities(limit: 2);
        var policy = new RadarEntitlementPolicy(new FixedAccountResolver(account), capabilities);
        var actor = new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, actorId);

        await policy.ValidateManageAsync(actor, 2, CancellationToken.None);
        var error = await Assert.ThrowsAsync<RadarValidationException>(() =>
            policy.ValidateManageAsync(actor, 3, CancellationToken.None));

        Assert.Contains("at most 2", error.Message);
        Assert.Equal(RadarEntitlementPolicy.CapabilityCode, capabilities.LastCapability);
    }

    [Fact]
    public async Task Evaluator_uses_followed_symbols_deduplicates_and_hands_off_notification_intent()
    {
        await using var db = CreateDb();
        var actor = new RadarActor(Guid.NewGuid(), Guid.NewGuid(), ActorType.User.ToString());
        var profile = CreateProfile(actor);
        var repository = new RadarRepository(db, new FixedTimeProvider(Now));
        await repository.SaveProfileAsync(profile, "Created", "Test", CancellationToken.None);
        var insightId = Guid.NewGuid();
        db.InsightEvents.Add(Insight(insightId, "company-1", Now.AddSeconds(-5), 90));
        db.InsightEvents.Add(Insight(Guid.NewGuid(), "not-followed", Now.AddSeconds(-5), 99));
        await db.SaveChangesAsync();
        var notifications = new CapturingNotificationPublisher();
        var processor = new RadarEvaluationProcessor(db, repository,
            new FixedFollowedSymbolRepository(actor, "company-1"), new AllowEntitlement(), new AllowGate(),
            notifications, Options.Create(new RadarOptions()), new FixedTimeProvider(Now),
            NullLogger<RadarEvaluationProcessor>.Instance);

        var first = await processor.EvaluateAsync(10, CancellationToken.None);
        var second = await processor.EvaluateAsync(10, CancellationToken.None);

        Assert.Equal(1, first.Matched);
        Assert.Equal(1, first.NotificationIntents);
        Assert.Equal(0, second.NotificationIntents);
        Assert.Single(notifications.Requests);
        Assert.Equal("company-1", notifications.Requests[0].EntityKey);
        Assert.Contains(insightId.ToString("N"), notifications.Requests[0].PayloadJson);
        Assert.Single(await db.RadarEventMatches.Where(item => item.InsightEventId == insightId).ToListAsync());
        Assert.DoesNotContain(await db.RadarEventMatches.ToListAsync(), item => item.ExternalCompanyId == "not-followed");
    }

    [Fact]
    public async Task Global_notification_gate_suppresses_handoff_without_losing_checkpoint()
    {
        await using var db = CreateDb();
        var actor = new RadarActor(Guid.NewGuid(), Guid.NewGuid(), ActorType.User.ToString());
        var repository = new RadarRepository(db, new FixedTimeProvider(Now));
        await repository.SaveProfileAsync(CreateProfile(actor), "Created", "Test", CancellationToken.None);
        db.InsightEvents.Add(Insight(Guid.NewGuid(), "company-1", Now.AddSeconds(-5), 90));
        await db.SaveChangesAsync();
        var notifications = new CapturingNotificationPublisher();
        var processor = new RadarEvaluationProcessor(db, repository,
            new FixedFollowedSymbolRepository(actor, "company-1"), new AllowEntitlement(), new DenyGate(),
            notifications, Options.Create(new RadarOptions()), new FixedTimeProvider(Now),
            NullLogger<RadarEvaluationProcessor>.Instance);

        var result = await processor.EvaluateAsync(10, CancellationToken.None);

        Assert.Equal(0, result.NotificationIntents);
        Assert.Equal(1, result.Suppressed);
        Assert.Empty(notifications.Requests);
        var match = await db.RadarEventMatches.SingleAsync();
        Assert.Equal(nameof(RadarSuppressionReason.GlobalNotificationPolicy), match.SuppressionReason);
    }

    [Fact]
    public async Task Composite_components_do_not_replay_as_individual_notifications()
    {
        await using var db = CreateDb();
        var actor = new RadarActor(Guid.NewGuid(), Guid.NewGuid(), ActorType.User.ToString());
        var repository = new RadarRepository(db, new FixedTimeProvider(Now));
        await repository.SaveProfileAsync(CreateProfile(actor), "Created", "Test", CancellationToken.None);
        db.InsightEvents.Add(Insight(Guid.NewGuid(), "company-1", Now.AddSeconds(-10), 90));
        var second = Insight(Guid.NewGuid(), "company-1", Now.AddSeconds(-5), 90);
        second.InsightType = nameof(InsightType.LargeTradeDetected);
        db.InsightEvents.Add(second);
        await db.SaveChangesAsync();
        var notifications = new CapturingNotificationPublisher();
        var processor = new RadarEvaluationProcessor(db, repository,
            new FixedFollowedSymbolRepository(actor, "company-1"), new AllowEntitlement(), new AllowGate(),
            notifications, Options.Create(new RadarOptions()), new FixedTimeProvider(Now),
            NullLogger<RadarEvaluationProcessor>.Instance);

        var first = await processor.EvaluateAsync(10, CancellationToken.None);
        var replay = await processor.EvaluateAsync(10, CancellationToken.None);

        Assert.Equal(1, first.CompositeMatches);
        Assert.Equal(1, first.NotificationIntents);
        Assert.Equal(0, replay.NotificationIntents);
        Assert.Single(notifications.Requests);
        Assert.Equal("RadarCompositeMatched", notifications.Requests[0].EventType);
    }

    [Fact]
    public async Task Paused_profile_is_not_evaluated_or_handed_off()
    {
        await using var db = CreateDb();
        var actor = new RadarActor(Guid.NewGuid(), Guid.NewGuid(), ActorType.User.ToString());
        var profile = CreateProfile(actor);
        var repository = new RadarRepository(db, new FixedTimeProvider(Now));
        await repository.SaveProfileAsync(profile, "Created", "Test", CancellationToken.None);
        profile.Update(1, profile.EventTypes, profile.MinimumSeverity, profile.MinimumImportance,
            profile.Sensitivity, profile.DeliveryMode, RadarState.Paused, Now);
        await repository.SaveProfileAsync(profile, "Paused", "Test", CancellationToken.None);
        db.InsightEvents.Add(Insight(Guid.NewGuid(), "company-1", Now.AddSeconds(-5), 90));
        await db.SaveChangesAsync();
        var notifications = new CapturingNotificationPublisher();
        var processor = new RadarEvaluationProcessor(db, repository,
            new FixedFollowedSymbolRepository(actor, "company-1"), new AllowEntitlement(), new AllowGate(),
            notifications, Options.Create(new RadarOptions()), new FixedTimeProvider(Now),
            NullLogger<RadarEvaluationProcessor>.Instance);

        var result = await processor.EvaluateAsync(10, CancellationToken.None);

        Assert.Equal(0, result.ProfilesConsidered);
        Assert.Empty(notifications.Requests);
    }

    private static RadarProfile CreateProfile(RadarActor? actor = null) => RadarProfile.Create(
        actor ?? new RadarActor(Guid.NewGuid(), Guid.NewGuid(), ActorType.User.ToString()),
        [InsightType.PriceMovement, InsightType.LargeTradeDetected], InsightSeverity.Notice, 50,
        RadarSensitivity.Balanced, RadarDeliveryMode.Immediate, RadarState.Active, Now);

    private static RadarEventFact Fact(
        InsightType type, InsightSeverity severity, decimal importance, DateTimeOffset freshness) =>
        new(Guid.NewGuid(), "company-1", type, severity, importance, 90, Now.AddSeconds(-5), freshness, "evidence-1");

    private static InsightEventRow Insight(Guid id, string companyId, DateTimeOffset detectedAt, decimal importance) => new()
    {
        Id = id,
        ExternalCompanyId = companyId,
        Symbol = companyId,
        InsightType = nameof(InsightType.PriceMovement),
        Severity = nameof(InsightSeverity.Critical),
        ImportanceScore = importance,
        ConfidenceScore = 90,
        Title = "Movement",
        Summary = "Movement detected.",
        Reason = "Threshold crossed.",
        EvidenceJson = JsonSerializer.Serialize(new[]
        {
            new InsightEvidenceItem("price", "100", "test", null, detectedAt)
        }),
        SourceProviderName = "test",
        SourceEntityType = nameof(InsightSourceEntityType.MarketQuote),
        DetectedAtUtc = detectedAt,
        ExpiresAtUtc = Now.AddHours(1),
        DeduplicationKey = $"insight:{id:N}",
        SuggestedActionsJson = "[]"
    };

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedFollowedSymbolRepository(RadarActor actor, string companyId) : IFollowedSymbolRepository
    {
        private readonly FollowedSymbol value = FollowedSymbol.Follow(
            new FollowedSymbolActor(actor.TenantId, actor.ActorId, actor.ActorType),
            new CanonicalFollowedCompany(companyId, "TEST", "Test Company", null), Now, "Test");

        public Task<IReadOnlyCollection<FollowedSymbol>> GetAsync(FollowedSymbolActor queryActor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FollowedSymbol>>([value]);
        public Task<FollowedSymbol?> FindAsync(FollowedSymbolActor queryActor, string externalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<FollowedSymbol?>(externalCompanyId == value.ExternalCompanyId ? value : null);
        public Task SaveAsync(FollowedSymbol followedSymbol, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReplaceAsync(FollowedSymbolActor queryActor, IReadOnlyCollection<FollowedSymbol> followedSymbols, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(FollowedSymbolActor queryActor, string externalCompanyId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AllowEntitlement : IRadarEntitlementPolicy
    {
        public Task ValidateManageAsync(CurrentActor actor, int count, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> CanEvaluateAsync(RadarActor actor, int count, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class AllowGate : IRadarNotificationPolicyGate
    {
        public Task<RadarNotificationGateDecision> EvaluateAsync(RadarActor actor, InsightSeverity severity,
            RadarDeliveryMode mode, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new RadarNotificationGateDecision(true, RadarSuppressionReason.None, now, "097-test"));
    }

    private sealed class DenyGate : IRadarNotificationPolicyGate
    {
        public Task<RadarNotificationGateDecision> EvaluateAsync(RadarActor actor, InsightSeverity severity,
            RadarDeliveryMode mode, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult(new RadarNotificationGateDecision(false, RadarSuppressionReason.GlobalNotificationPolicy, now, "097-test"));
    }

    private sealed class CapturingNotificationPublisher : INotificationIntentPublisher
    {
        public List<NotificationIntentRequest> Requests { get; } = [];
        public Task<NotificationIntentDto> EnqueueAsync(NotificationIntentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new NotificationIntentDto(Guid.NewGuid(), request.Actor, request.Channel,
                request.EventType, request.EntityKey, request.DeduplicationKey, request.Severity,
                NotificationIntentStatus.Pending, Now, request.NotBeforeUtc, request.ExpiresAtUtc));
        }
    }

    private sealed class FixedAccountResolver(CustomerAccount account) : IBillableAccountResolver
    {
        public Task<CustomerAccount> ResolveAsync(BillableActorContext actor, CancellationToken cancellationToken) =>
            Task.FromResult(account);
    }

    private sealed class FixedPlanCapabilities(decimal limit) : IPlanCapabilityService
    {
        public string? LastCapability { get; private set; }

        public Task ValidateCanExecuteAsync(CustomerAccount account, string operationCode, CancellationToken cancellationToken)
        {
            LastCapability = operationCode;
            return Task.CompletedTask;
        }

        public Task<decimal?> GetLimitAsync(CustomerAccount account, string capabilityCode, CancellationToken cancellationToken)
        {
            LastCapability = capabilityCode;
            return Task.FromResult<decimal?>(limit);
        }
    }
}
