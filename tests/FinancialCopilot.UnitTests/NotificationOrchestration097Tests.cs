using System.Text.Json;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class NotificationOrchestration097Tests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Policy_applies_explicit_mutes_and_critical_bypass_in_documented_precedence()
    {
        var preference = Preference(NotificationDeliveryMode.Immediate);
        var muted = NotificationPreferencePolicy.Evaluate(Context(preference,
            InsightSeverity.Critical, categoryEnabled: true, symbolMuted: true,
            deliveredToday: 100, isQuiet: true, lastSimilar: Now.AddMinutes(-1)));
        var critical = NotificationPreferencePolicy.Evaluate(Context(preference,
            InsightSeverity.Critical, categoryEnabled: true, symbolMuted: false,
            deliveredToday: 100, isQuiet: true, lastSimilar: Now.AddMinutes(-1)));

        Assert.Equal(NotificationPolicyAction.Suppress, muted.Action);
        Assert.Equal(NotificationSuppressionReason.SymbolMuted, muted.Reason);
        Assert.Equal(NotificationPolicyAction.Deliver, critical.Action);
        Assert.Contains("bypass", critical.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overnight_quiet_hours_are_detected_on_both_sides_of_midnight()
    {
        Assert.True(NotificationPreferencePolicy.IsQuietHours(new TimeOnly(23, 30),
            new TimeOnly(23, 0), new TimeOnly(7, 0)));
        Assert.True(NotificationPreferencePolicy.IsQuietHours(new TimeOnly(6, 59),
            new TimeOnly(23, 0), new TimeOnly(7, 0)));
        Assert.False(NotificationPreferencePolicy.IsQuietHours(new TimeOnly(12, 0),
            new TimeOnly(23, 0), new TimeOnly(7, 0)));
    }

    [Fact]
    public void Lifecycle_rejects_terminal_replay()
    {
        Assert.Throws<NotificationValidationException>(() =>
            NotificationIntentLifecycle.EnsureTransition(
                NotificationIntentState.Delivered, NotificationIntentState.Sending));
    }

    [Fact]
    public async Task Producer_replay_returns_one_durable_intent()
    {
        await using var db = Database();
        var publisher = new EfCoreNotificationIntentPublisher(db, new MutableTimeProvider(Now));
        var request = Request("dedup-one");

        var first = await publisher.EnqueueAsync(request, CancellationToken.None);
        var replay = await publisher.EnqueueAsync(request, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Single(db.NotificationIntents);
        Assert.Equal(NotificationIntentState.Pending.ToString(), db.NotificationIntents.Single().Status);
    }

    [Fact]
    public async Task Transient_retry_resumes_the_same_part_and_records_one_success()
    {
        await using var db = Database();
        var clock = new MutableTimeProvider(Now);
        var publisher = new EfCoreNotificationIntentPublisher(db, clock);
        var intent = await publisher.EnqueueAsync(Request("retry-one"), CancellationToken.None);
        var transport = new SequenceTransport(
            new NotificationTransportResult(NotificationTransportOutcome.RetryableFailure, null,
                "Telegram429", "Rate limited.", TimeSpan.FromSeconds(1)),
            new NotificationTransportResult(NotificationTransportOutcome.Delivered, "123", null, null));
        var dispatcher = Dispatcher(db, clock, transport, maximumAttempts: 3);

        var first = await dispatcher.DispatchDueAsync(10, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = await dispatcher.DispatchDueAsync(10, CancellationToken.None);

        Assert.Equal(1, first.Retried);
        Assert.Equal(1, second.Delivered);
        Assert.Equal(NotificationIntentState.Delivered.ToString(),
            db.NotificationIntents.Single(row => row.Id == intent.Id).Status);
        var attempts = db.NotificationDeliveryAttempts.Where(row => row.NotificationIntentId == intent.Id)
            .OrderBy(row => row.AttemptNumber).ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Single(attempts, row => row.Status == "Delivered");
        Assert.Single(attempts.Select(row => row.DeliveryPartKey).Distinct());
        Assert.Equal(2, transport.SendCount);
    }

    [Fact]
    public async Task Permanent_transport_failure_deadletters_once_and_creates_history_handoff()
    {
        await using var db = Database();
        var clock = new MutableTimeProvider(Now);
        var publisher = new EfCoreNotificationIntentPublisher(db, clock);
        var intent = await publisher.EnqueueAsync(Request("blocked-chat"), CancellationToken.None);
        var dispatcher = Dispatcher(db, clock, new SequenceTransport(
            new NotificationTransportResult(NotificationTransportOutcome.PermanentFailure,
                null, "Telegram403", "Bot was blocked.")));

        var result = await dispatcher.DispatchDueAsync(10, CancellationToken.None);

        Assert.Equal(1, result.DeadLettered);
        Assert.Equal(NotificationIntentState.DeadLettered.ToString(),
            db.NotificationIntents.Single(row => row.Id == intent.Id).Status);
        var handoff = Assert.Single(db.NotificationOutcomeHandoffs);
        Assert.Equal(intent.Id, handoff.NotificationIntentId);
        Assert.Equal("Pending", handoff.Status);
        Assert.DoesNotContain("chat", handoff.CorrelationId, StringComparison.OrdinalIgnoreCase);

        var operations = new NotificationOperations(db, clock);
        await operations.RetryDeadLetterAsync(intent.Id, Guid.NewGuid(), intent.Actor.TenantId,
            "manual-retry", CancellationToken.None);
        Assert.Equal(NotificationIntentState.Pending.ToString(),
            db.NotificationIntents.Single(row => row.Id == intent.Id).Status);
        Assert.Equal("ManualRetry", Assert.Single(db.NotificationOperationAudits).Action);
    }

    [Fact]
    public async Task Multipart_retry_skips_the_already_delivered_part()
    {
        await using var db = Database();
        var clock = new MutableTimeProvider(Now);
        var publisher = new EfCoreNotificationIntentPublisher(db, clock);
        var longPayload = JsonSerializer.Serialize(new { message = new string('x', 1_400) });
        var request = Request("multipart-one") with { PayloadJson = longPayload };
        var intent = await publisher.EnqueueAsync(request, CancellationToken.None);
        var transport = new SequenceTransport(
            new NotificationTransportResult(NotificationTransportOutcome.Delivered, "part-1", null, null),
            new NotificationTransportResult(NotificationTransportOutcome.RetryableFailure, null, "Telegram500", "Transient."),
            new NotificationTransportResult(NotificationTransportOutcome.Delivered, "part-2", null, null),
            new NotificationTransportResult(NotificationTransportOutcome.Delivered, "part-3", null, null));
        var dispatcher = Dispatcher(db, clock, transport, messagePartLength: 500);

        await dispatcher.DispatchDueAsync(10, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        await dispatcher.DispatchDueAsync(10, CancellationToken.None);

        var attempts = db.NotificationDeliveryAttempts.Where(row => row.NotificationIntentId == intent.Id).ToArray();
        Assert.Equal(NotificationIntentState.Delivered.ToString(), db.NotificationIntents.Single().Status);
        Assert.Single(attempts, row => row.DeliveryPartKey.EndsWith("PART:1") && row.Status == "Delivered");
        Assert.Equal(1, attempts.Count(row => row.DeliveryPartKey.EndsWith("PART:1")));
        Assert.All(attempts.GroupBy(row => row.DeliveryPartKey), group =>
            Assert.Single(group, row => row.Status == "Delivered"));
    }

    [Fact]
    public async Task Digest_preference_batches_then_delivers_once_at_the_actor_schedule()
    {
        await using var db = Database();
        var clock = new MutableTimeProvider(Now);
        AddPreference(db, NotificationDeliveryMode.Digest, new TimeOnly(12, 1));
        var publisher = new EfCoreNotificationIntentPublisher(db, clock);
        var intent = await publisher.EnqueueAsync(Request("digest-one"), CancellationToken.None);
        var transport = new SequenceTransport(
            new NotificationTransportResult(NotificationTransportOutcome.Delivered, "456", null, null));
        var dispatcher = Dispatcher(db, clock, transport);

        var first = await dispatcher.DispatchDueAsync(10, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await dispatcher.DispatchDueAsync(10, CancellationToken.None);

        Assert.Equal(1, first.Batched);
        Assert.Equal(1, second.Delivered);
        Assert.Equal(NotificationIntentState.Delivered.ToString(),
            db.NotificationIntents.Single(row => row.Id == intent.Id).Status);
        Assert.Equal("Delivered", Assert.Single(db.NotificationBatches).Status);
        Assert.Equal(1, transport.SendCount);
    }

    private static NotificationPolicyContext Context(
        NotificationPreference preference,
        InsightSeverity severity,
        bool categoryEnabled,
        bool symbolMuted,
        int deliveredToday,
        bool isQuiet,
        DateTimeOffset? lastSimilar) =>
        new(preference, severity, preference.MinimumSeverity, preference.CooldownMinutes,
            true, categoryEnabled, symbolMuted, deliveredToday, Now, Now.AddDays(1), isQuiet,
            Now.AddHours(1), Now.AddHours(2), lastSimilar, false);

    private static NotificationPreference Preference(NotificationDeliveryMode mode) =>
        NotificationPreference.Rehydrate(Guid.NewGuid(),
            new NotificationOwner(Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"), "User"),
            "UTC", mode, new TimeOnly(23, 0), new TimeOnly(7, 0), InsightSeverity.Notice,
            20, new TimeOnly(18, 0), 30, 1, Guid.NewGuid(), Now, Now);

    private static NotificationIntentRequest Request(string dedup) => new(
        new NotificationActor(Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"), "User"),
        NotificationChannel.Telegram, "PriceMovement", "100", dedup,
        InsightSeverity.Important, "{\"symbol\":\"TEST\",\"value\":42,\"sourceFreshnessUtc\":\"2026-07-15T11:59:00Z\"}",
        Now, Now.AddDays(1), "corr-097", Guid.NewGuid(), "evidence:price:100", "Market", "PriceMovement:100");

    private static FinancialIngestionDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        return new FinancialIngestionDbContext(options);
    }

    private static NotificationDispatcher Dispatcher(
        FinancialIngestionDbContext db,
        TimeProvider clock,
        ITelegramNotificationTransport transport,
        int maximumAttempts = 5,
        int messagePartLength = 3800) =>
        new(db, new AllowEntitlement(), new Recipient(), transport,
            Options.Create(new NotificationDispatcherOptions
            {
                BatchSize = 100, LeaseSeconds = 90, MaximumAttempts = maximumAttempts,
                InitialBackoffSeconds = 1, MaximumBackoffSeconds = 30,
                DigestMaximumItems = 25, MessagePartLength = messagePartLength,
                TransportErrorRetentionDays = 30, DeliveryAuditRetentionDays = 730
            }), clock, NullLogger<NotificationDispatcher>.Instance);

    private static void AddPreference(
        FinancialIngestionDbContext db,
        NotificationDeliveryMode mode,
        TimeOnly digestTime)
    {
        db.NotificationPreferences.Add(new NotificationPreferenceRow
        {
            Id = Guid.NewGuid(), TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222"), ActorType = "User",
            TimeZoneId = "UTC", DeliveryMode = mode.ToString(), MinimumSeverity = InsightSeverity.Notice.ToString(),
            DailyCap = 20, DigestTime = digestTime, CooldownMinutes = 0, Version = 1,
            ConcurrencyToken = Guid.NewGuid(), CreatedAtUtc = Now, UpdatedAtUtc = Now
        });
        db.SaveChanges();
    }

    private sealed class AllowEntitlement : INotificationEntitlementPolicy
    {
        public Task ValidateManageAsync(Application.Authentication.CurrentActor actor, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<bool> CanDeliverAsync(NotificationActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class Recipient : INotificationRecipientResolver
    {
        public Task<TelegramNotificationRecipient?> ResolveTelegramAsync(
            NotificationActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult<TelegramNotificationRecipient?>(new TelegramNotificationRecipient(123456));
    }

    private sealed class SequenceTransport(params NotificationTransportResult[] results) : ITelegramNotificationTransport
    {
        private int index;
        public int SendCount { get; private set; }
        public Task<NotificationTransportResult> SendAsync(
            long chatId,
            string text,
            string deliveryPartIdempotencyKey,
            CancellationToken cancellationToken)
        {
            SendCount++;
            var result = results[Math.Min(index, results.Length - 1)];
            index++;
            return Task.FromResult(result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now = now.Add(value);
    }
}
