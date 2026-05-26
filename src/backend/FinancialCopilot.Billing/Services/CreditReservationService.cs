using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.Billing.Services;

public sealed class CreditReservationService(
    IUsageReservationRepository reservations,
    IWalletProjectionRepository wallets,
    ICreditLinePolicyService creditLinePolicy,
    TimeProvider timeProvider) : ICreditReservationService
{
    private static readonly TimeSpan DefaultReservationDuration = TimeSpan.FromMinutes(5);

    public async Task<UsageReservation> ReserveAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal maximumCredits,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var existing = await reservations.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            ValidateExistingReservation(existing, account, operationCode, maximumCredits);
            return existing;
        }

        if (!creditLinePolicy.CanReserve(account, wallet, maximumCredits))
        {
            throw new InvalidOperationException("Available spending capacity is insufficient.");
        }

        var now = timeProvider.GetUtcNow();
        var reservation = new UsageReservation(
            Guid.NewGuid(),
            account.Id,
            idempotencyKey,
            operationCode,
            maximumCredits,
            now,
            now.Add(DefaultReservationDuration));

        await reservations.SaveAsync(reservation, cancellationToken);
        await wallets.SaveAsync(wallet.Reserve(maximumCredits, now), cancellationToken);

        return reservation;
    }

    public async Task CommitAsync(
        UsageReservation reservation,
        UsageChargeResult actualCharge,
        CancellationToken cancellationToken)
    {
        if (reservation.Status == UsageReservationStatus.Committed &&
            reservation.CommittedCredits == actualCharge.CreditsCharged)
        {
            return;
        }

        var wallet = await wallets.GetSnapshotAsync(reservation.CustomerAccountId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        reservation.Commit(actualCharge.CreditsCharged);
        await reservations.SaveAsync(reservation, cancellationToken);
        await wallets.SaveAsync(
            wallet.Commit(reservation.ReservedCredits, actualCharge.CreditsCharged, now),
            cancellationToken);
    }

    public async Task ReleaseAsync(
        UsageReservation reservation,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (reservation.Status == UsageReservationStatus.Released)
        {
            return;
        }

        var wallet = await wallets.GetSnapshotAsync(reservation.CustomerAccountId, cancellationToken);
        var now = timeProvider.GetUtcNow();

        reservation.Release();
        await reservations.SaveAsync(reservation, cancellationToken);
        await wallets.SaveAsync(wallet.Release(reservation.ReservedCredits, now), cancellationToken);
    }

    public async Task<int> ExpireAbandonedAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var now = timeProvider.GetUtcNow();
        var expiredReservations = await reservations.FindExpiredReservedAsync(
            now,
            maximumCount,
            cancellationToken);

        foreach (var reservation in expiredReservations)
        {
            var wallet = await wallets.GetSnapshotAsync(reservation.CustomerAccountId, cancellationToken);
            reservation.Expire();
            await reservations.SaveAsync(reservation, cancellationToken);
            await wallets.SaveAsync(
                wallet.Release(reservation.ReservedCredits, now),
                cancellationToken);
        }

        return expiredReservations.Count;
    }

    private static void ValidateExistingReservation(
        UsageReservation existing,
        CustomerAccount account,
        string operationCode,
        decimal maximumCredits)
    {
        if (existing.CustomerAccountId != account.Id ||
            !string.Equals(existing.OperationCode, operationCode, StringComparison.Ordinal) ||
            existing.ReservedCredits != maximumCredits)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different reservation.");
        }
    }
}
