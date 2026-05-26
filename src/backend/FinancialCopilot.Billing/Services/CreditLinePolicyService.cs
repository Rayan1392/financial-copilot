using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Billing.Services;

public sealed class CreditLinePolicyService : ICreditLinePolicyService
{
    public bool CanReserve(
        CustomerAccount account,
        WalletSnapshot wallet,
        decimal requestedCredits) =>
        account.CanReserve(wallet, requestedCredits);
}
