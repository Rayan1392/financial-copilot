namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum RelativeValuationSourceKind
{
    PEGauge,
    PSGauge,
    EquilibriumGauge,
    MarketPrice
}

public enum RelativeValuationFactReadiness
{
    Ready,
    NotFoundOrNoData,
    InvalidPayload,
    IdentityMismatch,
    InvalidNumericValue,
    AuthenticationFailed,
    RateLimited,
    Timeout,
    RemoteServerFailure,
    NetworkFailure
}

public sealed record RelativeValuationProviderFact(
    Guid CompanyId,
    string ProviderName,
    RelativeValuationSourceKind SourceKind,
    string SourceObservationId,
    decimal? CurrentValue,
    decimal? ReferenceValue,
    DateTimeOffset FetchedAtUtc,
    string SourceEndpoint,
    string IdentityEvidence,
    RelativeValuationFactReadiness Readiness,
    string QualityCode,
    string PayloadHash,
    string RawPayload);

public sealed record RelativeValuationProviderResult(
    RelativeValuationSourceKind SourceKind,
    decimal? CurrentValue,
    decimal? ReferenceValue,
    string SourceObservationId,
    string SourceEndpoint,
    string IdentityEvidence,
    RelativeValuationFactReadiness Readiness,
    string QualityCode,
    string PayloadHash,
    string RawPayload,
    DateTimeOffset? FetchedAtUtc = null)
{
    public bool IsSuccess => Readiness == RelativeValuationFactReadiness.Ready;
}

public interface ICyclicalWavesRelativeValuationProviderClient
{
    Task<RelativeValuationProviderResult> GetPeGaugeAsync(
        string isin,
        CancellationToken cancellationToken);

    Task<RelativeValuationProviderResult> GetEquilibriumGaugeAsync(
        string isin,
        CancellationToken cancellationToken);
}

public sealed record PsRelativeValuationProjection(
    Guid CompanyId,
    string ProviderName,
    string SourceObservationId,
    decimal CurrentPS,
    decimal HistoricalAveragePS,
    DateTimeOffset FetchedAtUtc,
    string SourceEndpoint,
    string IdentityEvidence,
    string PayloadHash,
    string RawPayload);

public static class PsRelativeValuationFactProjection
{
    public static PsRelativeValuationProjection FromGauge(
        Guid companyId,
        string providerName,
        string sourceObservationId,
        string sourceIsin,
        PsGaugeDistribution gauge,
        DateTimeOffset fetchedAtUtc,
        string payloadHash,
        string rawPayload)
    {
        if (gauge.GaugeClose <= 0m || gauge.GaugeAverage <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(gauge), "P/S close and avg must be positive.");
        }

        return new PsRelativeValuationProjection(
            companyId,
            providerName,
            sourceObservationId,
            gauge.GaugeClose,
            gauge.GaugeAverage,
            fetchedAtUtc,
            $"ps/circle-chart-data/{sourceIsin}",
            $"requested-isin:{sourceIsin}",
            payloadHash,
            rawPayload);
    }
}
