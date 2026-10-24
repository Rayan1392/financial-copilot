using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

/// <summary>
/// Writes the TahlilApp-sourced plan and expiry into the Billing customer account (Design §11, §14).
/// Idempotent: an identical plan and window is a no-op (no revision bump, no audit row). Grants no credits:
/// included credits are seeded once at wallet creation and top-ups go through CreditAdjustmentService.
/// </summary>
public sealed class ExternalSubscriptionSynchronizer(
    BillingDbContext dbContext,
    OwnedIdentityBillingProvisioner billingProvisioner,
    TimeProvider timeProvider) : ITahlilAppSubscriptionSynchronizer
{
    public const string AuditActionCode = "billing.subscription.synced";
    private const string CorrelationIdentifier = "tahlilapp-sync";
    private readonly SubscriptionCreditLifecycleService creditLifecycle = new(dbContext);

    public async Task SynchronizeAsync(
        Guid tenantId,
        Guid localUserId,
        TahlilAppSubscriptionDecision decision,
        long? externalRevision,
        CancellationToken cancellationToken)
    {
        await billingProvisioner.EnsureProvisionedAsync(tenantId, localUserId, cancellationToken);

        if (decision.PlanCode == "Plus" && decision.EffectiveTo is not null)
        {
            var current = await dbContext.CustomerAccounts.SingleAsync(
                row => row.TenantId == tenantId && row.UserId == localUserId,
                cancellationToken);
            if (current.SubscriptionPlanCode == "Pro")
            {
                decision = new TahlilAppSubscriptionDecision("Free", null, "unsupported-pro-downgrade");
            }
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var account = await dbContext.CustomerAccounts.SingleAsync(
                row => row.TenantId == tenantId && row.UserId == localUserId,
                cancellationToken);

            var effectiveFrom = decision.EffectiveFrom;
            if (effectiveFrom is null && account.SubscriptionActivationIsAuthoritative &&
                string.Equals(account.SubscriptionPlanCode, decision.PlanCode, StringComparison.Ordinal) &&
                SameInstant(account.SubscriptionEffectiveTo, decision.EffectiveTo))
            {
                // Legacy responses may omit the new field. Preserve a previously verified anchor
                // for the same subscription; never derive one from SapioAI observation time.
                effectiveFrom = account.SubscriptionEffectiveFrom;
            }

            if (externalRevision is { } incomingRevision && account.ExternalSubscriptionRevision is { } storedRevision &&
                incomingRevision <= storedRevision &&
                (incomingRevision < storedRevision ||
                 !string.Equals(account.SubscriptionPlanCode, decision.PlanCode, StringComparison.Ordinal) ||
                 !SameInstant(account.SubscriptionEffectiveTo, decision.EffectiveTo) ||
                 !SameInstant(account.SubscriptionEffectiveFrom, effectiveFrom)))
            {
                return;
            }

            if (string.Equals(account.SubscriptionPlanCode, decision.PlanCode, StringComparison.Ordinal) &&
                SameInstant(account.SubscriptionEffectiveTo, decision.EffectiveTo) &&
                SameInstant(account.SubscriptionEffectiveFrom, effectiveFrom))
            {
                if (externalRevision is { } latestRevision &&
                    (account.ExternalSubscriptionRevision is null || latestRevision > account.ExternalSubscriptionRevision))
                {
                    account.ExternalSubscriptionRevision = latestRevision;
                    account.SubscriptionRevision++;
                    try
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException) when (attempt == 0)
                    {
                        dbContext.ChangeTracker.Clear();
                        continue;
                    }
                }

                return;
            }

            var now = timeProvider.GetUtcNow();
            var previousPlanCode = account.SubscriptionPlanCode ?? "Free";
            string? creditLifecycleNote = null;
            if (!string.Equals(account.SubscriptionPlanCode, decision.PlanCode, StringComparison.Ordinal) ||
                !SameInstant(account.SubscriptionEffectiveFrom, effectiveFrom))
            {
                var wallet = await dbContext.WalletProjections.SingleOrDefaultAsync(
                    row => row.CustomerAccountId == account.Id,
                    cancellationToken) ??
                    throw new KeyNotFoundException("Wallet projection is not configured.");
                creditLifecycleNote = await creditLifecycle.ApplyPlanChangeAsync(
                    account,
                    wallet,
                    previousPlanCode,
                    decision.PlanCode,
                    now,
                    cancellationToken,
                    effectiveFrom);
            }

            account.SubscriptionPlanCode = decision.PlanCode;
            account.SubscriptionEffectiveFrom = effectiveFrom;
            account.SubscriptionEffectiveTo = decision.EffectiveTo;
            account.SubscriptionActivationIsAuthoritative = decision.PlanCode == "Free" ||
                effectiveFrom is not null && creditLifecycleNote is null &&
                (decision.EffectiveFrom is not null || account.SubscriptionActivationIsAuthoritative);
            account.SubscriptionRevision++;
            if (externalRevision is not null)
            {
                account.ExternalSubscriptionRevision = externalRevision;
            }

            dbContext.AdminAudits.Add(new BillingAdminAuditRow
            {
                Id = Guid.NewGuid(),
                OccurredAt = now,
                TenantId = tenantId,
                ActorId = Guid.Empty,
                ActionCode = AuditActionCode,
                TargetType = nameof(CustomerAccountRow),
                TargetId = account.Id.ToString(),
                Reason = BuildReason(decision.Reason, externalRevision, creditLifecycleNote),
                CorrelationId = CorrelationIdentifier,
                Before = previousPlanCode,
                After = decision.PlanCode
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // A concurrent worker or unique idempotency key won the transition; re-read and decide again.
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("TahlilApp subscription synchronisation did not converge.");
    }

    private static bool SameInstant(DateTimeOffset? stored, DateTimeOffset? incoming)
    {
        if (stored is null || incoming is null)
        {
            return stored is null && incoming is null;
        }

        // Storage and the exchange both carry second precision; a sub-second difference is the same window.
        return Math.Abs((stored.Value - incoming.Value).TotalSeconds) < 1;
    }

    private static string BuildReason(string decisionReason, long? externalRevision, string? creditLifecycleNote)
    {
        var reason = externalRevision is { } revision
            ? $"TahlilApp sync ({decisionReason}); external revision {revision}."
            : $"TahlilApp sync ({decisionReason}).";
        return string.IsNullOrWhiteSpace(creditLifecycleNote)
            ? reason
            : $"{reason} {creditLifecycleNote}";
    }
}
