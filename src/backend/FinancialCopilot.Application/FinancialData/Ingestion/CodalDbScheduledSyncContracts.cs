namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed record CodalDbScheduledSyncResult(
    bool FullReload,
    int CompaniesConsidered,
    int CompaniesEnqueued,
    int FailedCompanies,
    IReadOnlyCollection<int> FailedCompanyIds,
    DateTimeOffset? AdvancedWatermark,
    TimeSpan Duration);

public interface ICodalDbScheduledSyncService
{
    /// <param name="fullReload">When true, ignore the persisted watermark and enqueue every
    /// company present in any CodalDB source table. When false, only enqueue companies whose
    /// <c>ModifiedDateTime</c> is newer than the persisted watermark.</param>
    Task<CodalDbScheduledSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken);
}
