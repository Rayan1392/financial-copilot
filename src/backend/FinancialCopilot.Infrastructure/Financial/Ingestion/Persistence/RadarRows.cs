namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class RadarProfileRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string EventTypesJson { get; set; } = "[]";
    public string MinimumSeverity { get; set; } = string.Empty;
    public decimal MinimumImportance { get; set; }
    public string Sensitivity { get; set; } = string.Empty;
    public string DeliveryMode { get; set; } = string.Empty;
    public int Version { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public DateTimeOffset? LastEvaluatedAtUtc { get; set; }
    public DateTimeOffset? LastSourceFreshnessUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastFailure { get; set; }
}

public sealed class RadarSymbolOverrideRow
{
    public Guid Id { get; set; }
    public Guid RadarProfileId { get; set; }
    public string ExternalCompanyId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? EventTypesJson { get; set; }
    public string? MinimumSeverity { get; set; }
    public decimal? MinimumImportance { get; set; }
    public string? Sensitivity { get; set; }
    public int Version { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}

public sealed class RadarEventMatchRow
{
    public Guid Id { get; set; }
    public Guid RadarProfileId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid InsightEventId { get; set; }
    public string ExternalCompanyId { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string SuppressionReason { get; set; } = string.Empty;
    public string AppliedSensitivity { get; set; } = string.Empty;
    public string AppliedPolicyVersion { get; set; } = string.Empty;
    public string NotificationPolicyVersion { get; set; } = string.Empty;
    public decimal MatchScore { get; set; }
    public decimal HistoricalPercentile { get; set; }
    public string ComponentInsightEventIdsJson { get; set; } = "[]";
    public string EvidenceReference { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public Guid? NotificationIntentId { get; set; }
    public DateTimeOffset SourceFreshnessUtc { get; set; }
    public DateTimeOffset EvaluatedAtUtc { get; set; }
}

public sealed class RadarPreferenceAuditRow
{
    public Guid Id { get; set; }
    public Guid RadarProfileId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTimeOffset OccurredAtUtc { get; set; }
}
