using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Aggregates current activity snapshots from all sync providers into a single
/// <see cref="DataSyncActivitySnapshot"/>.  Each source is queried independently so a
/// single failing reader cannot suppress the others (spec 058 AC #3).
/// </summary>
public sealed class EfCoreDataSyncActivityReader(
    IDataSyncRunReader dataSyncRunReader,
    INadpcoScheduledSyncRunReader nadpcoScheduledSyncRunReader,
    IStockMarketDbSyncStateReader stockMarketDbSyncStateReader,
    ITsetmcSyncStateReader tsetmcSyncStateReader,
    IArchiveImportRunReader archiveImportRunReader,
    IMonthlyActivityBackfillCoordinator monthlyActivityBackfillCoordinator,
    IFundamentalIndexCatchUpRunReader fundamentalIndexCatchUpRunReader,
    ILogger<EfCoreDataSyncActivityReader> logger) : IDataSyncActivityReader
{
    // Non-terminal statuses in the DataSyncRunStatus enum.
    private static readonly HashSet<string> ActiveStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Queued", "Running" };

    public async Task<DataSyncActivitySnapshot> GetSnapshotAsync(
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        recentPerProvider = Math.Clamp(recentPerProvider, 1, 20);

        var active = new List<DataSyncActivityItem>();
        var recent = new List<DataSyncActivityItem>();

        await CollectDataSyncRunsAsync(active, recent, recentPerProvider, cancellationToken);
        await CollectNadpcoScheduledRunsAsync(active, recent, recentPerProvider, cancellationToken);
        await CollectStockMarketDbStateAsync(active, recent, cancellationToken);
        await CollectTsetmcSyncStateAsync(active, recent, cancellationToken);
        await CollectArchiveImportRunsAsync(active, recent, recentPerProvider, cancellationToken);
        await CollectMonthlyActivityBackfillAsync(active, recent, cancellationToken);
        await CollectFundamentalIndexCatchUpAsync(active, recent, recentPerProvider, cancellationToken);

        // Newest first within each list.
        active.Sort(ByStartedAtDescending);
        recent.Sort(ByStartedAtDescending);

        return new DataSyncActivitySnapshot(active, recent);
    }

    // --------------------------------------------------------------------
    // DataSyncRunRow (enqueued provider messages: symbols, statements, etc.)
    // --------------------------------------------------------------------

    private async Task CollectDataSyncRunsAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            // Query enough rows to cover active + recent budget.
            var runs = await dataSyncRunReader.QueryRecentAsync(
                recentPerProvider * 10 + 50, cancellationToken);

            var perProviderCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var run in runs.OrderByDescending(r => r.StartedAt ?? r.RequestedAt))
            {
                var item = MapDataSyncRun(run);
                if (ActiveStatuses.Contains(item.Status))
                {
                    active.Add(item);
                }
                else
                {
                    var key = item.Provider;
                    perProviderCount.TryGetValue(key, out var count);
                    if (count < recentPerProvider)
                    {
                        recent.Add(item);
                        perProviderCount[key] = count + 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read DataSyncRuns for activity snapshot.");
        }
    }

    private static DataSyncActivityItem MapDataSyncRun(DataSyncRun run)
    {
        var provider = run.Vendor?.ToString()
            ?? ProviderSources.TryResolve(run.ProviderName)?.Vendor.ToString()
            ?? run.ProviderName
            ?? "Unknown";

        var durationMs = run.StartedAt.HasValue && run.CompletedAt.HasValue
            ? (long)(run.CompletedAt.Value - run.StartedAt.Value).TotalMilliseconds
            : (long?)null;

        return new DataSyncActivityItem(
            RunId: run.Id.ToString(),
            Provider: provider,
            Dataset: run.Dataset.ToString(),
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            DurationMs: durationMs,
            ProcessedRecords: run.ProcessedRecords,
            ErrorCount: run.ErrorCount,
            ErrorMessage: run.ErrorMessage,
            TriggerSource: "Worker",
            RequestedShamsiMonth: FormatShamsiMonth(run.SourceDateRangeStartJalali),
            LogicalVendor: run.Vendor?.ToString(),
            PhysicalSource: run.Source?.ToString(),
            SourceMode: run.Mode?.ToString());
    }

    // --------------------------------------------------------------------
    // NadpcoScheduledSyncRun
    // --------------------------------------------------------------------

    private static readonly HashSet<string> ActiveNadpcoStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Running" };

    private async Task CollectNadpcoScheduledRunsAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var runs = await nadpcoScheduledSyncRunReader.QueryRecentAsync(
                recentPerProvider + 5, cancellationToken);

            var addedToRecent = 0;
            foreach (var run in runs.OrderByDescending(r => r.StartedAt))
            {
                var item = MapNadpcoScheduledRun(run);
                if (ActiveNadpcoStatuses.Contains(item.Status))
                {
                    active.Add(item);
                }
                else if (addedToRecent < recentPerProvider)
                {
                    recent.Add(item);
                    addedToRecent++;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read NadpcoScheduledSyncRuns for activity snapshot.");
        }
    }

    private static DataSyncActivityItem MapNadpcoScheduledRun(NadpcoScheduledSyncRun run)
    {
        var durationMs = run.CompletedAt.HasValue
            ? (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds
            : (long?)null;

        return new DataSyncActivityItem(
            RunId: run.RunId.ToString(),
            Provider: ProviderSources.NoavaranCurrentApiName,
            Dataset: "ScheduledSync",
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            DurationMs: durationMs,
            ProcessedRecords: run.ProcessedBatches,
            ErrorCount: run.FailedBatches,
            ErrorMessage: run.Diagnostics,
            TriggerSource: run.TriggerSource.ToString(),
            RequestedShamsiMonth: null,
            LogicalVendor: LogicalVendor.NoavaranAmin.ToString(),
            PhysicalSource: PhysicalSource.NoavaranCurrentApi.ToString(),
            SourceMode: SourceMode.CurrentIncremental.ToString());
    }

    // --------------------------------------------------------------------
    // StockMarketSyncState (one item per dataset from last-run watermarks)
    // --------------------------------------------------------------------

    private async Task CollectStockMarketDbStateAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        CancellationToken cancellationToken)
    {
        try
        {
            var states = await stockMarketDbSyncStateReader.QueryAsync(cancellationToken);
            foreach (var state in states)
            {
                if (state.LastRunStartedAt is null)
                    continue;

                var durationMs = state.LastRunStartedAt.HasValue && state.LastRunCompletedAt.HasValue
                    ? (long)(state.LastRunCompletedAt.Value - state.LastRunStartedAt.Value).TotalMilliseconds
                    : (long?)null;

                recent.Add(new DataSyncActivityItem(
                    RunId: $"stockmarketdb-{state.Dataset}-{state.LastRunStartedAt:yyyyMMddHHmmss}",
                    Provider: ProviderSources.StockMarketDbName,
                    Dataset: state.Dataset.ToString(),
                    Status: state.LastRunCompletedAt.HasValue ? "Completed" : "Running",
                    StartedAt: state.LastRunStartedAt,
                    CompletedAt: state.LastRunCompletedAt,
                    DurationMs: durationMs,
                    ProcessedRecords: 0,
                    ErrorCount: 0,
                    ErrorMessage: null,
                    TriggerSource: "Worker",
                    RequestedShamsiMonth: null,
                    LogicalVendor: LogicalVendor.Tsetmc.ToString(),
                    PhysicalSource: PhysicalSource.StockMarketDb.ToString(),
                    SourceMode: SourceMode.MigrationBridge.ToString()));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read StockMarketDbSyncState for activity snapshot.");
        }
    }

    // --------------------------------------------------------------------
    // TsetmcDirectFeedSyncState (one item per TSETMC dataset from last-run watermarks)
    // --------------------------------------------------------------------

    private async Task CollectTsetmcSyncStateAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        CancellationToken cancellationToken)
    {
        try
        {
            var states = await tsetmcSyncStateReader.QueryAsync(cancellationToken);
            foreach (var state in states)
            {
                if (state.LastRunStartedAt is null)
                    continue;

                var durationMs = state.LastRunStartedAt.HasValue && state.LastRunCompletedAt.HasValue
                    ? (long)(state.LastRunCompletedAt.Value - state.LastRunStartedAt.Value).TotalMilliseconds
                    : (long?)null;

                var status = state.LastRunCompletedAt.HasValue ? "Completed" : "Running";

                var item = new DataSyncActivityItem(
                    RunId: $"tsetmc-{state.Dataset}-{state.LastRunStartedAt:yyyyMMddHHmmss}",
                    Provider: ProviderSources.TsetmcWebServiceName,
                    Dataset: state.Dataset,
                    Status: status,
                    StartedAt: state.LastRunStartedAt,
                    CompletedAt: state.LastRunCompletedAt,
                    DurationMs: durationMs,
                    ProcessedRecords: 0,
                    ErrorCount: 0,
                    ErrorMessage: null,
                    TriggerSource: "Worker",
                    RequestedShamsiMonth: null,
                    LogicalVendor: state.LogicalVendor ?? LogicalVendor.Tsetmc.ToString(),
                    PhysicalSource: state.PhysicalSource ?? PhysicalSource.TsetmcWebService.ToString(),
                    SourceMode: state.SourceMode ?? SourceMode.CurrentIncremental.ToString());

                if (status == "Running")
                    active.Add(item);
                else
                    recent.Add(item);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read TsetmcSyncState for activity snapshot.");
        }
    }

    // --------------------------------------------------------------------
    // ArchiveImportRun
    // --------------------------------------------------------------------

    private static readonly HashSet<string> ActiveArchiveStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Running" };

    private async Task CollectArchiveImportRunsAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var runs = await archiveImportRunReader.QueryRecentAsync(
                recentPerProvider + 5, cancellationToken);

            var addedToRecent = 0;
            foreach (var run in runs.OrderByDescending(r => r.StartedAt))
            {
                var item = MapArchiveImportRun(run);
                if (ActiveArchiveStatuses.Contains(item.Status))
                {
                    active.Add(item);
                }
                else if (addedToRecent < recentPerProvider)
                {
                    recent.Add(item);
                    addedToRecent++;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read ArchiveImportRuns for activity snapshot.");
        }
    }

    private static DataSyncActivityItem MapArchiveImportRun(ArchiveImportRun run)
    {
        var durationMs = run.FinishedAt.HasValue
            ? (long)(run.FinishedAt.Value - run.StartedAt).TotalMilliseconds
            : (long?)null;

        return new DataSyncActivityItem(
            RunId: run.RunId.ToString(),
            Provider: ProviderSources.NoavaranArchiveSqlName,
            Dataset: run.Action.ToString(),
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.FinishedAt,
            DurationMs: durationMs,
            ProcessedRecords: run.RequestsEnqueued,
            ErrorCount: run.FailedCount,
            ErrorMessage: run.Diagnostics,
            TriggerSource: "DataAdmin",
            RequestedShamsiMonth: null,
            LogicalVendor: LogicalVendor.NoavaranAmin.ToString(),
            PhysicalSource: PhysicalSource.NoavaranArchiveSql.ToString(),
            SourceMode: SourceMode.ArchiveOneTime.ToString());
    }

    // --------------------------------------------------------------------
    // MonthlyActivityBackfill (per-month progress rows)
    // --------------------------------------------------------------------

    private async Task CollectMonthlyActivityBackfillAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = await monthlyActivityBackfillCoordinator.GetProgressAsync(cancellationToken);
            if (!progress.Started)
                return;

            foreach (var month in progress.Months)
            {
                // In-progress months are "Running", others are terminal.
                var isActive = month.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase)
                    || month.Status.Equals("Running", StringComparison.OrdinalIgnoreCase);

                var item = new DataSyncActivityItem(
                    RunId: $"monthly-backfill-{month.ShamsiYear}{month.ShamsiMonth:D2}",
                    Provider: ProviderSources.NoavaranCurrentApiName,
                    Dataset: "MonthlyActivityBackfill",
                    Status: isActive ? "Running" : month.Status,
                    StartedAt: progress.LastStartedAt,
                    CompletedAt: progress.IsCompleted ? progress.CompletedAt : null,
                    DurationMs: null,
                    ProcessedRecords: month.CompaniesCompleted,
                    ErrorCount: month.CompaniesFailed,
                    ErrorMessage: null,
                    TriggerSource: "DataAdmin",
                    RequestedShamsiMonth: $"{month.ShamsiYear}/{month.ShamsiMonth:D2}",
                    LogicalVendor: LogicalVendor.NoavaranAmin.ToString(),
                    PhysicalSource: PhysicalSource.NoavaranCurrentApi.ToString(),
                    SourceMode: SourceMode.CurrentIncremental.ToString());

                if (isActive)
                    active.Add(item);
                else
                    recent.Add(item);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read MonthlyActivityBackfill for activity snapshot.");
        }
    }

    // --------------------------------------------------------------------
    // FundamentalIndexCatchUpRun
    // --------------------------------------------------------------------

    private static readonly HashSet<string> ActiveCatchUpStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Running" };

    private async Task CollectFundamentalIndexCatchUpAsync(
        List<DataSyncActivityItem> active,
        List<DataSyncActivityItem> recent,
        int recentPerProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var runs = await fundamentalIndexCatchUpRunReader.QueryRecentAsync(
                recentPerProvider + 5, cancellationToken);

            var addedToRecent = 0;
            foreach (var run in runs.OrderByDescending(r => r.StartedAt))
            {
                var item = MapCatchUpRun(run);
                if (ActiveCatchUpStatuses.Contains(item.Status))
                {
                    active.Add(item);
                }
                else if (addedToRecent < recentPerProvider)
                {
                    recent.Add(item);
                    addedToRecent++;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read FundamentalIndexCatchUpRuns for activity snapshot.");
        }
    }

    private static DataSyncActivityItem MapCatchUpRun(FundamentalIndexCatchUpRun run)
    {
        var durationMs = run.FinishedAt.HasValue
            ? (long)(run.FinishedAt.Value - run.StartedAt).TotalMilliseconds
            : (long?)null;

        return new DataSyncActivityItem(
            RunId: run.RunId.ToString(),
            Provider: ProviderSources.NoavaranCurrentApiName,
            Dataset: "FundamentalIndexCatchUp",
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.FinishedAt,
            DurationMs: durationMs,
            ProcessedRecords: run.RequestsEnqueued,
            ErrorCount: run.FailedCompanies,
            ErrorMessage: run.Diagnostics,
            TriggerSource: "DataAdmin",
            RequestedShamsiMonth: null,
            LogicalVendor: LogicalVendor.NoavaranAmin.ToString(),
            PhysicalSource: PhysicalSource.NoavaranCurrentApi.ToString(),
            SourceMode: SourceMode.CurrentIncremental.ToString());
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private static int ByStartedAtDescending(DataSyncActivityItem a, DataSyncActivityItem b) =>
        (b.StartedAt ?? DateTimeOffset.MinValue).CompareTo(a.StartedAt ?? DateTimeOffset.MinValue);

    /// <summary>
    /// Formats a Jalali date-range start like "140502" or "1405/02/01" into "1405/02".
    /// Returns null when the input is blank or doesn't look like a Shamsi month reference.
    /// </summary>
    private static string? FormatShamsiMonth(string? jalaliStart)
    {
        if (string.IsNullOrWhiteSpace(jalaliStart))
            return null;

        // "140502" → "1405/02"
        var digits = jalaliStart.Replace("/", "").Replace("-", "");
        if (digits.Length >= 6 && long.TryParse(digits[..6], out _))
            return $"{digits[..4]}/{digits[4..6]}";

        return null;
    }
}
