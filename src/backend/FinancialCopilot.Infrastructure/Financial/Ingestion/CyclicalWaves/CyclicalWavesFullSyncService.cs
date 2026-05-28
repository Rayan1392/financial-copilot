using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesFullSyncService(
    IFinancialDataSyncProcessor processor,
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesFullSyncService> logger) : ICyclicalWavesFullSyncService
{
    private const string CyclicalWavesProvider = "CyclicalWaves";
    private const int MaxConcurrency = 1;

    public async Task<CyclicalWavesFullSyncResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();

        // Step 1: sync the symbol list
        logger.LogInformation("CyclicalWaves full sync — starting symbol sync.");
        var symbolRun = await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.Symbols,
                null,
                timeProvider.GetUtcNow(),
                $"cw-symbols:{Guid.NewGuid():N}"),
            cancellationToken);

        var symbolsSynced = symbolRun.Run.ProcessedRecords;
        logger.LogInformation(
            "CyclicalWaves symbol sync complete — {Count} symbols.",
            symbolsSynced);

        // Step 2: load all CyclicalWaves tickers from DB
        var tickers = await dbContext.Symbols
            .AsNoTracking()
            .Where(s => s.ProviderName == CyclicalWavesProvider)
            .Select(s => s.ExternalSymbolId)
            .Distinct()
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "CyclicalWaves full sync — processing {Count} tickers.",
            tickers.Count);

        // Step 3: sync financial statements + monthly reports per ticker (max 10 concurrent)
        var failed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var succeeded = 0;
        var throttle = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

        var tasks = tickers.Select(async ticker =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                await SyncTickerAsync(ticker, cancellationToken);
                Interlocked.Increment(ref succeeded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "CyclicalWaves sync failed for ticker {Ticker}.", ticker);
                failed.Add(ticker);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        var duration = timeProvider.GetUtcNow() - started;
        logger.LogInformation(
            "CyclicalWaves full sync complete — {Succeeded} succeeded, {Failed} failed in {Duration:g}.",
            succeeded,
            failed.Count,
            duration);

        return new CyclicalWavesFullSyncResult(
            symbolsSynced,
            succeeded,
            failed.Count,
            failed.Order().ToArray(),
            duration);
    }

    private async Task SyncTickerAsync(string ticker, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.FinancialStatements,
                ticker,
                now,
                $"cw-fs:{ticker}:{now:yyyyMMddHH}"),
            cancellationToken);

        await processor.ProcessAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.MonthlyProductionSales,
                ticker,
                now,
                $"cw-monthly:{ticker}:{now:yyyyMMddHH}"),
            cancellationToken);

        dbContext.ChangeTracker.Clear();
    }
}
