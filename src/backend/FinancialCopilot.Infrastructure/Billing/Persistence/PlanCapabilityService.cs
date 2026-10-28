using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class PlanCapabilityService(BillingDbContext dbContext) : IPlanCapabilityService
{
    public async Task ValidateCanExecuteAsync(
        CustomerAccount account,
        string operationCode,
        CancellationToken cancellationToken)
    {
        var planCode = await ReadEffectivePlanCodeAsync(account.Id, cancellationToken);

        // Existing organization integrations without commercial plan assignment retain their
        // current policy until subscription administration assigns a plan.
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return;
        }

        var capability = await dbContext.PlanCapabilities
            .Where(row =>
                row.PlanCode == planCode &&
                row.CapabilityCode == operationCode &&
                row.IsEnabled)
            .OrderByDescending(row => row.PolicyVersion)
            .FirstOrDefaultAsync(cancellationToken);
        if (capability is null)
        {
            throw new InvalidOperationException("The requested operation is not included in the active subscription plan.");
        }

        if (capability.Limit is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var usageCount = await dbContext.UsageLedgerEntries.CountAsync(
            row =>
                row.CustomerAccountId == account.Id &&
                row.OperationCode == operationCode &&
                row.OccurredAt >= windowStart,
            cancellationToken);
        if (usageCount >= capability.Limit)
        {
            throw new InvalidOperationException("The active subscription plan quota is exhausted for the requested operation.");
        }
    }

    /// <summary>
    /// Stored plan code after the expiry guard (Design §11, AC-35). A lapsed paid window reads as Free.
    /// </summary>
    private async Task<string?> ReadEffectivePlanCodeAsync(Guid customerAccountId, CancellationToken cancellationToken)
    {
        var external = await dbContext.CustomerAccounts.AsNoTracking()
            .Where(row => row.Id == customerAccountId)
            .Select(row => row.ExternalSubscriptionManaged)
            .SingleOrDefaultAsync(cancellationToken);
        if (external)
        {
            var now = DateTimeOffset.UtcNow;
            var active = await dbContext.ExternalPlanSubscriptions.AsNoTracking()
                .Where(row => row.CustomerAccountId == customerAccountId && row.StartsAtUtc <= now && row.ExpiresAtUtc > now)
                .Select(row => row.PlanCode)
                .ToListAsync(cancellationToken);
            return active.Contains("Pro", StringComparer.Ordinal) ? "Pro" :
                active.Contains("Plus", StringComparer.Ordinal) ? "Plus" : "Free";
        }

        var subscription = await dbContext.CustomerAccounts
            .Where(row => row.Id == customerAccountId)
            .Select(row => new { row.SubscriptionPlanCode, row.SubscriptionEffectiveTo })
            .SingleOrDefaultAsync(cancellationToken);
        return subscription is null
            ? null
            : SubscriptionEffectiveness.ResolveEffectivePlanCode(
                subscription.SubscriptionPlanCode,
                subscription.SubscriptionEffectiveTo,
                DateTimeOffset.UtcNow);
    }

    public async Task<decimal?> GetLimitAsync(
        CustomerAccount account,
        string capabilityCode,
        CancellationToken cancellationToken)
    {
        var planCode = await ReadEffectivePlanCodeAsync(account.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(planCode))
        {
            return null;
        }

        return await dbContext.PlanCapabilities
            .Where(row =>
                row.PlanCode == planCode &&
                row.CapabilityCode == capabilityCode &&
                row.IsEnabled)
            .OrderByDescending(row => row.PolicyVersion)
            .Select(row => row.Limit)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
