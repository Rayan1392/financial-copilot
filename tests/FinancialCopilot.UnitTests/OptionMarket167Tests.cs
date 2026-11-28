using System.Net;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Options;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.Options;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 167 — option chain data and call exercise ranking. Fixtures are cut from live TSETMC
/// responses of 2026-11-28 (a few rows per operation, credentials removed).
/// </summary>
public sealed class OptionMarket167Tests
{
    // 2026-11-28 10:00 IRST, a Saturday (trading day).
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-28T06:30:00Z");
    private static readonly DateOnly Today = new(2026, 11, 28);
    private static readonly DateOnly Expiry = new(2026, 12, 9);

    private const long UnderlyingCode = 1001;
    private const long CallCode = 2001;
    private const long PutCode = 2002;
    private const long OtherCallCode = 2003;

    // ── Ranking policy (AC-08, AC-09) ────────────────────────────────────────

    [Fact]
    public void Ranking_OrdersCallsByStrikePlusAsk_WithTheOwnersScreenshotFigures()
    {
        var chain = Chain(1489, Call("ضمخا9025", 1400, ask: 178, askVolume: 10), Call("ضمخا9019", 800, ask: 730, askVolume: 10));

        var result = CallExerciseRankingPolicy.Rank(chain, 1);

        Assert.Equal(["ضمخا9019", "ضمخا9025"], result.Ranked.Select(row => row.Option.Symbol));
        Assert.Equal([1, 2], result.Ranked.Select(row => row.Rank));
        Assert.Equal(1530m, result.Ranked[0].EffectivePurchaseCost);
        Assert.Equal(1578m, result.Ranked[1].EffectivePurchaseCost);
        Assert.Equal(0.028m, Math.Round(result.Ranked[0].PremiumOverSpot, 3));
        Assert.Equal(0.060m, Math.Round(result.Ranked[1].PremiumOverSpot, 3));
        Assert.Empty(result.Ineligible);
    }

    [Fact]
    public void Ranking_BreaksTiesByHigherAskVolumeThenSymbol()
    {
        var chain = Chain(1000,
            Call("ضب", 900, ask: 200, askVolume: 5),
            Call("ضالف", 900, ask: 200, askVolume: 5),
            Call("ضج", 800, ask: 300, askVolume: 50));

        var result = CallExerciseRankingPolicy.Rank(chain, 1);

        Assert.Equal(["ضج", "ضالف", "ضب"], result.Ranked.Select(row => row.Option.Symbol));
    }

    [Fact]
    public void Ranking_ReportsWhyACallIsNotEligible()
    {
        var chain = Chain(1000,
            Call("بدون-عرضه", 700, ask: null, askVolume: null),
            Call("حجم-کم", 800, ask: 250, askVolume: 2),
            Call("خوب", 900, ask: 150, askVolume: 3));

        var result = CallExerciseRankingPolicy.Rank(chain, 3);

        Assert.Equal("خوب", Assert.Single(result.Ranked).Option.Symbol);
        Assert.Equal(
            [("بدون-عرضه", CallExerciseIneligibility.NoAsk), ("حجم-کم", CallExerciseIneligibility.InsufficientAskVolume)],
            result.Ineligible.Select(row => (row.Option.Symbol, row.Reason)));
    }

    [Fact]
    public void Ranking_TreatsAnOrderBookOlderThanTheUnderlyingSessionAsStale()
    {
        var chain = Chain(1000, Call("ض", 900, ask: 150, askVolume: 3)) with
        {
            OrderBookObservedAt = Now.AddDays(-1)
        };

        var result = CallExerciseRankingPolicy.Rank(chain, 1);

        Assert.Empty(result.Ranked);
        Assert.Equal(CallExerciseIneligibility.StaleQuote, Assert.Single(result.Ineligible).Reason);
    }

    [Fact]
    public void Ranking_IgnoresPuts_AndRejectsANonPositiveQuantity()
    {
        var put = Call("ط", 900, ask: 10, askVolume: 10) with { Side = OptionSide.Put };
        var chain = Chain(1000) with { Strikes = [new OptionChainStrike(900, null, put)] };

        Assert.Empty(CallExerciseRankingPolicy.Rank(chain, 1).Ranked);
        Assert.Throws<ArgumentOutOfRangeException>(() => CallExerciseRankingPolicy.Rank(chain, 0));
    }

    // ── Domain validation ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1000, 0, OptionContractRejection.NonPositiveStrike)]
    [InlineData(1400, 0, 0, OptionContractRejection.NonPositiveContractSize)]
    [InlineData(1400, 1000, -1, OptionContractRejection.ExpiryBeforeBegin)]
    [InlineData(1400, 1000, 30, OptionContractRejection.None)]
    public void ContractTerms_AreValidatedAtConstruction(
        int strike, int contractSize, int daysToExpiry, OptionContractRejection expected)
    {
        var rejection = OptionContractTerms.TryCreate(
            OptionSide.Call, strike, Today.AddDays(daysToExpiry), Today, contractSize, out var terms);

        Assert.Equal(expected, rejection);
        Assert.Equal(expected == OptionContractRejection.None, terms is not null);
    }

