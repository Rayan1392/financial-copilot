using FinancialCopilot.Billing;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Application.Authentication.TahlilApp;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class UsageReservationAuthorizationService(
    BillingDbContext dbContext,
    ICreditLinePolicyService creditLinePolicy,
    TimeProvider timeProvider,
    ITahlilAppEntitlementRefresher? entitlementRefresher = null) :
    ICreditReservationService,
    IUsageReservationUpgradeService
{
    private static readonly TimeSpan DefaultReservationDuration = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ReservationLocks = new();
    private readonly SubscriptionCreditLifecycleService creditLifecycle = new(dbContext);

    public async Task<UsageReservation> ReserveAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal maximumCredits,
        string idempotencyKey,
        CancellationToken cancellationToken,
        string? requestFingerprint)
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
                ValidateExisting(existingRow, account, operationCode, maximumCredits, requestFingerprint);
                return MapReservation(existingRow);
            }

            // Revalidate only when due. The refresher is identity-provider scoped and is a no-op
            // for native accounts; the persisted TahlilApp external id is resolved inside its state store.
            var accountRow = await dbContext.CustomerAccounts.SingleOrDefaultAsync(
                row => row.Id == account.Id && row.TenantId == account.TenantId,
                cancellationToken);

            if (accountRow is null)
            {
                throw new KeyNotFoundException("Billing account is not configured in this tenant.");
            }

            if (entitlementRefresher is not null && accountRow.UserId is { } localUserId)
            {
                await entitlementRefresher.RefreshIfDueAsync(accountRow.TenantId, localUserId, cancellationToken);
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

            var now = timeProvider.GetUtcNow();
            await creditLifecycle.EnsureCurrentPeriodTrackedAsync(accountRow, walletRow, now, cancellationToken);
            currentWallet = MapWallet(walletRow);

            var cooldownUntilUtc = accountRow.ExternalSubscriptionManaged
                ? await creditLifecycle.GetActivePaidExpiryCooldownEndAsync(account.Id, now, cancellationToken)
                : null;
            if (cooldownUntilUtc is { } cooldownEnd)
            {
                // Persist the authoritative expiry cooldown even though this reservation is denied.
                await dbContext.SaveChangesAsync(cancellationToken);
                var freePlanCredits = await dbContext.SubscriptionPlans.AsNoTracking()
                    .Where(plan => plan.Code == "Free")
                    .Select(plan => (decimal?)plan.IncludedCredits)
                    .SingleOrDefaultAsync(cancellationToken);
                throw new InsufficientCreditException(
                    InsufficientCreditException.SubscriptionCooldownActiveCode,
                    cooldownEnd,
                    freePlanCredits);
            }

            if (!creditLinePolicy.CanReserve(account, currentWallet, maximumCredits))
            {
                throw new InsufficientCreditException();
            }

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
                RequestFingerprint = requestFingerprint,
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

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                // A different app instance reserved from the same wallet revision first.
                // Clear rolled-back tracked changes so the caller can refresh and retry once.
                dbContext.ChangeTracker.Clear();
                throw new InvalidOperationException("Reservation request used a stale wallet snapshot.", exception);
            }
            catch (DbUpdateException)
            {
                // SaveChanges is atomic. A different app instance may have inserted this key
                // after our read; only reuse its row after validating the payload fingerprint.
                dbContext.ChangeTracker.Clear();
                var winner = await dbContext.UsageReservations.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.IdempotencyKey == normalizedKey, cancellationToken);
                if (winner is null)
                    throw;

                ValidateExisting(winner, account, operationCode, maximumCredits, requestFingerprint);
                return MapReservation(winner);
            }

            return reservation;
        }
        finally
        {
            reservationLock.Release();
        }
    }

    public Task<UsageReservation> ReserveAsync(
        CustomerAccount account,
        WalletSnapshot wallet,
        string operationCode,
        decimal maximumCredits,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ReserveAsync(account, wallet, operationCode, maximumCredits, idempotencyKey, cancellationToken, null);

    public async Task<UsageReservation> EnsureReservedCapacityAsync(
        CustomerAccount account,
        string reservationIdempotencyKey,
        decimal requiredCredits,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationIdempotencyKey);
        if (requiredCredits <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredCredits));

        var reservationLock = ReservationLocks.GetOrAdd(account.Id, static _ => new SemaphoreSlim(1, 1));
        await reservationLock.WaitAsync(cancellationToken);
        try
        {
            var normalizedKey = reservationIdempotencyKey.Trim();
            var reservationRow = await dbContext.UsageReservations.SingleOrDefaultAsync(
                row => row.IdempotencyKey == normalizedKey,
                cancellationToken) ??
                throw new KeyNotFoundException("Usage reservation is not configured.");
            var reservation = MapReservation(reservationRow);

            if (reservation.CustomerAccountId != account.Id)
                throw new InvalidOperationException("Reservation belongs to a different billing account.");
            if (reservation.Status != UsageReservationStatus.Reserved)
                throw new InvalidOperationException("Only a reserved usage reservation can be upgraded.");
            if (requiredCredits <= reservation.ReservedCredits)
                return reservation;

            var walletRow = await dbContext.WalletProjections.SingleOrDefaultAsync(
                row => row.CustomerAccountId == account.Id,
                cancellationToken) ??
                throw new KeyNotFoundException("Wallet projection is not configured.");
            var wallet = MapWallet(walletRow);
            var increase = requiredCredits - reservation.ReservedCredits;
            var availableBalance = Math.Max(0m, wallet.Balance - wallet.ReservedAmount);
            if (availableBalance < increase)
                throw new InsufficientCreditException();

            var previousCredits = reservation.ReservedCredits;
            var now = timeProvider.GetUtcNow();
            var updatedWallet = wallet.Reserve(increase, now);
            reservationRow.ReservedCredits = requiredCredits;
            ApplyWallet(walletRow, updatedWallet);

            var outboxKey = $"{normalizedKey}:capacity:{requiredCredits.ToString("0.####", CultureInfo.InvariantCulture)}";
            BillingOutboxWriter.Add(
                dbContext,
                "UsageReservation",
                reservation.Id,
                "Billing.UsageReservationCapacityIncreased",
                outboxKey,
                new
                {
                    reservation.CustomerAccountId,
                    reservation.OperationCode,
                    PreviousReservedCredits = previousCredits,
                    ReservedCredits = requiredCredits,
                    Increase = increase,
                    updatedWallet.Revision
                },
                now);

            try
            {
                // One SaveChanges transaction persists the reservation increase, wallet hold,
                // and outbox event together. Wallet Revision is the cross-instance fence.
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                dbContext.Entry(walletRow).State = EntityState.Detached;
                dbContext.Entry(reservationRow).State = EntityState.Detached;
                foreach (var row in dbContext.OutboxMessages.Local
                             .Where(row => string.Equals(row.IdempotencyKey, outboxKey, StringComparison.Ordinal))
                             .ToArray())
                    dbContext.Entry(row).State = EntityState.Detached;

                var latest = await dbContext.UsageReservations.AsNoTracking().SingleAsync(
                    row => row.IdempotencyKey == normalizedKey,
                    cancellationToken);
                if (latest.Status == nameof(UsageReservationStatus.Reserved) &&
                    latest.ReservedCredits >= requiredCredits)
                    return MapReservation(latest);

                throw new InsufficientCreditException();
            }

            return MapReservation(reservationRow);
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
        UsageReservationRow existing,
        CustomerAccount account,
        string operationCode,
        decimal maximumCredits,
        string? requestFingerprint)
    {
        if (existing.CustomerAccountId != account.Id ||
            !string.Equals(existing.OperationCode, operationCode, StringComparison.Ordinal) ||
            existing.ReservedCredits != maximumCredits ||
            (requestFingerprint is not null && existing.RequestFingerprint is not null &&
             !string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal)))
        {
            throw new BillingIdempotencyConflictException(
                "An idempotency key cannot be reused for a different request payload or reservation.");
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
