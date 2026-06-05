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
    IScannerCache? scannerCache = null) :
    INadpcoApiScheduledSyncService,
    INadpcoApiSyncStateReader
{
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
        CancellationToken cancellationToken)
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
                    ProviderName: providerName),
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

        var failedIds = failed.OrderBy(id => id).ToArray();
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
                    ProviderName: providerName),
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

    private async Task<IReadOnlyList<int>> QueryKnownNadpcoCompanyIdsAsync(
        string providerName,
        CancellationToken cancellationToken)
    {
        var ids = await dbContext.Companies.AsNoTracking()
            .Where(row => row.ProviderName == providerName)
            .Select(row => row.ExternalCompanyId)
            .ToListAsync(cancellationToken);

        return ids
            .Select(id => int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
    }

    private Task<int> CountKnownNadpcoCompaniesAsync(
        string providerName,
        CancellationToken cancellationToken) =>
        dbContext.Companies.AsNoTracking()
            .CountAsync(row => row.ProviderName == providerName, cancellationToken);

    private async Task<int> EnqueueCompanyAsync(
        string providerName,
        int companyId,
        DateTimeOffset started,
        DateTimeOffset? overlapFrom,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var dataset in new[]
        {
            ProviderDataset.FinancialStatements,
            ProviderDataset.FundamentalIndexes,
            ProviderDataset.MonthlyProductionSales
        })
        {
            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    dataset,
                    companyId.ToString(CultureInfo.InvariantCulture),
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: BuildKey(dataset.ToString(), companyId, started, overlapFrom),
                    ProviderName: providerName),
                cancellationToken);
            count++;
        }

        return count;
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
