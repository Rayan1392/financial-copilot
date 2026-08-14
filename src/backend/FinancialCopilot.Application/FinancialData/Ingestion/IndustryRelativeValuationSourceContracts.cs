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
    DateTimeOffset? FetchedAtUtc = null,
    string? SourceWatermark = null)
{
    public bool IsSuccess => Readiness == RelativeValuationFactReadiness.Ready;
}

public sealed record RelativeValuationEligibleSymbol(string? SymbolIsin, Guid? CompanyId = null);

public interface IEligibleUniverseReader
{
    /// <summary>Reads only SymbolIsin from NoavaranEligibleCompanies.</summary>
    Task<IReadOnlyList<RelativeValuationEligibleSymbol>> ReadAsync(CancellationToken cancellationToken);
}

public enum LeaseState { Running, Handoff, Succeeded, Failed }

public sealed record LeaseOwnerId(
    string LeaseName,
    DateOnly CalculationDate,
    Guid FencingToken,
    LeaseState State)
{
    public string Envelope => LeaseFencingEnvelope.Serialize(this);
}

public sealed record LeaseHandle(
    string LeaseName,
    DateOnly CalculationDate,
    Guid FencingToken,
    DateTimeOffset ExpiresAtUtc,
    string? RunId = null,
    string? SupersededRunId = null)
{
    public bool RecoveredLease { get; init; }
    public LeaseOwnerId RunningOwner =>
        new(LeaseName, CalculationDate, FencingToken, LeaseState.Running);
}

public static class LeaseFencingEnvelope
{
    public static string Serialize(LeaseOwnerId owner) =>
        $"v1|{owner.State}|{owner.CalculationDate:yyyy-MM-dd}|{owner.FencingToken:N}";

    public static bool TryParse(string value, out LeaseOwnerId? owner)
    {
        owner = null;
        var parts = value.Split('|');
        if (parts.Length != 4 || parts[0] != "v1" ||
            !Enum.TryParse<LeaseState>(parts[1], ignoreCase: false, out var state) ||
            !DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", out var date) ||
            !Guid.TryParseExact(parts[3], "N", out var token))
            return false;
        owner = new LeaseOwnerId(string.Empty, date, token, state);
        return true;
    }
}

public interface IFeature126LeaseStore
{
    Task<LeaseHandle?> TryAcquireAsync(
        string leaseName,
        DateOnly calculationDate,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<LeaseHandle?> TryAcquireAsync(
        string leaseName,
        DateOnly calculationDate,
        TimeSpan duration,
        CancellationToken cancellationToken,
        string? runId) => TryAcquireAsync(leaseName, calculationDate, duration, cancellationToken);

    Task<bool> RenewAsync(LeaseHandle handle, TimeSpan duration, CancellationToken cancellationToken);

    Task<bool> IsOwnerAsync(LeaseHandle handle, CancellationToken cancellationToken);

    Task<bool> TransitionAsync(
        LeaseHandle handle,
        LeaseState state,
        CancellationToken cancellationToken);
}

public sealed record Feature126LeaseReadiness(bool LiveRow, bool RenewalCapable);

public interface IFeature126LeaseReadinessProbe
{
    Task<Feature126LeaseReadiness> ProbeReadinessAsync(CancellationToken cancellationToken);
}

public interface IFeature126LeaseRecoveryStore
{
    Task<bool> HasSucceededAsync(string leaseName, DateOnly calculationDate, CancellationToken cancellationToken);
}

public enum Feature126SourceFactWriteResult { Persisted, Unchanged, Rejected }

public interface IFeature126SourceFactStore
{
    Task<Feature126SourceFactWriteResult> PersistAcceptedAsync(
        Guid companyId,
        RelativeValuationProviderResult result,
        LeaseHandle owner,
        CancellationToken cancellationToken);

    Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(
        DateOnly calculationDate,
        CancellationToken cancellationToken);

    async Task<Feature126SourceSnapshotEvidence> ReadCurrentSnapshotAsync(
        DateOnly calculationDate,
        IReadOnlyList<RelativeValuationEligibleSymbol> admitted,
        CancellationToken cancellationToken) => await ReadCurrentSnapshotAsync(calculationDate, cancellationToken);
}

public interface IFeature125HandoffSubmissionBoundary
{
    Task<Feature125HandoffValidationResult> SubmitAsync(
        Feature126HandoffPackage package,
        Feature126HandoffLeaseState lease,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
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

public sealed record Feature126MetricOutcome(
    Guid? CompanyId,
    string? SymbolIsin,
    RelativeValuationSourceKind Metric,
    string Status,
    string? FailureCode = null,
    int Attempts = 0);

public sealed record Feature126IngestionRunResult(
    string CorrelationId,
    DateOnly TehranDate,
    int AdmittedSymbols,
    int PagesProcessed,
    int SuccessfulAcquisitions,
    int FailedAcquisitions,
    int PartialCompanies,
    int SkippedSymbols,
    int FactsPersisted,
    int FactsUnchanged,
    IReadOnlyList<Feature126MetricOutcome> Outcomes)
{
    public Feature126OperationalSummary? OperationalSummary { get; init; }
}

public sealed record IndustryRelativeValuationOrchestrationResult(
    string CorrelationId,
    int CompaniesConsidered,
    int FactsPersisted,
    int FactsUnchanged,
    int SourceFailures,
    int IndustriesCalculated,
    int PublishedSnapshots,
    int InconclusiveSnapshots);

/// <summary>
/// Application boundary for the Feature 125 downstream calculation pipeline.
/// Scheduling and retry ownership remain with the existing worker workflow.
/// </summary>
public interface IIndustryRelativeValuationOrchestrationService
{
    Task<IndustryRelativeValuationOrchestrationResult> RunAsync(
        string correlationId,
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
