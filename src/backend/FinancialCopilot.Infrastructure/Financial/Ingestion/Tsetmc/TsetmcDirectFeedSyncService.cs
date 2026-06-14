using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;

/// <summary>
/// Ingests market data directly from the TSETMC TsePublicV2 ASMX web service into the canonical
/// PostgreSQL tables (same rows as StockMarketDb). Provenance is stamped with
/// <see cref="ProviderSources.TsetmcWebService"/>. Does not replace StockMarketDb polling until
/// Phase 4 cutover.
/// </summary>
public sealed class TsetmcDirectFeedSyncService(
    FinancialIngestionDbContext dbContext,
    ITsetmcWebServiceClient client,
    IProviderRawPayloadStore rawPayloadStore,
    IOptions<TsetmcWebServiceOptions> options,
    IScannerCache scannerCache,
    IMarketViewCache marketViewCache,
    TimeProvider timeProvider,
    ILogger<TsetmcDirectFeedSyncService> logger) : ITsetmcDirectFeedSyncService
{
    private readonly TsetmcWebServiceOptions _options = options.Value;

    public bool IsOperational =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.UserName) &&
        !string.IsNullOrWhiteSpace(_options.Password);

    public async Task<TsetmcSyncResult> SynchronizeInstrumentsAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var totalFetched = 0;
        var totalPersisted = 0;

        foreach (var flow in _options.InstrumentFlows)
        {
            logger.LogInformation("TSETMC: fetching instruments for flow {Flow}.", flow);
            var records = await client.GetInstrumentsAsync(flow, cancellationToken);
            totalFetched += records.Count;

            if (records.Count > 0)
            {
                await StoreRawAsync("Instruments", flow, records, cancellationToken);
                totalPersisted += await PersistInstrumentsAsync(records, cancellationToken);
            }
        }

        await StampSyncStateAsync("Instruments", timeProvider.GetUtcNow(), cancellationToken);
        return new TsetmcSyncResult("Instruments", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeIntradayTradesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var totalFetched = 0;
        var totalPersisted = 0;

        foreach (var flow in _options.IntradayTradeFlows)
        {
            logger.LogInformation("TSETMC: fetching intraday trades for flow {Flow}.", flow);
            var records = await client.GetIntradayTradesAsync(flow, cancellationToken);
            totalFetched += records.Count;

            if (records.Count > 0)
            {
                await StoreRawAsync("IntradayTrades", flow, records, cancellationToken);
                totalPersisted += await PersistIntradayTradesAsync(records, cancellationToken);
            }
        }

        await StampSyncStateAsync("IntradayTrades", timeProvider.GetUtcNow(), cancellationToken);
        await scannerCache.InvalidateAsync(
            new ScannerCacheInvalidation("TsetmcWebService.IntradayTrades", timeProvider.GetUtcNow()),
            cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("IntradayTrades", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeDailyTradesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var totalFetched = 0;
        var totalPersisted = 0;

        var fromDate = ParseConfigDate(_options.DailyTradeFromDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
        var toDate = _options.DailyTradeToDate is not null
            ? ParseConfigDate(_options.DailyTradeToDate, DateOnly.FromDateTime(DateTime.UtcNow))
            : DateOnly.FromDateTime(DateTime.UtcNow);

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (IsWeekend(date)) continue;

            logger.LogDebug("TSETMC: fetching daily trades for {Date}.", date);
            var allFlowRecords = new List<TsetmcDailyTradeRecord>();

            for (byte flow = 0; flow <= 7; flow++)
            {
                var records = await client.GetDailyTradesAsync(date, flow, cancellationToken);
                allFlowRecords.AddRange(records);
            }

            totalFetched += allFlowRecords.Count;
            if (allFlowRecords.Count > 0)
            {
                await StoreRawAsync("DailyTrades", date.ToString("yyyyMMdd"), allFlowRecords, cancellationToken);
                totalPersisted += await PersistDailyTradesAsync(allFlowRecords, cancellationToken);
            }
        }

        await StampSyncStateAsync("DailyTrades", timeProvider.GetUtcNow(), cancellationToken);
        await scannerCache.InvalidateAsync(
            new ScannerCacheInvalidation("TsetmcWebService.DailyTrades", timeProvider.GetUtcNow()),
            cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("DailyTrades", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeDailyIndicesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var totalFetched = 0;
        var totalPersisted = 0;

        var fromDate = ParseConfigDate(_options.DailyIndexFromDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
        var toDate = _options.DailyIndexToDate is not null
            ? ParseConfigDate(_options.DailyIndexToDate, DateOnly.FromDateTime(DateTime.UtcNow))
            : DateOnly.FromDateTime(DateTime.UtcNow);

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            if (IsWeekend(date)) continue;

            logger.LogDebug("TSETMC: fetching daily indices for {Date}.", date);
            var records = await client.GetDailyIndicesAsync(date, cancellationToken);
            totalFetched += records.Count;

            if (records.Count > 0)
            {
                await StoreRawAsync("DailyIndices", date.ToString("yyyyMMdd"), records, cancellationToken);
                totalPersisted += await PersistDailyIndicesAsync(records, date, cancellationToken);
            }
        }

        await StampSyncStateAsync("HistoricalDailyIndices", timeProvider.GetUtcNow(), cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("DailyIndices", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeIntradayIndicesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var totalFetched = 0;
        var totalPersisted = 0;

        // IndexB1LastDayLastData flows: 0=normal, 1=bourse, 2=farabourse, 3=ati, 4=paye farabourse
        for (byte flow = 0; flow <= 4; flow++)
        {
            logger.LogInformation("TSETMC: fetching intraday indices for flow {Flow}.", flow);
            var records = await client.GetIntradayIndicesAsync(flow, cancellationToken);
            totalFetched += records.Count;

            if (records.Count > 0)
            {
                await StoreRawAsync("IntradayIndices", flow, records, cancellationToken);
                totalPersisted += await PersistIntradayIndicesAsync(records, cancellationToken);
            }
        }

        await StampSyncStateAsync("IntradayIndices", timeProvider.GetUtcNow(), cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("IntradayIndices", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    // --- persistence methods ---

    private async Task<int> PersistInstrumentsAsync(
        IReadOnlyList<TsetmcInstrumentRecord> records,
        CancellationToken cancellationToken)
    {
        // Resolve company linkage via InstrumentCode (InsCode as string)
        var codes = records.Select(r => r.InsCode.ToString()).Distinct().ToList();
        var companies = (await dbContext.Companies
                .Where(row => row.InstrumentCode != null && codes.Contains(row.InstrumentCode))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.InstrumentCode!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(row => row.LastSynchronizedAt).ThenBy(row => row.Id).First().Id);

        // Build stable deterministic ExternalInstrumentId from InsCode
        var insCodes = records.Select(r => BuildInstrumentGuid(r.InsCode)).Distinct().ToList();
        var existing = (await dbContext.TradingInstruments
                .Where(row => row.ProviderName == _options.ProviderName && insCodes.Contains(row.ExternalInstrumentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalInstrumentId);

        var now = timeProvider.GetUtcNow();
        foreach (var source in records)
        {
            var extId = BuildInstrumentGuid(source.InsCode);
            if (!existing.TryGetValue(extId, out var row))
            {
                row = new TradingInstrumentRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalInstrumentId = extId
                };
                dbContext.TradingInstruments.Add(row);
                existing[extId] = row;
            }

            row.InstrumentCode = source.InsCode;
            row.InstrumentIsin = source.InstrumentId;
            row.Symbol = source.Symbol;
            row.Name = source.CompanyName;
            row.MarketCode = source.MarketCode;
            row.InstrumentKind = source.InstrumentKind;
            row.NormalizedCompanyId = companies.TryGetValue(source.InsCode.ToString(), out var cId) ? cId : null;
            row.IsActive = source.Valid;
            row.SourceChangedAt = now;
            row.LastSynchronizedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<int> PersistIntradayTradesAsync(
        IReadOnlyList<TsetmcIntradayTradeRecord> records,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapByInsCodeAsync(records.Select(r => r.InsCode), cancellationToken);
        var now = timeProvider.GetUtcNow();

        // Intraday snapshots: keyed by (InsCode, TradingDate, TradingTime) — use a deterministic GUID
        var extIds = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => BuildIntradayTradeGuid(r.InsCode, r.TradingDate, r.TradingTime))
            .Distinct().ToList();
        var existing = (await dbContext.IntradayTradeSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);

        var instrumentIdsForQuotes = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => instruments[r.InsCode].Id)
            .Distinct().ToList();
        var quotes = await LatestQuoteMapAsync(instrumentIdsForQuotes, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            if (!instruments.TryGetValue(source.InsCode, out var instrument)) continue;

            var extId = BuildIntradayTradeGuid(source.InsCode, source.TradingDate, source.TradingTime);
            if (!existing.TryGetValue(extId, out var row))
            {
                row = new IntradayTradeSnapshotRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalSnapshotId = extId
                };
                dbContext.IntradayTradeSnapshots.Add(row);
                existing[extId] = row;
            }

            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.TradingDate;
            row.TradingTime = source.TradingTime;
            row.ClosingPrice = source.ClosingPrice;
            row.LastTradedPrice = source.LastTradedPrice;
            row.PriceChange = source.PriceChange;
            row.PriceYesterday = source.PriceYesterday;
            row.TotalTransactions = source.TotalTransactions;
            row.Volume = source.Volume;
            row.TotalCapital = source.TotalCapital;
            row.ReceivedAt = now;

            UpsertLatestQuote(quotes, instrument.Id, source.LastTradedPrice, source.PriceChange,
                source.PriceYesterday, "Intraday", source.TradingDate, now);
            persisted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistDailyTradesAsync(
        IReadOnlyList<TsetmcDailyTradeRecord> records,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapByInsCodeAsync(records.Select(r => r.InsCode), cancellationToken);
        var now = timeProvider.GetUtcNow();

        // Daily trades: keyed by (InsCode, TradingDate)
        var extIds = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => BuildDailyTradeGuid(r.InsCode, r.TradingDate))
            .Distinct().ToList();
        var existing = (await dbContext.DailyInstrumentTrades
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalTradeId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalTradeId);

        var instrumentIdsForQuotes = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => instruments[r.InsCode].Id)
            .Distinct().ToList();
        var quotes = await LatestQuoteMapAsync(instrumentIdsForQuotes, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            if (!instruments.TryGetValue(source.InsCode, out var instrument)) continue;

            var extId = BuildDailyTradeGuid(source.InsCode, source.TradingDate);
            if (!existing.TryGetValue(extId, out var row))
            {
                row = new DailyInstrumentTradeRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalTradeId = extId
                };
                dbContext.DailyInstrumentTrades.Add(row);
                existing[extId] = row;
            }

            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.TradingDate;
            row.ClosingPrice = source.ClosingPrice;
            row.LastTradedPrice = source.LastTradedPrice;
            row.PriceChange = source.PriceChange;
            row.PriceYesterday = source.PriceYesterday;
            row.TotalTransactions = source.TotalTransactions;
            row.Volume = source.Volume;
            row.TotalCapital = source.TotalCapital;
            row.MarketValue = 0; // TSETMC TradeOneDay does not include MarketValue
            row.SourceInsertedAt = now;

            UpsertLatestQuote(quotes, instrument.Id, source.LastTradedPrice, source.PriceChange,
                source.PriceYesterday, "Daily", source.TradingDate, now);
            persisted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistDailyIndicesAsync(
        IReadOnlyList<TsetmcDailyIndexRecord> records,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapByInsCodeAsync(records.Select(r => r.InsCode), cancellationToken);
        var now = timeProvider.GetUtcNow();

        var keys = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => (instruments[r.InsCode].Id, r.IndexDate))
            .Distinct().ToList();
        var dailyIndices = await DailyIndexMapAsync(keys, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            if (!instruments.TryGetValue(source.InsCode, out var instrument)) continue;
            UpsertDailyIndex(dailyIndices, instrument.Id, source.IndexDate, source.Value,
                source.High, source.Low, source.ChangePercent, "HistoricalBackfill", now);
            persisted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    private async Task<int> PersistIntradayIndicesAsync(
        IReadOnlyList<TsetmcIntradayIndexRecord> records,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapByInsCodeAsync(records.Select(r => r.InsCode), cancellationToken);
        var now = timeProvider.GetUtcNow();

        var extIds = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => BuildIntradayIndexGuid(r.InsCode, r.IndexDate, r.IndexTime))
            .Distinct().ToList();
        var existing = (await dbContext.IntradayIndexSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);

        var keys = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => (instruments[r.InsCode].Id, r.IndexDate))
            .Distinct().ToList();
        var dailyIndices = await DailyIndexMapAsync(keys, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            if (!instruments.TryGetValue(source.InsCode, out var instrument)) continue;

            var extId = BuildIntradayIndexGuid(source.InsCode, source.IndexDate, source.IndexTime);
            if (!existing.TryGetValue(extId, out var row))
            {
                row = new IntradayIndexSnapshotRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalSnapshotId = extId
                };
                dbContext.IntradayIndexSnapshots.Add(row);
                existing[extId] = row;
            }

            row.TradingInstrumentId = instrument.Id;
            row.TradingDate = source.IndexDate;
            row.TradingTime = source.IndexTime;
            row.Value = source.Value;
            row.ChangePercent = source.ChangePercent;
            row.SourceChangedAt = now;

            UpsertDailyIndex(dailyIndices, instrument.Id, source.IndexDate, source.Value,
                source.Value, source.Value, source.ChangePercent, "IntradayClose", now);
            persisted++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return persisted;
    }

    // --- infrastructure helpers ---

    private async Task<Dictionary<long, TradingInstrumentRow>> InstrumentMapByInsCodeAsync(
        IEnumerable<long> insCodes,
        CancellationToken cancellationToken)
    {
        var codes = insCodes.Distinct().ToList();
        return await dbContext.TradingInstruments
            .Where(row => row.ProviderName == _options.ProviderName && codes.Contains(row.InstrumentCode))
            .ToDictionaryAsync(row => row.InstrumentCode, cancellationToken);
    }

    private async Task<Dictionary<Guid, LatestMarketQuoteRow>> LatestQuoteMapAsync(
        IReadOnlyList<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        return await dbContext.LatestMarketQuotes
            .Where(row => row.ProviderName == _options.ProviderName && instrumentIds.Contains(row.TradingInstrumentId))
            .ToDictionaryAsync(row => row.TradingInstrumentId, cancellationToken);
    }

    private async Task<Dictionary<(Guid, DateOnly), DailyIndexSnapshotRow>> DailyIndexMapAsync(
        IReadOnlyList<(Guid InstrumentId, DateOnly Date)> keys,
        CancellationToken cancellationToken)
    {
        var instrumentIds = keys.Select(k => k.InstrumentId).Distinct().ToList();
        var dates = keys.Select(k => k.Date).Distinct().ToList();
        var rows = await dbContext.DailyIndexSnapshots
            .Where(row => row.ProviderName == _options.ProviderName &&
                          instrumentIds.Contains(row.TradingInstrumentId) &&
                          dates.Contains(row.TradingDate))
            .ToListAsync(cancellationToken);
        var wanted = keys.ToHashSet();
        return rows
            .Where(row => wanted.Contains((row.TradingInstrumentId, row.TradingDate)))
            .ToDictionary(row => (row.TradingInstrumentId, row.TradingDate));
    }

    private void UpsertLatestQuote(
        Dictionary<Guid, LatestMarketQuoteRow> quotes,
        Guid instrumentId, decimal price, decimal change, decimal yesterday,
        string sourceKind, DateOnly tradingDate, DateTimeOffset asOf)
    {
        if (quotes.TryGetValue(instrumentId, out var row))
        {
            if (row.SourceKind == "Intraday" && sourceKind == "Daily" && row.TradingDate == tradingDate) return;
            if (row.AsOf > asOf && !(row.SourceKind == "Daily" && sourceKind == "Intraday" && row.TradingDate == tradingDate)) return;
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

    private void UpsertDailyIndex(
        Dictionary<(Guid, DateOnly), DailyIndexSnapshotRow> dailyIndices,
        Guid instrumentId, DateOnly date, decimal? value, decimal? high, decimal? low,
        decimal? changePercent, string sourceKind, DateTimeOffset observedAt)
    {
        if (dailyIndices.TryGetValue((instrumentId, date), out var row) && row.ObservedAt > observedAt) return;
        if (row is null)
        {
            row = new DailyIndexSnapshotRow
            {
                Id = Guid.NewGuid(),
                ProviderName = _options.ProviderName,
                TradingInstrumentId = instrumentId,
                TradingDate = date
            };
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

    private async Task StampSyncStateAsync(string dataset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = await dbContext.StockMarketSyncStates
            .SingleOrDefaultAsync(row => row.Dataset == $"Tsetmc_{dataset}", cancellationToken);
        if (state is null)
        {
            state = new StockMarketSyncStateRow { Dataset = $"Tsetmc_{dataset}" };
            dbContext.StockMarketSyncStates.Add(state);
        }
        state.LogicalVendor = ProviderSources.TsetmcWebService.Vendor.ToString();
        state.PhysicalSource = ProviderSources.TsetmcWebService.Source.ToString();
        state.SourceMode = ProviderSources.TsetmcWebService.DefaultMode.ToString();
        state.Watermark = now;
        state.LastRunCompletedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task StoreRawAsync<T>(string dataset, object key, IReadOnlyList<T> records, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(records);
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var providerDataset = dataset switch
            {
                "Instruments" => ProviderDataset.TradingInstruments,
                "IntradayTrades" => ProviderDataset.IntradayTrades,
                "DailyTrades" => ProviderDataset.DailyTrades,
                "IntradayIndices" => ProviderDataset.IntradayIndices,
                "DailyIndices" => ProviderDataset.DailyIndices,
                _ => ProviderDataset.TradingInstruments
            };
            await rawPayloadStore.StoreAsync(
                new ProviderRawPayload(
                    Guid.NewGuid(),
                    _options.ProviderName,
                    providerDataset,
                    $"tsetmc://{dataset}",
                    $"{key}",
                    json,
                    checksum,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TSETMC: failed to store raw payload for {Dataset}/{Key}.", dataset, key);
        }
    }

    // --- deterministic GUID helpers (no external randomness) ---

    private static Guid BuildInstrumentGuid(long insCode) =>
        new(0, 0, 0, BitConverter.GetBytes(insCode).Concat(new byte[8]).Take(8).ToArray());

    private static Guid BuildDailyTradeGuid(long insCode, DateOnly date) =>
        GuidFromLongs(insCode, date.DayNumber);

    private static Guid BuildIntradayTradeGuid(long insCode, DateOnly date, TimeOnly time) =>
        GuidFromLongs(insCode, (long)date.DayNumber * 100000 + time.Hour * 3600 + time.Minute * 60 + time.Second);

    private static Guid BuildIntradayIndexGuid(long insCode, DateOnly date, TimeOnly time) =>
        GuidFromLongs(insCode + 1, (long)date.DayNumber * 100000 + time.Hour * 3600 + time.Minute * 60 + time.Second);

    private static Guid GuidFromLongs(long a, long b)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], a);
        BitConverter.TryWriteBytes(bytes[8..], b);
        return new Guid(bytes);
    }

    private static DateOnly ParseConfigDate(string s, DateOnly fallback) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : fallback;

    // Iranian calendar: Friday and Thursday are weekend
    private static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Thursday;
}
