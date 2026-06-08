using System.Collections.Concurrent;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Noavaran Amin <b>archive</b> import orchestrator (legacy CodalDB SQL snapshot, source name
/// <see cref="ProviderSources.NoavaranArchiveSqlName"/>). Spec 051 reclassifies this source as
/// one-time archive: it is invoked explicitly (admin maintenance/backfill), <b>not</b> driven by a
/// recurring worker. It enqueues <see cref="DataSyncRequest"/>s stamped with
/// <see cref="SourceMode.ArchiveOneTime"/> provenance. The watermark is retained only so an explicit
/// maintenance re-import can resume; ordinary recurring refresh belongs to the current API source.
/// Full-reload mode ignores the watermark.
/// </summary>
public sealed class CodalDbScheduledSyncService(
    ICodalDbQueryExecutor queryExecutor,
    ICodalDbSyncStateStore stateStore,
    IDataSyncRequestPublisher publisher,
    IOptions<CodalDbProviderOptions> providerOptions,
    TimeProvider timeProvider,
    ILogger<CodalDbScheduledSyncService> logger) : ICodalDbScheduledSyncService
{
    // Internal state key for the archive watermark row in CodalDbSyncStates (not a persisted
    // ProviderName); left unchanged to preserve any existing maintenance-resume state.
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
                ProviderName: providerName,
                Mode: SourceMode.ArchiveOneTime),
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
                    ProviderName: providerName,
                    Mode: SourceMode.ArchiveOneTime),
                cancellationToken);
        }
    }
}
