using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Reports ingestion freshness per catalogued source from <c>ProviderSyncRuns</c> provenance
/// (spec 051). Archive sources are reported as frozen archives whose freshness does not decay; current
/// sources are reported with their last successful run time. The recent-run sample is bounded to keep
/// the query cheap (Release It!: bounded result sets).
/// </summary>
public sealed class SourceFreshnessReader(FinancialIngestionDbContext dbContext) : ISourceFreshnessReader
{
    public async Task<IReadOnlyCollection<SourceFreshness>> QueryAsync(
        int recentRunSampleSize,
        CancellationToken cancellationToken)
    {
        if (recentRunSampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recentRunSampleSize));
        }

        var completedStatus = DataSyncRunStatus.Completed.ToString();
        var failedStatus = DataSyncRunStatus.Failed.ToString();

        var results = new List<SourceFreshness>(ProviderSources.All.Count);
        foreach (var descriptor in ProviderSources.All)
        {
            // Bounded recent window for this source's runs.
            var recentRuns = await dbContext.SyncRuns.AsNoTracking()
                .Where(row => row.ProviderName == descriptor.SourceName)
                .OrderByDescending(row => row.RequestedAt)
                .Take(recentRunSampleSize)
                .Select(row => new { row.Status, row.CompletedAt })
                .ToListAsync(cancellationToken);

            var lastSuccessful = recentRuns
                .Where(run => run.Status == completedStatus)
                .Select(run => run.CompletedAt)
                .Where(at => at is not null)
                .DefaultIfEmpty(null)
                .Max();

            var isFrozenArchive = descriptor.DefaultMode == SourceMode.ArchiveOneTime &&
                lastSuccessful is not null;

            results.Add(new SourceFreshness(
                descriptor.Vendor,
                descriptor.Source,
                descriptor.DefaultMode,
                descriptor.SourceName,
                isFrozenArchive,
                lastSuccessful,
                recentRuns.Count(run => run.Status == completedStatus),
                recentRuns.Count(run => run.Status == failedStatus)));
        }

        return results;
    }
}
