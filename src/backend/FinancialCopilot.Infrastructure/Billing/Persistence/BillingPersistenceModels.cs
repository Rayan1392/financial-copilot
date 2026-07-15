namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class CustomerAccountRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }

    public string AccountType { get; set; } = string.Empty;

    public string BillingMode { get; set; } = string.Empty;

    public decimal? CreditLineApprovedLimit { get; set; }

    public decimal? CreditLineWarningThreshold { get; set; }

    public string? SubscriptionPlanCode { get; set; }
    public DateTimeOffset? SubscriptionEffectiveFrom { get; set; }
    public DateTimeOffset? SubscriptionEffectiveTo { get; set; }
    public long SubscriptionRevision { get; set; }
}

public sealed class WalletProjectionRow
{
    public Guid CustomerAccountId { get; set; }

    public decimal Balance { get; set; }

    public decimal ReservedAmount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }
}

public sealed class UsageReservationRow
{
    public Guid Id { get; set; }

    public Guid CustomerAccountId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string OperationCode { get; set; } = string.Empty;

    public decimal ReservedCredits { get; set; }

    public decimal? CommittedCredits { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FinalizationReason { get; set; }
}

public sealed class UsageLedgerEntryRow
{
    public Guid Id { get; set; }

    public Guid CustomerAccountId { get; set; }

    public Guid ActorId { get; set; }

    public Guid TenantId { get; set; }

    public Guid? ApiClientId { get; set; }

    public string EntryType { get; set; } = string.Empty;

    public string OperationCode { get; set; } = string.Empty;

    public decimal CreditsCharged { get; set; }

    public string PricingPolicyVersion { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string? ExternalUserId { get; set; }

    public string? AuditDescription { get; set; }

    public Guid? RelatedEntryId { get; set; }

    public string? CompletionStatus { get; set; }

    public string? ProviderName { get; set; }

    public string? ModelName { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int? TotalTokens { get; set; }

    public decimal? EstimatedCost { get; set; }

    public string? AllocationSource { get; set; }

    public string? AllowanceDateKey { get; set; }
}

public sealed class DailyFreeAllowanceGrantRow
{
    public Guid Id { get; set; }
    public Guid CustomerAccountId { get; set; }
    public Guid ActorId { get; set; }
    public Guid TenantId { get; set; }
    public string AllowanceDateKey { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid LedgerEntryId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset? ExpiredAtUtc { get; set; }
    public decimal ExpiredCredits { get; set; }
}

public sealed class FinancialTransactionRow
{
    public Guid Id { get; set; }

    public Guid CustomerAccountId { get; set; }

    public string Type { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class SubscriptionPlanRow
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public decimal IncludedCredits { get; set; }

    public string PricingPolicyVersion { get; set; } = string.Empty;
}

public sealed class PlanCapabilityRow
{
    public string PlanCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public decimal? Limit { get; set; }
}

public sealed class InvoiceAccountRow
{
    public Guid CustomerAccountId { get; set; }

    public string LegalName { get; set; } = string.Empty;

    public string BillingEmail { get; set; } = string.Empty;

    public string SettlementTerms { get; set; } = string.Empty;
}

public sealed class BillingOutboxMessageRow
{
    public Guid Id { get; set; }

    public string AggregateType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public string? LastError { get; set; }
}

public sealed class BillingAdminAuditRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
}

public sealed class BillingPurchaseProductRow
{
    public string Code { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Credits { get; set; }
    public string? PlanCode { get; set; }
    public int? DurationDays { get; set; }
    public string Channel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class BillingCheckoutIntentRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public Guid CustomerAccountId { get; set; }
    public string ProductType { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductVersion { get; set; } = string.Empty;
    public string ProductDisplayName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Credits { get; set; }
    public string? PlanCode { get; set; }
    public int? DurationDays { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? ReceiptIdempotencyKey { get; set; }
    public string? ReviewIdempotencyKey { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderReferenceHash { get; set; }
    public string? ReceiptAttachmentKind { get; set; }
    public string? ReceiptAttachmentReference { get; set; }
    public string? ReceiptContentHash { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ReceiptSubmittedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public Guid? ReviewerActorId { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? FulfillmentLedgerEntryId { get; set; }
    public Guid? FulfillmentFinancialTransactionId { get; set; }
    public DateTimeOffset? FulfilledAtUtc { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public int Version { get; set; }
}
