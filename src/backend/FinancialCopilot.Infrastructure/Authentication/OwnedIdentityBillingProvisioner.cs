using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class OwnedIdentityBillingProvisioner(
    BillingDbContext dbContext,
    TimeProvider timeProvider)
{
    private const string DefaultPlanCode = "Free";
    private readonly SubscriptionCreditLifecycleService creditLifecycle = new(dbContext);

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

        var wallet = await dbContext.WalletProjections
            .SingleOrDefaultAsync(row => row.CustomerAccountId == account.Id, cancellationToken);
        var walletCreated = wallet is null;
        if (wallet is null)
        {
            wallet = new WalletProjectionRow
            {
                CustomerAccountId = account.Id,
                Balance = 0,
                ReservedAmount = 0,
                UpdatedAt = now
            };
            dbContext.WalletProjections.Add(wallet);
            await creditLifecycle.InitializeNewWalletAsync(account, wallet, now, cancellationToken);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (!await IsFullyProvisionedAsync(tenantId, userId, walletCreated, cancellationToken))
            {
                throw;
            }
        }
    }

    private async Task<bool> IsFullyProvisionedAsync(
        Guid tenantId,
        Guid userId,
        bool requireCreditPeriod,
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
                .AnyAsync(row => row.CustomerAccountId == account.Id, cancellationToken) &&
            (!requireCreditPeriod || await dbContext.SubscriptionCreditPeriods
                .AsNoTracking()
                .AnyAsync(row => row.CustomerAccountId == account.Id, cancellationToken));
    }
}
