using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Refreshes TahlilApp entitlement for one local user when it is due (Design §11 triggers 2 and 3).
/// Shared by the background worker and the direct-login path, so the rules exist once.
/// Never throws for external failure: unavailable status keeps the last known plan within the stale grace
/// window and otherwise downgrades to Free. Logs outcome categories and the local user id only.
/// </summary>
public sealed class TahlilAppEntitlementRefresher(
    ITahlilAppIdentityGateway gateway,
    ITahlilAppEntitlementStateStore state,
    ITahlilAppSubscriptionSynchronizer subscriptions,
    IOptions<TahlilAppOptions> options,
    TimeProvider timeProvider,
    ILogger<TahlilAppEntitlementRefresher> logger) : ITahlilAppEntitlementRefresher
{
    private const string UnavailableDecisionReason = "unavailable-stale-grace-expired";

    public async Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var statusTtl = TimeSpan.FromMinutes(settings.Revalidation.StatusTtlMinutes);
        var staleGrace = TimeSpan.FromHours(settings.Revalidation.StaleGraceHours);

        var identity = await state.FindByLocalUserAsync(localUserId, cancellationToken);
        if (identity is null ||
            identity.TenantId != tenantId ||
            !string.Equals(identity.Status, TahlilAppIdentityStatus.Active, StringComparison.Ordinal))
        {
            return;
        }

        if (!TahlilAppEntitlementSchedule.IsDue(identity, now, statusTtl))
        {
            return;
        }

        var result = await gateway.GetStatusAsync(identity.ExternalUserId, Guid.NewGuid().ToString("N"), cancellationToken);
        switch (result.Outcome)
        {
            case TahlilAppStatusOutcome.Succeeded when result.Snapshot is { } snapshot:
                await ApplySnapshotAsync(tenantId, localUserId, snapshot, now, statusTtl, cancellationToken);
                return;

            case TahlilAppStatusOutcome.NotFound:
                await state.RecordIdentityStatusAsync(localUserId, TahlilAppIdentityStatus.Revoked, now, cancellationToken);
                logger.LogWarning("TahlilApp identity is no longer present upstream; identity revoked. LocalUserId: {LocalUserId}", localUserId);
                return;

            default:
                await ApplyUnavailableAsync(tenantId, localUserId, identity, now, statusTtl, staleGrace, cancellationToken);
                return;
        }
    }

    private async Task ApplySnapshotAsync(
        Guid tenantId,
        Guid localUserId,
        TahlilAppStatusSnapshot snapshot,
        DateTimeOffset now,
        TimeSpan statusTtl,
        CancellationToken cancellationToken)
    {
        if (snapshot.IsSuspended)
        {
            await state.RecordIdentityStatusAsync(localUserId, TahlilAppIdentityStatus.Suspended, now, cancellationToken);
            logger.LogWarning("TahlilApp identity is suspended; identity marked suspended. LocalUserId: {LocalUserId}", localUserId);
            return;
        }

        // Same policy as the launch path: unknown plan, inactive, expired or missing expiry all become Free (Design §11, D7).
        var decision = TahlilAppSubscriptionPolicy.Decide(
            snapshot.ExternalPlan,
            snapshot.IsSubscriptionActive,
            snapshot.SubscriptionExpiresAt,
            now,
            options.Value.PlanMap);

        await subscriptions.SynchronizeAsync(tenantId, localUserId, decision, snapshot.ExternalRevision, cancellationToken);
        await state.RecordSuccessAsync(
            localUserId,
            TahlilAppEntitlementSchedule.CreateSuccess(decision, snapshot.ExternalRevision, now, statusTtl),
            cancellationToken);

        logger.LogInformation(
            "TahlilApp entitlement refreshed. Decision: {Decision}; LocalUserId: {LocalUserId}",
            decision.Reason, localUserId);
    }

    private async Task ApplyUnavailableAsync(
        Guid tenantId,
        Guid localUserId,
        TahlilAppIdentitySyncState identity,
        DateTimeOffset now,
        TimeSpan statusTtl,
        TimeSpan staleGrace,
        CancellationToken cancellationToken)
    {
        var failure = TahlilAppEntitlementSchedule.CreateUnavailableFailure(identity, now, statusTtl, staleGrace);
        if (failure.DowngradedToFree)
        {
            await subscriptions.SynchronizeAsync(
                tenantId,
                localUserId,
                new TahlilAppSubscriptionDecision(TahlilAppEntitlementSchedule.FreePlanCode, null, UnavailableDecisionReason),
                externalRevision: null,
                cancellationToken);
        }

        await state.RecordFailureAsync(localUserId, failure, cancellationToken);
        logger.LogWarning(
            "TahlilApp status unavailable. Category: {Category}; Downgraded: {Downgraded}; LocalUserId: {LocalUserId}",
            failure.Category, failure.DowngradedToFree, localUserId);
    }
}
