using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Services;

public sealed class WalletProjectionBuilder : IWalletProjectionBuilder
{
    public WalletSnapshot Rebuild(
        Guid customerAccountId,
        decimal openingBalance,
        decimal reservedAmount,
        IReadOnlyCollection<UsageLedgerEntry> usageEntries,
        DateTimeOffset asOf)
    {
        if (customerAccountId == Guid.Empty)
        {
            throw new ArgumentException("Customer account id is required.", nameof(customerAccountId));
        }

        if (reservedAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservedAmount));
        }

        var relevantEntries = usageEntries.Where(entry => entry.CustomerAccountId == customerAccountId);
        var balance = relevantEntries.Aggregate(
            openingBalance,
            (current, entry) => current + entry.BalanceImpact);

        return new WalletSnapshot(customerAccountId, balance, reservedAmount, asOf);
    }
}
