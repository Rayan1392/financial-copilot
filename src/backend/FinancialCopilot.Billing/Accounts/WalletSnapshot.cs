namespace FinancialCopilot.Billing.Accounts;

public sealed record WalletSnapshot(
    Guid CustomerAccountId,
    decimal Balance,
    decimal ReservedAmount,
    DateTimeOffset UpdatedAt)
{
    public decimal AvailableSpendingCapacity(CreditLine? creditLine) =>
        Balance + (creditLine?.ApprovedLimit ?? 0) - ReservedAmount;

    public WalletSnapshot Reserve(decimal credits, DateTimeOffset updatedAt)
    {
        if (credits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(credits));
        }

        return this with
        {
            ReservedAmount = ReservedAmount + credits,
            UpdatedAt = updatedAt
        };
    }

    public WalletSnapshot Commit(decimal reservedCredits, decimal chargedCredits, DateTimeOffset updatedAt)
    {
        ValidateReservedAmount(reservedCredits);

        if (chargedCredits < 0 || chargedCredits > reservedCredits)
        {
            throw new ArgumentOutOfRangeException(nameof(chargedCredits));
        }

        return this with
        {
            Balance = Balance - chargedCredits,
            ReservedAmount = ReservedAmount - reservedCredits,
            UpdatedAt = updatedAt
        };
    }

    public WalletSnapshot Release(decimal reservedCredits, DateTimeOffset updatedAt)
    {
        ValidateReservedAmount(reservedCredits);

        return this with
        {
            ReservedAmount = ReservedAmount - reservedCredits,
            UpdatedAt = updatedAt
        };
    }

    private void ValidateReservedAmount(decimal credits)
    {
        if (credits <= 0 || credits > ReservedAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(credits));
        }
    }
}
