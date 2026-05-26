using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class FinancialTransactionAccountingService(
    BillingDbContext dbContext) : IFinancialAccountingService
{
    public async Task RecordAsync(
        FinancialTransaction transaction,
        CancellationToken cancellationToken)
    {
        Validate(transaction);

        var normalizedKey = transaction.IdempotencyKey.Trim();
        var existing = await dbContext.FinancialTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKey == normalizedKey, cancellationToken);

        if (existing is not null)
        {
            ValidateExisting(existing, transaction, normalizedKey);
            return;
        }

        dbContext.FinancialTransactions.Add(new FinancialTransactionRow
        {
            Id = transaction.Id,
            CustomerAccountId = transaction.CustomerAccountId,
            Type = transaction.Type.ToString(),
            Amount = transaction.Amount,
            Currency = transaction.Currency.Trim(),
            IdempotencyKey = normalizedKey,
            OccurredAt = transaction.OccurredAt
        });
        BillingOutboxWriter.Add(
            dbContext,
            "FinancialTransaction",
            transaction.Id,
            $"Billing.{transaction.Type}Recorded",
            $"{normalizedKey}:recorded",
            new
            {
                transaction.CustomerAccountId,
                Type = transaction.Type.ToString(),
                transaction.Amount,
                Currency = transaction.Currency.Trim()
            },
            transaction.OccurredAt);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<FinancialTransaction>> QueryAsync(
        Guid customerAccountId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (customerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Customer account id is required.", nameof(customerAccountId));
        }

        if (to < from)
        {
            throw new ArgumentException("Transaction report end must be after its start.", nameof(to));
        }

        return (await dbContext.FinancialTransactions
            .AsNoTracking()
            .Where(row =>
                row.CustomerAccountId == customerAccountId &&
                row.OccurredAt >= from &&
                row.OccurredAt <= to)
            .OrderBy(row => row.OccurredAt)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToArray();
    }

    private static void Validate(FinancialTransaction transaction)
    {
        if (transaction.Id == Guid.Empty ||
            transaction.CustomerAccountId == Guid.Empty ||
            transaction.Amount <= 0 ||
            string.IsNullOrWhiteSpace(transaction.Currency) ||
            string.IsNullOrWhiteSpace(transaction.IdempotencyKey))
        {
            throw new ArgumentException("Financial transaction is invalid.", nameof(transaction));
        }
    }

    private static void ValidateExisting(
        FinancialTransactionRow existing,
        FinancialTransaction transaction,
        string normalizedKey)
    {
        if (existing.Id != transaction.Id ||
            existing.CustomerAccountId != transaction.CustomerAccountId ||
            existing.Type != transaction.Type.ToString() ||
            existing.Amount != transaction.Amount ||
            existing.Currency != transaction.Currency.Trim() ||
            existing.IdempotencyKey != normalizedKey ||
            existing.OccurredAt != transaction.OccurredAt)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different financial transaction.");
        }
    }

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
