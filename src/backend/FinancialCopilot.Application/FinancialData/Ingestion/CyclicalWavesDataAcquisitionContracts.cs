namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum CyclicalWavesMetricType
{
    PS,
    PE,
    Equilibrium
}

public enum CyclicalWavesAcquisitionResult
{
    Changed,
    NoChange,
    Failed
}

public static class CyclicalWavesAcquisitionFailureCodes
{
    public const string AuthenticationFailed = nameof(AuthenticationFailed);
    public const string ContractMismatch = nameof(ContractMismatch);
    public const string HttpClientError = nameof(HttpClientError);
    public const string IdentityMismatch = nameof(IdentityMismatch);
    public const string InvalidJson = nameof(InvalidJson);
    public const string MissingSymbolIsin = nameof(MissingSymbolIsin);
    public const string NetworkError = nameof(NetworkError);
    public const string NotFoundOrNoData = nameof(NotFoundOrNoData);
    public const string PersistenceFailure = nameof(PersistenceFailure);
    public const string ProviderServerError = nameof(ProviderServerError);
    public const string RateLimited = nameof(RateLimited);
    public const string Timeout = nameof(Timeout);
    public const string UnexpectedFailure = nameof(UnexpectedFailure);
}

public sealed record CyclicalWavesAcquisitionCompany(
    Guid CompanyId,
    string ExternalCompanyId,
    string? CompanySymbol,
    string? SymbolIsin);

public sealed record CyclicalWavesProviderAcquisitionResult(
    CyclicalWavesMetricType MetricType,
    string SourceEndpoint,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset? AcquisitionDateUtc,
    DateTimeOffset CompletedAtUtc,
    string? RawResponseJson,
    int? HttpStatusCode,
    short AttemptCount,
    string? FailureCode,
    string? FailureMessage)
{
    public bool IsAccepted =>
        RawResponseJson is not null &&
        FailureCode is null &&
        HttpStatusCode is >= 200 and <= 299;
}

public sealed record CyclicalWavesAcceptedAcquisition(
    DateOnly CycleDateUtc,
    Guid CompanyId,
    string SymbolIsin,
    CyclicalWavesMetricType MetricType,
    string RawResponseJson,
    string ResponseHash,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset AcquisitionDateUtc,
    DateTimeOffset CompletedAtUtc,
    string SourceEndpoint,
    int HttpStatusCode,
    short AttemptCount);

public sealed record CyclicalWavesFailedAcquisition(
    DateOnly CycleDateUtc,
    Guid CompanyId,
    string? SymbolIsin,
    CyclicalWavesMetricType MetricType,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset? RequestedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string SourceEndpoint,
    int? HttpStatusCode,
    short AttemptCount,
    string FailureCode,
    string FailureMessage);

public sealed record CyclicalWavesPersistenceResult(
    Guid CheckId,
    Guid SnapshotId,
    CyclicalWavesAcquisitionResult Result);

public sealed record CyclicalWavesAcquisitionCycleSummary(
    DateOnly CycleDateUtc,
    int Changed,
    int Unchanged,
    int Failed,
    int Skipped);

public interface ICyclicalWavesAcquisitionCompanySource
{
    Task<IReadOnlyList<CyclicalWavesAcquisitionCompany>> GetCompaniesAsync(
        CancellationToken cancellationToken);
}

public interface ICyclicalWavesDataAcquisitionClient
{
    Task<CyclicalWavesProviderAcquisitionResult> AcquireAsync(
        CyclicalWavesMetricType metricType,
        string normalizedIsin,
        CancellationToken cancellationToken);
}

public interface ICanonicalJsonHasher
{
    string ComputeHash(string rawJson);
}

public interface ICyclicalWavesDataAcquisitionRepository
{
    Task<bool> HasSuccessfulCheckAsync(
        DateOnly cycleDateUtc,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        CancellationToken cancellationToken);

    Task<CyclicalWavesPersistenceResult> PersistAcceptedAsync(
        CyclicalWavesAcceptedAcquisition acquisition,
        CancellationToken cancellationToken);

    Task<Guid> PersistFailedAsync(
        CyclicalWavesFailedAcquisition acquisition,
        CancellationToken cancellationToken);
}

public interface ICyclicalWavesDataAcquisitionService
{
    Task<CyclicalWavesAcquisitionCycleSummary> ExecuteAsync(
        DateOnly cycleDateUtc,
        CancellationToken cancellationToken);
}

public sealed record CyclicalWavesMetricSnapshot(
    Guid SnapshotId,
    Guid CompanyId,
    string ProviderName,
    CyclicalWavesMetricType MetricType,
    string RawResponseJson,
    string ResponseHash,
    DateTimeOffset AcquisitionDateUtc,
    DateTimeOffset SnapshotCreatedAtUtc,
    Guid AcquisitionCheckId,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset CheckCreatedAtUtc);

public interface ICyclicalWavesMetricSnapshotReader
{
    Task<IReadOnlyList<CyclicalWavesMetricSnapshot>> ReadLatestAsync(
        IReadOnlyCollection<Guid> companyIds,
        CancellationToken cancellationToken);
}
