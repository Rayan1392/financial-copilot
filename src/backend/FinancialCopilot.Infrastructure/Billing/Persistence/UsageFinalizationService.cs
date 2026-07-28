using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class UsageFinalizationService(
    BillingDbContext dbContext,
    TimeProvider timeProvider) : IUsageFinalizationService
{
    public async Task<UsageFinalizationResult> CommitAsync(
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
            throw new InvalidOperationException(
                "Usage ledger idempotency key already exists for an uncommitted reservation.");
        }

        var reservation = MapReservation(reservationRow);
        var wallet = MapWallet(walletRow);
        var now = timeProvider.GetUtcNow();
        reservation.Commit(command.ActualCharge.CreditsCharged);
        var updatedWallet = wallet.Commit(
            reservation.ReservedCredits,
            command.ActualCharge.CreditsCharged,
            now);
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            command.CustomerAccountId,
            command.ActorId,
            command.TenantId,
            command.ApiClientId,
            UsageLedgerEntryType.Charge,
            reservation.OperationCode,
            command.ActualCharge.CreditsCharged,
            command.ActualCharge.PricingPolicyVersion,
            command.LedgerIdempotencyKey.Trim(),
            now,
            command.ExternalUserId,
            CompletionStatus: command.CompletionStatus,
            ProviderName: command.ProviderName,
            ModelName: command.ModelName,
            PromptTokens: command.PromptTokens,
            CompletionTokens: command.CompletionTokens,
            TotalTokens: command.TotalTokens,
            EstimatedCost: command.EstimatedCost,
            AllocationSource: command.AllocationSource,
            AllowanceDateKey: command.AllowanceDateKey);

        reservationRow.Status = reservation.Status.ToString();
        reservationRow.CommittedCredits = reservation.CommittedCredits;
        walletRow.Balance = updatedWallet.Balance;
        walletRow.ReservedAmount = updatedWallet.ReservedAmount;
        walletRow.UpdatedAt = updatedWallet.UpdatedAt;
        walletRow.Revision = updatedWallet.Revision;
        dbContext.UsageLedgerEntries.Add(MapLedgerRow(entry));
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
                entry.AllowanceDateKey
            },
            now);

        await dbContext.SaveChangesAsync(cancellationToken);

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
        var updatedWallet = MapWallet(walletRow).Release(reservation.ReservedCredits, now);

        reservationRow.Status = reservation.Status.ToString();
        reservationRow.FinalizationReason = reservation.FinalizationReason;
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
        if (ledger.CustomerAccountId != command.CustomerAccountId ||
            ledger.ActorId != command.ActorId ||
            ledger.TenantId != command.TenantId ||
            ledger.ApiClientId != command.ApiClientId ||
            ledger.EntryType != nameof(UsageLedgerEntryType.Charge) ||
            ledger.OperationCode != reservation.OperationCode ||
            ledger.CreditsCharged != command.ActualCharge.CreditsCharged ||
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
            ledger.AllocationSource != command.AllocationSource ||
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
            row.FinalizationReason);

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
            row.AllowanceDateKey);

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
            AllowanceDateKey = entry.AllowanceDateKey
        };
}
