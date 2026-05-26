namespace FinancialCopilot.API.Contracts;

public sealed record BillingTransactionsResponse(
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    IReadOnlyCollection<BillingTransactionResponse> Transactions);

public sealed record BillingTransactionResponse(
    string Type,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt);
