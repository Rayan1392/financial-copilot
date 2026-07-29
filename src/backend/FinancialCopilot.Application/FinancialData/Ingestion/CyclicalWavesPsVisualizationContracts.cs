namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Provider-neutral result classification for the P/S visualization feed.</summary>
public enum PsVisualizationSyncErrorCode
{
    None,
    NotFoundOrNoData,
    AuthenticationFailed,
    RateLimited,
    TimeoutOrNetworkFailure,
    RemoteServerFailure,
    PayloadTooLarge,
    InvalidJsonOrContract,
    IdentityMismatch,
    DataQualityRejected,
    Cancelled
}

public enum GaugeRenderabilityStatus
{
    UnverifiedSemantics,
    Renderable,
    InvalidBoundaries,
    InvalidBucketTotal,
    ProviderUnavailable
}

public enum PsVisualizationComponentStatus { Unavailable, Invalid, Partial, Complete }

public sealed record PsEligibleCompany(Guid CompanyId, string CompanyIsin);

public sealed record PsScopeIssue(Guid? CompanyId, string? CompanyIsin, string Code);

public sealed record PsEligibleCompanyScope(
    int EligibleRowsRead,
    int DuplicateRowsRemoved,
    int SkippedMissingOrInvalidIsins,
    IReadOnlyList<PsEligibleCompany> Companies,
    IReadOnlyList<PsScopeIssue> Issues);

public sealed record PsGaugeDistribution(
    long BucketA, long BucketB, long BucketC, long BucketD, long BucketE, long BucketF,
    decimal GaugeClose, decimal BoundaryStart, decimal BoundaryMin, decimal BoundaryAverage,
    decimal BoundaryMax, decimal BoundaryEnd);

public sealed record PsCurrentValues(string Symbol, string Ticker, decimal TtmPsRatio, decimal ForwardPsRatio, DateOnly ObservationDate);

public sealed record PsHistoryPoint(string ProviderPointId, DateOnly ObservationDate, decimal PsRatio);

public sealed record PsHistorySeries(
    IReadOnlyList<PsHistoryPoint> Points, DateOnly? DeclaredFirstDate, DateOnly? DeclaredLastDate, long? DeclaredCount);

public sealed record PsProviderResult<T>(T? Value, PsVisualizationSyncErrorCode ErrorCode, string? WarningCode = null)
{
    public bool IsSuccess => ErrorCode == PsVisualizationSyncErrorCode.None && Value is not null;
}

public interface ICyclicalWavesPsProviderClient
{
    Task<PsProviderResult<PsGaugeDistribution>> GetGaugeAsync(string companyIsin, CancellationToken cancellationToken);
    Task<PsProviderResult<PsCurrentValues>> GetCurrentValuesAsync(string companyIsin, CancellationToken cancellationToken);
    Task<PsProviderResult<PsHistorySeries>> GetHistoryAsync(string companyIsin, CancellationToken cancellationToken);
}

public interface IPsEligibleCompanyScopeReader
{
    Task<PsEligibleCompanyScope> ReadAsync(int? maxCompanies, CancellationToken cancellationToken);
}

public sealed record PsVisualizationReadModel(
    Guid CompanyId,
    PsVisualizationComponentStatus CompletenessStatus,
    GaugeRenderabilityStatus GaugeRenderabilityStatus,
    DateOnly? SnapshotObservationDate,
    DateTimeOffset? LastSnapshotSyncAtUtc,
    DateTimeOffset? LastHistorySyncAtUtc,
    IReadOnlyList<string> WarningCodes,
    IReadOnlyList<PsHistoryPoint> HistoryPoints,
    PsPersistedSnapshotFacts? Snapshot = null);

public sealed record PsPersistedSnapshotFacts(
    string ProviderName,
    string? ProviderSymbol,
    decimal TtmPsRatio,
    decimal ForwardPsRatio,
    decimal GaugeClose,
    decimal BoundaryStart,
    decimal BoundaryMin,
    decimal BoundaryAverage,
    decimal BoundaryMax,
    decimal BoundaryEnd,
    long BucketA,
    long BucketB,
    long BucketC,
    long BucketD,
    long BucketE,
    long BucketF,
    DateTimeOffset LastSyncedAtUtc);

public interface ICompanyPsVisualizationReader
{
    Task<PsVisualizationReadModel?> GetAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed record PsVisualizationSyncRequest(
    bool DryRun = false,
    int? MaxCompanies = null,
    Guid? CompanyId = null,
    bool SnapshotOnly = false,
    bool HistoryOnly = false,
    string? CorrelationId = null);

public sealed record PsVisualizationSyncResult(
    string CorrelationId, int CompaniesConsidered, int SnapshotSucceeded, int HistorySucceeded,
    int Failed, int Unchanged, IReadOnlyList<PsScopeIssue> ScopeIssues);

public interface ICyclicalWavesPsVisualizationSyncService
{
    Task<PsVisualizationSyncResult> SyncAsync(PsVisualizationSyncRequest request, CancellationToken cancellationToken);
}
