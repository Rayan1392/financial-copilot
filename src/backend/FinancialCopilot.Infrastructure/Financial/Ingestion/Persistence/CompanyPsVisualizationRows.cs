namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyPsGaugeSnapshotRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceCompanyIsin { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public DateOnly ObservationDate { get; set; }
    public decimal TtmPsRatio { get; set; }
    public decimal ForwardPsRatio { get; set; }
    public decimal GaugeClose { get; set; }
    public decimal BoundaryStart { get; set; }
    public decimal BoundaryMin { get; set; }
    public decimal BoundaryAverage { get; set; }
    public decimal BoundaryMax { get; set; }
    public decimal BoundaryEnd { get; set; }
    public long BucketA { get; set; }
    public long BucketB { get; set; }
    public long BucketC { get; set; }
    public long BucketD { get; set; }
    public long BucketE { get; set; }
    public long BucketF { get; set; }
    public long BucketTotal { get; set; }
    public string? ProviderSymbol { get; set; }
    public DateTimeOffset GaugeFetchedAtUtc { get; set; }
    public DateTimeOffset CurrentValuesFetchedAtUtc { get; set; }
    public DateTimeOffset LastSyncedAtUtc { get; set; }
    public string CompletenessStatus { get; set; } = "Complete";
    public string GaugeRenderabilityStatus { get; set; } = "UnverifiedSemantics";
    public string QualityStatus { get; set; } = "Valid";
    public string QualityWarningsJson { get; set; } = "[]";
    public string GaugePayloadHash { get; set; } = string.Empty;
    public string CurrentValuesPayloadHash { get; set; } = string.Empty;
    public string NormalizedSnapshotHash { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAtUtc { get; set; }
}

public sealed class CompanyPsHistoryPointRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceCompanyIsin { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderPointId { get; set; } = string.Empty;
    public DateOnly ObservationDate { get; set; }
    public decimal PsRatio { get; set; }
    public bool IsActiveInLatestSuccessfulSeries { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? LastSeenInSuccessfulSeriesAtUtc { get; set; }
    public string SourcePayloadHash { get; set; } = string.Empty;
}

public sealed class CompanyPsSeriesSyncStateRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string SourceCompanyIsin { get; set; } = string.Empty;
    public DateOnly? DeclaredFirstHistoryDate { get; set; }
    public DateOnly? DeclaredLastHistoryDate { get; set; }
    public long? DeclaredHistoryCount { get; set; }
    public DateOnly? ActualFirstHistoryDate { get; set; }
    public DateOnly? ActualLastHistoryDate { get; set; }
    public long? ActualHistoryCount { get; set; }
    public string? NormalizedLatestSuccessfulSeriesHash { get; set; }
    public Guid? LastSuccessfulSnapshotId { get; set; }
    public DateOnly? LastSuccessfulSnapshotDate { get; set; }
    public DateTimeOffset? LastGaugeSuccessAtUtc { get; set; }
    public DateTimeOffset? LastCurrentValuesSuccessAtUtc { get; set; }
    public DateTimeOffset? LastHistorySuccessAtUtc { get; set; }
    public DateTimeOffset? LastGaugeAttemptAtUtc { get; set; }
    public DateTimeOffset? LastCurrentValuesAttemptAtUtc { get; set; }
    public DateTimeOffset? LastHistoryAttemptAtUtc { get; set; }
    public DateTimeOffset? LastCompleteHistoryRefreshAtUtc { get; set; }
    public int ConsecutiveGaugeFailures { get; set; }
    public int ConsecutiveCurrentValuesFailures { get; set; }
    public int ConsecutiveHistoryFailures { get; set; }
    public string LastWarningCodesJson { get; set; } = "[]";
    public string? LastErrorCode { get; set; }
    public bool BackfillCompleted { get; set; }
    public DateTimeOffset? NextEligibleHistoryRefreshAtUtc { get; set; }
    public string? LastSuccessfulCorrelationId { get; set; }
}

/// <summary>Singleton database-backed lease shared by worker and DataAdmin P/S sync runs.</summary>
public sealed class CompanyPsVisualizationLeaseRow
{
    public string LeaseName { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