    [Fact]
    public void OrderBookLevel_ZeroMeansNoOrder_AndAskBelowBidIsRejected()
    {
        Assert.True(OptionOrderBookLevel.TryCreate(1, 690, 15, 1, 0, 0, 0, out var bidOnly));
        Assert.Equal(690m, bidOnly!.BidPrice);
        Assert.Null(bidOnly.AskPrice);
        Assert.Null(bidOnly.AskVolume);

        Assert.True(OptionOrderBookLevel.TryCreate(5, 0, 0, 0, 0, 0, 0, out var empty));
        Assert.True(empty!.IsEmpty);

        Assert.False(OptionOrderBookLevel.TryCreate(1, 500, 10, 1, 400, 10, 1, out _));
        Assert.False(OptionOrderBookLevel.TryCreate(6, 500, 10, 1, 600, 10, 1, out _));
    }

    [Theory]
    [InlineData("IRO9MKBT0A01", OptionSide.Call)]
    [InlineData("iroaxxxx0001", OptionSide.Call)]
    [InlineData("IROFMAPN5382", OptionSide.Put)]
    [InlineData("IROBXXXX0001", OptionSide.Put)]
    public void OptionSide_ComesFromTheIsinPrefix(string isin, OptionSide expected) =>
        Assert.Equal(expected, TsetmcOptionIsin.SideOf(isin));

    [Theory]
    [InlineData("IRS4XXXX0001")]
    [InlineData("IRO1MKBT0001")]
    [InlineData("")]
    [InlineData(null)]
    public void OptionSide_IsUnknownForOtherInstruments(string? isin) =>
        Assert.Null(TsetmcOptionIsin.SideOf(isin));

    // ── Provider adapter ─────────────────────────────────────────────────────

