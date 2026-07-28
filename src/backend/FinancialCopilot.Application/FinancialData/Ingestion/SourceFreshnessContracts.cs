using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Per-source ingestion freshness (spec 051). Archive sources report freshness differently from
/// current API sources: an archive that completed its one-time import is <see cref="IsFrozenArchive"/>
/// and is <b>not</b> stale just because it has not run recently, whereas a current source's freshness
/// is measured against its last successful run.
/// </summary>
public sealed record SourceFreshness(
    LogicalVendor Vendor,
    PhysicalSource Source,
    SourceMode Mode,
    string SourceName,
    bool IsFrozenArchive,
    DateTimeOffset? LastSuccessfulRunAt,
    int RecentSuccessfulRuns,
    int RecentFailedRuns);

public interface ISourceFreshnessReader
{
    /// <summary>Summarizes recent ingestion freshness per catalogued source.</summary>
    Task<IReadOnlyCollection<SourceFreshness>> QueryAsync(
        int recentRunSampleSize,
        CancellationToken cancellationToken);
}
