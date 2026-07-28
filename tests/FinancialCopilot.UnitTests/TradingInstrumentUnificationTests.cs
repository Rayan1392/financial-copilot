using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Verifies spec 064 — TradingInstruments is provider-neutral and TSETMC is the sole writer.
/// </summary>
public sealed class TradingInstrumentUnificationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-14T10:00:00Z");
    private static readonly long InsCode = 9987529074833218L;
    private static readonly Guid InstrumentRef = Guid.Parse("935dc0fc-405d-4efd-ba38-af34f67d77c0");

    // --- Phase 1.2: stub auto-creation ---

    [Fact]
    public async Task IntradayTrades_AutoCreatesInstrumentStubWhenNoInstrumentSyncHasRun()
    {
        await using var db = CreateDb();
        // TradingInstruments table is empty — instruments sync has not yet run today.
        var client = new FakeTsetmcClient
        {
            IntradayTrades = [new TsetmcIntradayTradeRecord(
                InsCode,
                DateOnly.FromDateTime(Now.UtcDateTime),
                new TimeOnly(10, 0),
                5, 10_000, 5_000_000,
                2500, 2490, 10, 2480, 2520, 2460, 2400)]
        };

        await TsetmcService(db, client).SynchronizeIntradayTradesAsync(CancellationToken.None);

        // A stub instrument row must exist even though SynchronizeInstrumentsAsync was never called.
        var stub = await db.TradingInstruments.SingleAsync();
        Assert.Equal(InsCode, stub.InstrumentCode);

        // The trade snapshot must reference the stub.
        var snapshot = await db.IntradayTradeSnapshots.SingleAsync();
        Assert.Equal(stub.Id, snapshot.TradingInstrumentId);
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime), snapshot.TradingDate);
    }

    [Fact]
    public async Task IntradayTrades_StubLinksToNormalizedCompanyBeforeInstrumentSync()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "CodalDb",
            ExternalCompanyId = "company-1",
            Name = "Test Company",
            InstrumentCode = InsCode.ToString(),
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        var client = new FakeTsetmcClient
        {
            IntradayTrades = [new TsetmcIntradayTradeRecord(
                InsCode,
                DateOnly.FromDateTime(Now.UtcDateTime),
                new TimeOnly(10, 0),
                5, 10_000, 5_000_000,
                2500, 2490, 10, 2480, 2520, 2460, 2400)]
        };

        await TsetmcService(db, client).SynchronizeIntradayTradesAsync(CancellationToken.None);

        var stub = await db.TradingInstruments.SingleAsync();
        Assert.Equal(companyId, stub.NormalizedCompanyId);
    }

    [Fact]
    public async Task IntradayTrades_StubIsReusedAcrossMultipleCallsForSameInsCode()
    {
        await using var db = CreateDb();
        var trade = new TsetmcIntradayTradeRecord(
            InsCode,
            DateOnly.FromDateTime(Now.UtcDateTime),
            new TimeOnly(10, 0),
            5, 10_000, 5_000_000,
            2500, 2490, 10, 2480, 2520, 2460, 2400);
        var trade2 = new TsetmcIntradayTradeRecord(
            InsCode,
            DateOnly.FromDateTime(Now.UtcDateTime),
            new TimeOnly(10, 30),
            6, 12_000, 6_000_000,
            2510, 2495, 15, 2485, 2525, 2465, 2400);
        var client = new FakeTsetmcClient { IntradayTrades = [trade, trade2] };

        await TsetmcService(db, client).SynchronizeIntradayTradesAsync(CancellationToken.None);

        // Two snapshots but only one instrument stub.
        Assert.Equal(1, await db.TradingInstruments.CountAsync());
        Assert.Equal(2, await db.IntradayTradeSnapshots.CountAsync());
    }

    // --- Phase 1.3: StockMarketDb reads from shared provider-neutral table ---

    [Fact]
    public async Task StockMarketDbIntradayTrades_ResolvesInstrumentInsertedByTsetmc()
    {
        await using var db = CreateDb();
        // Seed an instrument that was inserted by the TSETMC provider (ProviderName = "TsetmcWebService").
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = InstrumentRef,
            InstrumentCode = 123,
            Symbol = "SYM",
            Name = "Test",
            IsActive = true,
            SourceChangedAt = Now,
            LastSynchronizedAt = Now
        });
        await db.SaveChangesAsync();

        var executor = new FakeStockMarketExecutor
        {
            IntradayTrades =
            [
                new(Guid.NewGuid(), InstrumentRef, new DateOnly(2026, 6, 14), 5, 100, 1000,
                    110, 10, 110, 10, 100, 110, 100, 100, new TimeOnly(10, 0), Now)
            ]
        };

        // StockMarketDb sync must find the TSETMC-owned instrument row without a ProviderName filter.
        await StockMarketDbService(db, executor).SynchronizeAsync(
            StockMarketDataset.IntradayTrades, true, CancellationToken.None);

        var snapshot = await db.IntradayTradeSnapshots.SingleAsync();
        Assert.Equal(110, snapshot.LastTradedPrice);
    }

    [Fact]
    public async Task StockMarketDbInstruments_DoesNotWriteToTradingInstrumentsTable()
    {
        await using var db = CreateDb();
        var executor = new FakeStockMarketExecutor
        {
            Instruments =
            [
                new(InstrumentRef, 123, "IRO1TEST0001", "TEST", "Test", "NO", "A", Now, true, false)
            ]
        };

        await StockMarketDbService(db, executor).SynchronizeAsync(
            StockMarketDataset.Instruments, true, CancellationToken.None);

        // TradingInstruments must remain empty — TSETMC owns this table exclusively.
        Assert.Equal(0, await db.TradingInstruments.CountAsync());
    }

    // --- factory helpers ---

    private static TsetmcDirectFeedSyncService TsetmcService(
        FinancialIngestionDbContext db,
        FakeTsetmcClient client) =>
        new(db, client, new FakeRawPayloadStore(),
            Options.Create(new TsetmcWebServiceOptions
            {
                ProviderName = "TsetmcWebService",
                Enabled = true,
                UserName = "u",
                Password = "p",
                IntradayTradeFlows = [0]
            }),
            new NoOpScannerCache(), new NoOpMarketViewCache(),
            new FixedTimeProvider(Now),
            NullLogger<TsetmcDirectFeedSyncService>.Instance);

    private static StockMarketDbSyncService StockMarketDbService(
        FinancialIngestionDbContext db,
        FakeStockMarketExecutor executor) =>
        new(db, executor, new FakeRawPayloadStore(),
            Options.Create(new StockMarketDbProviderOptions { PageSize = 100, OverlapMinutes = 10 }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now));

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeTsetmcClient : ITsetmcWebServiceClient
    {
        public IReadOnlyList<TsetmcIntradayTradeRecord> IntradayTrades { get; init; } = [];

        public Task<IReadOnlyList<TsetmcInstrumentRecord>> GetInstrumentsAsync(
            byte flow, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TsetmcInstrumentRecord>>([]);

        public Task<IReadOnlyList<TsetmcIntradayTradeRecord>> GetIntradayTradesAsync(
            byte flow, CancellationToken ct) =>
            Task.FromResult(IntradayTrades);

        public Task<IReadOnlyList<TsetmcDailyTradeRecord>> GetDailyTradesAsync(
            DateOnly date, byte flow, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TsetmcDailyTradeRecord>>([]);

        public Task<IReadOnlyList<TsetmcDailyIndexRecord>> GetDailyIndicesAsync(
            DateOnly date, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TsetmcDailyIndexRecord>>([]);

        public Task<IReadOnlyList<TsetmcIntradayIndexRecord>> GetIntradayIndicesAsync(
            byte flow, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TsetmcIntradayIndexRecord>>([]);
    }

    private sealed class FakeStockMarketExecutor : IStockMarketDbQueryExecutor
    {
        public IReadOnlyList<StockMarketInstrumentRecord> Instruments { get; init; } = [];
        public IReadOnlyList<StockMarketIntradayTradeRecord> IntradayTrades { get; init; } = [];

        public Task<IReadOnlyList<StockMarketInstrumentRecord>> QueryInstrumentsAsync(
            StockMarketPageCursor cursor, int take, CancellationToken ct) =>
            Task.FromResult(Instruments);

        public Task<IReadOnlyList<StockMarketIntradayTradeRecord>> QueryIntradayTradesAsync(
            StockMarketPageCursor cursor, int take, CancellationToken ct) =>
            Task.FromResult(IntradayTrades);

        public Task<IReadOnlyList<StockMarketDailyTradeRecord>> QueryDailyTradesAsync(
            StockMarketPageCursor cursor, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StockMarketDailyTradeRecord>>([]);

        public Task<IReadOnlyList<StockMarketIntradayIndexRecord>> QueryIntradayIndicesAsync(
            StockMarketPageCursor cursor, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StockMarketIntradayIndexRecord>>([]);

        public Task<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>> QueryHistoricalDailyIndicesAsync(
            StockMarketPageCursor cursor, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>>([]);
    }

    private sealed class FakeRawPayloadStore : IProviderRawPayloadStore
    {
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken ct) => Task.CompletedTask;

        public Task<ProviderRawPayload?> FindByChecksumAsync(
            string providerName, string checksum, CancellationToken ct) =>
            Task.FromResult<ProviderRawPayload?>(null);
    }

    private sealed class NoOpMarketViewCache : IMarketViewCache
    {
        public Task<MarketSummary?> GetSummaryAsync(CancellationToken ct) =>
            Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken ct) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
