using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Domain.Notifications;

public enum NotificationIntentState
{
    Pending,
    Deferred,
    Batched,
    Sending,
    Delivered,
    Suppressed,
    Expired,
    FailedRetryable,
    DeadLettered,
    Cancelled
}

public enum NotificationDeliveryMode
{
    Immediate,
    Digest
}

public enum NotificationSuppressionReason
{
    None,
    Duplicate,
    Cooldown,
    QuietHours,
    BelowMinimumSeverity,
    DailyCap,
    CategoryMuted,
    SymbolMuted,
    EntitlementDenied,
    MissingTelegramLink,
    Expired,
    PermanentTransportFailure,
    Cancelled
}

public enum NotificationPolicyAction
{
    Deliver,
    Defer,
    Batch,
    Suppress,
    Expire
}

public sealed record NotificationOwner
{
    public NotificationOwner(Guid tenantId, Guid actorId, string actorType)
    {
        if (tenantId == Guid.Empty) throw new NotificationValidationException("Tenant id is required.");
        if (actorId == Guid.Empty) throw new NotificationValidationException("Actor id is required.");
        if (string.IsNullOrWhiteSpace(actorType) || actorType.Trim().Length > 32)
            throw new NotificationValidationException("A bounded actor type is required.");

        TenantId = tenantId;
        ActorId = actorId;
        ActorType = actorType.Trim();
    }

    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public string ActorType { get; }
}

public sealed class NotificationPreference
{
    private NotificationPreference(
        Guid id,
        NotificationOwner owner,
        string timeZoneId,
        NotificationDeliveryMode deliveryMode,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        InsightSeverity minimumSeverity,
        int dailyCap,
        TimeOnly digestTime,
        int cooldownMinutes,
        int version,
        Guid concurrencyToken,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
    {
        Validate(timeZoneId, quietHoursStart, quietHoursEnd, dailyCap, cooldownMinutes, version);
        Id = id == Guid.Empty ? throw new NotificationValidationException("Preference id is required.") : id;
        Owner = owner;
        TimeZoneId = timeZoneId.Trim();
        DeliveryMode = deliveryMode;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        MinimumSeverity = minimumSeverity;
        DailyCap = dailyCap;
        DigestTime = digestTime;
        CooldownMinutes = cooldownMinutes;
        Version = version;
        ConcurrencyToken = concurrencyToken == Guid.Empty ? Guid.NewGuid() : concurrencyToken;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }
    public NotificationOwner Owner { get; }
    public string TimeZoneId { get; private set; }
    public NotificationDeliveryMode DeliveryMode { get; private set; }
    public TimeOnly? QuietHoursStart { get; private set; }
    public TimeOnly? QuietHoursEnd { get; private set; }
    public InsightSeverity MinimumSeverity { get; private set; }
    public int DailyCap { get; private set; }
    public TimeOnly DigestTime { get; private set; }
    public int CooldownMinutes { get; private set; }
    public int Version { get; private set; }
    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NotificationPreference CreateDefault(NotificationOwner owner, DateTimeOffset now) =>
        Create(owner, NotificationPreferencePolicy.DefaultTimeZoneId,
            NotificationDeliveryMode.Immediate, new TimeOnly(23, 0), new TimeOnly(7, 0),
            InsightSeverity.Notice, 20, new TimeOnly(18, 0), 30, now);

    public static NotificationPreference Create(
        NotificationOwner owner,
        string timeZoneId,
        NotificationDeliveryMode deliveryMode,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        InsightSeverity minimumSeverity,
        int dailyCap,
        TimeOnly digestTime,
        int cooldownMinutes,
        DateTimeOffset now) =>
        new(Guid.NewGuid(), owner, timeZoneId, deliveryMode, quietHoursStart, quietHoursEnd,
            minimumSeverity, dailyCap, digestTime, cooldownMinutes, 1, Guid.NewGuid(), now, now);

    public static NotificationPreference Rehydrate(
        Guid id,
        NotificationOwner owner,
        string timeZoneId,
        NotificationDeliveryMode deliveryMode,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        InsightSeverity minimumSeverity,
        int dailyCap,
        TimeOnly digestTime,
        int cooldownMinutes,
        int version,
        Guid concurrencyToken,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc) =>
        new(id, owner, timeZoneId, deliveryMode, quietHoursStart, quietHoursEnd,
            minimumSeverity, dailyCap, digestTime, cooldownMinutes, version,
            concurrencyToken, createdAtUtc, updatedAtUtc);

    public void Update(
        int expectedVersion,
        string timeZoneId,
        NotificationDeliveryMode deliveryMode,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        InsightSeverity minimumSeverity,
        int dailyCap,
        TimeOnly digestTime,
        int cooldownMinutes,
        DateTimeOffset now)
    {
        if (expectedVersion != Version)
            throw new NotificationValidationException(
                $"Notification preference version conflict. Expected {Version}, received {expectedVersion}.");
        Validate(timeZoneId, quietHoursStart, quietHoursEnd, dailyCap, cooldownMinutes, Version);
        TimeZoneId = timeZoneId.Trim();
        DeliveryMode = deliveryMode;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        MinimumSeverity = minimumSeverity;
        DailyCap = dailyCap;
        DigestTime = digestTime;
        CooldownMinutes = cooldownMinutes;
        Version++;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = now;
    }

    private static void Validate(
        string timeZoneId,
        TimeOnly? quietHoursStart,
        TimeOnly? quietHoursEnd,
        int dailyCap,
        int cooldownMinutes,
        int version)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Trim().Length > 128)
            throw new NotificationValidationException("A bounded IANA or system timezone id is required.");
        if (quietHoursStart.HasValue != quietHoursEnd.HasValue)
            throw new NotificationValidationException("Quiet-hours start and end must both be set or both be omitted.");
        if (quietHoursStart == quietHoursEnd && quietHoursStart.HasValue)
            throw new NotificationValidationException("Quiet-hours start and end cannot be equal.");
        if (dailyCap is < 1 or > 100)
            throw new NotificationValidationException("Daily cap must be between 1 and 100.");
        if (cooldownMinutes is < 0 or > 1_440)
            throw new NotificationValidationException("Cooldown must be between 0 and 1440 minutes.");
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
    }
}

