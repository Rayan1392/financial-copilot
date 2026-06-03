namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record NadpcoApiSyncResult(
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    int RequestsEnqueued,
    DateTimeOffset? OverlapFrom,
    DateTimeOffset? AdvancedWatermark,
    TimeSpan Duration);

public sealed record NadpcoApiSyncState(
    string Dataset,
    DateTimeOffset? LastSuccessfulSyncAt,
    DateTimeOffset? LastOverlapFrom,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    int LastCompaniesConsidered,
    int LastCompaniesEnqueued,
    int LastFailedCompanies,
    string? LastError);

public interface INadpcoApiScheduledSyncService
{
    Task<NadpcoApiSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken);
}

public interface INadpcoApiSyncStateReader
{
    Task<IReadOnlyCollection<NadpcoApiSyncState>> QueryAsync(CancellationToken cancellationToken);
}
