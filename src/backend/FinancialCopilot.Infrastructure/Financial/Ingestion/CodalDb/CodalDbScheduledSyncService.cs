using System.Collections.Concurrent;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Nightly CodalDB sync orchestrator. Computes the set of companies whose source
/// <c>ModifiedDateTime</c> is newer than the persisted watermark and enqueues
/// <see cref="DataSyncRequest"/>s (one Symbols + one per per-company dataset) with
/// <c>ProviderName = "CodalDb"</c>. Watermark advances only after a successful run. Full-reload
/// mode ignores the watermark.
/// </summary>
public sealed class CodalDbScheduledSyncService(
    ICodalDbQueryExecutor queryExecutor,
    ICodalDbSyncStateStore stateStore,
    IDataSyncRequestPublisher publisher,
    IOptions<CodalDbProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<CodalDbScheduledSyncService> logger) : ICodalDbScheduledSyncService
{
    private const string WatermarkKey = "CodalDb";

    public async Task<CodalDbScheduledSyncResult> ExecuteAsync(
        bool fullReload,
        CancellationToken cancellationToken)
    {
        var providerName = providerOptions.Value.ProviderName;
        var started = timeProvider.GetUtcNow();
        await stateStore.RecordRunStartAsync(WatermarkKey, started, cancellationToken);

        var watermark = fullReload
            ? null
            : await stateStore.GetWatermarkAsync(WatermarkKey, cancellationToken);

        logger.LogInformation(
            "CodalDB scheduled sync starting — mode={Mode} watermark={Watermark}.",
            fullReload ? "full" : "incremental",
            watermark);

        var changedIds = await queryExecutor.QueryChangedCompanyIdsAsync(watermark, cancellationToken);
        if (changedIds.Count == 0)
        {
            logger.LogInformation(
                "CodalDB scheduled sync — no companies changed since watermark; no requests enqueued.");
            return new CodalDbScheduledSyncResult(
                fullReload,
                CompaniesConsidered: 0,
                CompaniesEnqueued: 0,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                AdvancedWatermark: watermark,
                Duration: timeProvider.GetUtcNow() - started);
        }

        // Symbols first (single tenant-wide request).
        await publisher.PublishAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.Symbols,
                null,
                timeProvider.GetUtcNow(),
                IdempotencyKey: $"codaldb-symbols:{started:yyyyMMddHHmmss}",
                ProviderName: providerName),
            cancellationToken);

        var maxParallelism = Math.Max(1, providerOptions.Value.MaxReadParallelism);
        var throttle = new SemaphoreSlim(maxParallelism, maxParallelism);
        var failed = new ConcurrentBag<int>();
        var enqueued = 0;

        var tasks = changedIds.Select(async companyId =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                await EnqueueCompanyAsync(providerName, companyId, started, cancellationToken);
                Interlocked.Increment(ref enqueued);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "CodalDB sync failed to enqueue company {CompanyId}.", companyId);
                failed.Add(companyId);
            }
            finally
            {
                throttle.Release();
            }
        });
        await Task.WhenAll(tasks);

        DateTimeOffset? advancedWatermark = null;
        if (failed.IsEmpty)
        {
            var maxModified = await queryExecutor.QueryMaxModifiedDateTimeAsync(cancellationToken);
            if (maxModified is not null)
            {
                await stateStore.AdvanceWatermarkAsync(
                    WatermarkKey,
                    maxModified.Value,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                advancedWatermark = maxModified;
            }
        }

        var duration = timeProvider.GetUtcNow() - started;
        logger.LogInformation(
            "CodalDB scheduled sync complete — considered={Considered}, enqueued={Enqueued}, failed={Failed}, duration={Duration:g}.",
            changedIds.Count,
            enqueued,
            failed.Count,
            duration);

        return new CodalDbScheduledSyncResult(
            fullReload,
            CompaniesConsidered: changedIds.Count,
            CompaniesEnqueued: enqueued,
            FailedCompanies: failed.Count,
            FailedCompanyIds: failed.OrderBy(id => id).ToArray(),
            AdvancedWatermark: advancedWatermark,
            Duration: duration);
    }

    private async Task EnqueueCompanyAsync(
        string providerName,
        int companyId,
        DateTimeOffset runStarted,
        CancellationToken cancellationToken)
    {
        var externalReference = companyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stamp = runStarted.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var dataset in new[]
        {
            ProviderDataset.FinancialStatements,
            ProviderDataset.MonthlyProductionSales,
            ProviderDataset.FinancialRatios
        })
        {
            await publisher.PublishAsync(
                new DataSyncRequest(
                    Guid.NewGuid(),
                    dataset,
                    externalReference,
                    timeProvider.GetUtcNow(),
                    IdempotencyKey: $"codaldb-{dataset}-{companyId}-{stamp}",
                    ProviderName: providerName),
                cancellationToken);
        }
    }
}
