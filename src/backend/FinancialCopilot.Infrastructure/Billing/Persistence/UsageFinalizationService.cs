using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class UsageFinalizationService(
    BillingDbContext dbContext,
    TimeProvider timeProvider) : IUsageFinalizationService
{
    private readonly SubscriptionCreditLifecycleService creditLifecycle = new(dbContext);

    public async Task<UsageFinalizationResult> CommitAsync(
        UsageCommitCommand command,
        CancellationToken cancellationToken)
    {
        const int maximumConcurrencyAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CommitOnceAsync(command, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maximumConcurrencyAttempts)
            {
                // Expiry, source-aware settlement and concurrent source grants can all change
                // the same wallet/source revisions. SaveChanges rolls back its transaction;
                // reload the complete production operation and re-evaluate its persisted holds.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task<UsageFinalizationResult> CommitOnceAsync(
        UsageCommitCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var reservationRow = await GetReservationAsync(
            command.CustomerAccountId,
            command.ReservationIdempotencyKey,
            cancellationToken);
        await ValidateAccountTenantAsync(command.CustomerAccountId, command.TenantId, cancellationToken);
        var walletRow = await GetWalletAsync(command.CustomerAccountId, cancellationToken);
        var existingLedgerRow = await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.IdempotencyKey == command.LedgerIdempotencyKey.Trim(),
                cancellationToken);

        if (reservationRow.Status == nameof(UsageReservationStatus.Committed))
        {
            var existingLedger = existingLedgerRow ??
                throw new InvalidOperationException("Committed usage reservation has no ledger entry.");
            ValidateExistingCommit(reservationRow, existingLedger, command);

            return new UsageFinalizationResult(
                MapReservation(reservationRow),
                MapWallet(walletRow),
                MapLedger(existingLedger),
                AlreadyFinalized: true);
        }

        if (reservationRow.Status != nameof(UsageReservationStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved usage reservation can be committed.");
        }

        if (existingLedgerRow is not null)
        {
            // Under READ COMMITTED, this ledger row may become visible after the earlier
            // reservation read observed Reserved. A concurrent winner commits the reservation
            // and ledger atomically; refresh both tracked rows before deciding this is a
            // conflicting partial write.
            await dbContext.Entry(reservationRow).ReloadAsync(cancellationToken);
            await dbContext.Entry(walletRow).ReloadAsync(cancellationToken);
            if (reservationRow.Status == nameof(UsageReservationStatus.Committed))
            {
                ValidateExistingCommit(reservationRow, existingLedgerRow, command);
                return new UsageFinalizationResult(
                    MapReservation(reservationRow),
                    MapWallet(walletRow),
                    MapLedger(existingLedgerRow),
                    AlreadyFinalized: true);
            }

            throw new InvalidOperationException(
                "Usage ledger idempotency key already exists for an uncommitted reservation.");
        }

        var reservation = MapReservation(reservationRow);
        var wallet = MapWallet(walletRow);
        var now = timeProvider.GetUtcNow();
        var isUnlimited = reservation.QuotaMode == CreditQuotaMode.Unlimited;
        if (isUnlimited)
        {
            var sourceRevoked = await dbContext.ExternalPlanSubscriptions.AsNoTracking()
                .AnyAsync(row => row.Id == reservation.SourceSubscriptionId && row.IsRevoked, cancellationToken);
            if (sourceRevoked)
                throw new InvalidOperationException("A revoked Unlimited subscription cannot finalize pending usage.");
        }
        var creditsCharged = isUnlimited ? 0m : command.ActualCharge.CreditsCharged;
        IReadOnlyList<SubscriptionCreditApplication> subscriptionCreditApplications = isUnlimited
            ? Array.Empty<SubscriptionCreditApplication>()
            : await creditLifecycle.ApplyUsageChargeAsync(
            command.CustomerAccountId,
            reservation.CreatedAt,
            command.ActualCharge.CreditsCharged,
            command.AllocationSource,
            command.AllowanceDateKey,
            cancellationToken,
            reservationRow.Id);
        var topUpCharge = isUnlimited
            ? new TopUpUsageChargeResult(Array.Empty<TopUpCreditApplication>(), 0m)
            : await creditLifecycle.ApplyTopUpUsageChargeAsync(
            command.CustomerAccountId,
            reservation.CreatedAt,
            Math.Max(0m, command.ActualCharge.CreditsCharged - subscriptionCreditApplications.Sum(item => item.CreditsApplied)),
            now,
            reservationRow.Id,
            cancellationToken);
        var topUpCreditApplications = topUpCharge.Applications;
        reservation.Commit(creditsCharged);
        var updatedWallet = isUnlimited
            ? wallet
            : wallet.Commit(reservation.ReservedCredits, creditsCharged, now);
        if (topUpCharge.ExpiredRemainderCredits > 0)
            updatedWallet = updatedWallet with { Balance = Math.Max(0m, updatedWallet.Balance - topUpCharge.ExpiredRemainderCredits), UpdatedAt = now, Revision = updatedWallet.Revision + 1 };
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            command.CustomerAccountId,
            command.ActorId,
            command.TenantId,
            command.ApiClientId,
            UsageLedgerEntryType.Charge,
            reservation.OperationCode,
            creditsCharged,
            command.ActualCharge.PricingPolicyVersion,
            command.LedgerIdempotencyKey.Trim(),
            now,
            command.ExternalUserId,
            RelatedEntryId: subscriptionCreditApplications.FirstOrDefault()?.GrantLedgerEntryId,
            CompletionStatus: command.CompletionStatus,
            ProviderName: command.ProviderName,
            ModelName: command.ModelName,
            PromptTokens: command.PromptTokens,
            CompletionTokens: command.CompletionTokens,
            TotalTokens: command.TotalTokens,
            EstimatedCost: command.EstimatedCost,
            AllocationSource: command.AllocationSource ??
                (subscriptionCreditApplications.Count == 0
                    ? topUpCreditApplications.Count == 0 ? null : "tahlilapp-topup"
                    : SubscriptionCreditLifecycleService.SubscriptionAllocationSource),
            AllowanceDateKey: command.AllowanceDateKey,
            QuotaMode: reservation.QuotaMode,
            SourceSubscriptionId: reservation.SourceSubscriptionId,
            SourceSubscriptionExpiresAtUtc: reservation.SourceSubscriptionExpiresAtUtc);

        reservationRow.Status = reservation.Status.ToString();
        reservationRow.CommittedCredits = reservation.CommittedCredits;
        walletRow.Balance = updatedWallet.Balance;
        walletRow.ReservedAmount = updatedWallet.ReservedAmount;
        walletRow.UpdatedAt = updatedWallet.UpdatedAt;
        walletRow.Revision = updatedWallet.Revision;
        var ledgerRow = MapLedgerRow(entry);
        ledgerRow.RelatedEntryId = subscriptionCreditApplications.FirstOrDefault()?.GrantLedgerEntryId ??
            topUpCreditApplications.FirstOrDefault()?.GrantLedgerEntryId;
        ledgerRow.SubscriptionCreditsApplied = subscriptionCreditApplications.Sum(item => item.CreditsApplied) +
            topUpCreditApplications.Sum(item => item.CreditsApplied);
        ledgerRow.QuotaMode = reservation.QuotaMode.ToString();
        ledgerRow.SourceSubscriptionId = reservation.SourceSubscriptionId;
        ledgerRow.SourceSubscriptionExpiresAtUtc = reservation.SourceSubscriptionExpiresAtUtc;
        dbContext.UsageLedgerEntries.Add(ledgerRow);
        foreach (var allocation in subscriptionCreditApplications)
        {
            dbContext.UsageCreditAllocations.Add(new UsageCreditAllocationRow
            {
                Id = Guid.NewGuid(),
                UsageLedgerEntryId = ledgerRow.Id,
                SubscriptionCreditPeriodId = allocation.PeriodId,
                GrantLedgerEntryId = allocation.GrantLedgerEntryId,
                CreditsApplied = allocation.CreditsApplied
            });
        }
        foreach (var allocation in topUpCreditApplications)
        {
            dbContext.TopUpUsageCreditAllocations.Add(new TopUpUsageCreditAllocationRow
            {
                Id = Guid.NewGuid(), UsageLedgerEntryId = ledgerRow.Id,
                TopUpCreditSourceId = allocation.SourceId, GrantLedgerEntryId = allocation.GrantLedgerEntryId,
                CreditsApplied = allocation.CreditsApplied
            });
        }
        BillingOutboxWriter.Add(
            dbContext,
            "UsageReservation",
            reservation.Id,
            "Billing.UsageCommitted",
            $"{reservation.IdempotencyKey}:committed",
            new
            {
                entry.Id,
                entry.CustomerAccountId,
                entry.OperationCode,
                entry.CreditsCharged,
                entry.PricingPolicyVersion,
                entry.CompletionStatus,
                entry.ExternalUserId,
                entry.ProviderName,
                entry.ModelName,
                entry.PromptTokens,
                entry.CompletionTokens,
                entry.TotalTokens,
                entry.EstimatedCost,
                entry.AllocationSource,
                entry.AllowanceDateKey,
                entry.QuotaMode,
                entry.SourceSubscriptionId,
                entry.SourceSubscriptionExpiresAtUtc,
                SubscriptionCreditsApplied = subscriptionCreditApplications.Sum(item => item.CreditsApplied) +
                    topUpCreditApplications.Sum(item => item.CreditsApplied)
            },
            now);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another application instance may have committed the same logical operation
            // after our initial idempotency read. PostgreSQL can report the outbox or ledger
            // unique-index race before EF observes the reservation concurrency token.
            // Reuse only a fully persisted, payload-compatible winner; otherwise preserve the
            // original persistence failure for the caller to retry or diagnose.
            dbContext.ChangeTracker.Clear();
            var persistedReservation = await dbContext.UsageReservations.AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.IdempotencyKey == command.ReservationIdempotencyKey.Trim(),
                    cancellationToken);
            var persistedLedger = await dbContext.UsageLedgerEntries.AsNoTracking()
                .SingleOrDefaultAsync(
                    row => row.IdempotencyKey == command.LedgerIdempotencyKey.Trim(),
                    cancellationToken);

            if (persistedReservation?.Status != nameof(UsageReservationStatus.Committed) ||
                persistedLedger is null)
            {
                throw;
            }

            ValidateExistingCommit(persistedReservation, persistedLedger, command);
            var persistedWallet = await dbContext.WalletProjections.AsNoTracking()
                .SingleAsync(row => row.CustomerAccountId == command.CustomerAccountId, cancellationToken);
            return new UsageFinalizationResult(
                MapReservation(persistedReservation),
                MapWallet(persistedWallet),
                MapLedger(persistedLedger),
                AlreadyFinalized: true);
        }

        return new UsageFinalizationResult(reservation, updatedWallet, entry, AlreadyFinalized: false);
    }

    public async Task<UsageFinalizationResult> ReleaseAsync(
        UsageReleaseCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var reservationRow = await GetReservationAsync(
            command.CustomerAccountId,
            command.ReservationIdempotencyKey,
            cancellationToken);
        await ValidateAccountTenantAsync(command.CustomerAccountId, command.TenantId, cancellationToken);
        var walletRow = await GetWalletAsync(command.CustomerAccountId, cancellationToken);
        var reason = command.Reason.Trim();

        if (reservationRow.Status == nameof(UsageReservationStatus.Released))
        {
            if (!string.Equals(reservationRow.FinalizationReason, reason, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A finalized release cannot be replayed with a different reason.");
            }

            return new UsageFinalizationResult(
                MapReservation(reservationRow),
                MapWallet(walletRow),
                LedgerEntry: null,
                AlreadyFinalized: true);
        }

        if (reservationRow.Status != nameof(UsageReservationStatus.Reserved))
        {
            throw new InvalidOperationException("Only a reserved usage reservation can be released.");
        }

        var reservation = MapReservation(reservationRow);
        var now = timeProvider.GetUtcNow();
        reservation.Release(reason);
        var updatedWallet = reservation.QuotaMode == CreditQuotaMode.Unlimited
            ? MapWallet(walletRow)
            : MapWallet(walletRow).Release(reservation.ReservedCredits, now);

        reservationRow.Status = reservation.Status.ToString();
        reservationRow.FinalizationReason = reservation.FinalizationReason;
        var expiredTopUpCredits = await creditLifecycle.ReleaseHoldsAsync(reservationRow.Id, now, cancellationToken);
        if (expiredTopUpCredits > 0)
            updatedWallet = updatedWallet with { Balance = Math.Max(0m, updatedWallet.Balance - expiredTopUpCredits), UpdatedAt = now, Revision = updatedWallet.Revision + 1 };
        walletRow.Balance = updatedWallet.Balance;
        walletRow.ReservedAmount = updatedWallet.ReservedAmount;
        walletRow.UpdatedAt = updatedWallet.UpdatedAt;
        walletRow.Revision = updatedWallet.Revision;
        BillingOutboxWriter.Add(
            dbContext,
            "UsageReservation",
            reservation.Id,
            "Billing.UsageReleased",
            $"{reservation.IdempotencyKey}:released",
            new
            {
                reservation.CustomerAccountId,
                reservation.ReservedCredits,
                reservation.FinalizationReason
            },
            now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UsageFinalizationResult(
            reservation,
            updatedWallet,
            LedgerEntry: null,
            AlreadyFinalized: false);
    }

    private async Task<UsageReservationRow> GetReservationAsync(
        Guid customerAccountId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await dbContext.UsageReservations.SingleOrDefaultAsync(
            row => row.CustomerAccountId == customerAccountId &&
                   row.IdempotencyKey == idempotencyKey.Trim(),
            cancellationToken) ??
        throw new KeyNotFoundException("Usage reservation is not configured.");

    private async Task<WalletProjectionRow> GetWalletAsync(
        Guid customerAccountId,
        CancellationToken cancellationToken) =>
        await dbContext.WalletProjections.SingleOrDefaultAsync(
            row => row.CustomerAccountId == customerAccountId,
            cancellationToken) ??
        throw new KeyNotFoundException("Wallet projection is not configured.");

    private async Task ValidateAccountTenantAsync(
        Guid customerAccountId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.CustomerAccounts.AnyAsync(
            row => row.Id == customerAccountId && row.TenantId == tenantId,
            cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException("Billing account is not configured in this tenant.");
        }
    }

    private static void Validate(UsageCommitCommand command)
    {
        if (command.CustomerAccountId == Guid.Empty ||
            command.ActorId == Guid.Empty ||
            command.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.ReservationIdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.LedgerIdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.ActualCharge.PricingPolicyVersion) ||
            string.IsNullOrWhiteSpace(command.CompletionStatus) ||
            command.ActualCharge.CreditsCharged < 0)
        {
            throw new ArgumentException("Usage commit command is invalid.", nameof(command));
        }
    }

    private static void Validate(UsageReleaseCommand command)
    {
        if (command.CustomerAccountId == Guid.Empty ||
            command.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.ReservationIdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new ArgumentException("Usage release command is invalid.", nameof(command));
        }
    }

    private static void ValidateExistingCommit(
        UsageReservationRow reservation,
        UsageLedgerEntryRow ledger,
        UsageCommitCommand command)
    {
        var allocationSourceMatches = ledger.AllocationSource == command.AllocationSource ||
            (command.AllocationSource is null &&
             ledger.AllocationSource == SubscriptionCreditLifecycleService.SubscriptionAllocationSource &&
             ledger.SubscriptionCreditsApplied > 0);
        if (ledger.CustomerAccountId != command.CustomerAccountId ||
            ledger.ActorId != command.ActorId ||
            ledger.TenantId != command.TenantId ||
            ledger.ApiClientId != command.ApiClientId ||
            ledger.EntryType != nameof(UsageLedgerEntryType.Charge) ||
            ledger.OperationCode != reservation.OperationCode ||
            ledger.CreditsCharged != (Enum.Parse<CreditQuotaMode>(reservation.QuotaMode) == CreditQuotaMode.Unlimited ? 0m : command.ActualCharge.CreditsCharged) ||
            ledger.QuotaMode != reservation.QuotaMode ||
            ledger.SourceSubscriptionId != reservation.SourceSubscriptionId ||
            ledger.SourceSubscriptionExpiresAtUtc != reservation.SourceSubscriptionExpiresAtUtc ||
            ledger.PricingPolicyVersion != command.ActualCharge.PricingPolicyVersion ||
            (ledger.CompletionStatus is not null &&
                ledger.CompletionStatus != command.CompletionStatus) ||
            ledger.ExternalUserId != command.ExternalUserId ||
            ledger.ProviderName != command.ProviderName ||
            ledger.ModelName != command.ModelName ||
            ledger.PromptTokens != command.PromptTokens ||
            ledger.CompletionTokens != command.CompletionTokens ||
            ledger.TotalTokens != command.TotalTokens ||
            ledger.EstimatedCost != command.EstimatedCost ||
            !allocationSourceMatches ||
            ledger.AllowanceDateKey != command.AllowanceDateKey)
        {
            throw new InvalidOperationException(
                "A finalized usage reservation cannot be replayed with different charge data.");
        }
    }

    private static WalletSnapshot MapWallet(WalletProjectionRow row) =>
        new(row.CustomerAccountId, row.Balance, row.ReservedAmount, row.UpdatedAt, row.Revision);

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
            row.FinalizationReason,
            Enum.Parse<CreditQuotaMode>(row.QuotaMode),
            row.SourceSubscriptionId,
            row.SourceSubscriptionExpiresAtUtc);

    private static UsageLedgerEntry MapLedger(UsageLedgerEntryRow row) =>
        new(
            row.Id,
            row.CustomerAccountId,
            row.ActorId,
            row.TenantId,
            row.ApiClientId,
            Enum.Parse<UsageLedgerEntryType>(row.EntryType),
            row.OperationCode,
            row.CreditsCharged,
            row.PricingPolicyVersion,
            row.IdempotencyKey,
            row.OccurredAt,
            row.ExternalUserId,
            row.AuditDescription,
            row.RelatedEntryId,
            row.CompletionStatus,
            row.ProviderName,
            row.ModelName,
            row.PromptTokens,
            row.CompletionTokens,
            row.TotalTokens,
            row.EstimatedCost,
            row.AllocationSource,
            row.AllowanceDateKey,
            Enum.Parse<CreditQuotaMode>(row.QuotaMode),
            row.SourceSubscriptionId,
            row.SourceSubscriptionExpiresAtUtc);

    private static UsageLedgerEntryRow MapLedgerRow(UsageLedgerEntry entry) =>
        new()
        {
            Id = entry.Id,
            CustomerAccountId = entry.CustomerAccountId,
            ActorId = entry.ActorId,
            TenantId = entry.TenantId,
            ApiClientId = entry.ApiClientId,
            EntryType = entry.EntryType.ToString(),
            OperationCode = entry.OperationCode,
            CreditsCharged = entry.CreditsCharged,
            PricingPolicyVersion = entry.PricingPolicyVersion,
            IdempotencyKey = entry.IdempotencyKey,
            OccurredAt = entry.OccurredAt,
            ExternalUserId = entry.ExternalUserId,
            AuditDescription = entry.AuditDescription,
            RelatedEntryId = entry.RelatedEntryId,
            CompletionStatus = entry.CompletionStatus,
            ProviderName = entry.ProviderName,
            ModelName = entry.ModelName,
            PromptTokens = entry.PromptTokens,
            CompletionTokens = entry.CompletionTokens,
            TotalTokens = entry.TotalTokens,
            EstimatedCost = entry.EstimatedCost,
            AllocationSource = entry.AllocationSource,
            AllowanceDateKey = entry.AllowanceDateKey,
            QuotaMode = entry.QuotaMode.ToString(),
            SourceSubscriptionId = entry.SourceSubscriptionId,
            SourceSubscriptionExpiresAtUtc = entry.SourceSubscriptionExpiresAtUtc
        };
}
