using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Options;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 167 against a migrated PostgreSQL database: the migration applies, the syncs and the
/// chain query translate to SQL, constraints hold, and option contracts stay out of market pulse.
/// </summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class OptionMarket167PostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    // 2026-11-28 10:00 IRST, a Saturday (trading day, inside the session).
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-28T06:30:00Z");
    private static readonly DateOnly Today = new(2026, 11, 28);
    private static readonly DateOnly Expiry = new(2026, 12, 9);
    private const string Provider = "TsetmcWebService";

    [SkippableFact]
    public async Task SyncChainAndRankingWorkOnPostgreSql()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var underlying = AddInstrument(db, 1001, "اخابر", "IRO1MKBT0001");
        var call = AddInstrument(db, 2001, "ضمخا9025", "IRO9MKBT0A01");
        var put = AddInstrument(db, 2002, "طمخا9025", "IROFMKBT0A01");
        var deepCall = AddInstrument(db, 2003, "ضمخا9019", "IRO9MKBT0A11");
        AddQuote(db, underlying, 1489, 0.5m);
        AddQuote(db, call, 176, 28.99m);
        await db.SaveChangesAsync();
        var feed = new Feed
        {
            Contracts =
            [
                new(2001, "IRO9MKBT0A01", 109, 100, 1000, 1400, 1001, new DateOnly(2026, 9, 19), Expiry),
                new(2002, "IROFMKBT0A01", 8, 8, 1000, 1400, 1001, new DateOnly(2026, 9, 19), Expiry),
                new(2003, "IRO9MKBT0A11", 696, 690, 1000, 800, 1001, new DateOnly(2026, 9, 19), Expiry)
            ],
            BestLimits =
            [
                new(2001, 1, 20, 2, 170, 178, 1, 10, new TimeOnly(9, 58)),
                new(2001, 2, 5, 1, 165, 180, 1, 4, new TimeOnly(9, 57)),
                new(2002, 1, 8, 1, 1, 0, 0, 0, new TimeOnly(9, 1)),
                new(2003, 1, 3, 1, 723, 730, 1, 5, new TimeOnly(9, 50)),
                new(1001, 1, 100, 3, 1488, 1489, 2, 100, new TimeOnly(9, 59))
            ]
        };

        var contracts = await Sync(db, feed).SynchronizeContractsAsync(default);
        var book = await Sync(db, feed).SynchronizeOrderBookAsync(default);
        var repeat = await Sync(db, feed).SynchronizeOrderBookAsync(default);
        feed.BestLimits = feed.BestLimits.Where(row => row.InsCode != 2002).ToArray();
        var afterRemoval = await Sync(db, feed).SynchronizeOrderBookAsync(default);

        Assert.Equal((3, 0, 0), (contracts.RowsPersisted, contracts.RowsRejected, contracts.RowsUnresolved));
        Assert.Equal(4, book.RowsPersisted);
        Assert.Equal((0, 4), (repeat.RowsPersisted, repeat.RowsUnchanged));
        Assert.Equal(1, afterRemoval.RowsRemoved);

        db.ChangeTracker.Clear();
        var reader = new EfCoreOptionChainReader(db, new FixedTimeProvider(Now));
        var chain = await reader.GetChainAsync("اخابر", null, default);
        var (ranking, failure) = await new CallExerciseRankingUseCase(reader).ExecuteAsync("اخابر", Expiry, 1, default);

        Assert.NotNull(chain);
        Assert.Equal([800m, 1400m], chain.Strikes.Select(strike => strike.StrikePrice));
        Assert.Equal(1489m, chain.Underlying!.LastPrice);
        Assert.Equal("طمخا9025", chain.Strikes[1].Put!.Symbol);
        Assert.Null(chain.Strikes[1].Put!.BidPrice); // its level was removed with the last sync
        Assert.Equal(CallExerciseRankingFailure.None, failure);
        Assert.Equal([("ضمخا9019", 1530m), ("ضمخا9025", 1578m)],
            ranking!.Ranked.Select(row => (row.Option.Symbol, row.EffectivePurchaseCost)));

        // Database constraints back the domain rules.
        db.OptionContracts.Add(new OptionContractRow
        {
            Id = Guid.NewGuid(), TradingInstrumentId = underlying, UnderlyingInstrumentId = underlying,
            Side = "Call", StrikePrice = 0, ContractSize = 1000, ExpiryDate = Expiry, BeginDate = Today
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        _ = deepCall;
    }

    [SkippableFact]
    public async Task MarketPulseCountsTheSameInstrumentsOnceOptionContractsAreActive()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var underlying = AddInstrument(db, 1001, "اخابر", "IRO1MKBT0001");
        var call = AddInstrument(db, 2001, "ضمخا9025", "IRO9MKBT0A01");
        AddQuote(db, underlying, 1489, 0.5m);
        AddQuote(db, call, 176, 28.99m);
        await db.SaveChangesAsync();

        var before = await Pulse(db).CaptureAsync(null, default);

        var feed = new Feed
        {
            Contracts = [new(2001, "IRO9MKBT0A01", 109, 100, 1000, 1400, 1001, new DateOnly(2026, 9, 19), Expiry)]
        };
        await Sync(db, feed).SynchronizeContractsAsync(default);
        db.ChangeTracker.Clear();
        var after = await Pulse(db, Now.AddMinutes(10)).CaptureAsync(null, default);

        // Before the contract is known, the active call is just another instrument.
        Assert.Equal(2, before.Breadth.IncludedInstruments + before.Breadth.ExcludedInstruments);
        // Once it is a known option contract it no longer counts towards market breadth.
        Assert.Equal(1, after.Breadth.IncludedInstruments + after.Breadth.ExcludedInstruments);
        Assert.Equal(1, after.Breadth.Advancing);
    }

    private static Guid AddInstrument(FinancialIngestionDbContext db, long code, string symbol, string isin)
    {
        var row = new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = Provider, ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = code, Symbol = symbol, InstrumentIsin = isin, IsActive = true, LastSynchronizedAt = Now
        };
        db.TradingInstruments.Add(row);
        return row.Id;
    }

    private static void AddQuote(FinancialIngestionDbContext db, Guid instrumentId, decimal price, decimal changePercent) =>
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(), ProviderName = Provider, TradingInstrumentId = instrumentId, LatestPrice = price,
            PriceChangePercentage = changePercent, SourceKind = "Intraday", TradingDate = Today, AsOf = Now
        });

    private static TsetmcOptionFeedSyncService Sync(FinancialIngestionDbContext db, Feed feed) =>
        new(db, feed, Options.Create(new TsetmcWebServiceOptions { Enabled = true, UserName = "u", Password = "p" }),
            new FixedTimeProvider(Now), NullLogger<TsetmcOptionFeedSyncService>.Instance);

    // CaptureAsync does not touch the account or plan services; they guard the read endpoints only.
    private static MarketPulseService Pulse(FinancialIngestionDbContext db, DateTimeOffset? now = null) =>
        new(db, null!, null!, new PrimarySource(), Options.Create(new MarketViewOptions()),
            new FixedTimeProvider(now ?? Now), NullLogger<MarketPulseService>.Instance);

    private sealed class PrimarySource : IMarketQuoteSourcePriority
    {
        public string PrimarySourceName => Provider;
    }

    private sealed class Feed : ITsetmcOptionWebServiceClient
    {
        public IReadOnlyList<TsetmcOptionRecord> Contracts { get; set; } = [];
        public IReadOnlyList<TsetmcBestLimitRecord> BestLimits { get; set; } = [];

        public Task<IReadOnlyList<TsetmcOptionRecord>> GetOptionContractsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Contracts);

        public Task<IReadOnlyList<TsetmcBestLimitRecord>> GetBestLimitsAsync(byte flow, CancellationToken cancellationToken) =>
            Task.FromResult(BestLimits);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
