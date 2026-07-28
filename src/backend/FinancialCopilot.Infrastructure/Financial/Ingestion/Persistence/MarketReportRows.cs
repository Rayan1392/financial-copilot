namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class MarketReportRow
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorType { get; set; }
    public DateOnly TradingDate { get; set; }
    public string WindowKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int Revision { get; set; }
    public Guid? SupersedesReportId { get; set; }
    public string ReportVersion { get; set; } = string.Empty;
    public string EvidenceSchemaVersion { get; set; } = string.Empty;
    public string PromptPolicyVersion { get; set; } = string.Empty;
    public string RenderingPolicyVersion { get; set; } = string.Empty;
    public string SafetyPolicyVersion { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public string SnapshotIdsJson { get; set; } = "[]";
    public string InsightEventIdsJson { get; set; } = "[]";
    public string? Narrative { get; set; }
    public string CaveatsJson { get; set; } = "[]";
    public decimal Confidence { get; set; }
    public string? ProviderName { get; set; }
    public string? ModelName { get; set; }
    public string ModelMetadataJson { get; set; } = "{}";
    public string GenerationIdempotencyKey { get; set; } = string.Empty;
    public string? ReservationIdempotencyKey { get; set; }
    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
