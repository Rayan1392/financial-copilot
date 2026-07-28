namespace FinancialCopilot.Application.FinancialData.Ingestion;

public enum NadpcoApiSyncRunMode
{
    FullSync = 0,
    IncrementalSync = 1,
    CompanyCatalogCleanSlate = 2,
    CompanyCatalogRefresh = 3
}

public sealed record NadpcoApiSyncResult(
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    int RequestsEnqueued,
    DateTimeOffset? OverlapFrom,
    DateTimeOffset? AdvancedWatermark,
    TimeSpan Duration,
    NadpcoApiSyncRunMode RunMode = NadpcoApiSyncRunMode.FullSync,
    NadpcoCompanyCatalogCleanSlateResult? CleanSlate = null);

public sealed record NadpcoApiSyncState(
    string Dataset,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastOverlapFrom,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    int LastCompaniesConsidered,
    int LastCompaniesEnqueued,
    int LastFailedCompanies,
    string? LastRunMode,
    string? LastError);

public interface INadpcoApiScheduledSyncService
{
    /// <param name="fromShamsiYearOverride">When set, stamped on every enqueued company-scoped
    /// <c>DataSyncRequest</c> so the worker lowers the current-API Shamsi start boundary for this run
    /// only (spec 053 backfill). Null = use the configured boundary.</param>
    Task<NadpcoApiSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken,
        int? fromShamsiYearOverride = null);

    Task<NadpcoApiSyncResult> ExecuteCompanyCatalogAsync(
        bool cleanSlate,
        CancellationToken cancellationToken);
}

public interface INadpcoApiSyncStateReader
{
    Task<IReadOnlyCollection<NadpcoApiSyncState>> QueryAsync(CancellationToken cancellationToken);
}

public sealed record NadpcoCompanyCatalogCleanSlateResult(
    int MetricRecalculationRequestsDeleted,
    int FeatureComputationJobsDeleted,
    int FeatureSnapshotsDeleted,
    int DerivedMetricsDeleted,
    int SymbolsDeleted,
    int TradingInstrumentLinksCleared,
    int CompaniesDeleted);

public interface INadpcoCompanyCatalogCleanSlateService
{
    Task<NadpcoCompanyCatalogCleanSlateResult> ClearAsync(CancellationToken cancellationToken);
}
