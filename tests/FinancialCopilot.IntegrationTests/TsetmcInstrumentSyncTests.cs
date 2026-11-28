using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class TsetmcInstrumentSyncTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task DuplicateObservationsAndRepeatSyncPreserveInstrumentAndTradeIds()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var company = new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", InstrumentCode = "101", SymbolIsin = "IRO1TEST0001"
        };
        var instrument = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "StockMarketDb", InstrumentCode = 101, ExternalInstrumentId = Guid.NewGuid()
        };
        db.Companies.Add(company);
        db.TradingInstruments.Add(instrument);
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(), TradingInstrumentId = instrument.Id, LastTradedPrice = 11850
        });
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(db, cache, new Dictionary<byte, TsetmcInstrumentRecord[]>
        {
            [1] = [Instrument(101, " iro1test0001 ")],
            [2] = [Instrument(101, "IRO1TEST0001"), Instrument(102, "IRO1TEST0002")]
        }, 1, 2, 2);

        var first = await service.SynchronizeInstrumentsAsync(default);
        var newInstrumentId = (await db.TradingInstruments.SingleAsync(row => row.InstrumentCode == 102)).Id;
        var second = await service.SynchronizeInstrumentsAsync(default);

        Assert.Equal(3, first.RowsFetched);
        Assert.Equal(2, first.RowsPersisted);
        Assert.Equal(2, first.InstrumentCoverage!.SuccessfulFlows);
        Assert.True(first.InstrumentCoverage.IsComplete);
        Assert.Equal(2, second.RowsPersisted);
        Assert.Equal(2, await db.TradingInstruments.CountAsync());
        Assert.Equal(newInstrumentId, (await db.TradingInstruments.SingleAsync(row => row.InstrumentCode == 102)).Id);
        Assert.Equal(instrument.Id, (await db.DailyInstrumentTrades.SingleAsync()).TradingInstrumentId);
        Assert.Equal(company.Id, instrument.NormalizedCompanyId);
        Assert.Equal("IRO1TEST0001", instrument.InstrumentIsin);
        Assert.True(instrument.IsActive);
    }

    [Fact]
    public async Task ConflictingAndUnknownIdentitiesDoNotOverwriteExistingInstruments()
    {
        await using var db = CreateDb();
        AddInstrument(db, 101, "IRO1TEST0001");
        AddInstrument(db, 102, "IRO1TEST0002");
        AddInstrument(db, 103, "IRO1TEST0003");
        AddInstrument(db, 104, "IRO1TEST0004");
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(db, cache, new Dictionary<byte, TsetmcInstrumentRecord[]>
        {
            [1] = [Instrument(101, "IRO1TEST0001"), Instrument(102, "IRO1TEST0002", null),
                Instrument(103, "IRO1WRNG0003"), Instrument(104, "IRO1TEST0004", false),
                Instrument(105, "IRO1TEST0002")],
            [2] = [Instrument(101, "IRO1TEST0001", false)]
        }, 1, 2);

        var result = await service.SynchronizeInstrumentsAsync(default);

        Assert.Equal(4, result.InstrumentCoverage!.RejectedInstruments);
        Assert.False(result.InstrumentCoverage.IsComplete);
        Assert.Equal(1, result.RowsPersisted);
        Assert.Equal(new long[] { 101, 102, 103 }, await db.TradingInstruments
            .Where(row => row.IsActive).Select(row => row.InstrumentCode).OrderBy(code => code).ToArrayAsync());
        Assert.Equal("IRO1TEST0003", (await db.TradingInstruments.SingleAsync(row => row.InstrumentCode == 103)).InstrumentIsin);
        Assert.Null((await db.StockMarketSyncStates.SingleAsync()).Watermark);
    }

    [Fact]
    public async Task FailedFlowDoesNotBlockLaterFlowsOrAdvanceCompleteWatermark()
    {
        await using var db = CreateDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var responses = new Dictionary<byte, TsetmcInstrumentRecord[]>
        {
            [1] = [Instrument(101, "IRO1TEST0001")],
            [4] = [Instrument(104, "IRO1TEST0004")]
        };
        var service = CreateService(db, cache, responses, 1, 2, 4);

        var partial = await service.SynchronizeInstrumentsAsync(default);
        var state = await db.StockMarketSyncStates.SingleAsync();

        Assert.Equal(new byte[] { 2 }, partial.InstrumentCoverage!.FailedFlows);
        Assert.False(partial.InstrumentCoverage.IsComplete);
        Assert.Equal(2, partial.RowsPersisted);
        Assert.Null(state.Watermark);
        Assert.NotNull(state.LastRunCompletedAt);
        responses[2] = [Instrument(102, "IRO1TEST0002")];

        var full = await service.SynchronizeInstrumentsAsync(default);

        Assert.True(full.InstrumentCoverage!.IsComplete);
        Assert.Equal(3, await db.TradingInstruments.CountAsync());
        Assert.NotNull(state.Watermark);
    }

    [Fact]
    public async Task InstrumentSymbolIsThePersianTradingSymbol_NotTheLatinMnemonic()
    {
        await using var db = CreateDb();
        // 101: already overwritten with the Latin mnemonic by an earlier sync (BUG-055).
        // 102: Persian symbol from the daily feed; the provider row has no Persian symbol.
        // 103: numeric stub; the provider row has no Persian symbol.
        AddInstrument(db, 101, "IRO1MKBT0001");
        AddInstrument(db, 102, "IRO1FOLD0001");
        AddInstrument(db, 103, "IRO1TEST0003");
        await db.SaveChangesAsync();
        Row(db, 101).Symbol = "MKBT1";
        Row(db, 102).Symbol = "فولاد";
        Row(db, 103).Symbol = "103";
        await db.SaveChangesAsync();

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(db, cache, new Dictionary<byte, TsetmcInstrumentRecord[]>
        {
            [1] =
            [
                Instrument(101, "IRO1MKBT0001") with { Symbol = "MKBT1", SymbolFull = " اخابر " },
                Instrument(102, "IRO1FOLD0001") with { Symbol = "FOLD1", SymbolFull = "" },
                Instrument(103, "IRO1TEST0003") with { Symbol = "TEST3", SymbolFull = "" },
                Instrument(104, "IRO1IKCO0001") with { Symbol = "IKCO1", SymbolFull = "خودرو" }
            ]
        }, 1);

        await service.SynchronizeInstrumentsAsync(default);
        await service.SynchronizeInstrumentsAsync(default);

        Assert.Equal("اخابر", Row(db, 101).Symbol);   // repaired by the next sync
        Assert.Equal("فولاد", Row(db, 102).Symbol);   // a real symbol is never replaced by the mnemonic
        Assert.Equal("TEST3", Row(db, 103).Symbol);   // mnemonic only when nothing better exists
        Assert.Equal("خودرو", Row(db, 104).Symbol);   // new instrument
    }

    private static TradingInstrumentRow Row(FinancialIngestionDbContext db, long code) =>
        db.TradingInstruments.Local.Single(row => row.InstrumentCode == code);

    private static TsetmcInstrumentRecord Instrument(long code, string isin, bool? active = true) =>
        new(code, isin, "TEST", "Test instrument", "Test company", "NO", "A", "N1",
            active, new DateOnly(2024, 1, 1), 1000);

    private static void AddInstrument(FinancialIngestionDbContext db, long code, string isin) =>
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = code, InstrumentIsin = isin, IsActive = true
        });

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TsetmcDirectFeedSyncService CreateService(
        FinancialIngestionDbContext db, IMemoryCache cache, Dictionary<byte, TsetmcInstrumentRecord[]> responses, params byte[] flows) =>
        new(db, new InstrumentClient(responses), new NoOpPayloadStore(),
            Options.Create(new TsetmcWebServiceOptions { InstrumentFlows = flows }),
            new NoOpScannerCache(), new MemoryMarketViewCache(cache, Options.Create(new MarketViewOptions())),
            TimeProvider.System, NullLogger<TsetmcDirectFeedSyncService>.Instance);

    private sealed class InstrumentClient(Dictionary<byte, TsetmcInstrumentRecord[]> responses) : ITsetmcWebServiceClient
    {
        public Task<IReadOnlyList<TsetmcInstrumentRecord>> GetInstrumentsAsync(byte flow, CancellationToken cancellationToken) =>
            responses.TryGetValue(flow, out var rows)
                ? Task.FromResult<IReadOnlyList<TsetmcInstrumentRecord>>(rows)
                : Task.FromException<IReadOnlyList<TsetmcInstrumentRecord>>(
                    new FinancialProviderException(FinancialProviderErrorCode.RemoteUnavailable, "Unavailable flow"));

        public Task<IReadOnlyList<TsetmcIntradayTradeRecord>> GetIntradayTradesAsync(byte flow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TsetmcDailyTradeRecord>> GetDailyTradesAsync(
            DateOnly date, byte flow, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<TsetmcDailyIndexRecord>> GetDailyIndicesAsync(DateOnly date, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TsetmcIntradayIndexRecord>> GetIntradayIndicesAsync(byte flow, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpPayloadStore : IProviderRawPayloadStore
    {
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProviderRawPayload?> FindByChecksumAsync(string providerName, string checksum, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderRawPayload?>(null);
    }
}
