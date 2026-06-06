using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class CustomerAccountRepository(BillingDbContext dbContext) : ICustomerAccountRepository
{
    public async Task<CustomerAccount?> FindAsync(Guid customerAccountId, CancellationToken cancellationToken) =>
        Map(await dbContext.CustomerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == customerAccountId, cancellationToken));

    public async Task<CustomerAccount?> FindOrganizationByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        Map(await dbContext.CustomerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId &&
                       row.AccountType == nameof(CustomerAccountType.Organization),
                cancellationToken));

    public async Task<CustomerAccount?> FindIndividualByUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken) =>
        Map(await dbContext.CustomerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.UserId == userId &&
                       row.AccountType == nameof(CustomerAccountType.Individual),
                cancellationToken));

    private static CustomerAccount? Map(CustomerAccountRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var creditLine = row.CreditLineApprovedLimit.HasValue
            ? new CreditLine(
                row.CreditLineApprovedLimit.Value,
                row.CreditLineWarningThreshold ?? 0)
            : null;

        return new CustomerAccount(
            row.Id,
            row.TenantId,
            Enum.Parse<CustomerAccountType>(row.AccountType),
            Enum.Parse<BillingMode>(row.BillingMode),
            creditLine);
    }
}

public sealed class WalletProjectionRepository(BillingDbContext dbContext) : IWalletProjectionRepository
{
    public async Task<WalletSnapshot> GetSnapshotAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.WalletProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CustomerAccountId == customerAccountId,
                cancellationToken);

        return row is null
            ? throw new KeyNotFoundException("Wallet projection is not configured.")
            : new WalletSnapshot(
                row.CustomerAccountId,
                row.Balance,
                row.ReservedAmount,
                row.UpdatedAt,
                row.Revision);
    }

    public async Task SaveAsync(WalletSnapshot snapshot, CancellationToken cancellationToken)
    {
        var row = await dbContext.WalletProjections
            .SingleOrDefaultAsync(
                candidate => candidate.CustomerAccountId == snapshot.CustomerAccountId,
                cancellationToken);

        if (row is null)
        {
            if (snapshot.Revision != 0)
            {
                throw new InvalidOperationException(
                    "A new wallet projection must begin at revision zero.");
            }

            dbContext.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = snapshot.CustomerAccountId,
                Balance = snapshot.Balance,
                ReservedAmount = snapshot.ReservedAmount,
                UpdatedAt = snapshot.UpdatedAt,
                Revision = snapshot.Revision
            });
        }
        else
        {
            if (snapshot.Revision != row.Revision + 1)
            {
                throw new InvalidOperationException(
                    "Wallet projection write rejected because the supplied snapshot is stale.");
            }

            row.Balance = snapshot.Balance;
            row.ReservedAmount = snapshot.ReservedAmount;
            row.UpdatedAt = snapshot.UpdatedAt;
            row.Revision = snapshot.Revision;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class UsageReservationRepository(BillingDbContext dbContext) : IUsageReservationRepository
{
    public async Task<UsageReservation?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.UsageReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == idempotencyKey, cancellationToken);

        return row is null
            ? null
            : Map(row);
    }

    public async Task<IReadOnlyCollection<UsageReservation>> FindExpiredReservedAsync(
        DateTimeOffset asOf,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return (await dbContext.UsageReservations
                .AsNoTracking()
                .Where(row =>
                    row.Status == nameof(UsageReservationStatus.Reserved) &&
                    row.ExpiresAt <= asOf)
                .OrderBy(row => row.ExpiresAt)
                .Take(maximumCount)
                .ToListAsync(cancellationToken))
            .Select(Map)
            .ToArray();
    }

    public async Task SaveAsync(UsageReservation reservation, CancellationToken cancellationToken)
    {
        var row = await dbContext.UsageReservations.SingleOrDefaultAsync(
            candidate => candidate.Id == reservation.Id,
            cancellationToken);

        if (row is null)
        {
            row = new UsageReservationRow
            {
                Id = reservation.Id,
                CustomerAccountId = reservation.CustomerAccountId,
                IdempotencyKey = reservation.IdempotencyKey,
                OperationCode = reservation.OperationCode,
                ReservedCredits = reservation.ReservedCredits,
                CreatedAt = reservation.CreatedAt,
                ExpiresAt = reservation.ExpiresAt
            };
            dbContext.UsageReservations.Add(row);
        }

        row.Status = reservation.Status.ToString();
        row.CommittedCredits = reservation.CommittedCredits;
        row.FinalizationReason = reservation.FinalizationReason;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static UsageReservation Map(UsageReservationRow row) =>
        UsageReservation.Restore(
            row.Id,
            row.CustomerAccountId,
            row.IdempotencyKey,
            row.OperationCode,
            row.ReservedCredits,
            row.CommittedCredits,
            row.CreatedAt,
            row.ExpiresAt,
            Enum.Parse<UsageReservationStatus>(row.Status),
            row.FinalizationReason);
}

