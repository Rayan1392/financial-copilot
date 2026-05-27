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
