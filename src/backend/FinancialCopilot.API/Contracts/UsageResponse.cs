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
    IReadOnlyCollection<UsageEntryResponse> Entries,
    string? PlanCode = null,
    string? PlanName = null,
    decimal? PlanIncludedCredits = null);

public sealed record UsageEntryResponse(
    string OperationCode,
    string EntryType,
    decimal CreditsCharged,
    string PricingPolicyVersion,
    DateTimeOffset OccurredAt,
    string? ExternalUserId,
    string? CompletionStatus,
    string? ProviderName = null,
    string? ModelName = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    decimal? EstimatedCost = null,
    string? AllocationSource = null,
    string? AllowanceDateKey = null);