public sealed record NotificationPolicyContext(
    NotificationPreference Preference,
    InsightSeverity IntentSeverity,
    InsightSeverity EffectiveMinimumSeverity,
    int EffectiveCooldownMinutes,
    bool Entitled,
    bool CategoryEnabled,
    bool SymbolMuted,
    int DeliveredToday,
    DateTimeOffset NowUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsQuietHours,
    DateTimeOffset? QuietHoursEndUtc,
    DateTimeOffset NextDigestUtc,
    DateTimeOffset? LastSimilarDeliveredAtUtc,
    bool AlreadyBatched);

public sealed record NotificationPolicyDecision(
    NotificationPolicyAction Action,
    NotificationSuppressionReason Reason,
    DateTimeOffset NotBeforeUtc,
    string PolicyVersion,
    string Explanation);

public static class NotificationPreferencePolicy
{
    public const string Version = "notification-precedence-v1";
    public const string DefaultTimeZoneId = "Asia/Tehran";

    public static NotificationPolicyDecision Evaluate(NotificationPolicyContext context)
    {
        if (context.ExpiresAtUtc is not null && context.ExpiresAtUtc <= context.NowUtc)
            return Decide(NotificationPolicyAction.Expire, NotificationSuppressionReason.Expired,
                context.NowUtc, "The intent expired before delivery.");
        if (!context.Entitled)
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.EntitlementDenied,
                context.NowUtc, "The active plan does not permit Telegram notifications.");
        if (context.SymbolMuted)
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.SymbolMuted,
                context.NowUtc, "The canonical symbol is explicitly muted.");
        if (!context.CategoryEnabled)
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.CategoryMuted,
                context.NowUtc, "The event category is muted.");
        if (context.IntentSeverity < context.EffectiveMinimumSeverity)
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.BelowMinimumSeverity,
                context.NowUtc, "The event is below the effective minimum severity.");

        var critical = context.IntentSeverity == InsightSeverity.Critical;
        if (!critical && context.DeliveredToday >= context.Preference.DailyCap)
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.DailyCap,
                context.NowUtc, "The actor daily notification cap has been reached.");
        if (!critical && context.LastSimilarDeliveredAtUtc is not null &&
            context.NowUtc - context.LastSimilarDeliveredAtUtc < TimeSpan.FromMinutes(context.EffectiveCooldownMinutes))
            return Decide(NotificationPolicyAction.Suppress, NotificationSuppressionReason.Cooldown,
                context.NowUtc, "A similar notification was delivered inside the cooldown window.");
        if (!critical && context.IsQuietHours)
            return Decide(NotificationPolicyAction.Defer, NotificationSuppressionReason.QuietHours,
                context.QuietHoursEndUtc ?? context.NowUtc.AddHours(1),
                "Delivery is deferred until quiet hours end.");
        if (context.Preference.DeliveryMode == NotificationDeliveryMode.Digest && !context.AlreadyBatched)
            return Decide(NotificationPolicyAction.Batch, NotificationSuppressionReason.None,
                context.NextDigestUtc, "The intent is assigned to the next actor digest window.");

        return Decide(NotificationPolicyAction.Deliver, NotificationSuppressionReason.None,
            context.NowUtc, critical
                ? "Critical priority may bypass quiet hours, cooldown, and the daily cap; explicit mutes and entitlement still apply."
                : "The intent satisfies the effective notification policy.");
    }

    public static bool IsQuietHours(TimeOnly localTime, TimeOnly? start, TimeOnly? end)
    {
        if (start is null || end is null) return false;
        return start < end
            ? localTime >= start && localTime < end
            : localTime >= start || localTime < end;
    }

    private static NotificationPolicyDecision Decide(
        NotificationPolicyAction action,
        NotificationSuppressionReason reason,
        DateTimeOffset notBeforeUtc,
        string explanation) => new(action, reason, notBeforeUtc, Version, explanation);
}

