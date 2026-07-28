namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class AlertRuleRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ExternalCompanyId { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string MetricOrEventCode { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public decimal Threshold { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int? BaselineWindow { get; set; }
    public string Recurrence { get; set; } = string.Empty;
    public int CooldownMinutes { get; set; }
    public string ResetPolicy { get; set; } = string.Empty;
    public string SessionPolicy { get; set; } = string.Empty;
    public decimal? Hysteresis { get; set; }
    public string State { get; set; } = string.Empty;
    public int Version { get; set; }
    public string? OriginalText { get; set; }
    public string? ParserVersion { get; set; }
    public string ConfirmationNonce { get; set; } = string.Empty;
    public DateTimeOffset ConfirmationExpiresAtUtc { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
}

public sealed class AlertRuleEvaluationStateRow
{
    public Guid RuleId { get; set; }
    public decimal? LastValue { get; set; }
    public DateTimeOffset? LastObservedAtUtc { get; set; }
    public string? LastEvidenceIdentity { get; set; }
    public bool Armed { get; set; }
    public int TriggerSequence { get; set; }
    public DateTimeOffset? LastTriggeredAtUtc { get; set; }
    public DateTimeOffset? CooldownEndsAtUtc { get; set; }
    public DateTimeOffset? LastEvaluatedAtUtc { get; set; }
    public string? LastDecision { get; set; }
    public string? LastSkipReason { get; set; }
    public Guid ConcurrencyToken { get; set; }
}

public sealed class AlertRuleTriggerRow
{
    public Guid Id { get; set; }
    public Guid RuleId { get; set; }
    public int RuleVersion { get; set; }
    public int TriggerSequence { get; set; }
    public string EvidenceIdentity { get; set; } = string.Empty;
    public string DeduplicationKey { get; set; } = string.Empty;
    public decimal ObservedValue { get; set; }
    public decimal Threshold { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string SourceProvider { get; set; } = string.Empty;
    public string? SourcePeriod { get; set; }
    public DateTimeOffset SourceFreshnessUtc { get; set; }
    public DateTimeOffset TriggeredAtUtc { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public Guid? NotificationIntentId { get; set; }
}
