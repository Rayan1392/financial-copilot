using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 159 change «equilibrium-intraday-price»: which trade price the equilibrium card uses.</summary>
public sealed class ValuationGaugeEquilibriumPriceTests
{
    private static readonly TimeOnly Ready = new(19, 0);
    private static readonly DateOnly Wednesday = new(2026, 11, 25);
    private static readonly DateOnly Tuesday = new(2026, 11, 24);
    private const string Isin = "IRO1TEST0001";

    // Tehran is UTC+03:30.
    private static DateTimeOffset Tehran(DateOnly date, int hour, int minute) =>
        new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3.5));

    private static ValuationGaugeQuote Intraday(decimal price, DateOnly date) =>
        new(price, "Tsetmc", "IntradayToday", Tehran(date, 12, 29), date);

    private static ValuationGaugeQuote Daily(decimal price, DateOnly date) =>
        new(price, "Tsetmc", "LatestDailyFallback", Tehran(date, 18, 0), date);

    // ---- Selection policy -----------------------------------------------------------------------------------

    [Fact]
    public void AfterTheClose_BeforeTheThreshold_UsesTodaysIntradayTrade() // AC-01
    {
        var quote = ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(3900m, Tuesday), Tehran(Wednesday, 14, 0), Ready);

        Assert.Equal((4100m, "IntradayToday"), (quote!.Price, quote.SourceLabel));
    }

    [Fact]
    public void BeforeTheThreshold_TodaysIntradayTradeBeatsAnEarlyDailyRowOfToday()
    {
        var quote = ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(4120m, Wednesday), Tehran(Wednesday, 18, 59), Ready);

        Assert.Equal(4100m, quote!.Price);
    }

    [Theory]
    [InlineData(19, 0)]
    [InlineData(19, 30)]
    [InlineData(23, 59)]
    public void FromTheThreshold_TodaysDailyRowWins(int hour, int minute) // AC-02
    {
        var quote = ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(4120m, Wednesday), Tehran(Wednesday, hour, minute), Ready);

        Assert.Equal((4120m, "LatestDailyFallback"), (quote!.Price, quote.SourceLabel));
    }

    [Fact]
    public void FromTheThreshold_WithoutTodaysDailyRow_KeepsTheIntradayTrade() // AC-03, late Wednesday update
    {
        var quote = ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(3900m, Tuesday), Tehran(Wednesday, 19, 30), Ready);

        Assert.Equal((4100m, "IntradayToday"), (quote!.Price, quote.SourceLabel));
    }

    [Fact]
    public void WithoutAnIntradayTradeToday_UsesTheLatestDailyRow() // AC-04, Thursday
    {
        var quote = ValuationGaugeQuoteSelection.Select(null, Daily(4120m, Wednesday), Tehran(new DateOnly(2026, 11, 26), 10, 0), Ready);

        Assert.Equal((4120m, "LatestDailyFallback"), (quote!.Price, quote.SourceLabel));
    }

    [Fact]
    public void WithoutAnyTrade_ThereIsNoQuote() =>
        Assert.Null(ValuationGaugeQuoteSelection.Select(null, null, Tehran(Wednesday, 14, 0), Ready));

    [Fact]
    public void ALaterThreshold_MovesTheSwitchToTheDailyRow() // AC-06
    {
        var at = Tehran(Wednesday, 19, 30);

        Assert.Equal(4100m, ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(4120m, Wednesday), at, new TimeOnly(20, 30))!.Price);
        Assert.Equal(4120m, ValuationGaugeQuoteSelection.Select(
            Intraday(4100m, Wednesday), Daily(4120m, Wednesday), at.AddHours(1), new TimeOnly(20, 30))!.Price);
    }

    [Theory]
    [InlineData("19:00", true)]
    [InlineData("20:30", true)]
    [InlineData("00:00", true)]
    [InlineData("25:00", false)]
    [InlineData("7pm", false)]
    [InlineData("", false)]
    public void DailyPriceReadyTehran_IsValidatedAsHoursAndMinutes(string value, bool valid) // AC-06
    {
        var options = new ValuationGaugeCardsOptions { DailyPriceReadyTehran = value };

        Assert.Equal(valid, options.IsValid(out var error));
        if (!valid) Assert.Equal("DailyPriceReadyTehran must be a time HH:mm.", error);
    }

    [Fact]
    public void DailyPriceReadyTehran_DefaultsTo1900() =>
        Assert.Equal("19:00", new ValuationGaugeCardsOptions().DailyPriceReadyTehran);

    // ---- Reader over the trade tables -----------------------------------------------------------------------

    [Fact]
    public async Task Reader_AfterTheClose_UsesTodaysOldIntradayRowInsteadOfYesterdaysDailyRow() // AC-01 end to end
    {
        await using var db = Db();
        var instrument = Instrument(db);
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 29), 4100m, Tehran(Wednesday, 12, 30));
        AddIntraday(db, instrument, Wednesday, new TimeOnly(11, 0), 4000m, Tehran(Wednesday, 11, 1));
        AddDaily(db, instrument, Tuesday, 3900m, Tehran(Tuesday, 17, 5));
        await db.SaveChangesAsync();

        var quote = await Reader(db).ReadAsync(Isin, Tehran(Wednesday, 14, 0), CancellationToken.None);

        Assert.Equal((4100m, "IntradayToday", Wednesday), (quote!.Price, quote.SourceLabel, quote.TradingDate));
        Assert.Equal(Tehran(Wednesday, 12, 30), quote.AsOf);
    }

    [Fact]
    public async Task Reader_AfterTheThreshold_UsesTodaysDailyRow() // AC-02
    {
        await using var db = Db();
        var instrument = Instrument(db);
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 29), 4100m, Tehran(Wednesday, 12, 30));
        AddDaily(db, instrument, Wednesday, 4120m, Tehran(Wednesday, 19, 10));
        await db.SaveChangesAsync();

        var quote = await Reader(db).ReadAsync(Isin, Tehran(Wednesday, 19, 30), CancellationToken.None);

        Assert.Equal((4120m, "LatestDailyFallback", Wednesday), (quote!.Price, quote.SourceLabel, quote.TradingDate));
    }

    [Fact]
    public async Task Reader_LateWednesday_KeepsTheIntradayRowUntilTheDailyRowArrives() // AC-03
    {
        await using var db = Db();
        var instrument = Instrument(db);
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 29), 4100m, Tehran(Wednesday, 12, 30));
        AddDaily(db, instrument, Tuesday, 3900m, Tehran(Tuesday, 17, 5));
        // Today's daily row written after the request time is not visible yet.
        AddDaily(db, instrument, Wednesday, 4120m, Tehran(Wednesday, 20, 15));
        await db.SaveChangesAsync();

        var quote = await Reader(db).ReadAsync(Isin, Tehran(Wednesday, 19, 30), CancellationToken.None);

        Assert.Equal((4100m, "IntradayToday"), (quote!.Price, quote.SourceLabel));
    }

    [Fact]
    public async Task Reader_OnThursday_UsesWednesdaysDailyRow() // AC-04
    {
        await using var db = Db();
        var instrument = Instrument(db);
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 29), 4100m, Tehran(Wednesday, 12, 30));
        AddDaily(db, instrument, Wednesday, 4120m, Tehran(Wednesday, 20, 15));
        await db.SaveChangesAsync();

        var quote = await Reader(db).ReadAsync(Isin, Tehran(new DateOnly(2026, 11, 26), 10, 0), CancellationToken.None);

        Assert.Equal((4120m, "LatestDailyFallback", Wednesday), (quote!.Price, quote.SourceLabel, quote.TradingDate));
    }

    [Fact]
    public async Task Reader_IgnoresZeroPricesRowsWithoutTradingTimeAndRowsAfterTheRequestTime()
    {
        await using var db = Db();
        var instrument = Instrument(db);
        AddIntraday(db, instrument, Wednesday, new TimeOnly(10, 0), 4050m, Tehran(Wednesday, 10, 1));
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 0), 0m, Tehran(Wednesday, 12, 1));
        AddIntraday(db, instrument, Wednesday, null, 4500m, Tehran(Wednesday, 12, 2));
        AddIntraday(db, instrument, Wednesday, new TimeOnly(12, 20), 4600m, Tehran(Wednesday, 12, 21));
        await db.SaveChangesAsync();

        var quote = await Reader(db).ReadAsync(Isin, Tehran(Wednesday, 12, 10), CancellationToken.None);

        Assert.Equal(4050m, quote!.Price);
    }

    [Fact]
    public async Task Reader_UnknownIsin_HasNoQuote()
    {
        await using var db = Db();
        Instrument(db);
        await db.SaveChangesAsync();

        Assert.Null(await Reader(db).ReadAsync("IRO1OTHER001", Tehran(Wednesday, 14, 0), CancellationToken.None));
    }

    private static FinancialIngestionDbContext Db() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ValuationGaugeQuoteReader Reader(FinancialIngestionDbContext db) =>
        new(new PersistedMarketDataProvider(db, TimeProvider.System), Options.Create(new ValuationGaugeCardsOptions()));

    private static Guid Instrument(FinancialIngestionDbContext db)
    {
        var id = Guid.NewGuid();
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = id,
            ProviderName = "Tsetmc",
            ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = 1,
            InstrumentIsin = Isin,
            Symbol = "تست",
            Name = "تست",
            MarketCode = "1",
            InstrumentKind = "Share",
            IsActive = true,
        });
        return id;
    }

    private static void AddIntraday(FinancialIngestionDbContext db, Guid instrument, DateOnly date, TimeOnly? time, decimal price, DateTimeOffset receivedAt) =>
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "Tsetmc",
            ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = instrument,
            TradingDate = date,
            TradingTime = time,
            LastTradedPrice = price,
            ClosingPrice = price,
            PriceYesterday = 3900m,
            ReceivedAt = receivedAt,
        });

    private static void AddDaily(FinancialIngestionDbContext db, Guid instrument, DateOnly date, decimal price, DateTimeOffset insertedAt) =>
        db.DailyInstrumentTrades.Add(new DailyInstrumentTradeRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "Tsetmc",
            ExternalTradeId = Guid.NewGuid(),
            TradingInstrumentId = instrument,
            TradingDate = date,
            LastTradedPrice = price,
            ClosingPrice = price,
            PriceYesterday = 3800m,
            SourceInsertedAt = insertedAt,
        });
}
