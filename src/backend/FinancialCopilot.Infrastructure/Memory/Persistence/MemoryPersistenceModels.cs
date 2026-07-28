namespace FinancialCopilot.Infrastructure.Memory.Persistence;

public sealed class MemoryConsentPolicyRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubjectId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public DateTimeOffset? GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class MemoryRecordRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubjectId { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Sensitivity { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int MemoryVersion { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string ProvenanceSourceType { get; set; } = string.Empty;
    public string? ProvenanceSourceRef { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class MemoryAuditEventRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? MemoryId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Reason { get; set; }
}
