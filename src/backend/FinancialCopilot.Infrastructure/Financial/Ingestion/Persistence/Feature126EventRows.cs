namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class Feature126EventStreamRow
{
    public string RunId { get; set; } = string.Empty;
    public string TehranDate { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public Guid FencingToken { get; set; }
    public long NextSequence { get; set; }
    public string State { get; set; } = string.Empty;
    public bool IsTerminal { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class Feature126EventRow
{
    public string EventId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public long EventSequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ExpectedPredecessorState { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public Guid FencingToken { get; set; }
    public string TehranDate { get; set; } = string.Empty;
    public string AttemptReason { get; set; } = string.Empty;
    public string? RecoveredFromRunId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public int SchemaVersion { get; set; }
    public DateTimeOffset AppendedAtUtc { get; set; }
}
