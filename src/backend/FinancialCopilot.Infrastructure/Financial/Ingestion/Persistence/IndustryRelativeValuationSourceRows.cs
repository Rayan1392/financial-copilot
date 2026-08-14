namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class IndustryRelativeValuationSourceFactRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceObservationId { get; set; } = string.Empty;
    public decimal? CurrentValue { get; set; }
    public decimal? ReferenceValue { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public DateTimeOffset PersistedAtUtc { get; set; }
    public string SourceEndpoint { get; set; } = string.Empty;
    public string SourceWatermark { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string Readiness { get; set; } = string.Empty;
    public string QualityCode { get; set; } = string.Empty;
    public string IdentityEvidence { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
}

public sealed class IndustryRelativeValuationSourceLeaseRow
{
    public string LeaseName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string? CurrentRunId { get; set; }
    public string? SupersededRunId { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
