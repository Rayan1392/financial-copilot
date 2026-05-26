using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Services;

public sealed class UsageAccountingService(IUsageLedgerRepository ledger) : IUsageAccountingService
{
    public async Task AppendAsync(UsageLedgerEntry entry, CancellationToken cancellationToken)
    {
        Validate(entry);

        var existing = await ledger.FindByIdempotencyKeyAsync(entry.IdempotencyKey, cancellationToken);

        if (existing is null)
        {
            await ledger.AppendAsync(entry, cancellationToken);
            return;
        }

        if (existing != entry)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different usage ledger entry.");
        }
    }

    private static void Validate(UsageLedgerEntry entry)
    {
        if (entry.Id == Guid.Empty ||
            entry.CustomerAccountId == Guid.Empty ||
            entry.ActorId == Guid.Empty ||
            entry.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(entry.OperationCode) ||
            string.IsNullOrWhiteSpace(entry.PricingPolicyVersion) ||
            string.IsNullOrWhiteSpace(entry.IdempotencyKey) ||
            entry.CreditsCharged < 0)
        {
            throw new ArgumentException("Usage ledger entry is invalid.", nameof(entry));
        }
    }
}
