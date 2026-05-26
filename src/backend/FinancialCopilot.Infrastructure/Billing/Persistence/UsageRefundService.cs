using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class UsageRefundService(
    BillingDbContext dbContext,
    TimeProvider timeProvider) : IUsageRefundService
{
    public async Task<UsageRefundResult> RefundAsync(
        UsageRefundCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var idempotencyKey = command.IdempotencyKey.Trim();
        var chargeKey = command.OriginalChargeIdempotencyKey.Trim();
        var reason = command.Reason.Trim();

        await ValidateAccountTenantAsync(command.CustomerAccountId, command.TenantId, cancellationToken);
        var walletRow = await dbContext.WalletProjections.SingleOrDefaultAsync(
            row => row.CustomerAccountId == command.CustomerAccountId,
            cancellationToken) ??
            throw new KeyNotFoundException("Wallet projection is not configured.");
        var originalCharge = await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.CustomerAccountId == command.CustomerAccountId &&
                       row.IdempotencyKey == chargeKey &&
                       row.EntryType == nameof(UsageLedgerEntryType.Charge),
                cancellationToken) ??
            throw new KeyNotFoundException("Original usage charge is not configured.");
        var existing = await dbContext.UsageLedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            ValidateExisting(existing, command, originalCharge.Id, reason);
            return new UsageRefundResult(MapEntry(existing), MapWallet(walletRow), AlreadyApplied: true);
        }

        var refundedCredits = await dbContext.UsageLedgerEntries
            .Where(row =>
                row.CustomerAccountId == command.CustomerAccountId &&
                row.EntryType == nameof(UsageLedgerEntryType.Refund) &&
                row.RelatedEntryId == originalCharge.Id)
            .SumAsync(row => row.CreditsCharged, cancellationToken);

        if (refundedCredits + command.Credits > originalCharge.CreditsCharged)
        {
            throw new InvalidOperationException("Refunded credits cannot exceed the original usage charge.");
        }

        var now = timeProvider.GetUtcNow();
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            command.CustomerAccountId,
            command.ActorId,
            command.TenantId,
            ApiClientId: null,
            UsageLedgerEntryType.Refund,
            originalCharge.OperationCode,
            command.Credits,
            originalCharge.PricingPolicyVersion,
            idempotencyKey,
            now,
            AuditDescription: reason,
            RelatedEntryId: originalCharge.Id);
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
            AuditDescription = entry.AuditDescription,
            RelatedEntryId = entry.RelatedEntryId
        });
        walletRow.Balance = updatedWallet.Balance;
        walletRow.ReservedAmount = updatedWallet.ReservedAmount;
        walletRow.UpdatedAt = updatedWallet.UpdatedAt;
        walletRow.Revision = updatedWallet.Revision;
        BillingOutboxWriter.Add(
            dbContext,
            "UsageLedgerEntry",
            entry.Id,
            "Billing.UsageRefunded",
            $"{entry.IdempotencyKey}:refunded",
            new
            {
                entry.CustomerAccountId,
                entry.RelatedEntryId,
                Credits = entry.CreditsCharged,
                entry.AuditDescription
            },
            now);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UsageRefundResult(entry, updatedWallet, AlreadyApplied: false);
    }

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

    private static void Validate(UsageRefundCommand command)
    {
        if (command.CustomerAccountId == Guid.Empty ||
            command.ActorId == Guid.Empty ||
            command.TenantId == Guid.Empty ||
            command.Credits <= 0 ||
            string.IsNullOrWhiteSpace(command.OriginalChargeIdempotencyKey) ||
            string.IsNullOrWhiteSpace(command.Reason) ||
            string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ArgumentException("Usage refund command is invalid.", nameof(command));
        }
    }

    private static void ValidateExisting(
        UsageLedgerEntryRow existing,
        UsageRefundCommand command,
        Guid originalChargeId,
        string reason)
    {
        if (existing.CustomerAccountId != command.CustomerAccountId ||
            existing.ActorId != command.ActorId ||
            existing.TenantId != command.TenantId ||
            existing.EntryType != nameof(UsageLedgerEntryType.Refund) ||
            existing.CreditsCharged != command.Credits ||
            existing.RelatedEntryId != originalChargeId ||
            existing.AuditDescription != reason)
        {
            throw new InvalidOperationException(
                "An idempotency key cannot be reused for a different usage refund.");
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
