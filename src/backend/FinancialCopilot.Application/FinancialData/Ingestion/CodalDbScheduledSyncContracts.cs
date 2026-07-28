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
    /// <param name="dryRun">When true, compute the companies that would be processed and report the
    /// counts without enqueuing any <c>DataSyncRequest</c> or advancing the watermark (spec 052
    /// dry-run). The result's <c>CompaniesEnqueued</c> is the count that <em>would</em> be enqueued.</param>
    Task<CodalDbScheduledSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken,
        bool dryRun = false);
}
