using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Application.Authentication.TahlilApp;

// Entitlement sync state (Design §10 sync fields, §11 refresh triggers and stale-grace policy).
// Ports are owned here; the EF-backed adapter lives in Infrastructure.

/// <summary>Local identity state needed to decide a refresh. Contains no profile PII.</summary>
public sealed record TahlilAppIdentitySyncState(
    Guid TenantId,
    Guid LocalUserId,
    string ExternalUserId,
    string Status,
    DateTimeOffset? LastEntitlementSyncAtUtc,
    DateTimeOffset? NextEntitlementSyncDueAtUtc,
    DateTimeOffset? LastKnownSubscriptionExpiresAtUtc,
    string? LastSyncedPlanCode);

/// <summary>A successful synchronisation, as recorded after the Billing synchroniser has run.</summary>
public sealed record TahlilAppSyncSuccess(
    DateTimeOffset SyncedAtUtc,
    string PlanCode,
    DateTimeOffset? EffectiveTo,
    long? ExternalRevision,
    DateTimeOffset? NextDueAtUtc);

/// <summary>A failed status lookup. <see cref="DowngradedToFree"/> is true when the stale-grace rule removed paid access.</summary>
public sealed record TahlilAppSyncFailure(
    DateTimeOffset AttemptedAtUtc,
    string Category,
    DateTimeOffset? NextDueAtUtc,
    bool DowngradedToFree);

public sealed record TahlilAppDueIdentity(Guid TenantId, Guid LocalUserId);

public static class TahlilAppIdentityStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
}

/// <summary>Records a completed synchronisation. Used by the launch path (Design §11 trigger 1).</summary>
public interface ITahlilAppSyncStateRecorder
{
    Task RecordSuccessAsync(Guid localUserId, TahlilAppSyncSuccess success, CancellationToken cancellationToken);
}

/// <summary>Read, record and lease operations for the refresher and the revalidation worker.</summary>
public interface ITahlilAppEntitlementStateStore : ITahlilAppSyncStateRecorder
{
    Task<TahlilAppIdentitySyncState?> FindByLocalUserAsync(Guid localUserId, CancellationToken cancellationToken);

    Task RecordFailureAsync(Guid localUserId, TahlilAppSyncFailure failure, CancellationToken cancellationToken);

    /// <summary>Sets Suspended or Revoked and records the check time. Clears the next due time.</summary>
    Task RecordIdentityStatusAsync(Guid localUserId, string status, DateTimeOffset checkedAtUtc, CancellationToken cancellationToken);

    /// <summary>Claims up to <paramref name="batchSize"/> due, paid, Active identities whose lease is absent or expired.</summary>
    Task<IReadOnlyList<TahlilAppDueIdentity>> ClaimDueAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        int batchSize,
        CancellationToken cancellationToken);

    Task ExtendLeaseAsync(Guid localUserId, string leaseOwner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken);

    Task ReleaseLeaseAsync(Guid localUserId, string leaseOwner, CancellationToken cancellationToken);
}

/// <summary>Pure entitlement schedule and stale-grace rules (Design §11). No I/O.</summary>
public static class TahlilAppEntitlementSchedule
{
    public static readonly TimeSpan DefaultStatusTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultStaleGrace = TimeSpan.FromHours(6);

    public const string FreePlanCode = "Free";
    public const string UnavailableCategory = "unavailable";

    public static TahlilAppSyncSuccess CreateSuccess(
        TahlilAppSubscriptionDecision decision,
        long? externalRevision,
        DateTimeOffset now,
        TimeSpan statusTtl)
    {
        // Paid: due at min(now + TTL, expiry). Free has no expiry and therefore no background due time (Design §11).
        DateTimeOffset? nextDue = decision.EffectiveTo is { } expiry
            ? Min(now + statusTtl, expiry)
            : null;
        return new TahlilAppSyncSuccess(now, decision.PlanCode, decision.EffectiveTo, externalRevision, nextDue);
    }

    public static bool IsPaid(string? planCode) =>
        !string.IsNullOrWhiteSpace(planCode) && !string.Equals(planCode, FreePlanCode, StringComparison.Ordinal);

    /// <summary>
    /// Due when the stored next due time has passed. Without one (Free, or never synchronised), due when the
    /// status TTL has elapsed since the last successful sync.
    /// </summary>
    public static bool IsDue(TahlilAppIdentitySyncState state, DateTimeOffset now, TimeSpan statusTtl)
    {
        if (state.NextEntitlementSyncDueAtUtc is { } nextDue)
        {
            return now >= nextDue;
        }

        return state.LastEntitlementSyncAtUtc is not { } lastSync || now >= lastSync + statusTtl;
    }

    /// <summary>The instant after which a last-known paid plan must not be kept: min(known expiry, last sync + grace).</summary>
    public static DateTimeOffset? LapseAt(TahlilAppIdentitySyncState state, TimeSpan staleGrace)
    {
        if (state.LastKnownSubscriptionExpiresAtUtc is not { } expiry || state.LastEntitlementSyncAtUtc is not { } lastSync)
        {
            return null;
        }

        return Min(expiry, lastSync + staleGrace);
    }

    /// <summary>Stale-grace rule: keep the last known paid plan only while now is before the lapse instant.</summary>
    public static bool CanKeepLastKnownPlan(TahlilAppIdentitySyncState state, DateTimeOffset now, TimeSpan staleGrace) =>
        IsPaid(state.LastSyncedPlanCode) &&
        LapseAt(state, staleGrace) is { } lapse &&
        now < lapse;

    /// <summary>Failure record for an unavailable status call. Fails closed to Free once the grace window has passed.</summary>
    public static TahlilAppSyncFailure CreateUnavailableFailure(
        TahlilAppIdentitySyncState state,
        DateTimeOffset now,
        TimeSpan statusTtl,
        TimeSpan staleGrace)
    {
        if (CanKeepLastKnownPlan(state, now, staleGrace))
        {
            var lapse = LapseAt(state, staleGrace)!.Value;
            return new TahlilAppSyncFailure(now, UnavailableCategory, Min(now + statusTtl, lapse), DowngradedToFree: false);
        }

        return new TahlilAppSyncFailure(now, UnavailableCategory, now + statusTtl, DowngradedToFree: true);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
}
