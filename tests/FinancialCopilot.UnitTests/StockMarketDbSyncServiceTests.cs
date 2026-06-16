using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class StockMarketDbSyncServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T12:40:00Z");
    private static readonly Guid InstrumentRef = Guid.Parse("935dc0fc-405d-4efd-ba38-af34f67d77c0");

    [Fact]
    public async Task Instruments_DoesNotWriteToTradingInstrumentsTable()
    {
        // spec 064: TradingInstruments is owned exclusively by TsetmcDirectFeedSyncService.
        // StockMarketDbSyncService must not insert or update instrument rows.
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "CodalDb", ExternalCompanyId = "1",
            Name = "Company", InstrumentCode = "123", LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_SyncCompletesWithoutErrorWhenInstrumentCodeMatchesMultipleCompanies()
    {
        // spec 064: StockMarketDb instruments sync is now a no-op for TradingInstruments.
        // It should still complete successfully even when duplicate company rows exist.
        await using var db = CreateDb();
        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranArchiveSql", ExternalCompanyId = "A",
                Name = "Archive", InstrumentCode = "123", LastSynchronizedAt = Now.AddYears(-1)
            },
            new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "C",
                Name = "Current", InstrumentCode = "123", LastSynchronizedAt = Now
            });
        await db.SaveChangesAsync();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        // Must not throw; TradingInstruments remains empty.
        await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_PersistCanonicalRawPayloadChecksum()
    {
        await using var db = CreateDb();
        var store = new FakeRawPayloadStore();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };
        var service = new StockMarketDbSyncService(
            db, executor, store,
            Options.Create(new StockMarketDbProviderOptions { PageSize = 100 }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now));

        await service.SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        var payload = Assert.Single(store.Payloads);
        Assert.Equal(ProviderDataset.TradingInstruments, payload.Dataset);
        Assert.Equal(64, payload.Checksum.Length);
    }

    [Fact]
    public async Task Instruments_SyncRecordsRowsReadForWatermarkEvenWithoutInstrumentWrite()
    {
        // spec 064: StockMarketDb instruments sync still fetches and counts records for watermark
        // continuation, even though it no longer writes to TradingInstruments.
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        var result = await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Equal(1, result.RowsRead);
        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    [Fact]
    public async Task Instruments_FullPageContinuesFromTimestampAndSourceId()
    {
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };
        var service = new StockMarketDbSyncService(
            db, executor, new FakeRawPayloadStore(),
            Options.Create(new StockMarketDbProviderOptions { PageSize = 1, OverlapMinutes = 10 }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now));

        await service.SynchronizeAsync(StockMarketDataset.Instruments, false, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.Instruments, false, CancellationToken.None);

        Assert.Equal(new StockMarketPageCursor(Now, LastGuidId: InstrumentRef), executor.InstrumentCursors[1]);
    }

    [Fact]
    public async Task IntradayTrade_UpdatesLatestQuoteAndPersistedProviderReadsIt()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(110, quote.LatestPrice);
        Assert.Equal(10, quote.PriceChangePercentage);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
    }

    [Fact]
    public async Task PersistedQuote_ReachableByInstrumentTickerWhenCompanyLinkageIsMissing()
    {
        await using var db = CreateDb();
        // Instrument without a normalized company link (the common case for funds, rights, and
        // listings whose company row comes from another provider): the quote must still resolve
        // through the instrument's own TSE ticker.
        await SeedInstrumentAsync(db, companyId: null);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(110, quote.LatestPrice);
        Assert.Equal(MarketQuoteSource.LiveQuote, quote.Source);
        Assert.Empty(result.UnavailableSymbols);
    }

    [Fact]
    public async Task PersistedQuote_IntradayFromPreviousSessionIsNotReportedLive()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db, companyId: null);
        var instrumentId = await db.TradingInstruments.Select(row => row.Id).SingleAsync();
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", TradingInstrumentId = instrumentId,
            LatestPrice = 95, PriceChangePercentage = -1.5m, SourceKind = "Intraday",
            TradingDate = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1), AsOf = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await Provider(db).GetLatestQuotesAsync([new SymbolCode("SYM")], CancellationToken.None);

        var quote = Assert.Single(result.Observations);
        Assert.Equal(MarketQuoteSource.PreviousTradingDay, quote.Source);
    }

    [Fact]
    public async Task IntradayTrade_MultipleSnapshotsInPageUpdateSingleLatestQuote()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now.AddMinutes(-1)),
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 6, 120, 1200,
                    120, 20, 120, 20, 100, 120, 100, 100, new TimeOnly(12, 31), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);

        Assert.Equal(2, await db.IntradayTradeSnapshots.CountAsync());
        Assert.Equal(120, (await db.LatestMarketQuotes.SingleAsync()).LatestPrice);
    }

    [Fact]
    public async Task IntradayTrade_DuplicateSourceIdInSamePageUpsertsSingleRow()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var snapshotId = Guid.NewGuid();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now.AddMinutes(-1)),
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 6, 120, 1200,
                    125, 25, 125, 25, 100, 125, 100, 100, new TimeOnly(12, 31), Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);

        var row = Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Equal(125, row.LastTradedPrice);
    }

    [Fact]
    public async Task OlderDailyTrade_DoesNotOverwriteNewerIntradayQuote()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ],
            DailyTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 5, 31), 90, 90, 5, 100, 1000,
                    -10, 90, 100, 100, 100, 5000, Now.AddDays(-1))
            ]
        };
        var service = Service(db, executor);

        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, true, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.DailyTrades, true, CancellationToken.None);

        Assert.Equal(110, (await db.LatestMarketQuotes.SingleAsync()).LatestPrice);
    }

    [Fact]
    public async Task IntradayIndex_LastObservationBecomesDailyClose()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayIndices =
            [
                new(Guid.NewGuid(), InstrumentRef, 123, new DateOnly(2026, 6, 1), new TimeOnly(12, 30), 2500, 1.25m, Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.IntradayIndices, true, CancellationToken.None);

        var daily = await db.DailyIndexSnapshots.SingleAsync();
        Assert.Equal(2500, daily.Value);
        Assert.Equal("IntradayClose", daily.SourceKind);
    }

    [Fact]
    public async Task HistoricalDailyIndex_PersistsNamedIndexCloseForTradingDay()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            HistoricalIndices =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1),
                    Value: 2_100_000, High: 2_150_000, Low: 2_050_000, ChangePercent: 0.75m, ChangeTime: Now)
            ]
        };

        await Service(db, executor).SynchronizeAsync(
            StockMarketDataset.HistoricalDailyIndices, true, CancellationToken.None);

        var daily = await db.DailyIndexSnapshots.SingleAsync();
        Assert.Equal(2_100_000, daily.Value);
        Assert.Equal(new DateOnly(2026, 6, 1), daily.TradingDate);
        Assert.Equal("HistoricalBackfill", daily.SourceKind);
    }

    [Fact]
    public void NamedIndexCatalog_ExposesTheSixDailyIndexInstrumentRefs()
    {
        var expected = new[]
        {
            Guid.Parse("36423CB8-D33B-47AD-89D4-06FA49592CBA"), // شاخص کل
            Guid.Parse("1B32B991-F48A-4F7E-9C0C-328D0B093EA5"), // شاخص کل فرابورس
            Guid.Parse("B27FA320-194F-4710-8D12-277E245D33C5"), // شاخص بازده نقدی و قیمت
            Guid.Parse("47CE7543-C052-4C44-BF0D-29281818FCA5"), // شاخص ۵۰ شرکت فعال‌تر
            Guid.Parse("42FCE63E-6CEB-405B-9179-78606C210D86"), // شاخص قیمت (هم‌وزن)
            Guid.Parse("D01F9D84-A1C8-46F3-A959-800DEF9E112F"), // شاخص کل (هم‌وزن)
        };

        Assert.Equal(expected, StockMarketNamedIndices.InstrumentRefs);
        Assert.Equal(expected.Length, StockMarketNamedIndices.InstrumentRefs.Distinct().Count());
        Assert.All(StockMarketNamedIndices.All, index => Assert.False(string.IsNullOrWhiteSpace(index.PersianName)));
    }

    [Fact]
    public async Task IntradayTrade_RepeatedOverlapReadIsIdempotentAndInvalidatesCache()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var snapshotId = Guid.NewGuid();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(snapshotId, InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };
        var cache = new TrackingScannerCache();
        var service = Service(db, executor, cache);

        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);
        await service.SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);

        Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Single(await db.LatestMarketQuotes.ToListAsync());
        Assert.Equal([null, Now.AddMinutes(-10)], executor.IntradayTradeCursors.Select(item => item.After));
        Assert.Equal(2, cache.Invalidations.Count);
        Assert.All(cache.Invalidations, item => Assert.Equal("StockMarketDb.IntradayTrades", item.Reason));
    }

    [Fact]
    public async Task IntradayTrade_InvalidatesMarketViewCache()
    {
        await using var db = CreateDb();
        await SeedLinkedInstrumentAsync(db);
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };
        var marketCache = new TrackingMarketViewCache();

        await Service(db, executor, marketViewCache: marketCache)
            .SynchronizeAsync(StockMarketDataset.IntradayTrades, false, CancellationToken.None);

        Assert.Equal(1, marketCache.InvalidationCount);
    }

    [Fact]
    public async Task IntradayTrade_UnresolvedInstrumentDoesNotAdvanceWatermark()
    {
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 1), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(12, 30), Now)
            ]
        };

        var exception = await Assert.ThrowsAsync<StockMarketUnresolvedInstrumentException>(
            () => Service(db, executor).SynchronizeAsync(
                StockMarketDataset.IntradayTrades, false, CancellationToken.None));

        Assert.Equal(StockMarketDataset.IntradayTrades, exception.Dataset);
        Assert.Equal(1, exception.UnresolvedCount);
        Assert.Null((await db.StockMarketSyncStates.SingleAsync()).Watermark);
    }

    [Fact]
    public async Task Retention_DeletesOnlyExpiredIntradayHistory()
    {
        await using var db = CreateDb();
        await SeedInstrumentAsync(db);
        var instrumentId = await db.TradingInstruments.Select(row => row.Id).SingleAsync();
        db.IntradayTradeSnapshots.AddRange(
            IntradayTrade(instrumentId, Now.AddDays(-31), new DateOnly(2026, 5, 1)),
            IntradayTrade(instrumentId, Now.AddDays(-1), new DateOnly(2026, 5, 31)));
        db.IntradayIndexSnapshots.AddRange(
            IntradayIndex(instrumentId, Now.AddDays(-31), new DateOnly(2026, 5, 1)),
            IntradayIndex(instrumentId, Now.AddDays(-1), new DateOnly(2026, 5, 31)));
        await db.SaveChangesAsync();
        var service = new StockMarketHistoryRetentionService(
            db,
            Options.Create(new StockMarketDbProviderOptions
            {
                RetainIntradayTradeDays = 30,
                RetainIntradayIndexDays = 30
            }),
            new FixedTimeProvider(Now));

        var result = await service.DeleteExpiredAsync(CancellationToken.None);

        Assert.Equal(1, result.IntradayTradesDeleted);
        Assert.Equal(1, result.IntradayIndicesDeleted);
        Assert.Single(await db.IntradayTradeSnapshots.ToListAsync());
        Assert.Single(await db.IntradayIndexSnapshots.ToListAsync());
    }

    private static StockMarketDbSyncService Service(
        FinancialIngestionDbContext db,
        FakeExecutor executor,
        IScannerCache? cache = null,
        IMarketViewCache? marketViewCache = null) =>
        new(db, executor, new FakeRawPayloadStore(), Options.Create(new StockMarketDbProviderOptions { PageSize = 100, OverlapMinutes = 10 }),
            cache ?? new NoOpScannerCache(), marketViewCache ?? new NoOpMarketViewCache(), new FixedTimeProvider(Now));

    private static PersistedMarketDataProvider Provider(FinancialIngestionDbContext db) =>
        new(db, new FixedMarketQuoteSourcePriority(ProviderSources.StockMarketDbName), new FixedTimeProvider(Now));

    private sealed class FixedMarketQuoteSourcePriority(string sourceName) : IMarketQuoteSourcePriority
    {
        public string PrimarySourceName => sourceName;
    }

    private static async Task SeedLinkedInstrumentAsync(FinancialIngestionDbContext db)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = "CodalDb", ExternalCompanyId = "1", Name = "Company",
            InstrumentCode = "123", LastSynchronizedAt = Now
        });
        await SeedInstrumentAsync(db, companyId);
    }

    private static async Task SeedInstrumentAsync(FinancialIngestionDbContext db, Guid? companyId = null)
    {
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalInstrumentId = InstrumentRef,
            InstrumentCode = 123, InstrumentIsin = "IRO1TEST0001", Symbol = "SYM", Name = "Test",
            MarketCode = "NO", InstrumentKind = "A", NormalizedCompanyId = companyId,
            IsActive = true, SourceChangedAt = Now, LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IntradayTradeSnapshotRow IntradayTrade(
        Guid instrumentId,
        DateTimeOffset receivedAt,
        DateOnly tradingDate) =>
        new()
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId, TradingDate = tradingDate, ReceivedAt = receivedAt
        };

    private static IntradayIndexSnapshotRow IntradayIndex(
        Guid instrumentId,
        DateTimeOffset changedAt,
        DateOnly tradingDate) =>
        new()
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrumentId, TradingDate = tradingDate, SourceChangedAt = changedAt
        };

    private sealed class FakeExecutor : IStockMarketDbQueryExecutor
    {
        public IReadOnlyList<StockMarketInstrumentRecord> Instruments { get; init; } = [];
        public IReadOnlyList<StockMarketIntradayTradeRecord> IntradayTrades { get; init; } = [];
        public IReadOnlyList<StockMarketDailyTradeRecord> DailyTrades { get; init; } = [];
        public IReadOnlyList<StockMarketIntradayIndexRecord> IntradayIndices { get; init; } = [];
        public IReadOnlyList<StockMarketHistoricalDailyIndexRecord> HistoricalIndices { get; init; } = [];
        public List<StockMarketPageCursor> InstrumentCursors { get; } = [];
        public List<StockMarketPageCursor> IntradayTradeCursors { get; } = [];
        public Task<IReadOnlyList<StockMarketInstrumentRecord>> QueryInstrumentsAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken)
        {
            InstrumentCursors.Add(cursor);
            return Task.FromResult(Instruments);
        }
        public Task<IReadOnlyList<StockMarketIntradayTradeRecord>> QueryIntradayTradesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken)
        {
            IntradayTradeCursors.Add(cursor);
            return Task.FromResult(IntradayTrades);
        }
        public Task<IReadOnlyList<StockMarketDailyTradeRecord>> QueryDailyTradesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(DailyTrades);
        public Task<IReadOnlyList<StockMarketIntradayIndexRecord>> QueryIntradayIndicesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(IntradayIndices);
        public Task<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>> QueryHistoricalDailyIndicesAsync(StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) => Task.FromResult(HistoricalIndices);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        // Pin the local zone so "current trading day" derivation is deterministic on any machine.
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeRawPayloadStore : IProviderRawPayloadStore
    {
        public List<ProviderRawPayload> Payloads { get; } = [];
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
        {
            Payloads.Add(payload);
            return Task.CompletedTask;
        }
        public Task<ProviderRawPayload?> FindByChecksumAsync(string providerName, string checksum, CancellationToken cancellationToken) =>
            Task.FromResult(Payloads.FirstOrDefault(payload => payload.ProviderName == providerName && payload.Checksum == checksum));
    }

    private sealed class TrackingScannerCache : IScannerCache
    {
        public List<ScannerCacheInvalidation> Invalidations { get; } = [];
        public Task<string> GetDataVersionAsync(CancellationToken cancellationToken) => Task.FromResult("test");
        public Task<ScannerParseResult?> GetPlanAsync(ScannerCacheScope scope, string dataVersion, ScannerParseRequest request, CancellationToken cancellationToken) => Task.FromResult<ScannerParseResult?>(null);
        public Task SetPlanAsync(ScannerCacheScope scope, string dataVersion, ScannerParseRequest request, ScannerParseResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ScannerTableResult?> GetResultAsync(ScannerCacheScope scope, string dataVersion, ScannerExecutionRequest request, CancellationToken cancellationToken) => Task.FromResult<ScannerTableResult?>(null);
        public Task SetResultAsync(ScannerCacheScope scope, string dataVersion, ScannerExecutionRequest request, ScannerTableResult result, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(ScannerCacheInvalidation invalidation, CancellationToken cancellationToken)
        {
            Invalidations.Add(invalidation);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpMarketViewCache : IMarketViewCache
    {
        public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TrackingMarketViewCache : IMarketViewCache
    {
        public int InvalidationCount { get; private set; }

        public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken)
        {
            InvalidationCount++;
            return Task.CompletedTask;
        }
    }
}
