namespace FinancialCopilot.Billing.Usage;

public enum FinancialTransactionType
{
    TopUp,
    Payment,
    Refund,
    ManualAdjustment,
    InvoiceSettlement
}

public sealed record FinancialTransaction(
    Guid Id,
    Guid CustomerAccountId,
    FinancialTransactionType Type,
    decimal Amount,
    string Currency,
    string IdempotencyKey,
    DateTimeOffset OccurredAt);
