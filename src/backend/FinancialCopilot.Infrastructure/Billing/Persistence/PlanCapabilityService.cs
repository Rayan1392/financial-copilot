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
        var planCode = await dbContext.CustomerAccounts
            .Where(row => row.Id == account.Id)
            .Select(row => row.SubscriptionPlanCode)
            .SingleOrDefaultAsync(cancellationToken);

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

    public async Task<decimal?> GetLimitAsync(
        CustomerAccount account,
        string capabilityCode,
        CancellationToken cancellationToken)
    {
        var planCode = await dbContext.CustomerAccounts
            .Where(row => row.Id == account.Id)
            .Select(row => row.SubscriptionPlanCode)
            .SingleOrDefaultAsync(cancellationToken);
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
