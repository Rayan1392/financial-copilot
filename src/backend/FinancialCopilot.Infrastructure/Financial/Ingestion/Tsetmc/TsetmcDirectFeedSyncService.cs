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
using Npgsql;

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
    ILogger<TsetmcDirectFeedSyncService> logger) : ITsetmcDirectFeedSyncService, ITsetmcSyncStateReader
{
    private readonly TsetmcWebServiceOptions _options = options.Value;

    public bool IsOperational =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.UserName) &&
        !string.IsNullOrWhiteSpace(_options.Password);

    public async Task<TsetmcSyncResult> SynchronizeInstrumentsAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var fetched = new List<TsetmcInstrumentRecord>();
        var failedFlows = new List<byte>();
        var successfulFlows = 0;

        foreach (var flow in _options.InstrumentFlows.Distinct())
        {
            logger.LogInformation("TSETMC: fetching instruments for flow {Flow}.", flow);
            IReadOnlyList<TsetmcInstrumentRecord> records;
            try
            {
                records = await client.GetInstrumentsAsync(flow, cancellationToken);
            }
            catch (FinancialProviderException exception) when (!cancellationToken.IsCancellationRequested)
            {
                failedFlows.Add(flow);
                logger.LogWarning(exception, "TSETMC: instrument flow {Flow} failed.", flow);
                continue;
            }

            if (records.Count == 0)
            {
                failedFlows.Add(flow);
                logger.LogWarning("TSETMC: instrument flow {Flow} returned an empty catalog.", flow);
                continue;
            }

            successfulFlows++;
            fetched.AddRange(records);
            // Derivative flows are filtered to listed option contracts by the client; their raw
            // response (tens of megabytes) is deliberately not stored.
            if (!_options.DerivativeInstrumentFlows.Contains(flow))
                await StoreRawAsync("Instruments", flow, records, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var byCode = fetched.ToLookup(record => record.InsCode);
        var candidates = byCode
            .Select(group => group.First() with { InstrumentId = NormalizeInstrumentIsin(group.First().InstrumentId) })
            .Where(record => record.InsCode > 0 && record.InstrumentId.Length > 0 && record.Valid.HasValue &&
                byCode[record.InsCode].All(other =>
                    NormalizeInstrumentIsin(other.InstrumentId) == record.InstrumentId && other.Valid == record.Valid))
            .ToArray();
        var duplicateActiveIsins = candidates.Where(record => record.Valid == true)
            .GroupBy(record => record.InstrumentId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        candidates = candidates.Where(record => record.Valid != true || !duplicateActiveIsins.Contains(record.InstrumentId))
            .ToArray();
        var candidateCodes = candidates.Select(record => record.InsCode).ToHashSet();
        if (candidateCodes.Count < byCode.Count)
            logger.LogWarning("TSETMC: rejected {Count} invalid or conflicting instrument codes. Sample: {Codes}.",
                byCode.Count - candidateCodes.Count,
                string.Join(", ", byCode.Select(group => group.Key).Where(code => !candidateCodes.Contains(code)).Take(20)));

        var coverage = new TsetmcInstrumentSyncCoverage(successfulFlows, failedFlows, byCode.Count - candidates.Length);
        int persisted;
        try
        {
            persisted = await SaveInstrumentSyncAsync(candidates, started, coverage, cancellationToken);
        }
        catch (DbUpdateException exception) when (!cancellationToken.IsCancellationRequested &&
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A trade sync can create a stub for a new instrument between our read and insert.
            dbContext.ChangeTracker.Clear();
            persisted = await SaveInstrumentSyncAsync(candidates, started, coverage, cancellationToken);
        }

        return new TsetmcSyncResult("Instruments", fetched.Count, persisted, timeProvider.GetUtcNow() - started)
        {
            InstrumentCoverage = coverage with { RejectedInstruments = byCode.Count - persisted }
        };
    }

    private async Task<int> SaveInstrumentSyncAsync(
        IReadOnlyList<TsetmcInstrumentRecord> records,
        DateTimeOffset started,
        TsetmcInstrumentSyncCoverage coverage,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsNpgsql()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (transaction is not null)
            await dbContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(hashtext('tsetmc-instrument-catalog'));", cancellationToken);

        await StampSyncStateStartAsync("Instruments", started, cancellationToken);
        var persisted = records.Count == 0 ? 0 : await PersistInstrumentsAsync(records, cancellationToken);
        await StampSyncStateEndAsync("Instruments", timeProvider.GetUtcNow(), cancellationToken,
            advanceWatermark: coverage.IsComplete && persisted == records.Count);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return persisted;
    }

    public async Task<TsetmcSyncResult> SynchronizeIntradayTradesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        await StampSyncStateStartAsync("IntradayTrades", started, cancellationToken);
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

        await StampSyncStateEndAsync("IntradayTrades", timeProvider.GetUtcNow(), cancellationToken);
        await scannerCache.InvalidateAsync(
            new ScannerCacheInvalidation("TsetmcWebService.IntradayTrades", timeProvider.GetUtcNow()),
            cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("IntradayTrades", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeDailyTradesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        await StampSyncStateStartAsync("DailyTrades", started, cancellationToken);
        var totalFetched = 0;
        var totalPersisted = 0;

        var lasttradedDate = await dbContext.DailyInstrumentTrades
            .Where(row => row.ProviderName == _options.ProviderName)
            .MaxAsync(row => (DateOnly?)row.TradingDate, cancellationToken);

        var fromDate = lasttradedDate.HasValue
            ? lasttradedDate.Value.AddDays(1)
            : ParseConfigDate(_options.DailyTradeFromDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
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

        await StampSyncStateEndAsync("DailyTrades", timeProvider.GetUtcNow(), cancellationToken);
        await scannerCache.InvalidateAsync(
            new ScannerCacheInvalidation("TsetmcWebService.DailyTrades", timeProvider.GetUtcNow()),
            cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("DailyTrades", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeDailyIndicesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        await StampSyncStateStartAsync("HistoricalDailyIndices", started, cancellationToken);
        var totalFetched = 0;
        var totalPersisted = 0;

        var lastIndexedDate = await dbContext.DailyIndexSnapshots
            .Where(row => row.ProviderName == _options.ProviderName)
            .MaxAsync(row => (DateOnly?)row.TradingDate, cancellationToken);
        var fromDate = lastIndexedDate.HasValue
            ? lastIndexedDate.Value.AddDays(1)
            : ParseConfigDate(_options.DailyIndexFromDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));
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

        await StampSyncStateEndAsync("HistoricalDailyIndices", timeProvider.GetUtcNow(), cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("DailyIndices", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    public async Task<TsetmcSyncResult> SynchronizeIntradayIndicesAsync(CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        await StampSyncStateStartAsync("IntradayIndices", started, cancellationToken);
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

        await StampSyncStateEndAsync("IntradayIndices", timeProvider.GetUtcNow(), cancellationToken);
        await marketViewCache.InvalidateAsync(cancellationToken);

        return new TsetmcSyncResult("IntradayIndices", totalFetched, totalPersisted, timeProvider.GetUtcNow() - started);
    }

    // --- persistence methods ---

    private async Task<int> PersistInstrumentsAsync(
        IReadOnlyList<TsetmcInstrumentRecord> records,
        CancellationToken cancellationToken)
    {
        // Resolve company linkage via InstrumentCode (InsCode as string)
        var instrumentCodes = records.Select(row => row.InsCode).ToArray();
        var codes = instrumentCodes.Select(code => code.ToString()).ToArray();
        var companies = (await dbContext.Companies
                .AsNoTracking().Where(row => row.InstrumentCode != null && codes.Contains(row.InstrumentCode))
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.InstrumentCode!);

        // Prices may already reference a stub or an instrument originally loaded from the bridge.
        var activeIsins = records.Where(row => row.Valid == true).Select(row => row.InstrumentId).ToArray();
        var existing = await dbContext.TradingInstruments
            .Where(row => instrumentCodes.Contains(row.InstrumentCode) ||
                (row.IsActive && activeIsins.Contains(row.InstrumentIsin.Trim().ToUpper())))
            .ToListAsync(cancellationToken);
        var existingByCode = existing.ToLookup(row => row.InstrumentCode);
        var existingActiveByIsin = existing.Where(row => row.IsActive)
            .ToLookup(row => NormalizeInstrumentIsin(row.InstrumentIsin), StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow();
        var rejected = new List<long>();
        foreach (var source in records)
        {
            var existingRows = existingByCode[source.InsCode];
            var row = existingRows.FirstOrDefault();
            var existingIsin = NormalizeInstrumentIsin(row?.InstrumentIsin ?? string.Empty);
            if (existingRows.Count() > 1 || (existingIsin.Length > 0 && existingIsin != source.InstrumentId) ||
                (source.Valid == true && existingActiveByIsin[source.InstrumentId].Any(other => other.InstrumentCode != source.InsCode)))
            {
                rejected.Add(source.InsCode);
                continue;
            }

            if (row is null)
            {
                row = new TradingInstrumentRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalInstrumentId = BuildInstrumentGuid(source.InsCode),
                    InstrumentCode = source.InsCode
                };
                dbContext.TradingInstruments.Add(row);
            }

            row.InstrumentIsin = source.InstrumentId;
            // The symbol of an instrument is its Persian trading symbol (LVal18AFC, for example
            // «اخابر»), the same value the daily trade feed writes and every reader matches on.
            // The provider's Latin mnemonic (CValMne, for example MKBT1) is only a fallback for a
            // row that has no usable symbol at all (BUG-055).
            var tradingSymbol = source.SymbolFull.Trim();
            if (tradingSymbol.Length > 0)
                row.Symbol = tradingSymbol;
            else if (TsetmcInstrumentStub.HasStubSymbol(row.Symbol, row.InstrumentCode))
                row.Symbol = source.Symbol.Trim();
            row.Name = source.CompanyName;
            row.MarketCode = source.MarketCode;
            row.InstrumentKind = source.InstrumentKind;
            row.NormalizedCompanyId ??= companies[source.InsCode.ToString()]
                .Where(company => NormalizeInstrumentIsin(company.SymbolIsin ?? string.Empty) == source.InstrumentId)
                .OrderByDescending(company => company.LastSynchronizedAt).ThenBy(company => company.Id)
                .Select(company => (Guid?)company.Id).FirstOrDefault();
            row.IsActive = source.Valid == true;
            row.SourceChangedAt = now;
            row.LastSynchronizedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        if (rejected.Count > 0)
            logger.LogWarning("TSETMC: skipped {Count} instrument codes with conflicting persisted identities. Sample: {Codes}.",
                rejected.Count, string.Join(", ", rejected.Take(20)));
        return records.Count - rejected.Count;
    }

    private static string NormalizeInstrumentIsin(string isin) => isin.Trim().ToUpperInvariant();

    private async Task<int> PersistIntradayTradesAsync(
        IReadOnlyList<TsetmcIntradayTradeRecord> records,
        CancellationToken cancellationToken)
    {
        var instruments = await InstrumentMapByInsCodeAsync(records.Select(r => r.InsCode), cancellationToken);
        var now = timeProvider.GetUtcNow();

        // Intraday snapshots: keyed by (InsCode, TradingDate, TradingTime) — use a deterministic GUID.
        // All records are included — EnsureInstrumentStub creates stub rows for unseen InsCodes.
        var extIds = records
            .Select(r => BuildIntradayTradeGuid(r.InsCode, r.TradingDate, r.TradingTime))
            .Distinct().ToList();
        var existing = (await dbContext.IntradayTradeSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);

        // Quote map is pre-seeded from known instruments; stubs added during the loop are included
        // via UpsertLatestQuote which creates new rows when missing.
        var instrumentIdsForQuotes = instruments.Values.Select(v => v.Id).Distinct().ToList();
        var quotes = await LatestQuoteMapAsync(instrumentIdsForQuotes, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            // TsetmcIntradayTradeRecord has no Symbol field; use InsCode string as stub fallback.
            var instrument = EnsureInstrumentStub(instruments, source.InsCode, source.InsCode.ToString(), now);

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

        // Daily trades: keyed by (InsCode, TradingDate) — all records included, stubs created as needed.
        var extIds = records
            .Select(r => BuildDailyTradeGuid(r.InsCode, r.TradingDate))
            .Distinct().ToList();
        var existing = (await dbContext.DailyInstrumentTrades
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalTradeId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalTradeId);

        var instrumentIdsForQuotes = instruments.Values.Select(v => v.Id).Distinct().ToList();
        var quotes = await LatestQuoteMapAsync(instrumentIdsForQuotes, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            // TsetmcDailyTradeRecord has Symbol (from LVal18AFC).
            var instrument = EnsureInstrumentStub(instruments, source.InsCode, source.Symbol, now);
            // An instrument first seen through the symbol-less intraday feed carries its numeric
            // code as symbol. The daily feed is the first one that names it (Feature 167).
            if (!string.IsNullOrWhiteSpace(source.Symbol) &&
                TsetmcInstrumentStub.HasStubSymbol(instrument.Symbol, instrument.InstrumentCode))
            {
                instrument.Symbol = source.Symbol.Trim();
                instrument.SourceChangedAt = now;
            }

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

        // Stubs are created in the loop below; pre-load keys only for already-known instruments.
        var knownKeys = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => (instruments[r.InsCode].Id, r.IndexDate))
            .Distinct().ToList();
        var dailyIndices = await DailyIndexMapAsync(knownKeys, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            var instrument = EnsureInstrumentStub(instruments, source.InsCode, source.InsCode.ToString(), now);
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

        // All records included — stubs created in loop for unseen InsCodes.
        var extIds = records
            .Select(r => BuildIntradayIndexGuid(r.InsCode, r.IndexDate, r.IndexTime))
            .Distinct().ToList();
        var existing = (await dbContext.IntradayIndexSnapshots
                .Where(row => row.ProviderName == _options.ProviderName && extIds.Contains(row.ExternalSnapshotId))
                .ToListAsync(cancellationToken))
            .ToDictionary(row => row.ExternalSnapshotId);

        var knownKeys = records
            .Where(r => instruments.ContainsKey(r.InsCode))
            .Select(r => (instruments[r.InsCode].Id, r.IndexDate))
            .Distinct().ToList();
        var dailyIndices = await DailyIndexMapAsync(knownKeys, cancellationToken);

        var persisted = 0;
        foreach (var source in records)
        {
            var instrument = EnsureInstrumentStub(instruments, source.InsCode, source.InsCode.ToString(), now);

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

    /// <summary>
    /// Returns the existing instrument row for <paramref name="insCode"/>, or creates a minimal
    /// stub row so that trade/index records are never silently dropped while waiting for the
    /// next full instruments sync. The stub is filled in by the next <see cref="SynchronizeInstrumentsAsync"/> run.
    /// </summary>
    private TradingInstrumentRow EnsureInstrumentStub(
        Dictionary<long, TradingInstrumentRow> instruments,
        long insCode,
        string symbol,
        DateTimeOffset now)
    {
        if (instruments.TryGetValue(insCode, out var existing))
            return existing;

        var stub = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(),
            ProviderName = _options.ProviderName,
            ExternalInstrumentId = BuildInstrumentGuid(insCode),
            InstrumentCode = insCode,
            Symbol = symbol,
            LastSynchronizedAt = now,
            SourceChangedAt = now
        };
        dbContext.TradingInstruments.Add(stub);
        instruments[insCode] = stub;
        return stub;
    }

    private async Task<Dictionary<long, TradingInstrumentRow>> InstrumentMapByInsCodeAsync(
        IEnumerable<long> insCodes,
        CancellationToken cancellationToken)
    {
        var codes = insCodes.Distinct().ToList();
        // TradingInstruments is a provider-neutral dimension: do not filter by ProviderName.
        // Rows inserted by StockMarketDbSyncService (bridge phase) or by any prior TSETMC sync
        // are all valid lookup targets.
        var instruments = await dbContext.TradingInstruments
            .Where(row => codes.Contains(row.InstrumentCode))
            .GroupBy(row => row.InstrumentCode)
            .Select(g => g.OrderByDescending(r => r.LastSynchronizedAt).First())
            .ToDictionaryAsync(row => row.InstrumentCode, cancellationToken);

        // Intraday polling can create instrument stubs before the scheduled full
        // instruments sync runs. Resolve those stubs against the normalized company
        // catalog now so insight detection can associate fresh trades with symbols.
        var codeStrings = codes
            .Select(code => code.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var companyIdsByCode = (await dbContext.Companies.AsNoTracking()
                .Where(row => row.InstrumentCode != null && codeStrings.Contains(row.InstrumentCode))
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.InstrumentCode!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.LastSynchronizedAt).ThenBy(row => row.Id).First().Id,
                StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow();
        foreach (var code in codes)
        {
            if (!instruments.TryGetValue(code, out var instrument))
            {
                instrument = new TradingInstrumentRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = _options.ProviderName,
                    ExternalInstrumentId = BuildInstrumentGuid(code),
                    InstrumentCode = code,
                    Symbol = code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    LastSynchronizedAt = now,
                    SourceChangedAt = now
                };
                dbContext.TradingInstruments.Add(instrument);
                instruments[code] = instrument;
            }

            if (!instrument.NormalizedCompanyId.HasValue &&
                companyIdsByCode.TryGetValue(
                    code.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    out var companyId))
            {
                instrument.NormalizedCompanyId = companyId;
            }
        }

        return instruments;
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
            // Intraday always beats Daily — regardless of which trading date each carries.
            // A Daily record for a previous session must never overwrite a live Intraday quote
            // even if the Daily record's AsOf timestamp is later (e.g. end-of-day batch arrives
            // after the intraday snapshot was written).
            if (row.SourceKind == "Intraday" && sourceKind == "Daily") return;

            // For two records of the same kind, keep the newer AsOf — but allow an Intraday to
            // replace a Daily for the same trading day regardless of timestamps.
            if (row.AsOf > asOf && !(row.SourceKind == "Daily" && sourceKind == "Intraday")) return;
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

    public async Task<IReadOnlyCollection<TsetmcSyncState>> QueryAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.StockMarketSyncStates.AsNoTracking()
            .Where(r => r.Dataset.StartsWith("Tsetmc_"))
            .OrderBy(r => r.Dataset)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new TsetmcSyncState(
            Dataset: r.Dataset,
            LastRunStartedAt: r.LastRunStartedAt,
            LastRunCompletedAt: r.LastRunCompletedAt,
            LogicalVendor: r.LogicalVendor,
            PhysicalSource: r.PhysicalSource,
            SourceMode: r.SourceMode)).ToArray();
    }

    private async Task StampSyncStateStartAsync(string dataset, DateTimeOffset started, CancellationToken cancellationToken)
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
        state.LastRunStartedAt = started;
        state.LastRunCompletedAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task StampSyncStateEndAsync(
        string dataset, DateTimeOffset now, CancellationToken cancellationToken, bool advanceWatermark = true)
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
        if (advanceWatermark)
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
