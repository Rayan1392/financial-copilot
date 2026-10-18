using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// EF-backed adapter for the entitlement sync state on <c>auth_external_identities</c> (Design §10).
/// Holds no policy: due times, grace and downgrade decisions arrive as values from the Application schedule.
/// Lease claims are conditional updates, so two workers cannot both win the same identity.
/// </summary>
public sealed class TahlilAppSyncStateStore(AuthDbContext dbContext) : ITahlilAppEntitlementStateStore
{
    public async Task<TahlilAppIdentitySyncState?> FindByLocalUserAsync(Guid localUserId, CancellationToken cancellationToken) =>
        await dbContext.TahlilAppExternalIdentities
            .AsNoTracking()
            .Where(row => row.Provider == ExternalUserId.Provider && row.LocalUserId == localUserId)
            .Select(row => new TahlilAppIdentitySyncState(
                row.TenantId,
                row.LocalUserId,
                row.ExternalUserId,
                row.Status,
                row.LastEntitlementSyncAtUtc,
                row.NextEntitlementSyncDueAtUtc,
                row.LastKnownSubscriptionExpiresAtUtc,
                row.LastSyncedPlanCode))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task RecordSuccessAsync(Guid localUserId, TahlilAppSyncSuccess success, CancellationToken cancellationToken)
    {
        var row = await FindTrackedAsync(localUserId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.LastEntitlementSyncAtUtc = success.SyncedAtUtc;
        row.NextEntitlementSyncDueAtUtc = success.NextDueAtUtc;
        row.LastKnownSubscriptionExpiresAtUtc = success.EffectiveTo;
        row.LastSyncedPlanCode = success.PlanCode;
        row.LastExternalRevision = success.ExternalRevision;
        row.SyncAttemptCount = 0;
        row.LastSyncFailureCategory = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordFailureAsync(Guid localUserId, TahlilAppSyncFailure failure, CancellationToken cancellationToken)
    {
        var row = await FindTrackedAsync(localUserId, cancellationToken);
        if (row is null)
        {
            return;
        }

        if (row.SyncAttemptCount < int.MaxValue)
        {
            row.SyncAttemptCount++;
        }

        row.LastSyncFailureCategory = failure.Category;
        row.NextEntitlementSyncDueAtUtc = failure.NextDueAtUtc;
        if (failure.DowngradedToFree)
        {
            // Paid access lapsed under the stale-grace rule: the stored state now reads as Free (Design §11).
            row.LastSyncedPlanCode = TahlilAppEntitlementSchedule.FreePlanCode;
            row.LastKnownSubscriptionExpiresAtUtc = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordIdentityStatusAsync(
        Guid localUserId,
        string status,
        DateTimeOffset checkedAtUtc,
        CancellationToken cancellationToken)
    {
        var row = await FindTrackedAsync(localUserId, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.Status = status;
        row.LastEntitlementSyncAtUtc = checkedAtUtc;
        row.NextEntitlementSyncDueAtUtc = null;
        row.SyncAttemptCount = 0;
        row.LastSyncFailureCategory = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TahlilAppDueIdentity>> ClaimDueAsync(
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Paid, Active identities only: Free users are synchronised lazily on launch and login (Design §11).
        var candidates = await dbContext.TahlilAppExternalIdentities
            .AsNoTracking()
            .Where(row =>
                row.Provider == ExternalUserId.Provider &&
                row.Status == TahlilAppIdentityStatus.Active &&
                row.NextEntitlementSyncDueAtUtc != null &&
                row.NextEntitlementSyncDueAtUtc <= now &&
                row.LastSyncedPlanCode != null &&
                row.LastSyncedPlanCode != TahlilAppEntitlementSchedule.FreePlanCode &&
                (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc < now))
            .OrderBy(row => row.NextEntitlementSyncDueAtUtc)
            .Take(batchSize)
            .Select(row => new { row.Id, row.TenantId, row.LocalUserId })
            .ToListAsync(cancellationToken);

        var claimed = new List<TahlilAppDueIdentity>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var updated = await dbContext.TahlilAppExternalIdentities
                .Where(row => row.Id == candidate.Id && (row.LeaseExpiresAtUtc == null || row.LeaseExpiresAtUtc < now))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.LeaseOwner, leaseOwner)
                        .SetProperty(row => row.LeaseExpiresAtUtc, leaseExpiresAt),
                    cancellationToken);
            if (updated == 1)
            {
                claimed.Add(new TahlilAppDueIdentity(candidate.TenantId, candidate.LocalUserId));
            }
        }

        return claimed;
    }

    public async Task ExtendLeaseAsync(Guid localUserId, string leaseOwner, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
        await dbContext.TahlilAppExternalIdentities
            .Where(row => row.LocalUserId == localUserId && row.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.LeaseExpiresAtUtc, leaseExpiresAt),
                cancellationToken);

    public async Task ReleaseLeaseAsync(Guid localUserId, string leaseOwner, CancellationToken cancellationToken) =>
        await dbContext.TahlilAppExternalIdentities
            .Where(row => row.LocalUserId == localUserId && row.LeaseOwner == leaseOwner)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.LeaseOwner, (string?)null)
                    .SetProperty(row => row.LeaseExpiresAtUtc, (DateTimeOffset?)null),
                cancellationToken);

    private Task<TahlilAppExternalIdentityRow?> FindTrackedAsync(Guid localUserId, CancellationToken cancellationToken) =>
        dbContext.TahlilAppExternalIdentities.SingleOrDefaultAsync(
            row => row.Provider == ExternalUserId.Provider && row.LocalUserId == localUserId,
            cancellationToken);
}
