namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class NotificationIntentRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class CodalAlertSummaryRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid InsightEventId { get; set; }
    public Guid? NotificationIntentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string? SummaryText { get; set; }
    public string? ProviderName { get; set; }
    public string? ModelName { get; set; }
    public string PromptPolicyVersion { get; set; } = string.Empty;
    public string? ReservationIdempotencyKey { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
