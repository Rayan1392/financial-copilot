using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class CreditAdjustmentService(
    BillingDbContext dbContext,
    TimeProvider timeProvider) : ICreditAdjustmentService
{
    private const string AdjustmentOperationCode = "Billing.ManualAdjustment";
    private const string AdjustmentPolicyVersion = "manual-adjustment-v1";

    public async Task<CreditAdjustmentResult> ApplyAsync(
        CreditAdjustmentCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);
        var normalizedReason = command.Reason.Trim();
        var normalizedIdempotencyKey = command.IdempotencyKey.Trim();

        var existing = await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.IdempotencyKey == normalizedIdempotencyKey,
                cancellationToken);
        var walletRow = await dbContext.WalletProjections
            .SingleOrDefaultAsync(
                row => row.CustomerAccountId == command.CustomerAccountId,
                cancellationToken) ??
            throw new KeyNotFoundException("Wallet projection is not configured.");

        if (existing is not null)
        {
            ValidateExisting(existing, command, normalizedReason);
            return new CreditAdjustmentResult(
                MapEntry(existing),
                MapWallet(walletRow),
                AlreadyApplied: true);
        }

        var accountExists = await dbContext.CustomerAccounts
            .AnyAsync(
                row => row.Id == command.CustomerAccountId && row.TenantId == command.TenantId,
                cancellationToken);

        if (!accountExists)
        {
            throw new KeyNotFoundException("Billing account is not configured in this tenant.");
        }

        var now = timeProvider.GetUtcNow();
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            command.CustomerAccountId,
            command.ActorId,
            command.TenantId,
            ApiClientId: null,
            UsageLedgerEntryType.Adjustment,
            AdjustmentOperationCode,
            command.Credits,
            AdjustmentPolicyVersion,
            normalizedIdempotencyKey,
            now,
            AuditDescription: normalizedReason);
        var updatedWallet = MapWallet(walletRow).AddCredits(command.Credits, now);

        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = entry.Id,
            CustomerAccountId = entry.CustomerAccountId,
            ActorId = entry.ActorId,
            TenantId = entry.TenantId,
            EntryType = entry.EntryType.ToString(),
            OperationCode = entry.OperationCode,
            CreditsCharged = entry.CreditsCharged,
            PricingPolicyVersion = entry.PricingPolicyVersion,
            IdempotencyKey = entry.IdempotencyKey,
            OccurredAt = entry.OccurredAt,
            AuditDescription = entry.AuditDescription
        });
        walletRow.Balance = updatedWallet.Balance;
        walletRow.ReservedAmount = updatedWallet.ReservedAmount;
        walletRow.UpdatedAt = updatedWallet.UpdatedAt;
        walletRow.Revision = updatedWallet.Revision;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreditAdjustmentResult(entry, updatedWallet, AlreadyApplied: false);
    }

    private static void Validate(CreditAdjustmentCommand command)
    {
        if (command.CustomerAccountId == Guid.Empty ||
            command.ActorId == Guid.Empty ||
            command.TenantId == Guid.Empty ||
            command.Credits <= 0 ||
            string.IsNullOrWhiteSpace(command.Reason) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("Credit adjustment command is invalid.", nameof(command));
        }
    }

    private static void ValidateExisting(
        UsageLedgerEntryRow existing,
        CreditAdjustmentCommand command,
        string reason)
    {
        if (existing.CustomerAccountId != command.CustomerAccountId ||
            existing.ActorId != command.ActorId ||
            existing.TenantId != command.TenantId ||
            existing.EntryType != nameof(UsageLedgerEntryType.Adjustment) ||
            existing.OperationCode != AdjustmentOperationCode ||
            existing.CreditsCharged != command.Credits ||
            existing.AuditDescription != reason)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different credit adjustment.");
        }
    }

    private static WalletSnapshot MapWallet(WalletProjectionRow row) =>
        new(row.CustomerAccountId, row.Balance, row.ReservedAmount, row.UpdatedAt, row.Revision);

    private static UsageLedgerEntry MapEntry(UsageLedgerEntryRow row) =>
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
            row.RelatedEntryId);
}
