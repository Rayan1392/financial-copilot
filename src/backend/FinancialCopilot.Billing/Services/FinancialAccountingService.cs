using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Services;

public sealed class FinancialAccountingService(
    IFinancialTransactionRepository transactions) : IFinancialAccountingService
{
    public async Task RecordAsync(
        FinancialTransaction transaction,
        CancellationToken cancellationToken)
    {
        Validate(transaction);

        var existing = await transactions.FindByIdempotencyKeyAsync(
            transaction.IdempotencyKey,
            cancellationToken);

        if (existing is null)
        {
            await transactions.AppendAsync(transaction, cancellationToken);
            return;
        }

        if (existing != transaction)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different financial transaction.");
        }
    }

    public Task<IReadOnlyCollection<FinancialTransaction>> QueryAsync(
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

        return transactions.QueryAsync(customerAccountId, from, to, cancellationToken);
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
}
