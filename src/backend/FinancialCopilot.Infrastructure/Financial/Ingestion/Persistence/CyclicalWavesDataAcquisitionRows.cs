namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CyclicalWavesMetricSnapshotRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SymbolIsin { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string MetricType { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = string.Empty;
    public string ResponseHash { get; set; } = string.Empty;
    public DateTimeOffset AcquisitionDateUtc { get; set; }
    public DateOnly? ProviderObservationDate { get; set; }
    public string SourceEndpoint { get; set; } = string.Empty;
    public Guid? PreviousSnapshotId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CyclicalWavesAcquisitionCheckRow
{
    public Guid Id { get; set; }
    public DateOnly CycleDateUtc { get; set; }
    public Guid CompanyId { get; set; }
    public string? SymbolIsin { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string MetricType { get; set; } = string.Empty;
    public DateTimeOffset CheckedAtUtc { get; set; }
    public DateTimeOffset? RequestedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public string? ResponseHash { get; set; }
    public string Result { get; set; } = string.Empty;
    public Guid? SnapshotId { get; set; }
    public string SourceEndpoint { get; set; } = string.Empty;
    public short? HttpStatusCode { get; set; }
    public short AttemptCount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
