namespace FinancialCopilot.API.Contracts;

public sealed record AdminBillingWalletResponse(
    Guid CustomerAccountId,
    string CustomerType,
    string BillingMode,
    decimal Balance,
    decimal ReservedCredits,
    decimal CreditLineApprovedLimit,
    decimal CreditLineWarningThreshold,
    decimal AvailableSpendingCapacity,
    DateTimeOffset WalletUpdatedAt);

public sealed record AdminInvoiceAccountResponse(
    Guid CustomerAccountId,
    string LegalName,
    string BillingEmail,
    string SettlementTerms);

public sealed record AdminCreditAdjustmentRequest(
    decimal Credits,
    string Reason,
    string IdempotencyKey);

public sealed record AdminCreditAdjustmentResponse(
    Guid LedgerEntryId,
    decimal Credits,
    decimal UpdatedBalance,
    decimal AvailableSpendingCapacity,
    bool AlreadyApplied);

public sealed record AdminUsageRefundRequest(
    string OriginalChargeIdempotencyKey,
    decimal Credits,
    string Reason,
    string IdempotencyKey);

public sealed record AdminUsageRefundResponse(
    Guid LedgerEntryId,
    Guid OriginalChargeLedgerEntryId,
    decimal Credits,
    decimal UpdatedBalance,
    decimal AvailableSpendingCapacity,
    bool AlreadyApplied);
