using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Infrastructure.Billing;

/// <summary>
/// Keeps non-upgrade reservation implementations compatible while failing closed if an
/// Enforced billing flow actually needs to increase a reservation.
/// </summary>
internal sealed class UnsupportedUsageReservationUpgradeService : IUsageReservationUpgradeService
{
    public Task<UsageReservation> EnsureReservedCapacityAsync(
        CustomerAccount account,
        string reservationIdempotencyKey,
        decimal requiredCredits,
        CancellationToken cancellationToken) =>
        Task.FromException<UsageReservation>(new InvalidOperationException(
            "Enforced usage billing requires the configured credit reservation service to support reservation upgrades."));
}
