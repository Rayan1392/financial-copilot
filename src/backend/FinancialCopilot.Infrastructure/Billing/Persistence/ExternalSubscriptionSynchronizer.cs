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

    public async Task SynchronizeAsync(
        Guid tenantId,
        Guid localUserId,
        TahlilAppSubscriptionDecision decision,
        long? externalRevision,
        CancellationToken cancellationToken)
    {
        await billingProvisioner.EnsureProvisionedAsync(tenantId, localUserId, cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var account = await dbContext.CustomerAccounts.SingleAsync(
                row => row.TenantId == tenantId && row.UserId == localUserId,
                cancellationToken);

            if (string.Equals(account.SubscriptionPlanCode, decision.PlanCode, StringComparison.Ordinal) &&
                SameInstant(account.SubscriptionEffectiveTo, decision.EffectiveTo))
            {
                return;
            }

            var now = timeProvider.GetUtcNow();
            var previousPlanCode = account.SubscriptionPlanCode;
            if (!string.Equals(previousPlanCode, decision.PlanCode, StringComparison.Ordinal))
            {
                account.SubscriptionEffectiveFrom = now;
            }

            account.SubscriptionPlanCode = decision.PlanCode;
            account.SubscriptionEffectiveTo = decision.EffectiveTo;
            account.SubscriptionRevision++;

            dbContext.AdminAudits.Add(new BillingAdminAuditRow
            {
                Id = Guid.NewGuid(),
                OccurredAt = now,
                TenantId = tenantId,
                ActorId = Guid.Empty,
                ActionCode = AuditActionCode,
                TargetType = nameof(CustomerAccountRow),
                TargetId = account.Id.ToString(),
                Reason = BuildReason(decision.Reason, externalRevision),
                CorrelationId = CorrelationIdentifier,
                Before = previousPlanCode,
                After = decision.PlanCode
            });

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // A concurrent subscription change won the revision check; re-read and decide again.
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

    private static string BuildReason(string decisionReason, long? externalRevision) =>
        externalRevision is { } revision
            ? $"TahlilApp sync ({decisionReason}); external revision {revision}."
            : $"TahlilApp sync ({decisionReason}).";
}