public static class NotificationIntentLifecycle
{
    private static readonly IReadOnlyDictionary<NotificationIntentState, NotificationIntentState[]> Allowed =
        new Dictionary<NotificationIntentState, NotificationIntentState[]>
        {
            [NotificationIntentState.Pending] = [NotificationIntentState.Deferred, NotificationIntentState.Batched,
                NotificationIntentState.Sending, NotificationIntentState.Suppressed, NotificationIntentState.Expired,
                NotificationIntentState.Cancelled],
            [NotificationIntentState.Deferred] = [NotificationIntentState.Deferred, NotificationIntentState.Batched,
                NotificationIntentState.Sending, NotificationIntentState.Suppressed, NotificationIntentState.Expired,
                NotificationIntentState.Cancelled],
            [NotificationIntentState.Batched] = [NotificationIntentState.Sending, NotificationIntentState.Suppressed,
                NotificationIntentState.Expired, NotificationIntentState.Cancelled],
            [NotificationIntentState.Sending] = [NotificationIntentState.Deferred, NotificationIntentState.Batched,
                NotificationIntentState.Delivered, NotificationIntentState.Suppressed, NotificationIntentState.Expired,
                NotificationIntentState.FailedRetryable, NotificationIntentState.DeadLettered,
                NotificationIntentState.Cancelled],
            [NotificationIntentState.FailedRetryable] = [NotificationIntentState.Sending,
                NotificationIntentState.DeadLettered, NotificationIntentState.Expired, NotificationIntentState.Cancelled],
            [NotificationIntentState.Delivered] = [],
            [NotificationIntentState.Suppressed] = [],
            [NotificationIntentState.Expired] = [],
            [NotificationIntentState.DeadLettered] = [],
            [NotificationIntentState.Cancelled] = []
        };

    public static void EnsureTransition(NotificationIntentState current, NotificationIntentState next)
    {
        if (current == next && current == NotificationIntentState.Deferred) return;
        if (!Allowed[current].Contains(next))
            throw new NotificationValidationException($"Notification intent cannot transition from {current} to {next}.");
    }

    public static void EnsureManualRetry(NotificationIntentState current)
    {
        if (current != NotificationIntentState.DeadLettered)
            throw new NotificationValidationException("Only a dead-lettered notification can be retried manually.");
    }
}

public sealed class NotificationValidationException(string message) : InvalidOperationException(message);
