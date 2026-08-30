using System.Collections.Concurrent;
using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

/// <summary>
/// Provider-specific NADPCO orchestrator. NADPCO endpoints do not expose a reliable modified-since
/// cursor, so incremental mode reconciles an overlap window and re-enqueues bounded company-scoped
/// requests against the existing raw-payload/normalization/recalculation/cache-invalidation path.
/// </summary>
public sealed class NadpcoApiScheduledSyncService(
    FinancialIngestionDbContext dbContext,
    INadpcoApiSyncStateStore stateStore,
    INadpcoCompanyCatalogCleanSlateService cleanSlateService,
    IDataSyncRequestPublisher publisher,
    IOptions<NadpcoApiProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<NadpcoApiScheduledSyncService> logger,
    IScannerCache? scannerCache = null,
    IMonthlyActivityBackfillStateReader? monthlyBackfillState = null) :
    INadpcoApiScheduledSyncService,
    INadpcoApiSyncStateReader
{
    /// <summary>Per-run monthly-activity request scope (spec 057 Phase B).</summary>
    private sealed record MonthlyActivityWindow(string FromDate, string ToDate, string KeySuffix);

    private static readonly string[] LogicalDatasets =
    [
        ProviderDataset.Symbols.ToString(),
        ProviderDataset.FinancialStatements.ToString(),
        ProviderDataset.FundamentalIndexes.ToString(),
        "ProductSales",
        "ServiceSales"
    ];

    public Task<IReadOnlyCollection<NadpcoApiSyncState>> QueryAsync(CancellationToken cancellationToken) =>
        stateStore.QueryAsync(cancellationToken);

    public async Task<NadpcoApiSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken,
        int? fromShamsiYearOverride = null)
    {
        var settings = providerOptions.Value;
        var providerName = settings.ProviderName;
        var runMode = fullReload
            ? NadpcoApiSyncRunMode.FullSync
            : NadpcoApiSyncRunMode.IncrementalSync;
        var started = timeProvider.GetUtcNow();
        var overlapFrom = fullReload
            ? null
            : await ResolveOverlapFromAsync(settings, cancellationToken);

        await stateStore.RecordRunStartAsync(LogicalDatasets, started, overlapFrom, runMode, cancellationToken);
        logger.LogInformation(
            "NADPCO API sync starting mode={Mode} overlapFrom={OverlapFrom}.",
            runMode,
            overlapFrom);

        // Spec 057 Phase B: incremental runs request monthly activity only for the previous Shamsi
        // month, and only after the manual backfill has completed; the backfill operation owns
        // history. Manual full-reload runs keep the configured full range (window = null).
        var (includeMonthlyActivity, monthlyWindow) = fullReload
            ? (true, (MonthlyActivityWindow?)null)
            : await ResolveMonthlyActivityScopeAsync(started, cancellationToken);

        var companyIds = await QueryKnownNadpcoCompanyIdsAsync(providerName, cancellationToken);
        var requestsEnqueued = 0;

        try
        {
            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    ProviderDataset.Symbols,
                    ExternalReference: null,
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: BuildKey("symbols", null, started, overlapFrom),
                    ProviderName: providerName,
                    Mode: SourceMode.CurrentIncremental),
                cancellationToken);
            requestsEnqueued++;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "NADPCO API sync failed to enqueue company catalog.");
            await stateStore.RecordRunCompletionAsync(
                LogicalDatasets,
                timeProvider.GetUtcNow(),
                started,
                companyIds.Count,
                companiesEnqueued: 0,
                failedCompanies: companyIds.Count,
                runMode,
                error: "Failed to enqueue company catalog: " + exception.Message,
                cancellationToken);
            return new NadpcoApiSyncResult(
                fullReload,
                companyIds.Count,
                CompaniesEnqueued: 0,
                FailedCompanies: companyIds.Count,
                FailedCompanyIds: companyIds,
                requestsEnqueued,
                overlapFrom,
                AdvancedWatermark: null,
                timeProvider.GetUtcNow() - started,
                runMode);
        }

        var maxParallelism = Math.Max(1, settings.MaxReadParallelism);
        var throttle = new SemaphoreSlim(maxParallelism, maxParallelism);
        var failed = new ConcurrentBag<int>();
        var enqueuedCompanies = 0;
        var requestCounter = requestsEnqueued;

        var tasks = companyIds.Select(async companyId =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var requestCount = await EnqueueCompanyAsync(
                    providerName,
                    companyId,
                    started,
                    overlapFrom,
                    fromShamsiYearOverride,
                    includeMonthlyActivity: false,
                    monthlyWindow: null,
                    cancellationToken);
                Interlocked.Add(ref requestCounter, requestCount);
                Interlocked.Increment(ref enqueuedCompanies);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "NADPCO API sync failed to enqueue company {CompanyId}.", companyId);
                failed.Add(companyId);
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        if (includeMonthlyActivity)
        {
            // Monthly ProductSales are published in output-type waves. This guarantees that the
            // queue receives every eligible company for type 0 before type 1, and so on through 4.
            for (var outputType = 0; outputType <= 4; outputType++)
            {
                foreach (var companyId in companyIds)
                {
                    try
                    {
                        await publisher.PublishAsync(
                            new DataSyncRequest(
                                Guid.NewGuid(),
                                ProviderDataset.MonthlyProductionSales,
                                companyId.ToString(CultureInfo.InvariantCulture),
                                timeProvider.GetUtcNow(),
                                IdempotencyKey: BuildMonthlyActivityKey(
                                    companyId,
                                    started,
                                    overlapFrom,
                                    fromShamsiYearOverride,
                                    monthlyWindow,
                                    outputType),
                                ProviderName: providerName,
                                Mode: SourceMode.CurrentIncremental,
                                SourceDateRangeStartJalali: monthlyWindow?.FromDate,
                                SourceDateRangeEndJalali: monthlyWindow?.ToDate,
                                FromShamsiYearOverride: fromShamsiYearOverride,
                                MonthlyActivityOutputType: outputType),
                            cancellationToken);
                        Interlocked.Increment(ref requestCounter);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(
                            exception,
                            "NADPCO API sync failed to enqueue monthly activity output type {OutputType} for company {CompanyId}.",
                            outputType,
                            companyId);
                        failed.Add(companyId);
                    }
                }
            }
        }

        var failedIds = failed.Distinct().OrderBy(id => id).ToArray();
        var completed = timeProvider.GetUtcNow();
        var error = failedIds.Length == 0
            ? null
            : $"Failed to enqueue {failedIds.Length} company batch(es).";

        await stateStore.RecordRunCompletionAsync(
            LogicalDatasets,
            completed,
            started,
            companyIds.Count,
            enqueuedCompanies,
            failedIds.Length,
            runMode,
            error,
            cancellationToken);

        logger.LogInformation(
            "NADPCO API sync complete considered={Considered} enqueued={Enqueued} failed={Failed} requests={Requests}.",
            companyIds.Count,
            enqueuedCompanies,
            failedIds.Length,
            requestCounter);

        return new NadpcoApiSyncResult(
            fullReload,
            companyIds.Count,
            enqueuedCompanies,
            failedIds.Length,
            failedIds,
            requestCounter,
            overlapFrom,
            failedIds.Length == 0 ? started : null,
            completed - started,
            runMode);
    }

    public async Task<NadpcoApiSyncResult> ExecuteCompanyCatalogAsync(
        bool cleanSlate,
        CancellationToken cancellationToken)
    {
        var settings = providerOptions.Value;
        var providerName = settings.ProviderName;
        var runMode = cleanSlate
            ? NadpcoApiSyncRunMode.CompanyCatalogCleanSlate
            : NadpcoApiSyncRunMode.CompanyCatalogRefresh;
        var dataset = ProviderDataset.Symbols.ToString();
        var started = timeProvider.GetUtcNow();
        var companiesConsidered = await CountKnownNadpcoCompaniesAsync(providerName, cancellationToken);

        await stateStore.RecordRunStartAsync([dataset], started, overlapFrom: null, runMode, cancellationToken);
        logger.LogInformation("NADPCO company catalog sync starting mode={Mode}.", runMode);

        NadpcoCompanyCatalogCleanSlateResult? cleanSlateResult = null;
        try
        {
            if (cleanSlate)
            {
                cleanSlateResult = await cleanSlateService.ClearAsync(cancellationToken);
                companiesConsidered = cleanSlateResult.CompaniesDeleted;
                if (scannerCache is not null)
                {
                    await scannerCache.InvalidateAsync(
                        new ScannerCacheInvalidation(
                            "NadpcoApi.CompanyCatalogCleanSlate",
                            timeProvider.GetUtcNow()),
                        cancellationToken);
                }
            }

            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    ProviderDataset.Symbols,
                    ExternalReference: null,
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: BuildKey("symbols", null, started, overlapFrom: null, runMode),
                    ProviderName: providerName,
                    Mode: SourceMode.CurrentIncremental),
                cancellationToken);

            var completed = timeProvider.GetUtcNow();
            await stateStore.RecordRunCompletionAsync(
                [dataset],
                completed,
                started,
                companiesConsidered,
                companiesEnqueued: 0,
                failedCompanies: 0,
                runMode,
                error: null,
                cancellationToken);

            return new NadpcoApiSyncResult(
                FullReload: cleanSlate,
                CompaniesConsidered: companiesConsidered,
                CompaniesEnqueued: 0,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                RequestsEnqueued: 1,
                OverlapFrom: null,
                AdvancedWatermark: started,
                Duration: completed - started,
                RunMode: runMode,
                CleanSlate: cleanSlateResult);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "NADPCO company catalog sync failed mode={Mode}.", runMode);
            var completed = timeProvider.GetUtcNow();
            await stateStore.RecordRunCompletionAsync(
                [dataset],
                completed,
                started,
                companiesConsidered,
                companiesEnqueued: 0,
                failedCompanies: 1,
                runMode,
                error: "Failed to enqueue company catalog: " + exception.Message,
                cancellationToken);

            return new NadpcoApiSyncResult(
                FullReload: cleanSlate,
                CompaniesConsidered: companiesConsidered,
                CompaniesEnqueued: 0,
                FailedCompanies: 1,
                FailedCompanyIds: [],
                RequestsEnqueued: 0,
                OverlapFrom: null,
                AdvancedWatermark: null,
                Duration: completed - started,
                RunMode: runMode,
                CleanSlate: cleanSlateResult);
        }
    }

    private async Task<DateTimeOffset?> ResolveOverlapFromAsync(
        NadpcoApiProviderOptions settings,
        CancellationToken cancellationToken)
    {
        var last = await stateStore.GetLastSuccessfulSyncAsync(
            ProviderDataset.FinancialStatements.ToString(),
            cancellationToken);
        return last?.AddDays(-Math.Max(0, settings.OrchestrationOverlapDays));
    }

    // Per-company vendor requests are limited to the Noavaran eligibility scope (equities on
    // بورس/فرابورس/پایه); the unscoped company catalog itself is fetched by the Symbols dataset.
    private Task<IReadOnlyList<int>> QueryKnownNadpcoCompanyIdsAsync(
        string providerName,
        CancellationToken cancellationToken) =>
        NoavaranCompanyScope.EligibleCompanyIdsAsync(dbContext, providerName, cancellationToken);

    private Task<int> CountKnownNadpcoCompaniesAsync(
        string providerName,
        CancellationToken cancellationToken) =>
        dbContext.Companies.AsNoTracking()
            .CountAsync(row => row.ProviderName == providerName, cancellationToken);

    // Resolves the incremental monthly-activity scope (spec 057 Phase B): previous Shamsi month
    // once the manual backfill marker exists; excluded entirely while it does not.
    private async Task<(bool Include, MonthlyActivityWindow? Window)> ResolveMonthlyActivityScopeAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var backfillCompleted = monthlyBackfillState is not null &&
            await monthlyBackfillState.IsBackfillCompletedAsync(cancellationToken);
        if (!backfillCompleted)
        {
            logger.LogWarning(
                "NADPCO monthly-activity scheduled refresh skipped: the manual monthly backfill " +
                "has not completed. Run the DataAdmin monthly-activity backfill first (spec 057).");
            return (false, null);
        }

        var month = ShamsiMonthCalculator.LatestPublishedMonth(now);
        return (true, new MonthlyActivityWindow(
            month.FirstDayJalali,
            ShamsiMonthCalculator.LastDayJalali(month),
            $"-m{month.Year:D4}{month.Month:D2}"));
    }

    private async Task<int> EnqueueCompanyAsync(
        string providerName,
        int companyId,
        DateTimeOffset started,
        DateTimeOffset? overlapFrom,
        int? fromShamsiYearOverride,
        bool includeMonthlyActivity,
        MonthlyActivityWindow? monthlyWindow,
        CancellationToken cancellationToken)
    {
        var count = 0;
        // A backfill override widens coverage, so it must produce distinct idempotency keys from an
        // ordinary run for the same company/period; the override year is folded into the key.
        var keySuffix = fromShamsiYearOverride is { } year ? $"-bf{year}" : string.Empty;
        ProviderDataset[] datasets = includeMonthlyActivity
            ?
            [
                ProviderDataset.FinancialStatements,
                ProviderDataset.FundamentalIndexes,
                ProviderDataset.MonthlyProductionSales
            ]
            : [ProviderDataset.FinancialStatements, ProviderDataset.FundamentalIndexes];
        foreach (var dataset in datasets)
        {
            var isWindowedMonthly = dataset == ProviderDataset.MonthlyProductionSales && monthlyWindow is not null;
            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    dataset,
                    companyId.ToString(CultureInfo.InvariantCulture),
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: BuildKey(dataset.ToString(), companyId, started, overlapFrom) + keySuffix +
                        (isWindowedMonthly ? monthlyWindow!.KeySuffix : string.Empty),
                    ProviderName: providerName,
                    Mode: SourceMode.CurrentIncremental,
                    SourceDateRangeStartJalali: isWindowedMonthly ? monthlyWindow!.FromDate : null,
                    SourceDateRangeEndJalali: isWindowedMonthly ? monthlyWindow!.ToDate : null,
                    FromShamsiYearOverride: fromShamsiYearOverride),
                cancellationToken);
            count++;
        }

        return count;
    }

    private static string BuildMonthlyActivityKey(
        int companyId,
        DateTimeOffset started,
        DateTimeOffset? overlapFrom,
        int? fromShamsiYearOverride,
        MonthlyActivityWindow? monthlyWindow,
        int outputType)
    {
        var keySuffix = fromShamsiYearOverride is { } year ? $"-bf{year}" : string.Empty;
        var windowSuffix = monthlyWindow is null ? string.Empty : monthlyWindow.KeySuffix;
        return BuildKey(
            ProviderDataset.MonthlyProductionSales.ToString(),
            companyId,
            started,
            overlapFrom) + $"-ot{outputType}" + keySuffix + windowSuffix;
    }

    private static string BuildKey(
        string dataset,
        int? companyId,
        DateTimeOffset started,
        DateTimeOffset? overlapFrom,
        NadpcoApiSyncRunMode? runMode = null)
    {
        var companyPart = companyId?.ToString(CultureInfo.InvariantCulture) ?? "all";
        var overlapPart = overlapFrom?.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) ?? "full";
        var modePart = runMode?.ToString() ?? "sync";
        return $"nadpcoapi-{modePart}-{dataset}-{companyPart}-{started:yyyyMMddHHmmss}-{overlapPart}";
    }
}