public sealed class UsageLedgerRepository(BillingDbContext dbContext) :
    IUsageLedgerRepository,
    IFinancialTransactionRepository
{
    public async Task<UsageLedgerEntry?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == idempotencyKey, cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task AppendAsync(UsageLedgerEntry entry, CancellationToken cancellationToken)
    {
        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = entry.Id,
            CustomerAccountId = entry.CustomerAccountId,
            ActorId = entry.ActorId,
            TenantId = entry.TenantId,
            ApiClientId = entry.ApiClientId,
            EntryType = entry.EntryType.ToString(),
            OperationCode = entry.OperationCode,
            CreditsCharged = entry.CreditsCharged,
            PricingPolicyVersion = entry.PricingPolicyVersion,
            IdempotencyKey = entry.IdempotencyKey,
            OccurredAt = entry.OccurredAt,
            ExternalUserId = entry.ExternalUserId,
            AuditDescription = entry.AuditDescription,
            RelatedEntryId = entry.RelatedEntryId,
            CompletionStatus = entry.CompletionStatus,
            ProviderName = entry.ProviderName,
            ModelName = entry.ModelName,
            PromptTokens = entry.PromptTokens,
            CompletionTokens = entry.CompletionTokens,
            TotalTokens = entry.TotalTokens,
            EstimatedCost = entry.EstimatedCost
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<UsageLedgerEntry>> QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        (await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == customerAccountId &&
                row.OccurredAt >= from &&
                row.OccurredAt <= to)
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    public async Task<IReadOnlyCollection<UsageLedgerEntry>> QueryForApiClientAsync(
        Guid customerAccountId,
        Guid apiClientId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        (await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == customerAccountId &&
                row.ApiClientId == apiClientId &&
                row.OccurredAt >= from &&
                row.OccurredAt <= to)
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    public async Task AppendAsync(FinancialTransaction transaction, CancellationToken cancellationToken)
    {
        dbContext.FinancialTransactions.Add(new FinancialTransactionRow
        {
            Id = transaction.Id,
            CustomerAccountId = transaction.CustomerAccountId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            IdempotencyKey = transaction.IdempotencyKey,
            OccurredAt = transaction.OccurredAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    async Task<FinancialTransaction?> IFinancialTransactionRepository.FindByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.FinancialTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKey == idempotencyKey, cancellationToken);

        return row is null ? null : Map(row);
    }

    async Task<IReadOnlyCollection<FinancialTransaction>> IFinancialTransactionRepository.QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        (await dbContext.FinancialTransactions
            .AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == customerAccountId &&
                row.OccurredAt >= from &&
                row.OccurredAt <= to)
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();

    private static UsageLedgerEntry Map(UsageLedgerEntryRow row) =>
        new(
            row.Id,
            row.CustomerAccountId,
            row.ActorId,
            row.TenantId,
            row.ApiClientId,
            Enum.Parse<UsageLedgerEntryType>(row.EntryType),
            row.OperationCode,
            row.CreditsCharged,
            row.PricingPolicyVersion,
            row.IdempotencyKey,
            row.OccurredAt,
            row.ExternalUserId,
            row.AuditDescription,
            row.RelatedEntryId,
            row.CompletionStatus,
            row.ProviderName,
            row.ModelName,
            row.PromptTokens,
            row.CompletionTokens,
            row.TotalTokens,
            row.EstimatedCost);

    private static FinancialTransaction Map(FinancialTransactionRow row) =>
        new(
            row.Id,
            row.CustomerAccountId,
            Enum.Parse<FinancialTransactionType>(row.Type),
            row.Amount,
            row.Currency,
            row.IdempotencyKey,
            row.OccurredAt);
}

public sealed class InvoiceAccountRepository(BillingDbContext dbContext) : IInvoiceAccountRepository
{
    public async Task<InvoiceAccount?> FindAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.InvoiceAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CustomerAccountId == customerAccountId,
                cancellationToken);

        return row is null
            ? null
            : new InvoiceAccount(
                row.CustomerAccountId,
                row.LegalName,
                row.BillingEmail,
                row.SettlementTerms);
    }
}

public sealed class SubscriptionPlanRepository(BillingDbContext dbContext) : ISubscriptionPlanRepository
{
    public async Task<SubscriptionPlan?> FindForCustomerAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken)
    {
        var query =
            from account in dbContext.CustomerAccounts.AsNoTracking()
            join plan in dbContext.SubscriptionPlans.AsNoTracking()
                on account.SubscriptionPlanCode equals plan.Code
            where account.Id == customerAccountId
            select plan;
        var row = await query.SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new SubscriptionPlan(
                row.Code,
                row.Name,
                row.IncludedCredits,
                row.PricingPolicyVersion).Validate();
    }
}
