using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class OwnedIdentityBillingProvisioner(
    BillingDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string DefaultPlanCode = "Free";

    public async Task EnsureProvisionedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var plan = await dbContext.SubscriptionPlans
            .SingleOrDefaultAsync(row => row.Code == DefaultPlanCode, cancellationToken) ??
            throw new InvalidOperationException(
                $"Billing subscription plan '{DefaultPlanCode}' is not configured.");
        var now = timeProvider.GetUtcNow();
        var account = await dbContext.CustomerAccounts
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.UserId == userId,
                cancellationToken);

        if (account is null)
        {
            account = new CustomerAccountRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                AccountType = nameof(CustomerAccountType.Individual),
                BillingMode = nameof(BillingMode.Prepaid),
                SubscriptionPlanCode = plan.Code,
                SubscriptionEffectiveFrom = now
            };
            dbContext.CustomerAccounts.Add(account);
        }
        else
        {
            if (account.AccountType != nameof(CustomerAccountType.Individual) ||
                account.BillingMode != nameof(BillingMode.Prepaid))
            {
                throw new InvalidOperationException(
                    "The owned web user billing account must be an individual prepaid account.");
            }

            if (string.IsNullOrWhiteSpace(account.SubscriptionPlanCode))
            {
                account.SubscriptionPlanCode = plan.Code;
                account.SubscriptionEffectiveFrom = now;
                account.SubscriptionEffectiveTo = null;
                account.SubscriptionRevision++;
            }
        }

        if (!await dbContext.WalletProjections
                .AnyAsync(row => row.CustomerAccountId == account.Id, cancellationToken))
        {
            dbContext.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = account.Id,
                Balance = plan.IncludedCredits,
                ReservedAmount = 0,
                UpdatedAt = now
            });
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (!await IsFullyProvisionedAsync(tenantId, userId, cancellationToken))
            {
                throw;
            }
        }
    }

    private async Task<bool> IsFullyProvisionedAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var account = await dbContext.CustomerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.UserId == userId,
                cancellationToken);
        return account is not null &&
            account.AccountType == nameof(CustomerAccountType.Individual) &&
            account.BillingMode == nameof(BillingMode.Prepaid) &&
            !string.IsNullOrWhiteSpace(account.SubscriptionPlanCode) &&
            await dbContext.WalletProjections
                .AsNoTracking()
                .AnyAsync(row => row.CustomerAccountId == account.Id, cancellationToken);
    }
}
