using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class AlertHistory099Tests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly CurrentActor Actor = new(
        ActorType.User,
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        AuthenticationMode.WebAppUser,
        UserId: Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public async Task Handoff_projection_creates_one_actor_visible_alert_and_replay_does_not_duplicate()
    {
        await using var db = Database();
        var ids = SeedDeliveredAlert(db);
        var useCases = UseCases(db);

        var first = await useCases.ProcessPendingAsync(10, CancellationToken.None);
        db.NotificationOutcomeHandoffs.Single().Status = "Pending";
        await db.SaveChangesAsync();
        var replay = await useCases.ProcessPendingAsync(10, CancellationToken.None);

        Assert.Equal(1, first.Created);
        Assert.Equal(1, replay.Duplicates);
        var record = Assert.Single(db.UserAlertRecords);
        Assert.Equal(ids.IntentId, record.NotificationIntentId);
        Assert.Equal("Delivered", record.DeliveryStatus);
        Assert.Contains("observed 125", record.WhyText, StringComparison.Ordinal);
        Assert.Contains("threshold 100", record.WhyText, StringComparison.Ordinal);
        Assert.Contains("delivery outcome Delivered", record.WhyText, StringComparison.Ordinal);
        Assert.Equal(64, record.EvidenceHash.Length);
        Assert.Contains(db.UserAlertDeliveryTimeline, row => row.AttemptNumber == 1 && row.Status == "Delivered");
        Assert.Contains(db.UserAlertDeliveryTimeline, row => row.AttemptNumber == null && row.Status == "Delivered");
    }

    [Fact]
    public async Task Dismiss_feedback_and_reaction_refresh_do_not_mutate_immutable_evidence()
    {
        await using var db = Database();
        SeedDeliveredAlert(db);
        var useCases = UseCases(db);
        await useCases.ProcessPendingAsync(10, CancellationToken.None);
        var alertId = db.UserAlertRecords.Single().Id;
        var before = await useCases.GetDetailAsync(Actor, alertId, CancellationToken.None);

        await useCases.DismissAsync(new DismissAlertCommand(Actor, alertId, "corr-dismiss"), CancellationToken.None);
        await useCases.RecordFeedbackAsync(new FeedbackAlertCommand(Actor, alertId, "helpful", "corr-feedback"), CancellationToken.None);
        var reactions = await useCases.RefreshReactionAsync(new RefreshAlertReactionCommand(
            Actor, alertId, null, "corr-reaction"), CancellationToken.None);
        var after = await useCases.GetDetailAsync(Actor, alertId, CancellationToken.None);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before.Record.EvidenceHash, after.Record.EvidenceHash);
        Assert.Equal(before.EvidenceSnapshotJson, after.EvidenceSnapshotJson);
        Assert.NotNull(after.Record.DismissedAtUtc);
        Assert.All(reactions, reaction =>
        {
            Assert.Equal("Unavailable", reaction.Status);
            Assert.Contains("guessed prices", reaction.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Detail_lookup_is_actor_scoped_and_does_not_leak_cross_actor_alerts()
    {
        await using var db = Database();
        SeedDeliveredAlert(db);
        var useCases = UseCases(db);
        await useCases.ProcessPendingAsync(10, CancellationToken.None);
        var alertId = db.UserAlertRecords.Single().Id;
        var other = Actor with { ActorId = Guid.Parse("33333333-3333-3333-3333-333333333333") };

        var detail = await useCases.GetDetailAsync(other, alertId, CancellationToken.None);
        var history = await useCases.GetHistoryAsync(new AlertHistoryQuery(other), CancellationToken.None);

        Assert.Null(detail);
        Assert.Empty(history.Items);
    }

    private static (Guid IntentId, Guid HandoffId) SeedDeliveredAlert(FinancialIngestionDbContext db)
    {
        var intentId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var triggerId = Guid.NewGuid();
        var handoffId = Guid.NewGuid();
        db.NotificationIntents.Add(new NotificationIntentRow
        {
            Id = intentId,
            TenantId = Actor.TenantId,
            ActorId = Actor.ActorId,
            ActorType = Actor.ActorType.ToString(),
            Channel = NotificationChannel.Telegram.ToString(),
            EventType = "PriceBreakout",
            EntityKey = "100",
            DeduplicationKey = "dedup-099",
            Severity = InsightSeverity.Important.ToString(),
            Status = NotificationIntentState.Delivered.ToString(),
            Category = "Market",
            PayloadJson = "{\"detectorVersion\":\"detector-099\",\"observedValue\":125}",
            SourceEventId = sourceEventId,
            EvidenceReference = "insight:price:100",
            PolicyVersion = "notification-policy-099",
            PreferenceVersion = 7,
            DecisionReason = NotificationSuppressionReason.None.ToString(),
            DecisionExplanation = "category enabled and severity matched",
            DecisionAtUtc = Now,
            DeliveredAtUtc = Now,
            CreatedAtUtc = Now.AddMinutes(-1),
            NotBeforeUtc = Now.AddMinutes(-1),
            ExpiresAtUtc = Now.AddDays(1),
            CorrelationId = "corr-099",
            ConcurrencyToken = Guid.NewGuid()
        });
        db.NotificationOutcomeHandoffs.Add(new NotificationOutcomeHandoffRow
        {
            Id = handoffId,
            NotificationIntentId = intentId,
            Sequence = 1,
            TenantId = Actor.TenantId,
            ActorId = Actor.ActorId,
            ActorType = Actor.ActorType.ToString(),
            TerminalStatus = NotificationIntentState.Delivered.ToString(),
            Reason = NotificationSuppressionReason.None.ToString(),
            EvidenceReference = "insight:price:100",
            CorrelationId = "corr-099",
            Status = "Pending",
            CreatedAtUtc = Now
        });
        db.AlertRules.Add(new AlertRuleRow
        {
            Id = ruleId,
            TenantId = Actor.TenantId,
            ActorId = Actor.ActorId,
            ActorType = Actor.ActorType.ToString(),
            ExternalCompanyId = "100",
            RuleType = "Metric",
            MetricOrEventCode = "LATEST_PRICE",
            Operator = ">=",
            Threshold = 100,
            Unit = "IRR",
            BaselineWindow = 5,
            Recurrence = "Once",
            CooldownMinutes = 30,
            ResetPolicy = "Manual",
            SessionPolicy = "Regular",
            State = "Active",
            Version = 3,
            ConfirmationNonce = "nonce",
            ConfirmationExpiresAtUtc = Now.AddDays(1),
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now
        });
        db.AlertRuleTriggers.Add(new AlertRuleTriggerRow
        {
            Id = triggerId,
            RuleId = ruleId,
            RuleVersion = 3,
            TriggerSequence = 1,
            EvidenceIdentity = "price:100:2026-07-15",
            DeduplicationKey = "trigger-099",
            ObservedValue = 125,
            Threshold = 100,
            Operator = ">=",
            Unit = "IRR",
            SourceProvider = "Tsetmc",
            SourcePeriod = "2026-07-15",
            SourceFreshnessUtc = Now.AddMinutes(-2),
            TriggeredAtUtc = Now.AddMinutes(-1),
            EvidenceJson = "{\"price\":125,\"threshold\":100}",
            NotificationIntentId = intentId
        });
        db.InsightEvents.Add(new InsightEventRow
        {
            Id = sourceEventId,
            ExternalCompanyId = "100",
            Symbol = "TEST",
            IndustryCode = "IDX",
            InsightType = "PriceBreakout",
            Severity = InsightSeverity.Important.ToString(),
            ImportanceScore = 0.8m,
            ConfidenceScore = 0.9m,
            Title = "TEST breakout",
            Summary = "Observed value 125 crossed threshold 100.",
            Reason = "Rule trigger",
            EvidenceJson = "{\"price\":125}",
            SourceProviderName = "Tsetmc",
            SourceEntityType = "Quote",
            SourceEntityId = "100",
            SourcePeriod = "2026-07-15",
            DetectedAtUtc = Now,
            DeduplicationKey = "insight-099"
        });
        db.NotificationDeliveryAttempts.Add(new NotificationDeliveryAttemptRow
        {
            Id = Guid.NewGuid(),
            NotificationIntentId = intentId,
            PartNumber = 1,
            DeliveryPartKey = "part-099",
            IdempotencyKey = "part-099:1",
            Status = "Delivered",
            AttemptNumber = 1,
            ProviderMessageId = "telegram-1",
            StartedAtUtc = Now.AddSeconds(-10),
            CompletedAtUtc = Now
        });
        db.SaveChanges();
        return (intentId, handoffId);
    }

    private static AlertHistoryUseCases UseCases(FinancialIngestionDbContext db) =>
        new(db, new FakeNotificationUseCases(), new AllowEntitlement(),
            Options.Create(new AlertHistoryOptions()), new FixedTimeProvider(Now),
            NullLogger<AlertHistoryUseCases>.Instance);

    private static FinancialIngestionDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FinancialIngestionDbContext(options);
    }

    private sealed class FakeNotificationUseCases : INotificationUseCases
    {
        public Task<NotificationPreferenceDto> GetPreferencesAsync(
            CurrentActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationPreferenceDto(
                Guid.NewGuid(), "UTC", NotificationDeliveryMode.Immediate, null, null,
                InsightSeverity.Notice, 20, new TimeOnly(18, 0), 30, 1, [], [],
                NotificationPreferencePolicy.Version, "test policy", Now));

        public Task<NotificationPreferenceDto> UpdatePreferencesAsync(
            UpdateNotificationPreferenceCommand command,
            CancellationToken cancellationToken) =>
            GetPreferencesAsync(command.Actor, cancellationToken);

        public Task<NotificationHistoryPage> GetHistoryAsync(
            CurrentActor actor,
            int offset,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationHistoryPage([], offset, pageSize, false));
    }

    private sealed class AllowEntitlement : INotificationEntitlementPolicy
    {
        public Task ValidateManageAsync(CurrentActor actor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> CanDeliverAsync(NotificationActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
