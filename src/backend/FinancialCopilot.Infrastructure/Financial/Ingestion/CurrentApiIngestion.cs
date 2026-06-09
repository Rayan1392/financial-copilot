using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
// SourcePriorityOptions is consumed by the gap reader via IOptions.

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Reports where the Noavaran current API covers (company, dataset, Gregorian fiscal year) periods
/// at/after the configured Shamsi boundary that the frozen archive lacks (spec 053 AC #4). Pure read
/// over the normalized rows keyed by physical source name; never mutates archive freeze/import state.
/// The boundary is compared against the Gregorian calendar year (boundary Shamsi year + 621), a
/// deterministic approximation sufficient for a coverage report.
/// </summary>
public sealed class EfCoreCurrentApiGapReader(
    FinancialIngestionDbContext dbContext,
    IOptions<SourcePriorityOptions> priorityOptions) : ICurrentApiGapReader
{
    private const string ArchiveSource = ProviderSources.NoavaranArchiveSqlName;
    private const string CurrentSource = ProviderSources.NoavaranCurrentApiName;

    public async Task<CurrentApiGapReport> ReportAsync(CancellationToken cancellationToken)
    {
        var boundaryShamsiYear = priorityOptions.Value.CurrentApiBoundaryShamsiYear;
        // Shamsi year N starts within Gregorian year N+621; current-API coverage is owned from the
        // boundary year onward, so only consider rows whose Gregorian fiscal year is at/after it.
        var minGregorianYear = boundaryShamsiYear + 621;

        var statementGaps = await BuildGapsAsync(
            dbContext.FinancialStatements.AsNoTracking().Select(row =>
                new CoverageProjection(row.ProviderName, row.ExternalCompanyId, row.PeriodEnd.Year)),
            ArchiveImportDataset.FinancialStatements.ToString(),
            minGregorianYear,
            cancellationToken);

        var monthlyGaps = await BuildGapsAsync(
            dbContext.MonthlyReports.AsNoTracking().Select(row =>
                new CoverageProjection(row.ProviderName, row.ExternalCompanyId, row.PeriodEnd.Year)),
            ArchiveImportDataset.MonthlyActivity.ToString(),
            minGregorianYear,
            cancellationToken);

        var gaps = statementGaps.Concat(monthlyGaps)
            .OrderBy(gap => gap.Dataset)
            .ThenBy(gap => gap.ExternalCompanyId)
            .ThenBy(gap => gap.FiscalYear)
            .ToArray();

        return new CurrentApiGapReport(boundaryShamsiYear, gaps.Sum(gap => gap.CurrentApiRowCount), gaps);
    }

    private static async Task<IReadOnlyList<CurrentApiCoverageGap>> BuildGapsAsync(
        IQueryable<CoverageProjection> source,
        string dataset,
        int minGregorianYear,
        CancellationToken cancellationToken)
    {
        // Counts per (company, fiscalYear) split by archive vs current source, at/after the boundary.
        var counts = await source
            .Where(row => (row.ProviderName == ArchiveSource || row.ProviderName == CurrentSource) &&
                row.FiscalYear >= minGregorianYear)
            .GroupBy(row => new { row.ExternalCompanyId, row.FiscalYear, row.ProviderName })
            .Select(group => new
            {
                group.Key.ExternalCompanyId,
                group.Key.FiscalYear,
                group.Key.ProviderName,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return counts
            .GroupBy(row => new { row.ExternalCompanyId, row.FiscalYear })
            .Select(group =>
            {
                var current = group.Where(r => r.ProviderName == CurrentSource).Sum(r => r.Count);
                var archive = group.Where(r => r.ProviderName == ArchiveSource).Sum(r => r.Count);
                return new CurrentApiCoverageGap(
                    dataset, group.Key.ExternalCompanyId, group.Key.FiscalYear, current, archive);
            })
            // A gap exists where the current API has rows the archive does not.
            .Where(gap => gap.CurrentApiRowCount > gap.ArchiveRowCount)
            .ToArray();
    }

    private sealed record CoverageProjection(string ProviderName, string ExternalCompanyId, int FiscalYear);
}

/// <summary>
/// Drives a current-API backfill/gap-fill through the existing bounded NADPCO/current-API
/// orchestration (single ingestion path — no second normalization/recalculation path). A
/// <see cref="CurrentApiBackfillRequest.FromShamsiYearOverride"/> lowers the start boundary for this
/// run only (AC #3). Backfill runs through the current-API source exclusively and never reads or
/// mutates archive freeze/import state (AC #10). Health combines provider health with the latest
/// scheduled-sync execution (AC #9).
/// </summary>
public sealed class CurrentApiBackfillCoordinator(
    INadpcoApiScheduledSyncService currentApiSync,
    INadpcoScheduledSyncCoordinator scheduledSyncCoordinator,
    NadpcoApiDataProviderClient currentApiProvider,
    ILogger<CurrentApiBackfillCoordinator> logger) : ICurrentApiBackfillCoordinator
{
    public async Task<CurrentApiBackfillResult> BackfillAsync(
        CurrentApiBackfillRequest request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Noavaran current-API backfill requested by {RequestedBy} fromShamsiYearOverride={Override}.",
            request.RequestedBy,
            request.FromShamsiYearOverride);

        // Full reload across known current-API companies, with the optional one-off start override
        // stamped onto every enqueued request so the worker scope applies it.
        var result = await currentApiSync.ExecuteAsync(
            fullReload: true,
            cancellationToken,
            fromShamsiYearOverride: request.FromShamsiYearOverride);

        return new CurrentApiBackfillResult(
            result.FullReload,
            request.FromShamsiYearOverride,
            result.CompaniesConsidered,
            result.RequestsEnqueued,
            result.FailedCompanies,
            result.Duration.ToString("g"));
    }

    public async Task<CurrentApiHealthStatus> GetHealthAsync(CancellationToken cancellationToken)
    {
        var health = await currentApiProvider.CheckAsync(cancellationToken);
        var status = await scheduledSyncCoordinator.GetStatusAsync(recentRunLimit: 1, cancellationToken);
        return new CurrentApiHealthStatus(
            ProviderSources.NoavaranCurrentApiName,
            health.Status.ToString(),
            health.Detail,
            status.Enabled,
            status.LastSuccessfulExecutionAt,
            status.NextDueAt,
            health.CheckedAt);
    }
}
