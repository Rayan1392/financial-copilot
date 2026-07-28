namespace FinancialCopilot.Billing.Accounts;

public sealed class CustomerAccount
{
    public CustomerAccount(
        Guid id,
        Guid tenantId,
        CustomerAccountType accountType,
        BillingMode billingMode,
        CreditLine? creditLine = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Customer account id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (accountType == CustomerAccountType.Individual && billingMode != BillingMode.Prepaid)
        {
            throw new ArgumentException(
                "Individual accounts support prepaid execution only by default.",
                nameof(billingMode));
        }

        if (accountType == CustomerAccountType.Individual && creditLine is not null)
        {
            throw new ArgumentException(
                "Individual accounts do not support a credit line by default.",
                nameof(creditLine));
        }

        if (billingMode is BillingMode.Postpaid or BillingMode.Hybrid && creditLine is null)
        {
            throw new ArgumentException(
                "Postpaid and hybrid accounts require an approved credit line.",
                nameof(creditLine));
        }

        Id = id;
        TenantId = tenantId;
        AccountType = accountType;
        BillingMode = billingMode;
        CreditLine = creditLine;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public CustomerAccountType AccountType { get; }

    public BillingMode BillingMode { get; }

    public CreditLine? CreditLine { get; }

    public decimal GetAvailableSpendingCapacity(WalletSnapshot wallet)
    {
        ValidateWalletOwnership(wallet);
        return wallet.AvailableSpendingCapacity(CreditLine);
    }

    public bool CanReserve(WalletSnapshot wallet, decimal requestedCredits)
    {
        if (requestedCredits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedCredits),
                "Requested credits must be positive.");
        }

        return GetAvailableSpendingCapacity(wallet) >= requestedCredits;
    }

    private void ValidateWalletOwnership(WalletSnapshot wallet)
    {
        if (wallet.CustomerAccountId != Id)
        {
            throw new ArgumentException(
                "Wallet snapshot belongs to a different customer account.",
                nameof(wallet));
        }
    }
}
