using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;

namespace FinancialCopilot.Billing.Services;

public sealed class CreditLinePolicyService : ICreditLinePolicyService
{
    public CreditLineReservationAssessment AssessReservation(
        CustomerAccount account,
        WalletSnapshot wallet,
        decimal requestedCredits)
    {
        if (requestedCredits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedCredits),
                "Requested credits must be positive.");
        }

        var available = account.GetAvailableSpendingCapacity(wallet);
        var remaining = available - requestedCredits;
        var projectedWalletCapacity = wallet.Balance - wallet.ReservedAmount - requestedCredits;
        var usedCredit = account.CreditLine is null
            ? 0
            : decimal.Max(0, -projectedWalletCapacity);
        var lineRemaining = account.CreditLine is null
            ? 0
            : account.CreditLine.ApprovedLimit - usedCredit;
        var warningReached = account.CreditLine is not null &&
            usedCredit > 0 &&
            lineRemaining <= account.CreditLine.WarningThreshold;

        return new CreditLineReservationAssessment(
            Approved: remaining >= 0,
            available,
            remaining,
            usedCredit,
            warningReached);
    }

    public bool CanReserve(
        CustomerAccount account,
        WalletSnapshot wallet,
        decimal requestedCredits) =>
        AssessReservation(account, wallet, requestedCredits).Approved;
}
