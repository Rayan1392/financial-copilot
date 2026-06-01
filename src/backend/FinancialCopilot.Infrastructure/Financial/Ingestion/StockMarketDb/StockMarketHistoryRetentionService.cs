using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;

public sealed class StockMarketHistoryRetentionService(
    FinancialIngestionDbContext dbContext,
    IOptions<StockMarketDbProviderOptions> options,
    TimeProvider timeProvider) : IStockMarketHistoryRetentionService
{
    private readonly StockMarketDbProviderOptions _options = options.Value;

    public async Task<StockMarketHistoryRetentionResult> DeleteExpiredAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var tradeCutoff = today.AddDays(-Math.Max(1, _options.RetainIntradayTradeDays));
        var indexCutoff = today.AddDays(-Math.Max(1, _options.RetainIntradayIndexDays));

        var trades = await dbContext.IntradayTradeSnapshots
            .Where(row => row.ProviderName == _options.ProviderName && row.TradingDate < tradeCutoff)
            .ToListAsync(cancellationToken);
        var indices = await dbContext.IntradayIndexSnapshots
            .Where(row => row.ProviderName == _options.ProviderName && row.TradingDate < indexCutoff)
            .ToListAsync(cancellationToken);

        dbContext.IntradayTradeSnapshots.RemoveRange(trades);
        dbContext.IntradayIndexSnapshots.RemoveRange(indices);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new StockMarketHistoryRetentionResult(trades.Count, indices.Count);
    }
}