    [Fact]
    public async Task Client_ParsesOptionRows()
    {
        var client = Client(_ => Soap("Option", """
            <Option diffgr:id="Option1" msdata:rowOrder="0">
              <InsCode>70160331666811226</InsCode><InstrumentID>IRO9AHRM0G51</InstrumentID>
              <BuyOP>10824</BuyOP><SellOP>10824</SellOP><ContractSize>1000</ContractSize>
              <StrikePrice>46000.00</StrikePrice><UAInsCode>17914401175772326</UAInsCode>
              <BeginDate>20260919</BeginDate><EndDate>20261216</EndDate>
              <AFactor>0.2</AFactor><BFactor>0.1</BFactor><CFactor>10000</CFactor>
              <YesterdayOP>10795</YesterdayOP><Delta>1</Delta><MarketSameRiskOP>2039959</MarketSameRiskOP>
            </Option>
            <Option diffgr:id="Option2" msdata:rowOrder="1">
              <InsCode>59046324684454732</InsCode><InstrumentID>IROFAHRM0G61</InstrumentID>
              <BuyOP>15470</BuyOP><SellOP>15470</SellOP><ContractSize>1000</ContractSize>
              <StrikePrice>50000.00</StrikePrice><UAInsCode>17914401175772326</UAInsCode>
              <BeginDate>20260919</BeginDate><EndDate>20261216</EndDate><YesterdayOP>15396</YesterdayOP>
            </Option>
            """));

        var rows = await client.GetOptionContractsAsync(default);

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new TsetmcOptionRecord(70160331666811226, "IRO9AHRM0G51", 10824, 10795, 1000, 46000.00m,
                17914401175772326, new DateOnly(2026, 9, 19), new DateOnly(2026, 12, 16)),
            rows[0]);
        Assert.Equal("IROFAHRM0G61", rows[1].InstrumentId);
    }

    [Fact]
    public async Task Client_ParsesBestLimitRows()
    {
        var client = Client(_ => Soap("BestLimitsAllIns", """
            <InstBestLimit diffgr:id="InstBestLimit1" msdata:rowOrder="0">
              <number>1</number><InsCode>68105102338277095</InsCode><QTitMeDem>5</QTitMeDem>
              <ZOrdMeDem>1</ZOrdMeDem><PMeDem>160</PMeDem><PMeOf>890</PMeOf><ZOrdMeOf>2</ZOrdMeOf>
              <QTitMeOf>1000</QTitMeOf><HEven>114147</HEven>
            </InstBestLimit>
            <InstBestLimit diffgr:id="InstBestLimit2" msdata:rowOrder="1">
              <number>5</number><InsCode>68105102338277095</InsCode><QTitMeDem>0</QTitMeDem>
              <ZOrdMeDem>0</ZOrdMeDem><PMeDem>0</PMeDem><PMeOf>0</PMeOf><ZOrdMeOf>0</ZOrdMeOf>
              <QTitMeOf>0</QTitMeOf><HEven>60132</HEven>
            </InstBestLimit>
            """));

        var rows = await client.GetBestLimitsAsync(3, default);

        Assert.Equal(
            new TsetmcBestLimitRecord(68105102338277095, 1, 5, 1, 160, 890, 2, 1000, new TimeOnly(11, 41, 47)),
            rows[0]);
        Assert.Equal(new TimeOnly(6, 1, 32), rows[1].Time);
        Assert.Equal(5, rows[1].Level);
    }

    [Fact]
    public async Task Client_ReadsOnlyValidListedOptionsFromADerivativeInstrumentFlow_WithPersianSymbol()
    {
        static string Row(int id, long code, string isin, string persian, string valid) => $"""
            <TseInstruments diffgr:id="TseInstruments{id}" msdata:rowOrder="{id}">
              <InsCode>{code}</InsCode><InstrumentID>{isin}</InstrumentID><CValMne>5A382</CValMne>
              <LSoc30>مخابرات ايران</LSoc30><LVal18AFC>{persian}</LVal18AFC>
              <LVal30>اختيارخ اخابر-1400-1405/09/18</LVal30><CGdSVal>A</CGdSVal><CGrValCot>F2</CGrValCot>
              <DInMar>20210411</DInMar><YMarNSC>NO</YMarNSC><ZTitad>1000</ZTitad><Valid>{valid}</Valid>
            </TseInstruments>
            """;
        var client = Client(_ => Soap("Instrument",
            Row(1, 11, "IRO9MKBT0A01", "ضمخا9025", "1") +   // listed call, valid
            Row(2, 12, "IROFMKBT0A01", "طمخا9025", "1") +   // listed put, valid
            Row(3, 13, "IRO9MKBT0B01", "ضمخا8025", "0") +   // expired
            Row(4, 14, "IRB6XXXX0001", "صمخا", "1")));       // valid, but not a listed option

        var rows = await ((ITsetmcWebServiceClient)client).GetInstrumentsAsync(3, default);

        Assert.Equal([11L, 12L], rows.Select(row => row.InsCode));
        Assert.Equal("ضمخا9025", rows[0].Symbol);
        Assert.Equal("اختيارخ اخابر-1400-1405/09/18", rows[0].CompanyName);
        Assert.All(rows, row => Assert.True(row.Valid));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Client_TreatsTheProvidersHtmlPageAsAFailure_NotAsAnEmptyResult(HttpStatusCode status)
    {
        var calls = 0;
        var client = Client(_ =>
        {
            calls++;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("<html><body><h1>.:TSETMC:.</h1></body></html>", Encoding.UTF8, "text/html")
            };
        });

        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() => client.GetOptionContractsAsync(default));

        Assert.Equal(FinancialProviderErrorCode.RemoteUnavailable, exception.Code);
        Assert.True(calls <= 2, "retries are bounded by RetryCount");
    }

    // ── Contract sync (AC-03, AC-04) ─────────────────────────────────────────

    [Fact]
    public async Task ContractSync_StoresACallAndAPut_AndASecondIdenticalRunAddsNothing()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed
        {
            Contracts =
            [
                Contract(CallCode, "IRO9MKBT0A01", 1400, openPositions: 109, yesterday: 100),
                Contract(PutCode, "IROFMKBT0A01", 1400, openPositions: 8, yesterday: 8)
            ]
        };

        var first = await OptionSync(db, feed).SynchronizeContractsAsync(default);
        var second = await OptionSync(db, feed).SynchronizeContractsAsync(default);

        Assert.Equal((2, 2, 0), (first.RowsFetched, first.RowsPersisted, first.RowsUnchanged));
        Assert.Equal((0, 2), (second.RowsPersisted, second.RowsUnchanged));
        var rows = await db.OptionContracts.OrderBy(row => row.Side).ToListAsync();
        Assert.Equal(["Call", "Put"], rows.Select(row => row.Side));
        var call = rows[0];
        Assert.Equal(InstrumentId(db, CallCode), call.TradingInstrumentId);
        Assert.Equal(InstrumentId(db, UnderlyingCode), call.UnderlyingInstrumentId);
        Assert.Equal((1400m, Expiry, 1000, 109L, 100L), (call.StrikePrice, call.ExpiryDate, call.ContractSize, call.OpenInterest, call.PreviousOpenInterest));
        Assert.NotNull((await db.StockMarketSyncStates.SingleAsync(row => row.Dataset == "Tsetmc_OptionContracts")).Watermark);
    }

    [Fact]
    public async Task ContractSync_RejectsInvalidRows_IgnoresUnlistedContractKinds_AndDefersUnknownInstruments()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        AddInstrument(db, 2004, "ه-تبعي", "IRS4XXXX0001");
        await db.SaveChangesAsync();
        var feed = new FakeOptionFeed
        {
            Contracts =
            [
                Contract(CallCode, "IRO9MKBT0A01", 1400),
                Contract(PutCode, "IROFMKBT0A01", 0),                    // non-positive strike
                Contract(OtherCallCode, "IRO9MKBT0A11", 1500) with { ContractSize = 0 },
                Contract(2004, "IRS4XXXX0001", 1200),                    // embedded put: not a listed call or put
                Contract(9999, "IRO9XXXX0001", 1000)                     // instrument not in the catalog yet
            ]
        };

        var result = await OptionSync(db, feed).SynchronizeContractsAsync(default);

        Assert.Equal((5, 1, 2, 1, 1),
            (result.RowsFetched, result.RowsPersisted, result.RowsRejected, result.RowsUnresolved, result.RowsIgnored));
        Assert.Equal(InstrumentId(db, CallCode), (await db.OptionContracts.SingleAsync()).TradingInstrumentId);
        Assert.Equal(5, await db.TradingInstruments.CountAsync()); // the sync never creates instruments
    }

    [Fact]
    public async Task ContractSync_ProviderFailureLeavesStoredContractsAndWatermarkUntouched()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed { Contracts = [Contract(CallCode, "IRO9MKBT0A01", 1400, openPositions: 109)] };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);
        var watermark = (await db.StockMarketSyncStates.SingleAsync()).Watermark;

        feed.Failure = new FinancialProviderException(FinancialProviderErrorCode.RemoteUnavailable, "403");
        await Assert.ThrowsAsync<FinancialProviderException>(() => OptionSync(db, feed, Now.AddMinutes(5)).SynchronizeContractsAsync(default));
        feed.Failure = null;
        feed.Contracts = [];
        await Assert.ThrowsAsync<FinancialProviderException>(() => OptionSync(db, feed, Now.AddMinutes(6)).SynchronizeContractsAsync(default));

        Assert.Equal(109, (await db.OptionContracts.SingleAsync()).OpenInterest);
        var state = await db.StockMarketSyncStates.SingleAsync();
        Assert.Equal(watermark, state.Watermark);
        Assert.Equal(Now.AddMinutes(6), state.LastRunCompletedAt);
    }

    // ── Order-book sync (AC-05, AC-06) ───────────────────────────────────────

    [Fact]
    public async Task OrderBookSync_StoresLevelsOfOptionContractsOnly_AndWritesNothingWhenUnchanged()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed
        {
            Contracts = [Contract(CallCode, "IRO9MKBT0A01", 1400)],
            BestLimits =
            [
                Level(CallCode, 1, bid: 170, bidVolume: 20, ask: 178, askVolume: 10),
                Level(CallCode, 2, bid: 165, bidVolume: 5, ask: 180, askVolume: 4),
                Level(CallCode, 3, bid: 160, bidVolume: 5, ask: 0, askVolume: 0),
                Level(CallCode, 4, bid: 150, bidVolume: 5, ask: 0, askVolume: 0),
                Level(CallCode, 5, bid: 140, bidVolume: 5, ask: 0, askVolume: 0),
                Level(UnderlyingCode, 1, bid: 1488, bidVolume: 100, ask: 1489, askVolume: 100) // not an option contract
            ]
        };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);

        var first = await OptionSync(db, feed).SynchronizeOrderBookAsync(default);
        var second = await OptionSync(db, feed, Now.AddMinutes(1)).SynchronizeOrderBookAsync(default);

        Assert.Equal((6, 5, 0, 1), (first.RowsFetched, first.RowsPersisted, first.RowsRejected, first.RowsIgnored));
        Assert.Equal((0, 5, 0), (second.RowsPersisted, second.RowsUnchanged, second.RowsRemoved));
        var levels = await db.OptionOrderBookLevels.OrderBy(row => row.Level).ToListAsync();
        Assert.Equal([1, 2, 3, 4, 5], levels.Select(row => (int)row.Level));
        Assert.All(levels, row => Assert.Equal(InstrumentId(db, CallCode), row.TradingInstrumentId));
        Assert.Equal((170m, 20L, 178m, 10L), (levels[0].BidPrice, levels[0].BidVolume, levels[0].AskPrice, levels[0].AskVolume));
        Assert.Null(levels[2].AskPrice);
        Assert.Null(levels[2].AskVolume);
        Assert.All(levels, row => Assert.Equal(Now, row.ObservedAt)); // unchanged rows were not rewritten
        Assert.Equal(Now.AddMinutes(1), (await db.StockMarketSyncStates.SingleAsync(row => row.Dataset == "Tsetmc_OptionOrderBook")).Watermark);
    }

    [Fact]
    public async Task OrderBookSync_RemovesLevelsTheProviderNoLongerReports_AndRejectsCrossedLevels()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed
        {
            Contracts = [Contract(CallCode, "IRO9MKBT0A01", 1400), Contract(PutCode, "IROFMKBT0A01", 1400)],
            BestLimits =
            [
                Level(CallCode, 1, bid: 170, bidVolume: 20, ask: 178, askVolume: 10),
                Level(CallCode, 2, bid: 165, bidVolume: 5, ask: 180, askVolume: 4),
                Level(PutCode, 1, bid: 1, bidVolume: 8, ask: 2, askVolume: 8)
            ]
        };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);
        await OptionSync(db, feed).SynchronizeOrderBookAsync(default);

        feed.BestLimits =
        [
            Level(CallCode, 1, bid: 171, bidVolume: 20, ask: 178, askVolume: 10),   // changed
            Level(PutCode, 1, bid: 5, bidVolume: 8, ask: 2, askVolume: 8)           // ask below bid
        ];
        var result = await OptionSync(db, feed, Now.AddMinutes(1)).SynchronizeOrderBookAsync(default);

        Assert.Equal((1, 1, 2), (result.RowsPersisted, result.RowsRejected, result.RowsRemoved));
        var level = await db.OptionOrderBookLevels.SingleAsync();
        Assert.Equal((InstrumentId(db, CallCode), (short)1, 171m), (level.TradingInstrumentId, level.Level, level.BidPrice));
    }

    [Fact]
    public async Task OrderBookSync_ProviderFailureOrEmptyResponseKeepsTheStoredBook()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed
        {
            Contracts = [Contract(CallCode, "IRO9MKBT0A01", 1400)],
            BestLimits = [Level(CallCode, 1, bid: 170, bidVolume: 20, ask: 178, askVolume: 10)]
        };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);
        await OptionSync(db, feed).SynchronizeOrderBookAsync(default);

        feed.BestLimits = [];
        await Assert.ThrowsAsync<FinancialProviderException>(() => OptionSync(db, feed, Now.AddMinutes(1)).SynchronizeOrderBookAsync(default));
        feed.Failure = new FinancialProviderException(FinancialProviderErrorCode.Timeout, "timeout");
        await Assert.ThrowsAsync<FinancialProviderException>(() => OptionSync(db, feed, Now.AddMinutes(2)).SynchronizeOrderBookAsync(default));

        Assert.Equal(178m, (await db.OptionOrderBookLevels.SingleAsync()).AskPrice);
        Assert.Equal(Now, (await db.StockMarketSyncStates.SingleAsync(row => row.Dataset == "Tsetmc_OptionOrderBook")).Watermark);
    }

    [Fact]
    public async Task OptionDatasets_AppearInTheTsetmcSyncStateQuery()
    {
        await using var db = CreateDb();
        SeedInstruments(db);
        var feed = new FakeOptionFeed
        {
            Contracts = [Contract(CallCode, "IRO9MKBT0A01", 1400)],
            BestLimits = [Level(CallCode, 1, bid: 170, bidVolume: 20, ask: 178, askVolume: 10)]
        };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);
        await OptionSync(db, feed).SynchronizeOrderBookAsync(default);

        var states = await DirectFeed(db, new FakeTradeFeed()).QueryAsync(default);

        Assert.Equal(
            ["Tsetmc_OptionContracts", "Tsetmc_OptionOrderBook"],
            states.Select(state => state.Dataset).Order());
        Assert.All(states, state => Assert.Equal(Now, state.LastRunCompletedAt));
    }

    // ── Instrument identity (AC-01, AC-02) ───────────────────────────────────

    [Fact]
    public async Task DailyTrades_GiveANumericStubItsRealSymbol_AndLeaveNamedInstrumentsAlone()
    {
        await using var db = CreateDb();
        AddInstrument(db, CallCode, CallCode.ToString(), isin: "");
        AddInstrument(db, UnderlyingCode, "اخابر", "IRO1MKBT0001");
        await db.SaveChangesAsync();
        var tradingDay = LastTradingDay();
        var feed = new FakeTradeFeed
        {
            DailyTrades = (date, flow) => date == tradingDay && flow == 3
                ? [DailyTrade(CallCode, "ضمخا1043", date), DailyTrade(UnderlyingCode, "نام-ديگر", date)]
                : []
        };

        await DirectFeed(db, feed, fromDate: tradingDay).SynchronizeDailyTradesAsync(default);

        Assert.Equal("ضمخا1043", (await db.TradingInstruments.SingleAsync(row => row.InstrumentCode == CallCode)).Symbol);
        Assert.Equal("اخابر", (await db.TradingInstruments.SingleAsync(row => row.InstrumentCode == UnderlyingCode)).Symbol);
    }

    [Fact]
    public async Task StubBackfill_RenamesFromStoredDailyPayloads_NewestFirst_AndASecondRunChangesNothing()
    {
        await using var db = CreateDb();
        await using var providerDb = new FinancialProviderDbContext(
            new DbContextOptionsBuilder<FinancialProviderDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        AddInstrument(db, CallCode, CallCode.ToString(), isin: "");
        AddInstrument(db, PutCode, PutCode.ToString(), isin: "");
        AddInstrument(db, OtherCallCode, OtherCallCode.ToString(), isin: "");   // never in a payload
        AddInstrument(db, UnderlyingCode, "اخابر", "IRO1MKBT0001");
        await db.SaveChangesAsync();
        AddDailyPayload(providerDb, Now.AddDays(-2), (CallCode, "ضمخا-قديمي"), (PutCode, "طمخا1043"));
        AddDailyPayload(providerDb, Now.AddDays(-1), (CallCode, "ضمخا1043"), (UnderlyingCode, "نام-ديگر"));
        providerDb.ProviderRawPayloads.Add(new ProviderRawPayloadRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", Dataset = "DailyTrades",
            Payload = "not json", Checksum = Guid.NewGuid().ToString("N"), ReceivedAt = Now
        });
        await providerDb.SaveChangesAsync();
        var backfill = new TsetmcInstrumentStubSymbolBackfill(
            db, providerDb, Options.Create(new TsetmcWebServiceOptions()), new FixedTimeProvider(Now),
            NullLogger<TsetmcInstrumentStubSymbolBackfill>.Instance);

        var first = await backfill.BackfillAsync(default);
        var second = await backfill.BackfillAsync(default);

        Assert.Equal(new InstrumentStubBackfillResult(3, 2, 1, 3), first);
        Assert.Equal((1, 0, 1), (second.StubsFound, second.StubsRenamed, second.StubsSkipped));
        var symbols = await db.TradingInstruments.ToDictionaryAsync(row => row.InstrumentCode, row => row.Symbol);
        Assert.Equal("ضمخا1043", symbols[CallCode]);
        Assert.Equal("طمخا1043", symbols[PutCode]);
        Assert.Equal(OtherCallCode.ToString(), symbols[OtherCallCode]);
        Assert.Equal("اخابر", symbols[UnderlyingCode]);
    }

    // ── Option chain and ranking over stored data (AC-07, AC-08, AC-10) ──────

    [Fact]
    public async Task Chain_ReturnsCallAndPutPerStrike_ForTheNearestUnexpiredExpiry()
    {
        await using var db = CreateDb();
        await SeedChainAsync(db);

        // The stored symbol uses Arabic letters; the request uses Persian ones.
        var chain = await new EfCoreOptionChainReader(db, new FixedTimeProvider(Now)).GetChainAsync(" اخابر ", null, default);

        Assert.NotNull(chain);
        Assert.Equal(Expiry, chain.ExpiryDate);
        Assert.Equal([Expiry, Expiry.AddMonths(2)], chain.AvailableExpiryDates);
        Assert.Equal(new OptionUnderlyingQuote(1489, Today, Now), chain.Underlying);
        Assert.Equal([800m, 1400m], chain.Strikes.Select(strike => strike.StrikePrice));
        var strike = chain.Strikes[1];
        Assert.Equal(
            ("ضمخا9025", OptionSide.Call, 178m, 10L, 170m, 20L, 109L, 1000, 176m, 174m),
            (strike.Call!.Symbol, strike.Call.Side, strike.Call.AskPrice!.Value, strike.Call.AskVolume!.Value,
                strike.Call.BidPrice!.Value, strike.Call.BidVolume!.Value, strike.Call.OpenInterest,
                strike.Call.ContractSize, strike.Call.LastPrice!.Value, strike.Call.ClosingPrice!.Value));
        Assert.Equal("طمخا9025", strike.Put!.Symbol);
        Assert.Null(strike.Put.AskPrice);
        Assert.Null(chain.Strikes[0].Put);
        Assert.Equal(Now, chain.OrderBookObservedAt);
        Assert.Equal(Now, chain.OpenInterestObservedAt);
    }

    [Fact]
    public async Task Chain_LeavesOutExpiredContracts_AndIsNullForUnknownSymbolOrExpiry()
    {
        await using var db = CreateDb();
        await SeedChainAsync(db);
        var afterExpiry = new EfCoreOptionChainReader(db, new FixedTimeProvider(Now.AddDays(20)));
        var reader = new EfCoreOptionChainReader(db, new FixedTimeProvider(Now));

        var later = await afterExpiry.GetChainAsync("اخابر", null, default);

        Assert.Equal(Expiry.AddMonths(2), later!.ExpiryDate);
        Assert.Equal([Expiry.AddMonths(2)], later.AvailableExpiryDates);
        Assert.Null(await reader.GetChainAsync("فولاد", null, default));
        Assert.Null(await reader.GetChainAsync("اخابر", Today.AddDays(1), default));
        Assert.Null(await reader.GetChainAsync("  ", null, default));
    }

    [Fact]
    public async Task RankingUseCase_RanksStoredCalls_AndReportsMissingData()
    {
        await using var db = CreateDb();
        await SeedChainAsync(db);
        var useCase = new CallExerciseRankingUseCase(new EfCoreOptionChainReader(db, new FixedTimeProvider(Now)));

        var (result, failure) = await useCase.ExecuteAsync("اخابر", null, 1, default);
        var (unknown, unknownFailure) = await useCase.ExecuteAsync("فولاد", null, 1, default);

        Assert.Equal(CallExerciseRankingFailure.None, failure);
        Assert.Equal([("ضمخا9019", 1530m), ("ضمخا9025", 1578m)],
            result!.Ranked.Select(row => (row.Option.Symbol, row.EffectivePurchaseCost)));
        Assert.Equal(Now, result.OrderBookObservedAt);
        Assert.Equal(Now, result.OpenInterestObservedAt);
        Assert.Equal(Now, result.Underlying.ObservedAt);
        Assert.Null(unknown);
        Assert.Equal(CallExerciseRankingFailure.NoOptionContracts, unknownFailure);

        db.LatestMarketQuotes.RemoveRange(db.LatestMarketQuotes);
        await db.SaveChangesAsync();
        Assert.Equal(CallExerciseRankingFailure.NoUnderlyingPrice, (await useCase.ExecuteAsync("اخابر", null, 1, default)).Failure);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static OptionChain Chain(decimal underlyingPrice, params OptionQuote[] calls) =>
        new("اخابر", new OptionUnderlyingQuote(underlyingPrice, Today, Now), Expiry, [Expiry],
            calls.Select(call => new OptionChainStrike(call.StrikePrice, call, null)).ToArray(), Now, Now);

    private static OptionQuote Call(string symbol, decimal strike, decimal? ask, long? askVolume) =>
        new(symbol, symbol, OptionSide.Call, strike, Expiry, 1000, null, null, null, null, null, ask, askVolume, 0, Now);

    private static TsetmcOptionRecord Contract(
        long code, string isin, decimal strike, long openPositions = 0, long yesterday = 0) =>
        new(code, isin, openPositions, yesterday, 1000, strike, UnderlyingCode, new DateOnly(2026, 9, 19), Expiry);

    private static TsetmcBestLimitRecord Level(long code, int level, decimal bid, long bidVolume, decimal ask, long askVolume) =>
        new(code, level, bidVolume, bidVolume > 0 ? 1 : 0, bid, ask, askVolume > 0 ? 1 : 0, askVolume, new TimeOnly(10, 0));

    private static TsetmcDailyTradeRecord DailyTrade(long code, string symbol, DateOnly date) =>
        new(code, symbol, date, 100, 100, 100, 100, 100, 100, 0, 1, 1, 100);

    private static void SeedInstruments(FinancialIngestionDbContext db)
    {
        AddInstrument(db, UnderlyingCode, "اخابر", "IRO1MKBT0001");
        AddInstrument(db, CallCode, "ضمخا9025", "IRO9MKBT0A01");
        AddInstrument(db, PutCode, "طمخا9025", "IROFMKBT0A01");
        AddInstrument(db, OtherCallCode, "ضمخا9019", "IRO9MKBT0A11");
        db.SaveChanges();
    }

    private static async Task SeedChainAsync(FinancialIngestionDbContext db)
    {
        SeedInstruments(db);
        AddInstrument(db, 2010, "ضمخا1116", "IRO9MKBT0B01");
        await db.SaveChangesAsync();
        var feed = new FakeOptionFeed
        {
            Contracts =
            [
                Contract(CallCode, "IRO9MKBT0A01", 1400, openPositions: 109),
                Contract(PutCode, "IROFMKBT0A01", 1400, openPositions: 8),
                Contract(OtherCallCode, "IRO9MKBT0A11", 800, openPositions: 696),
                Contract(2010, "IRO9MKBT0B01", 1100) with { EndDate = Expiry.AddMonths(2) }
            ],
            BestLimits =
            [
                Level(CallCode, 1, bid: 170, bidVolume: 20, ask: 178, askVolume: 10),
                Level(CallCode, 2, bid: 165, bidVolume: 5, ask: 180, askVolume: 4),
                Level(PutCode, 1, bid: 1, bidVolume: 8, ask: 0, askVolume: 0),
                Level(OtherCallCode, 1, bid: 723, bidVolume: 3, ask: 730, askVolume: 5)
            ]
        };
        await OptionSync(db, feed).SynchronizeContractsAsync(default);
        await OptionSync(db, feed).SynchronizeOrderBookAsync(default);

        AddQuote(db, UnderlyingCode, 1489);
        AddQuote(db, CallCode, 176);
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = InstrumentId(db, CallCode), TradingDate = Today, TradingTime = new TimeOnly(9, 30),
            ClosingPrice = 170, LastTradedPrice = 171
        });
        db.IntradayTradeSnapshots.Add(new IntradayTradeSnapshotRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", ExternalSnapshotId = Guid.NewGuid(),
            TradingInstrumentId = InstrumentId(db, CallCode), TradingDate = Today, TradingTime = new TimeOnly(9, 58),
            ClosingPrice = 174, LastTradedPrice = 176
        });
        await db.SaveChangesAsync();
    }

    private static void AddQuote(FinancialIngestionDbContext db, long code, decimal price) =>
        db.LatestMarketQuotes.Add(new LatestMarketQuoteRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", TradingInstrumentId = InstrumentId(db, code),
            LatestPrice = price, SourceKind = "Intraday", TradingDate = Today, AsOf = Now
        });

    private static void AddInstrument(FinancialIngestionDbContext db, long code, string symbol, string isin) =>
        db.TradingInstruments.Add(new TradingInstrumentRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", ExternalInstrumentId = Guid.NewGuid(),
            InstrumentCode = code, Symbol = symbol, InstrumentIsin = isin, IsActive = isin.Length > 0,
            LastSynchronizedAt = Now
        });

    private static Guid InstrumentId(FinancialIngestionDbContext db, long code) =>
        db.TradingInstruments.Local.Single(row => row.InstrumentCode == code).Id;

    private static void AddDailyPayload(
        FinancialProviderDbContext providerDb, DateTimeOffset receivedAt, params (long InsCode, string Symbol)[] rows) =>
        providerDb.ProviderRawPayloads.Add(new ProviderRawPayloadRow
        {
            Id = Guid.NewGuid(), ProviderName = "TsetmcWebService", Dataset = "DailyTrades",
            Payload = JsonSerializer.Serialize(rows.Select(row => DailyTrade(row.InsCode, row.Symbol, Today))),
            Checksum = Guid.NewGuid().ToString("N"), ReceivedAt = receivedAt
        });

    /// <summary>The most recent Iranian trading day (Saturday–Wednesday) on the real clock, which the daily sync uses.</summary>
    private static DateOnly LastTradingDay()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        while (date.DayOfWeek is DayOfWeek.Thursday or DayOfWeek.Friday) date = date.AddDays(-1);
        return date;
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TsetmcOptionFeedSyncService OptionSync(
        FinancialIngestionDbContext db, FakeOptionFeed feed, DateTimeOffset? now = null) =>
        new(db, feed, Options.Create(new TsetmcWebServiceOptions { Enabled = true, UserName = "u", Password = "p" }),
            new FixedTimeProvider(now ?? Now), NullLogger<TsetmcOptionFeedSyncService>.Instance);

    private static TsetmcDirectFeedSyncService DirectFeed(
        FinancialIngestionDbContext db, FakeTradeFeed feed, DateOnly? fromDate = null) =>
        new(db, feed, new NoOpPayloadStore(),
            Options.Create(new TsetmcWebServiceOptions
            {
                Enabled = true, UserName = "u", Password = "p",
                DailyTradeFromDate = (fromDate ?? Today).ToString("yyyyMMdd")
            }),
            new NoOpScannerCache(), new NoOpMarketViewCache(), new FixedTimeProvider(Now),
            NullLogger<TsetmcDirectFeedSyncService>.Instance);

    private static TsetmcWebServiceClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://tsetmc.test/service.asmx") },
            Options.Create(new TsetmcWebServiceOptions { UserName = "u", Password = "p", RetryCount = 2 }),
            NullLogger<TsetmcWebServiceClient>.Instance);

    private static HttpResponseMessage Soap(string action, string rows) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"""
                <?xml version="1.0" encoding="utf-8"?>
                <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
                  <soap:Body>
                    <{action}Response xmlns="http://tsetmc.com/">
                      <{action}Result>
                        <xs:schema id="Data" xmlns="" xmlns:xs="http://www.w3.org/2001/XMLSchema">
                          <xs:element name="Data"><xs:complexType /></xs:element>
                        </xs:schema>
                        <diffgr:diffgram xmlns:msdata="urn:schemas-microsoft-com:xml-msdata" xmlns:diffgr="urn:schemas-microsoft-com:xml-diffgram-v1">
                          <Data xmlns="">{rows}</Data>
                        </diffgr:diffgram>
                      </{action}Result>
                    </{action}Response>
                  </soap:Body>
                </soap:Envelope>
                """.TrimStart(), Encoding.UTF8, "text/xml")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeOptionFeed : ITsetmcOptionWebServiceClient
    {
        public IReadOnlyList<TsetmcOptionRecord> Contracts { get; set; } = [];
        public IReadOnlyList<TsetmcBestLimitRecord> BestLimits { get; set; } = [];
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<TsetmcOptionRecord>> GetOptionContractsAsync(CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(Contracts) : Task.FromException<IReadOnlyList<TsetmcOptionRecord>>(Failure);

        public Task<IReadOnlyList<TsetmcBestLimitRecord>> GetBestLimitsAsync(byte flow, CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(BestLimits) : Task.FromException<IReadOnlyList<TsetmcBestLimitRecord>>(Failure);
    }

    private sealed class FakeTradeFeed : ITsetmcWebServiceClient
    {
        public Func<DateOnly, byte, IReadOnlyList<TsetmcDailyTradeRecord>> DailyTrades { get; init; } = (_, _) => [];

        public Task<IReadOnlyList<TsetmcInstrumentRecord>> GetInstrumentsAsync(byte flow, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TsetmcInstrumentRecord>>([]);

        public Task<IReadOnlyList<TsetmcIntradayTradeRecord>> GetIntradayTradesAsync(byte flow, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TsetmcIntradayTradeRecord>>([]);

        public Task<IReadOnlyList<TsetmcDailyTradeRecord>> GetDailyTradesAsync(DateOnly date, byte flow, CancellationToken cancellationToken) =>
            Task.FromResult(DailyTrades(date, flow));

        public Task<IReadOnlyList<TsetmcDailyIndexRecord>> GetDailyIndicesAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TsetmcDailyIndexRecord>>([]);

        public Task<IReadOnlyList<TsetmcIntradayIndexRecord>> GetIntradayIndicesAsync(byte flow, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TsetmcIntradayIndexRecord>>([]);
    }

    private sealed class NoOpPayloadStore : IProviderRawPayloadStore
    {
        public Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProviderRawPayload?> FindByChecksumAsync(string providerName, string checksum, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderRawPayload?>(null);
    }

    private sealed class NoOpMarketViewCache : IMarketViewCache
    {
        public Task<MarketSummary?> GetSummaryAsync(CancellationToken cancellationToken) => Task.FromResult<MarketSummary?>(null);

        public Task SetSummaryAsync(MarketSummary summary, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
