using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Application.Authentication.TahlilApp;

/// <summary>
/// Orchestrates one TahlilApp launch (Design §7, §8, §10, §11, §12).
/// Contains no HTTP, EF or ASP.NET types. Every external dependency is a port.
/// </summary>
public sealed class TahlilAppLaunchUseCase(
    ITahlilAppIdentityGateway gateway,
    ITahlilAppLaunchAttemptStore attempts,
    ITahlilAppAccountProvisioner provisioner,
    ITahlilAppSubscriptionSynchronizer subscriptions,
    ITahlilAppSyncStateRecorder syncState,
    ITahlilAppSessionIssuer sessions,
    TahlilAppLaunchPolicy policy,
    TimeProvider timeProvider)
{
    public async Task<TahlilAppLaunchResult> ExecuteAsync(
        TahlilAppLaunchRequest request,
        CancellationToken cancellationToken)
    {
        if (!policy.Enabled)
        {
            return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Disabled);
        }

        // Malformed codes never reach the replay guard or TahlilApp (Design §8: opaque URL-safe base64).
        if (!IsWellFormedCode(request.Code))
        {
            return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Invalid);
        }

        // Local replay guard: a duplicate never reaches TahlilApp (AC-82).
        if (!await attempts.TryBeginAttemptAsync(request.Code, request.CorrelationId, cancellationToken))
        {
            return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Invalid);
        }

        try
        {
            var exchange = await gateway.ExchangeAsync(request.Code, request.CorrelationId, cancellationToken);
            if (exchange.Outcome == TahlilAppExchangeOutcome.InvalidCode)
            {
                await attempts.CompleteAttemptAsync(request.Code, "invalid", cancellationToken);
                return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Invalid);
            }

            if (exchange.Outcome != TahlilAppExchangeOutcome.Succeeded || exchange.Snapshot is null)
            {
                await attempts.CompleteAttemptAsync(request.Code, "unavailable", cancellationToken);
                return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Unavailable);
            }

            var snapshot = exchange.Snapshot;
            if (snapshot.IsSuspended)
            {
                await attempts.CompleteAttemptAsync(request.Code, "account-disabled", cancellationToken);
                return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.AccountDisabled);
            }

            var now = timeProvider.GetUtcNow();
            var provisioned = await provisioner.ResolveAsync(snapshot, now, cancellationToken);
            if (provisioned.Outcome == TahlilAppProvisioningOutcome.AccountDisabled)
            {
                await attempts.CompleteAttemptAsync(request.Code, "account-disabled", cancellationToken);
                return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.AccountDisabled);
            }

            // Plan is decided by policy from the external subscription; it is never read from the browser (AC-09).
            var decision = TahlilAppSubscriptionPolicy.Decide(
                snapshot.ExternalPlan,
                snapshot.IsSubscriptionActive,
                snapshot.SubscriptionEffectiveFrom,
                snapshot.SubscriptionExpiresAt,
                now,
                policy.PlanMap);
            await subscriptions.SynchronizeAsync(
                provisioned.TenantId,
                provisioned.LocalUserId,
                decision,
                snapshot.ExternalRevision,
                cancellationToken);

            // Design §11 trigger 1: the launch exchange is authoritative; record the sync so the due time is known.
            await syncState.RecordSuccessAsync(
                provisioned.LocalUserId,
                TahlilAppEntitlementSchedule.CreateSuccess(
                    decision,
                    snapshot.ExternalRevision,
                    now,
                    policy.StatusTtl ?? TahlilAppEntitlementSchedule.DefaultStatusTtl),
                cancellationToken);

            var session = await sessions.IssueAsync(
                provisioned.LocalUserId,
                provisioned.TenantId,
                request.PresentedRefreshToken,
                cancellationToken);

            await attempts.CompleteAttemptAsync(request.Code, "succeeded", cancellationToken);
            return new TahlilAppLaunchResult(TahlilAppLaunchOutcome.Succeeded, session);
        }
        catch
        {
            // Persist the failure class for the replay guard, then let the host report the fault (no PII, no code).
            await attempts.CompleteAttemptAsync(request.Code, "error", CancellationToken.None);
            throw;
        }
    }

    private static bool IsWellFormedCode(string? code) =>
        !string.IsNullOrEmpty(code) &&
        code.Length is >= 16 and <= 256 &&
        code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
