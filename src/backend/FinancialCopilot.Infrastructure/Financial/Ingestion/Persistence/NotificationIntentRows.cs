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
    public string Category { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public Guid? SourceEventId { get; set; }
    public string? EvidenceReference { get; set; }
    public string? CooldownKey { get; set; }
    public string? PolicyVersion { get; set; }
    public int? PreferenceVersion { get; set; }
    public string? DecisionReason { get; set; }
    public string? DecisionExplanation { get; set; }
    public DateTimeOffset? DecisionAtUtc { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorRedacted { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? SuppressedAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}

public sealed class NotificationPreferenceRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string DeliveryMode { get; set; } = string.Empty;
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }
    public string MinimumSeverity { get; set; } = string.Empty;
    public int DailyCap { get; set; }
    public TimeOnly DigestTime { get; set; }
    public int CooldownMinutes { get; set; }
    public int Version { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class NotificationCategoryPreferenceRow
{
    public Guid Id { get; set; }
    public Guid PreferenceId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? MinimumSeverity { get; set; }
    public int? CooldownMinutes { get; set; }
}

public sealed class NotificationSymbolPreferenceRow
{
    public Guid Id { get; set; }
    public Guid PreferenceId { get; set; }
    public string ExternalCompanyId { get; set; } = string.Empty;
    public bool Muted { get; set; }
}

public sealed class NotificationPreferenceAuditRow
{
    public Guid Id { get; set; }
    public Guid PreferenceId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class NotificationBatchRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTimeOffset ScheduledForUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MaximumItems { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
}

public sealed class NotificationDeliveryAttemptRow
{
    public Guid Id { get; set; }
    public Guid NotificationIntentId { get; set; }
    public int PartNumber { get; set; }
    public string DeliveryPartKey { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorRedacted { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? NextRetryAtUtc { get; set; }
}

public sealed class NotificationOutcomeHandoffRow
{
    public Guid Id { get; set; }
    public Guid NotificationIntentId { get; set; }
    public int Sequence { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string TerminalStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

public sealed class NotificationOperationAuditRow
{
    public Guid Id { get; set; }
    public Guid NotificationIntentId { get; set; }
    public Guid OperatorActorId { get; set; }
    public Guid OperatorTenantId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
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

public sealed class UserAlertRecordRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid NotificationIntentId { get; set; }
    public Guid OutcomeHandoffId { get; set; }
    public int OutcomeSequence { get; set; }
    public Guid? SourceEventId { get; set; }
    public Guid? AlertRuleId { get; set; }
    public Guid? AlertRuleTriggerId { get; set; }
    public string SymbolKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string DeliveryReason { get; set; } = string.Empty;
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? SuppressedAtUtc { get; set; }
    public DateTimeOffset? DeadLetteredAtUtc { get; set; }
    public DateTimeOffset TerminalAtUtc { get; set; }
    public string? EvidenceReference { get; set; }
    public string EvidenceSnapshotJson { get; set; } = "{}";
    public string EvidenceHash { get; set; } = string.Empty;
    public string DetectorVersion { get; set; } = string.Empty;
    public int? RuleVersion { get; set; }
    public int? PreferenceVersion { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string WhyText { get; set; } = string.Empty;
    public string SimilarityKey { get; set; } = string.Empty;
    public DateTimeOffset? DismissedAtUtc { get; set; }
    public DateTimeOffset? RestoredAtUtc { get; set; }
    public DateTimeOffset? MutedAtUtc { get; set; }
    public string? MutedScope { get; set; }
    public string? Feedback { get; set; }
    public DateTimeOffset? FeedbackAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset RetainEvidenceUntilUtc { get; set; }
    public DateTimeOffset RetainFeedbackUntilUtc { get; set; }
    public DateTimeOffset? RedactedAtUtc { get; set; }
}

public sealed class UserAlertDeliveryTimelineRow
{
    public Guid Id { get; set; }
    public Guid UserAlertRecordId { get; set; }
    public Guid NotificationIntentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? AttemptNumber { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class UserAlertReactionSnapshotRow
{
    public Guid Id { get; set; }
    public Guid UserAlertRecordId { get; set; }
    public string HorizonCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CalculationVersion { get; set; } = string.Empty;
    public decimal? AnchorPrice { get; set; }
    public DateTimeOffset? AnchorAtUtc { get; set; }
    public decimal? ReactionPercent { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset? CalculatedAtUtc { get; set; }
    public string InputRevision { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
