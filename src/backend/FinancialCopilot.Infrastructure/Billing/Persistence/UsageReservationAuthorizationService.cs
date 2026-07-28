using FinancialCopilot.Billing;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class UsageReservationAuthorizationService(
    BillingDbContext dbContext,
    ICreditLinePolicyService creditLinePolicy,
    TimeProvider timeProvider) : ICreditReservationService
{
    private static readonly TimeSpan DefaultReservationDuration = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ReservationLocks = new();

    public async Task<UsageReservation> ReserveAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal maximumCredits,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var reservationLock = ReservationLocks.GetOrAdd(account.Id, static _ => new SemaphoreSlim(1, 1));
        await reservationLock.WaitAsync(cancellationToken);
        try
        {
            var normalizedKey = idempotencyKey.Trim();
            var existingRow = await dbContext.UsageReservations
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.IdempotencyKey == normalizedKey, cancellationToken);

            if (existingRow is not null)
            {
                var existing = MapReservation(existingRow);
                ValidateExisting(existing, account, operationCode, maximumCredits);
                return existing;
            }

            var accountExists = await dbContext.CustomerAccounts.AnyAsync(
                row => row.Id == account.Id && row.TenantId == account.TenantId,
                cancellationToken);

            if (!accountExists)
            {
                throw new KeyNotFoundException("Billing account is not configured in this tenant.");
            }

            var walletRow = await dbContext.WalletProjections.SingleOrDefaultAsync(
                row => row.CustomerAccountId == account.Id,
                cancellationToken) ??
                throw new KeyNotFoundException("Wallet projection is not configured.");
            var currentWallet = MapWallet(walletRow);

            if (wallet.CustomerAccountId != currentWallet.CustomerAccountId ||
                wallet.Revision != currentWallet.Revision)
            {
                throw new InvalidOperationException("Reservation request used a stale wallet snapshot.");
            }

            if (!creditLinePolicy.CanReserve(account, currentWallet, maximumCredits))
            {
                throw new InsufficientCreditException();
            }

            var now = timeProvider.GetUtcNow();
            var reservation = new UsageReservation(
                Guid.NewGuid(),
                account.Id,
                normalizedKey,
                operationCode,
                maximumCredits,
                now,
                now.Add(DefaultReservationDuration));
            var reservedWallet = currentWallet.Reserve(maximumCredits, now);

            dbContext.UsageReservations.Add(new UsageReservationRow
            {
                Id = reservation.Id,
                CustomerAccountId = reservation.CustomerAccountId,
                IdempotencyKey = reservation.IdempotencyKey,
                OperationCode = reservation.OperationCode,
                ReservedCredits = reservation.ReservedCredits,
                CreatedAt = reservation.CreatedAt,
                ExpiresAt = reservation.ExpiresAt,
                Status = reservation.Status.ToString()
            });
            ApplyWallet(walletRow, reservedWallet);
            BillingOutboxWriter.Add(
                dbContext,
                "UsageReservation",
                reservation.Id,
                "Billing.UsageReservationCreated",
                $"{reservation.IdempotencyKey}:created",
                new
                {
                    reservation.CustomerAccountId,
                    reservation.OperationCode,
                    reservation.ReservedCredits,
                    reservation.ExpiresAt
                },
                now);

            await dbContext.SaveChangesAsync(cancellationToken);

            return reservation;
        }
        finally
        {
            reservationLock.Release();
        }
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
        var reservationRows = await dbContext.UsageReservations
            .Where(row =>
                row.Status == nameof(UsageReservationStatus.Reserved) &&
                row.ExpiresAt <= now)
            .OrderBy(row => row.ExpiresAt)
            .Take(maximumCount)
            .ToListAsync(cancellationToken);

        foreach (var reservationRow in reservationRows)
        {
            var reservation = MapReservation(reservationRow);
            var walletRow = await dbContext.WalletProjections.SingleAsync(
                row => row.CustomerAccountId == reservation.CustomerAccountId,
                cancellationToken);
            var releasedWallet = MapWallet(walletRow).Release(reservation.ReservedCredits, now);

            reservation.Expire("Reservation expired before finalization.");
            reservationRow.Status = reservation.Status.ToString();
            reservationRow.FinalizationReason = reservation.FinalizationReason;
            ApplyWallet(walletRow, releasedWallet);
            BillingOutboxWriter.Add(
                dbContext,
                "UsageReservation",
                reservation.Id,
                "Billing.UsageReservationExpired",
                $"{reservation.IdempotencyKey}:expired",
                new
                {
                    reservation.CustomerAccountId,
                    reservation.ReservedCredits,
                    reservation.FinalizationReason
                },
                now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return reservationRows.Count;
    }

    private static void ValidateExisting(
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

    private static WalletSnapshot MapWallet(WalletProjectionRow row) =>
        new(row.CustomerAccountId, row.Balance, row.ReservedAmount, row.UpdatedAt, row.Revision);

    private static void ApplyWallet(WalletProjectionRow row, WalletSnapshot wallet)
    {
        row.Balance = wallet.Balance;
        row.ReservedAmount = wallet.ReservedAmount;
        row.UpdatedAt = wallet.UpdatedAt;
        row.Revision = wallet.Revision;
    }

    private static UsageReservation MapReservation(UsageReservationRow row) =>
        UsageReservation.Restore(
            row.Id,
            row.CustomerAccountId,
            row.IdempotencyKey,
            row.OperationCode,
            row.ReservedCredits,
            row.CommittedCredits,
            row.CreatedAt,
            row.ExpiresAt,
            Enum.Parse<UsageReservationStatus>(row.Status),
            row.FinalizationReason);
}
