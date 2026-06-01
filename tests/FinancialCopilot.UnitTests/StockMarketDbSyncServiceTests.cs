using FinancialCopilot.Application.FinancialData.Ingestion;
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
    public async Task Instruments_LinkExistingCompanyByInstrumentCode()
    {
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

        var instrument = await db.TradingInstruments.SingleAsync();
        Assert.NotNull(instrument.NormalizedCompanyId);
        Assert.True(instrument.IsActive);
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
            new NoOpScannerCache(), new FixedTimeProvider(Now));

        await service.SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        var payload = Assert.Single(store.Payloads);
        Assert.Equal(ProviderDataset.TradingInstruments, payload.Dataset);
        Assert.Equal(64, payload.Checksum.Length);
    }

    [Fact]
    public async Task Instruments_LeaveCompanyLinkNullWhenInstrumentHasNoCompany()
    {
        await using var db = CreateDb();
        var executor = new FakeExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        await Service(db, executor).SynchronizeAsync(StockMarketDataset.Instruments, true, CancellationToken.None);

        Assert.Null((await db.TradingInstruments.SingleAsync()).NormalizedCompanyId);
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
            new NoOpScannerCache(), new FixedTimeProvider(Now));

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
                new(1, InstrumentRef, 123, new DateOnly(2026, 5, 31), 90, 90, 5, 100, 1000,
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

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(db, executor).SynchronizeAsync(
                StockMarketDataset.IntradayTrades, false, CancellationToken.None));

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
        IScannerCache? cache = null) =>
        new(db, executor, new FakeRawPayloadStore(), Options.Create(new StockMarketDbProviderOptions { PageSize = 100, OverlapMinutes = 10 }),
            cache ?? new NoOpScannerCache(), new FixedTimeProvider(Now));

    private static PersistedMarketDataProvider Provider(FinancialIngestionDbContext db) =>
        new(db, Options.Create(new StockMarketDbProviderOptions()));

    private static async Task SeedLinkedInstrumentAsync(FinancialIngestionDbContext db)
    {
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = "CodalDb", ExternalCompanyId = "1", Name = "Company",
            InstrumentCode = "123", LastSynchronizedAt = Now
        });
        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = Guid.NewGuid(), CompanyId = companyId, ProviderName = "CodalDb",
            ExternalSymbolId = "1", SymbolCode = "SYM", LastSynchronizedAt = Now
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
}
