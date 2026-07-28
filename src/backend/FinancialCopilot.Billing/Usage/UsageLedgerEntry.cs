namespace FinancialCopilot.Billing.Usage;

public sealed record UsageLedgerEntry(
    Guid Id,
    Guid CustomerAccountId,
    Guid ActorId,
    Guid TenantId,
    Guid? ApiClientId,
    UsageLedgerEntryType EntryType,
    string OperationCode,
    decimal CreditsCharged,
    string PricingPolicyVersion,
    string IdempotencyKey,
    DateTimeOffset OccurredAt,
    string? ExternalUserId = null,
    string? AuditDescription = null,
    Guid? RelatedEntryId = null,
    string? CompletionStatus = null,
    string? ProviderName = null,
    string? ModelName = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    decimal? EstimatedCost = null,
    string? AllocationSource = null,
    string? AllowanceDateKey = null)
{
    public decimal BalanceImpact => EntryType switch
    {
        UsageLedgerEntryType.Charge => -CreditsCharged,
        UsageLedgerEntryType.Refund => CreditsCharged,
        UsageLedgerEntryType.Adjustment => CreditsCharged,
        _ => throw new InvalidOperationException("Unsupported usage ledger entry type.")
    };
}
