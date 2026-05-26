namespace FinancialCopilot.API.Contracts;

public sealed record UsageSummaryResponse(
    string CustomerType,
    string BillingMode,
    decimal Balance,
    decimal ReservedCredits,
    decimal AvailableSpendingCapacity,
    DateTimeOffset WalletUpdatedAt,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    IReadOnlyCollection<UsageEntryResponse> Entries);

public sealed record UsageEntryResponse(
    string OperationCode,
    string EntryType,
    decimal CreditsCharged,
    string PricingPolicyVersion,
    DateTimeOffset OccurredAt,
    string? ExternalUserId);
