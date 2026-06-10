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

        var cursor = BuildCursor(state, fullReload);
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
                observedWatermark = Max(rows.Select(row => row.ChangeTime), observedWatermark);
                SetContinuation(state, rows.Count, rows.LastOrDefault()?.ChangeTime, rows.LastOrDefault()?.Id.ToString());
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
        // The same InstrumentCode can map to more than one normalized company row because
        // companies are provider-scoped (e.g. NoavaranCurrentApi and NoavaranArchiveSql both carry
        // the listing). Pick one deterministic canonical company per code — the most recently
        // synchronized (current source beats archive), tie-broken by Id — so linkage is stable and
        // the dictionary build cannot throw on duplicates.
        var companies = (await dbContext.Companies
                .Where(row => row.InstrumentCode != null && codes.Contains(row.InstrumentCode))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.InstrumentCode!)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.LastSynchronizedAt)
                    .ThenBy(row => row.Id)
                    .First().Id);
        var sourceIds = rows.Select(row => row.Id).Distinct().ToList();
        var existing = (await dbContext.TradingInstruments
                .Where(row => row.ProviderName == _options.ProviderName && sourceIds.Contains(row.ExternalInstrumentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalInstrumentId);

        foreach (var source in rows)
        {
            if (!existing.TryGetValue(source.Id, out var row))
            {
                row = new TradingInstrumentRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalInstrumentId = source.Id
                };
                dbContext.TradingInstruments.Add(row);
                existing[source.Id] = row;
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
        var sourceIds = rows.Select(row => row.Id).Distinct().ToList();
        var existing = (await dbContext.IntradayTradeSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && sourceIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);
        var quotes = await LatestQuoteMapAsync(
            rows.Where(row => instruments.ContainsKey(row.InstrumentRef))
                .Select(row => instruments[row.InstrumentRef].Id),
            cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            if (!existing.TryGetValue(source.Id, out var row))
            {
                row = new IntradayTradeSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalSnapshotId = source.Id };
                dbContext.IntradayTradeSnapshots.Add(row);
                existing[source.Id] = row;
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
            UpsertLatestQuote(quotes, instrument.Id, source.LastTradedPrice, source.PriceChange, source.PriceYesterday, "Intraday", source.TradeDate, source.ReceiveDate);
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
        var sourceIds = rows.Select(row => row.Id).Distinct().ToList();
        var existing = (await dbContext.DailyInstrumentTrades
                .Where(row => row.ProviderName == _options.ProviderName && sourceIds.Contains(row.ExternalTradeId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalTradeId);
        var quotes = await LatestQuoteMapAsync(
            rows.Where(row => instruments.ContainsKey(row.InstrumentRef))
                .Select(row => instruments[row.InstrumentRef].Id),
            cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            if (!existing.TryGetValue(source.Id, out var row))
            {
                row = new DailyInstrumentTradeRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalTradeId = source.Id };
                dbContext.DailyInstrumentTrades.Add(row);
                existing[source.Id] = row;
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
            row.SourceInsertedAt = source.ChangeTime;
            UpsertLatestQuote(quotes, instrument.Id, source.LastTradedPrice, source.PriceChange, source.PriceYesterday, "Daily", source.TradeDate, source.ChangeTime);
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
        var sourceIds = rows.Select(row => row.Id).Distinct().ToList();
        var existing = (await dbContext.IntradayIndexSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && sourceIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);
        var dailyIndices = await DailyIndexMapAsync(
            rows.Where(row => instruments.ContainsKey(row.InstrumentRef))
                .Select(row => (instruments[row.InstrumentRef].Id, row.IndexDate)),
            cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            if (!existing.TryGetValue(source.Id, out var row))
            {
                row = new IntradayIndexSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, ExternalSnapshotId = source.Id };
                dbContext.IntradayIndexSnapshots.Add(row);
                existing[source.Id] = row;
            }
            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.IndexDate;
            row.TradingTime = source.IndexTime;
            row.Value = source.Value;
            row.ChangePercent = source.ChangePercent;
            row.SourceChangedAt = source.ChangeTime;
            UpsertDailyIndex(dailyIndices, instrument.Id, source.IndexDate, source.Value, source.Value, source.Value, source.ChangePercent, "IntradayClose", source.ChangeTime);
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
        var dailyIndices = await DailyIndexMapAsync(
            rows.Where(row => instruments.ContainsKey(row.InstrumentRef))
                .Select(row => (instruments[row.InstrumentRef].Id, row.IndexDate)),
            cancellationToken);
        var persisted = 0;
        foreach (var source in rows)
        {
            if (!instruments.TryGetValue(source.InstrumentRef, out var instrument)) continue;
            UpsertDailyIndex(dailyIndices, instrument.Id, source.IndexDate, source.Value, source.High, source.Low, source.ChangePercent, "HistoricalBackfill", source.ChangeTime ?? DateTimeOffset.MinValue);
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

    private async Task<Dictionary<Guid, LatestMarketQuoteRow>> LatestQuoteMapAsync(
        IEnumerable<Guid> instrumentIds, CancellationToken cancellationToken)
    {
        var ids = instrumentIds.Distinct().ToList();
        var rows = await dbContext.LatestMarketQuotes
            .Where(row => row.ProviderName == _options.ProviderName && ids.Contains(row.TradingInstrumentId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.TradingInstrumentId);
    }

    private void UpsertLatestQuote(
        Dictionary<Guid, LatestMarketQuoteRow> quotes,
        Guid instrumentId, decimal price, decimal change, decimal yesterday,
        string sourceKind, DateOnly tradingDate, DateTimeOffset asOf)
    {
        if (quotes.TryGetValue(instrumentId, out var row))
        {
            // Intraday data for a given trading day takes precedence over Daily data for the same
            // day, regardless of which sync ran last. This ensures a live intraday quote is never
            // silently replaced by the daily close record when both arrive on the same session day.
            if (row.SourceKind == "Intraday" && sourceKind == "Daily" && row.TradingDate == tradingDate)
                return;
            if (row.AsOf > asOf && !(row.SourceKind == "Daily" && sourceKind == "Intraday" && row.TradingDate == tradingDate))
                return;
        }
        if (row is null)
        {
            row = new LatestMarketQuoteRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, TradingInstrumentId = instrumentId };
            dbContext.LatestMarketQuotes.Add(row);
            quotes[instrumentId] = row;
        }
        row.LatestPrice = price;
        row.PriceChangePercentage = yesterday == 0 ? 0 : change / yesterday * 100;
        row.SourceKind = sourceKind;
        row.TradingDate = tradingDate;
        row.AsOf = asOf;
    }

    private async Task<Dictionary<(Guid, DateOnly), DailyIndexSnapshotRow>> DailyIndexMapAsync(
        IEnumerable<(Guid InstrumentId, DateOnly Date)> keys, CancellationToken cancellationToken)
    {
        var distinct = keys.Distinct().ToList();
        var instrumentIds = distinct.Select(key => key.InstrumentId).Distinct().ToList();
        var dates = distinct.Select(key => key.Date).Distinct().ToList();
        // Over-fetch by the cross product of the page's instruments and dates, then filter to the
        // exact pairs in memory. The page is bounded, so this stays a single small query.
        var rows = await dbContext.DailyIndexSnapshots
            .Where(row => row.ProviderName == _options.ProviderName &&
                          instrumentIds.Contains(row.TradingInstrumentId) &&
                          dates.Contains(row.TradingDate))
            .ToListAsync(cancellationToken);
        var wanted = distinct.ToHashSet();
        return rows
            .Where(row => wanted.Contains((row.TradingInstrumentId, row.TradingDate)))
            .ToDictionary(row => (row.TradingInstrumentId, row.TradingDate));
    }

    private void UpsertDailyIndex(
        Dictionary<(Guid, DateOnly), DailyIndexSnapshotRow> dailyIndices,
        Guid instrumentId, DateOnly date, decimal? value, decimal? high, decimal? low,
        decimal? changePercent, string sourceKind, DateTimeOffset observedAt)
    {
        if (dailyIndices.TryGetValue((instrumentId, date), out var row) && row.ObservedAt > observedAt) return;
        if (row is null)
        {
            row = new DailyIndexSnapshotRow { Id = Guid.NewGuid(), ProviderName = _options.ProviderName, TradingInstrumentId = instrumentId, TradingDate = date };
            dbContext.DailyIndexSnapshots.Add(row);
            dailyIndices[(instrumentId, date)] = row;
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
        bool fullReload)
    {
        if (fullReload) return new StockMarketPageCursor(null);
        if (state.ContinuationWatermark is not null)
        {
            // All datasets key on a uniqueidentifier source Id and watermark on a source timestamp,
            // so the keyset continuation cursor is uniform across them.
            return new StockMarketPageCursor(
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
        throw new StockMarketUnresolvedInstrumentException(dataset, rowsRead - rowsPersisted);
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
