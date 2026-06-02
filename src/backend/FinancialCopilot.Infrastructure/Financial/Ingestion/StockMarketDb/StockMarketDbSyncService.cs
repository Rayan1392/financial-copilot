using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;

public sealed class StockMarketDbSyncService(
    FinancialIngestionDbContext dbContext,
    IStockMarketDbQueryExecutor queryExecutor,
    IProviderRawPayloadStore rawPayloadStore,
    IOptions<StockMarketDbProviderOptions> options,
    IScannerCache scannerCache,
    IMarketViewCache marketViewCache,
    TimeProvider timeProvider) : IStockMarketDbSyncService, IStockMarketDbSyncStateReader
{
    private readonly StockMarketDbProviderOptions _options = options.Value;

    public async Task<StockMarketSyncResult> SynchronizeAsync(
        StockMarketDataset dataset,
        bool fullReload,
        CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var state = await dbContext.StockMarketSyncStates.SingleOrDefaultAsync(
            row => row.Dataset == dataset.ToString(), cancellationToken);
        if (state is null)
        {
            state = new StockMarketSyncStateRow { Dataset = dataset.ToString() };
            dbContext.StockMarketSyncStates.Add(state);
        }

        state.LastRunStartedAt = started;
        await dbContext.SaveChangesAsync(cancellationToken);

        var cursor = BuildCursor(state, dataset, fullReload);
        var rowsRead = 0;
        var rowsPersisted = 0;
        DateTimeOffset? observedWatermark = state.Watermark;

        switch (dataset)
        {
            case StockMarketDataset.Instruments:
            {
                var rows = await queryExecutor.QueryInstrumentsAsync(cursor, _options.PageSize, cancellationToken);
                await StoreRawPageAsync(ProviderDataset.TradingInstruments, rows, cursor.After, cancellationToken);
                rowsRead = rows.Count;
                rowsPersisted = await PersistInstrumentsAsync(rows, cancellationToken);
                observedWatermark = Max(rows.Select(row => row.ChangeTime), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.ChangeTime, rows.LastOrDefault()?.Id.ToString());
                break;
            }
            case StockMarketDataset.IntradayTrades:
            {
                var rows = await queryExecutor.QueryIntradayTradesAsync(cursor, _options.PageSize, cancellationToken);
                await StoreRawPageAsync(ProviderDataset.IntradayTrades, rows, cursor.After, cancellationToken);
                rowsRead = rows.Count;
                rowsPersisted = await PersistIntradayTradesAsync(rows, cancellationToken);
                EnsureAllReferencesResolved(dataset, rowsRead, rowsPersisted);
                observedWatermark = Max(rows.Select(row => row.ReceiveDate), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.ReceiveDate, rows.LastOrDefault()?.Id.ToString());
                break;
            }
            case StockMarketDataset.DailyTrades:
            {
                var rows = await queryExecutor.QueryDailyTradesAsync(cursor, _options.PageSize, cancellationToken);
                await StoreRawPageAsync(ProviderDataset.DailyTrades, rows, cursor.After, cancellationToken);
                rowsRead = rows.Count;
                rowsPersisted = await PersistDailyTradesAsync(rows, cancellationToken);
                EnsureAllReferencesResolved(dataset, rowsRead, rowsPersisted);
                observedWatermark = Max(rows.Select(row => row.InsertDateTime), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.InsertDateTime, rows.LastOrDefault()?.Id.ToString());
                break;
            }
            case StockMarketDataset.IntradayIndices:
            {
                var rows = await queryExecutor.QueryIntradayIndicesAsync(cursor, _options.PageSize, cancellationToken);
                await StoreRawPageAsync(ProviderDataset.IntradayIndices, rows, cursor.After, cancellationToken);
                rowsRead = rows.Count;
                rowsPersisted = await PersistIntradayIndicesAsync(rows, cancellationToken);
                EnsureAllReferencesResolved(dataset, rowsRead, rowsPersisted);
                observedWatermark = Max(rows.Select(row => row.ChangeTime), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.ChangeTime, rows.LastOrDefault()?.Id.ToString());
                break;
            }
            case StockMarketDataset.HistoricalDailyIndices:
            {
                var rows = await queryExecutor.QueryHistoricalDailyIndicesAsync(cursor, _options.PageSize, cancellationToken);
                await StoreRawPageAsync(ProviderDataset.DailyIndices, rows, cursor.After, cancellationToken);
                rowsRead = rows.Count;
                rowsPersisted = await PersistHistoricalIndicesAsync(rows, cancellationToken);
                EnsureAllReferencesResolved(dataset, rowsRead, rowsPersisted);
                observedWatermark = Max(rows.Select(row => row.ChangeTime).OfType<DateTimeOffset>(), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.ChangeTime, rows.LastOrDefault()?.Id.ToString());
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(dataset), dataset, null);
        }

        state.Watermark = observedWatermark;
        state.LastRunCompletedAt = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);

        if (dataset is StockMarketDataset.IntradayTrades or StockMarketDataset.DailyTrades)
        {
            await scannerCache.InvalidateAsync(
                new ScannerCacheInvalidation($"StockMarketDb.{dataset}", timeProvider.GetUtcNow()),
                cancellationToken);
        }
        if (dataset is StockMarketDataset.IntradayTrades or
            StockMarketDataset.DailyTrades or
            StockMarketDataset.IntradayIndices or
            StockMarketDataset.HistoricalDailyIndices)
        {
            await marketViewCache.InvalidateAsync(cancellationToken);
        }

        return new StockMarketSyncResult(
            dataset,
            rowsRead,
            rowsPersisted,
            state.Watermark,
            timeProvider.GetUtcNow() - started);
    }

    public async Task<IReadOnlyCollection<StockMarketSyncState>> QueryAsync(CancellationToken cancellationToken) =>
        (await dbContext.StockMarketSyncStates.AsNoTracking()
            .OrderBy(row => row.Dataset)
            .ToListAsync(cancellationToken))
        .Select(row => new StockMarketSyncState(
            Enum.Parse<StockMarketDataset>(row.Dataset),
            row.Watermark,
            row.LastRunStartedAt,
            row.LastRunCompletedAt))
        .ToArray();

    private async Task<int> PersistInstrumentsAsync(
        IReadOnlyCollection<StockMarketInstrumentRecord> rows,
        CancellationToken cancellationToken)
    {
        var codes = rows.Select(row => row.InsCode.ToString()).ToList();
        var companies = await dbContext.Companies
            .Where(row => row.InstrumentCode != null && codes.Contains(row.InstrumentCode))
            .ToDictionaryAsync(row => row.InstrumentCode!, row => row.Id, cancellationToken);

        foreach (var source in rows)
        {
            var row = await dbContext.TradingInstruments.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName &&
                        item.ExternalInstrumentId == source.Id,
                cancellationToken);
            if (row is null)
            {
                row = new TradingInstrumentRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalInstrumentId = source.Id
                };
                dbContext.TradingInstruments.Add(row);
            }

            row.InstrumentCode = source.InsCode;
            row.InstrumentIsin = source.InstrumentId;
            row.Symbol = source.Symbol;
            row.Name = source.Name;
            row.MarketCode = source.MarketCode;
            row.InstrumentKind = source.InstrumentKind;
            row.NormalizedCompanyId = companies.TryGetValue(source.InsCode.ToString(), out var companyId)
                ? companyId
                : null;
            row.IsActive = source.Valid == true && source.IsDeleted != true;
            row.SourceChangedAt = source.ChangeTime;
            row.LastSynchronizedAt = timeProvider.GetUtcNow();
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private async Task<int> PersistIntradayTradesAsync(
        IReadOnlyCollection<StockMarketIntradayTradeRecord> rows,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapAsync(rows.Select(row => row.InstrumentRef), cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            var row = await dbContext.IntradayTradeSnapshots.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName && item.ExternalSnapshotId == source.Id,
                cancellationToken);
            if (row is null)
            {
                row = new IntradayTradeSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalSnapshotId = source.Id };
                dbContext.IntradayTradeSnapshots.Add(row);
            }
            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.TradeDate;
            row.TradingTime = source.TradeTime;
            row.ClosingPrice = source.ClosingPrice;
            row.LastTradedPrice = source.LastTradedPrice;
            row.PriceChange = source.PriceChange;
            row.PriceYesterday = source.PriceYesterday;
            row.TotalTransactions = source.TotalTransactions;
            row.Volume = source.VolumeOfTradedShares;
            row.TotalCapital = source.TotalCapital;
            row.ReceivedAt = source.ReceiveDate;
            await UpsertLatestQuoteAsync(instrument.Id, source.LastTradedPrice, source.PriceChange, source.PriceYesterday, "Intraday", source.ReceiveDate, cancellationToken);
            persisted++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistDailyTradesAsync(
        IReadOnlyCollection<StockMarketDailyTradeRecord> rows,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapAsync(rows.Select(row => row.InstrumentRef), cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            var row = await dbContext.DailyInstrumentTrades.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName && item.ExternalTradeId == source.Id,
                cancellationToken);
            if (row is null)
            {
                row = new DailyInstrumentTradeRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalTradeId = source.Id };
                dbContext.DailyInstrumentTrades.Add(row);
            }
            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.TradeDate;
            row.ClosingPrice = source.ClosingPrice;
            row.LastTradedPrice = source.LastTradedPrice;
            row.PriceChange = source.PriceChange;
            row.PriceYesterday = source.PriceYesterday;
            row.TotalTransactions = source.TotalTransactions;
            row.Volume = source.VolumeOfTradedShares;
            row.TotalCapital = source.TotalCapital;
            row.MarketValue = source.MarketValue;
            row.SourceInsertedAt = source.InsertDateTime;
            await UpsertLatestQuoteAsync(instrument.Id, source.LastTradedPrice, source.PriceChange, source.PriceYesterday, "Daily", source.InsertDateTime, cancellationToken);
            persisted++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistIntradayIndicesAsync(
        IReadOnlyCollection<StockMarketIntradayIndexRecord> rows,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapAsync(rows.Select(row => row.InstrumentRef), cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            var row = await dbContext.IntradayIndexSnapshots.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName && item.ExternalSnapshotId == source.Id,
                cancellationToken);
            if (row is null)
            {
                row = new IntradayIndexSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalSnapshotId = source.Id };
                dbContext.IntradayIndexSnapshots.Add(row);
            }
            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.IndexDate;
            row.TradingTime = source.IndexTime;
            row.Value = source.Value;
            row.ChangePercent = source.ChangePercent;
            row.SourceChangedAt = source.ChangeTime;
            await UpsertDailyIndexAsync(instrument.Id, source.IndexDate, source.Value, source.Value, source.Value, source.ChangePercent, "IntradayClose", source.ChangeTime, cancellationToken);
            persisted++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistHistoricalIndicesAsync(
        IReadOnlyCollection<StockMarketHistoricalDailyIndexRecord> rows,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapAsync(rows.Select(row => row.InstrumentRef), cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            await UpsertDailyIndexAsync(instrument.Id, source.IndexDate, source.Value, source.High, source.Low, source.ChangePercent, "HistoricalBackfill", source.ChangeTime ?? DateTimeOffset.MinValue, cancellationToken);
            persisted++;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<Dictionary<Guid, TradingInstrumentRow>> InstrumentMapAsync(
        IEnumerable<Guid> refs,
        CancellationToken cancellationToken)
    {
        var ids = refs.Distinct().ToList();
        return await dbContext.TradingInstruments
            .Where(row => row.ProviderName == _options.ProviderName && ids.Contains(row.ExternalInstrumentId))
            .ToDictionaryAsync(row => row.ExternalInstrumentId, cancellationToken);
    }

    private async Task UpsertLatestQuoteAsync(
        Guid instrumentId, decimal price, decimal change, decimal yesterday,
        string sourceKind, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        var row = dbContext.LatestMarketQuotes.Local.SingleOrDefault(
            item => item.ProviderName == _options.ProviderName && item.TradingInstrumentId == instrumentId)
            ?? await dbContext.LatestMarketQuotes.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName && item.TradingInstrumentId == instrumentId,
                cancellationToken);
        if (row is not null && row.AsOf > asOf) return;
        if (row is null)
        {
            row = new LatestMarketQuoteRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, TradingInstrumentId = instrumentId };
            dbContext.LatestMarketQuotes.Add(row);
        }
        row.LatestPrice = price;
        row.PriceChangePercentage = yesterday == 0 ? 0 : change / yesterday * 100;
        row.SourceKind = sourceKind;
        row.AsOf = asOf;
    }

    private async Task UpsertDailyIndexAsync(
        Guid instrumentId, DateOnly date, decimal? value, decimal? high, decimal? low,
        decimal? changePercent, string sourceKind, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        var row = dbContext.DailyIndexSnapshots.Local.SingleOrDefault(
            item => item.ProviderName == _options.ProviderName &&
                    item.TradingInstrumentId == instrumentId &&
                    item.TradingDate == date)
            ?? await dbContext.DailyIndexSnapshots.SingleOrDefaultAsync(
                item => item.ProviderName == _options.ProviderName &&
                        item.TradingInstrumentId == instrumentId &&
                        item.TradingDate == date,
                cancellationToken);
        if (row is not null && row.ObservedAt > observedAt) return;
        if (row is null)
        {
            row = new DailyIndexSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, TradingInstrumentId = instrumentId, TradingDate = date };
            dbContext.DailyIndexSnapshots.Add(row);
        }
        row.Value = value;
        row.High = high;
        row.Low = low;
        row.ChangePercent = changePercent;
        row.SourceKind = sourceKind;
        row.ObservedAt = observedAt;
    }

    private static DateTimeOffset? Max(IEnumerable<DateTimeOffset> values, DateTimeOffset? current)
    {
        var materialized = values.ToList();
        return materialized.Count == 0 ? current : materialized.Max();
    }

    private StockMarketPageCursor BuildCursor(
        StockMarketSyncStateRow state,
        StockMarketDataset dataset,
        bool fullReload)
    {
        if (fullReload) return new StockMarketPageCursor(null);
        if (state.ContinuationWatermark is not null)
        {
            return dataset == StockMarketDataset.DailyTrades
                ? new StockMarketPageCursor(
                    state.ContinuationWatermark,
                    LastLongId: long.Parse(state.ContinuationExternalId!))
                : new StockMarketPageCursor(
                    state.ContinuationWatermark,
                    LastGuidId: Guid.Parse(state.ContinuationExternalId!));
        }
        return new StockMarketPageCursor(
            state.Watermark?.AddMinutes(-Math.Max(0, _options.OverlapMinutes)));
    }

    private void SetContinuation(
        StockMarketSyncStateRow state,
        int rowsRead,
        DateTimeOffset? watermark,
        string? externalId)
    {
        if (rowsRead >= _options.PageSize && watermark is not null && externalId is not null)
        {
            state.ContinuationWatermark = watermark;
            state.ContinuationExternalId = externalId;
            return;
        }
        state.ContinuationWatermark = null;
        state.ContinuationExternalId = null;
    }

    private static void EnsureAllReferencesResolved(StockMarketDataset dataset, int rowsRead, int rowsPersisted)
    {
        if (rowsRead == rowsPersisted) return;
        throw new InvalidOperationException(
            $"{dataset} contained {rowsRead - rowsPersisted} unresolved instrument references. " +
            "Synchronize the instrument dimension before retrying this page.");
    }

    private async Task StoreRawPageAsync<T>(
        ProviderDataset dataset,
        IReadOnlyCollection<T> rows,
        DateTimeOffset? after,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(rows);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await rawPayloadStore.StoreAsync(
            new ProviderRawPayload(
                Guid.NewGuid(),
                _options.ProviderName,
                dataset,
                $"stockmarketdb://{dataset}",
                after?.ToString("O") ?? "full",
                json,
                checksum,
                timeProvider.GetUtcNow()),
            cancellationToken);
    }
}
