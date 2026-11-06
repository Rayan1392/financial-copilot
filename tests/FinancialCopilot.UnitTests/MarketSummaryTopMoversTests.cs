using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MarketSummaryTopMoversTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-03T08:00:00Z");

    [Fact]
    public async Task TopMoversComeOnlyFromEligibleCompaniesEvenWhenTheirInstrumentsAreInactive()
    {
        await using var db = CreateDb();
        // Listed companies; TSETMC marks their instruments as not valid (IsActive = false).
        var saipa = Company(db, "وساپا", "گروه سرمایه گذاری سایپا");
        var cement = Company(db, "سفید", "سیمان سفید شرق");
        var insurer = Company(db, "البرز", "بیمه البرز");
        Quote(db, Instrument(db, "وساپا", saipa, isActive: false), 4.2m);
        Quote(db, Instrument(db, "سفید", cement, isActive: false), 9.8m);
        Quote(db, Instrument(db, "البرز", insurer, isActive: false), -3.1m);
        // Active non-company instruments with extreme moves: an option and a commodity fund.
        Quote(db, Instrument(db, "W3101", companyId: null, isActive: true), 42768000m);
        Quote(db, Instrument(db, "LOTF2", companyId: null, isActive: true), -100m);
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetAsync(CancellationToken.None);

        Assert.Equal(["سفید", "وساپا"], summary.TopGainers.Select(mover => mover.Symbol));
        Assert.Equal(["البرز"], summary.TopLosers.Select(mover => mover.Symbol));
        Assert.Equal("سیمان سفید شرق", summary.TopGainers.First().Name);
    }

    [Fact]
    public async Task ACompanyQuoteIsFoundByTickerWhenTheInstrumentHasNoCompanyLink()
    {
        await using var db = CreateDb();
        Company(db, "وایرا", "سرمایه گذاری صنایع ایران");
        Quote(db, Instrument(db, "وایرا", companyId: null, isActive: true), 2.5m);
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetAsync(CancellationToken.None);

        var mover = Assert.Single(summary.TopGainers);
        Assert.Equal("وایرا", mover.Symbol);
        Assert.Equal("سرمایه گذاری صنایع ایران", mover.Name);
    }

    [Fact]
    public async Task AMoverCarriesItsTradingSessionEvenWhenObservedOnALaterDay()
    {
        await using var db = CreateDb();
        var company = Company(db, "کگهر", "معدنی و صنعتی گهر زمین");
        // Wednesday's session, synced after close on Thursday (production shows such next-day AsOf values).
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            TradingInstrumentId = Instrument(db, "کگهر", company, isActive: true),
            LatestPrice = 9_550m,
            PriceChangePercentage = 1.75m,
            SourceKind = "EndOfDay",
            TradingDate = new DateOnly(2026, 11, 4),
            AsOf = DateTimeOffset.Parse("2026-11-05T13:30:00Z")
        });
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetAsync(CancellationToken.None);

        var mover = Assert.Single(summary.TopGainers);
        Assert.Equal(new DateOnly(2026, 11, 4), mover.TradingDate);
    }

    [Fact]
    public async Task ACompanyLinkedBothWaysAppearsOnceWithItsLatestQuote()
    {
        await using var db = CreateDb();
        var company = Company(db, "ومشان", "سرمایه گذاری فنی و مهندسی مشانیر");
        Quote(db, Instrument(db, "ومشان", company, isActive: false), 1.5m, Now.AddDays(-1));
        Quote(db, Instrument(db, "ومشان", company, isActive: true), 3.5m, Now);
        await db.SaveChangesAsync();

        var summary = await CreateService(db).GetAsync(CancellationToken.None);

        var mover = Assert.Single(summary.TopGainers);
        Assert.Equal(3.5m, mover.ChangePercent);
    }

    private static Guid Company(FinancialIngestionDbContext db, string symbol, string name)
    {
        var id = Guid.NewGuid();
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = id,
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = id.ToString("N"),
            Name = name,
            CompanySymbol = symbol,
            TseSymbol = symbol
        });
        return id;
    }

    private static Guid Instrument(FinancialIngestionDbContext db, string symbol, Guid? companyId, bool isActive)
    {
        var id = Guid.NewGuid();
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = id,
            ProviderName = "TsetmcWebService",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = Random.Shared.NextInt64(),
            Symbol = symbol,
            Name = "انرژي",
            NormalizedCompanyId = companyId,
            IsActive = isActive
        });
        return id;
    }

    private static void Quote(
        FinancialIngestionDbContext db,
        Guid instrumentId,
        decimal changePercent,
        DateTimeOffset? asOf = null) =>
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "TsetmcWebService",
            TradingInstrumentId = instrumentId,
            LatestPrice = 1_000m,
            PriceChangePercentage = changePercent,
            SourceKind = "Intraday",
            TradingDate = DateOnly.FromDateTime((asOf ?? Now).UtcDateTime),
            AsOf = asOf ?? Now
        });

    private static MarketSummaryService CreateService(FinancialIngestionDbContext db)
    {
        var options = Options.Create(new MarketViewOptions());
        return new MarketSummaryService(
            db,
            new MemoryMarketViewCache(new MemoryCache(new MemoryCacheOptions()), options),
            options,
            new FixedTimeProvider(Now));
    }

    private static FinancialIngestionDbContext CreateDb() => new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
