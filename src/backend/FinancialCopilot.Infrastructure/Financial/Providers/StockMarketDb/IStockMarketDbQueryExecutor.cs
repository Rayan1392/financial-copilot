namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed record StockMarketPageCursor(
    DateTimeOffset? After,
    Guid? LastGuidId = null,
    long? LastLongId = null);

public interface IStockMarketDbQueryExecutor
{
    Task<IReadOnlyList<StockMarketInstrumentRecord>> QueryInstrumentsAsync(
        StockMarketPageCursor cursor,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMarketIntradayTradeRecord>> QueryIntradayTradesAsync(
        StockMarketPageCursor cursor,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMarketDailyTradeRecord>> QueryDailyTradesAsync(
        StockMarketPageCursor cursor,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMarketIntradayIndexRecord>> QueryIntradayIndicesAsync(
        StockMarketPageCursor cursor,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>> QueryHistoricalDailyIndicesAsync(
        StockMarketPageCursor cursor,
        int take,
        CancellationToken cancellationToken);
}
