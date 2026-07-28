using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesFullSyncService(
    IFinancialDataSyncProcessor processor,
    IFinancialStatementProvider statementProvider,
    FinancialIngestionDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<CyclicalWavesFullSyncService> logger) : ICyclicalWavesFullSyncService
{
    private const string CyclicalWavesProvider = "CyclicalWaves";
    private const int MaxConcurrency = 1;

    public async Task<CyclicalWavesFullSyncResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();

        // Spec 068: CyclicalWaves must not update Companies/Symbols from its ticker list.
        // Use the existing NADPCO-owned company catalog as the source of ticker work items.
        var tickerCandidates = await NoavaranCompanyScope
            .EligibleCompanies(dbContext, NadpcoApiCompanyNormalizer.NadpcoApiProviderName)
            .AsNoTracking()
            .Select(c => c.Ticker ?? c.CompanySymbol ?? c.TseSymbol)
            .Distinct()
            .ToListAsync(cancellationToken);
        var tickers = tickerCandidates
            .Select(t => t?.Trim())
            .OfType<string>()
            .Where(t => t.Length > 0)
            .Where(t => t.Any(c => c is >= '؀' and <= 'ۿ'))
            .ToList();

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
            SymbolsSynced: 0,
            succeeded,
            failed.Count,
            failed.Order().ToArray(),
            duration);
    }

    private async Task SyncTickerAsync(string ticker, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var payload = await statementProvider.FetchFinancialStatementsAsync(ticker, cancellationToken);

        await processor.ProcessPayloadAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.FinancialStatements,
                ticker,
                now,
                $"cw-fs:{ticker}:{now:yyyyMMddHH}",
                ProviderName: CyclicalWavesProvider),
            payload,
            cancellationToken);

        await processor.ProcessPayloadAsync(
            new DataSyncRequest(
                Guid.NewGuid(),
                ProviderDataset.MonthlyProductionSales,
                ticker,
                now,
                $"cw-monthly:{ticker}:{now:yyyyMMddHH}",
                ProviderName: CyclicalWavesProvider),
            payload,
            cancellationToken);

        dbContext.ChangeTracker.Clear();
    }
}
