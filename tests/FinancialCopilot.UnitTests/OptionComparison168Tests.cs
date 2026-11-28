using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 168 — option comparison answers: measures, fill, highlights, intent rules, text.</summary>
public sealed class OptionComparison168Tests
{
    // 2026-11-28 10:00 IRST.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-28T06:30:00Z");
    private static readonly DateOnly Today = new(2026, 11, 28);
    private static readonly OptionAnswersOptions Options = new();

    // ── Fill (AC-02) ─────────────────────────────────────────────────────────

    [Fact]
    public void Fill_IsTheVolumeWeightedPriceAcrossLevels()
    {
        var levels = Levels((0, 0, 544, 10), (0, 0, 550, 10));

        Assert.Equal(new OptionFill(OptionRowState.Fillable, 544m, 20), OptionComparisonPolicy.Fill(levels, 10, askSide: true));
        Assert.Equal(new OptionFill(OptionRowState.Fillable, 546m, 20), OptionComparisonPolicy.Fill(levels, 15, askSide: true));
        Assert.Equal(new OptionFill(OptionRowState.NotFillable, null, 20), OptionComparisonPolicy.Fill(levels, 25, askSide: true));
        Assert.Equal(new OptionFill(OptionRowState.NoOrder, null, 0), OptionComparisonPolicy.Fill(levels, 1, askSide: false));
        Assert.Equal(new OptionFill(OptionRowState.NoOrder, null, 0), OptionComparisonPolicy.Fill([], 1, askSide: true));
    }

    [Fact]
    public void Fill_UsesTheBidSideForAWriter()
    {
        var levels = Levels((120, 5, 130, 5), (118, 5, 0, 0));

        Assert.Equal(new OptionFill(OptionRowState.Fillable, 119m, 10), OptionComparisonPolicy.Fill(levels, 10, askSide: false));
    }

    // ── Call view (AC-01, AC-03, AC-04) ──────────────────────────────────────

    [Fact]
    public void CallMeasures_IncludeFees_WithTheAcceptanceFigures()
    {
        var comparison = Build(1489, Share, Call("ضمخا9021", 1000, Levels((0, 0, 544, 10))));

        var row = Rows(comparison, OptionComparisonView.Call).Single();

        Assert.Equal(1545.044m, row.EffectivePurchaseCost);
        Assert.Equal(0.038m, Math.Round(row.BreakEvenDistance!.Value, 3));
        Assert.Equal(489m, row.IntrinsicValue);
        Assert.Equal(55m, row.TimeValue);
        Assert.Equal(2.74m, Math.Round(row.Leverage!.Value, 2));
        Assert.Equal(544.544m * 1000, row.CashNow);
        Assert.Equal(1000.5m * 1000, row.CashAtExpiry);
        Assert.Equal(55m / 1000 * 365 / 11, row.ImpliedAnnualRate);
    }

    [Fact]
    public void ImpliedAnnualRate_OnlyForInTheMoneyCallsAboveTheAnnualizationFloor()
    {
        var inTheMoney = Call("ض-در-سود", 1000, Levels((0, 0, 544, 10)));
        var outOfTheMoney = Call("ض-بی-سود", 1600, Levels((0, 0, 20, 10)));
        var soon = Today.AddDays(2);

        var normal = Rows(Build(1489, Share, inTheMoney, outOfTheMoney), OptionComparisonView.Call);
        var nearExpiry = Rows(Build(1489, Share, inTheMoney with { ExpiryDate = soon }), OptionComparisonView.Call);

        Assert.NotNull(normal.Single(row => row.Symbol == "ض-در-سود").ImpliedAnnualRate);
        Assert.Null(normal.Single(row => row.Symbol == "ض-بی-سود").ImpliedAnnualRate);
        Assert.Null(nearExpiry.Single().ImpliedAnnualRate);
        Assert.NotNull(nearExpiry.Single().EffectivePurchaseCost);
    }

    [Fact]
    public void Highlights_NeedTwoFillableRows_AndBreakTiesByVolumeThenSymbol()
    {
        var single = Rows(Build(1489, Share, Call("ض1", 1000, Levels((0, 0, 544, 10)))), OptionComparisonView.Call);
        var tied = Rows(Build(1489, Share,
            Call("ضب", 1000, Levels((0, 0, 544, 10))),
            Call("ضالف", 1000, Levels((0, 0, 544, 10))),
            Call("ضج", 1000, Levels((0, 0, 544, 50))),
            Call("ضد", 900, Levels((0, 0, 700, 1)))), OptionComparisonView.Call);

        Assert.Empty(single.Single().Highlights);
        Assert.Equal("ضج", tied.Single(row => row.Highlights.Contains(OptionHighlights.LowestEffectivePurchaseCost)).Symbol);
        Assert.Single(tied, row => row.Highlights.Contains(OptionHighlights.LowestImpliedAnnualRate));
    }

    [Fact]
    public void Rows_ReportFillStateAndTheNearestStrike_AndMarkTheRequestedContract()
    {
        var comparison = Build(1489, Share, quantity: 25, requested: "ض1500", contracts:
        [
            Call("ض1400", 1400, Levels((0, 0, 178, 20))),
            Call("ض1500", 1500, Levels((0, 0, 116, 30))),
            Call("ض1600", 1600, [])
        ]);

        var rows = Rows(comparison, OptionComparisonView.Call);

        Assert.Equal([OptionRowState.NotFillable, OptionRowState.Fillable, OptionRowState.NoOrder], rows.Select(row => row.State));
        Assert.Equal(20, rows[0].AvailableVolume);
        Assert.Null(rows[0].EffectivePurchaseCost);
        Assert.Equal("ض1500", rows.Single(row => row.IsNearestToMoney).Symbol);
        Assert.Equal("ض1500", rows.Single(row => row.IsRequestedContract).Symbol);
        Assert.Equal(25, comparison.QuantityInContracts);
    }

    // ── Freshness (AC-05) ────────────────────────────────────────────────────

    [Fact]
    public void AnOrderBookOlderThanTheUnderlyingSession_GivesTradePricesOnly()
    {
        var comparison = Build(1489, Share, orderBookObservedAt: Now.AddDays(-1),
            contracts: Call("ضمخا9021", 1000, Levels((0, 0, 544, 10))) with { LastPrice = 540 });

        var row = Rows(comparison, OptionComparisonView.Call).Single();

        Assert.False(comparison.OrderBookIsCurrent);
        Assert.Null(row.FillPrice);
        Assert.Null(row.EffectivePurchaseCost);
        Assert.Equal(540m, row.LastPrice);
        Assert.Equal(7, row.OpenInterest);
        Assert.True(OptionComparisonPolicy.IsOrderBookCurrent(Now, Today));
        Assert.False(OptionComparisonPolicy.IsOrderBookCurrent(null, Today));
    }

    // ── Put and covered call (AC-13, AC-14) ──────────────────────────────────

    [Theory]
    [InlineData("IRO1MKBT0001", 1372.28)]
    [InlineData("IRT1AHRM0001", 1379.28)]
    [InlineData("", 1372.28)]
    public void PutSalePrice_PaysTheTransferTaxOnSharesOnly(string underlyingIsin, double expected)
    {
        var comparison = Build(1489, underlyingIsin, Put("طمخا9025", 1400, Levels((0, 0, 20, 10))));

        var row = Rows(comparison, OptionComparisonView.Put).Single();

        Assert.Equal((decimal)expected, row.EffectiveSalePrice);
        Assert.Equal(20.02m / 1489, row.ProtectionCost);
        Assert.Equal(underlyingIsin.Length == 0 ? OptionUnderlyingClass.Unknown
            : underlyingIsin.StartsWith("IRT") ? OptionUnderlyingClass.FundUnit : OptionUnderlyingClass.Share, comparison.UnderlyingClass);
    }

    [Fact]
    public void CoveredCall_UsesTheBidSide_WithTheAcceptanceFigures()
    {
        var comparison = Build(1489, Share,
            Call("ضمخا1500", 1500, Levels((120, 10, 130, 10))) with { ExpiryDate = Today.AddDays(30) });

        var row = Rows(comparison, OptionComparisonView.CoveredCall).Single();

        Assert.Equal(119.88m, row.PremiumIncome);
        Assert.Equal(0.082m, Math.Round(row.YieldIfExercised!.Value, 3));
        Assert.Equal(0.081m, Math.Round(row.YieldIfNotExercised!.Value, 3));
        Assert.Equal(row.YieldIfExercised * 365 / 30, row.YieldIfExercisedAnnualized);
        Assert.Equal(row.YieldIfNotExercised, row.DownsideCushion);
        Assert.Equal(130m, Rows(comparison, OptionComparisonView.Call).Single().FillPrice);
    }

    [Fact]
    public void Views_HoldCallsPutsAndCoveredCallsOfTheSameChain()
    {
        var comparison = Build(1489, Share,
            Call("ض1", 1400, Levels((170, 20, 178, 10))),
            Put("ط1", 1400, Levels((1, 8, 2, 8))));

        Assert.Equal(["ض1"], Rows(comparison, OptionComparisonView.Call).Select(row => row.Symbol));
        Assert.Equal(["ط1"], Rows(comparison, OptionComparisonView.Put).Select(row => row.Symbol));
        Assert.Equal(["ض1"], Rows(comparison, OptionComparisonView.CoveredCall).Select(row => row.Symbol));
        Assert.True(comparison.HasRows());
        Assert.Equal(Options.Fees, comparison.Fees);
    }

    // ── Theoretical price (AC-17, AC-18) ─────────────────────────────────────

    [Fact]
    public void BlackScholes_MatchesReferenceValues_AndRecoversTheVolatility()
    {
        // Hull, Options, Futures and Other Derivatives: S=42, K=40, r=10%, sigma=20%, T=0.5 → c=4.76, p=0.81.
        Assert.Equal(4.7594, BlackScholes.Price(OptionSide.Call, 42, 40, 0.5, 0.10, 0.20), 3);
        Assert.Equal(0.8086, BlackScholes.Price(OptionSide.Put, 42, 40, 0.5, 0.10, 0.20), 3);

        var price = BlackScholes.Price(OptionSide.Call, 1489, 1500, 30 / 365d, Math.Log(1.415), 0.60);
        Assert.Equal(0.60, BlackScholes.ImpliedVolatility(OptionSide.Call, 1489, 1500, 30 / 365d, Math.Log(1.415), price)!.Value, 3);
        Assert.Null(BlackScholes.ImpliedVolatility(OptionSide.Call, 1489, 1000, 30 / 365d, Math.Log(1.415), 100));
        Assert.Null(BlackScholes.ImpliedVolatility(OptionSide.Call, 1489, 1500, 0, Math.Log(1.415), 100));
    }

    [Fact]
    public void HistoricalVolatility_NeedsTwentyReturns()
    {
        var closes = Enumerable.Range(0, 91).Select(index => 1000m * (index % 2 == 0 ? 1.00m : 1.02m)).ToArray();

        var volatility = BlackScholes.HistoricalVolatility(closes, 240);

        Assert.InRange(volatility!.Value, 0.29, 0.32);
        Assert.Null(BlackScholes.HistoricalVolatility(closes.Take(20).ToArray(), 240));
        Assert.Null(BlackScholes.HistoricalVolatility(closes.Select(_ => 0m).ToArray(), 240));
    }

    [Fact]
    public async Task UseCase_AddsTheoreticalColumnsOnlyWhenTheRateAndEnoughClosesExist()
    {
        var snapshot = Snapshot(1489, Share, Now, Call("ض1", 1500, Levels((0, 0, 100, 10))) with { ExpiryDate = Today.AddDays(30) });
        var closes = Enumerable.Range(0, 91).Select(index => 1000m * (index % 2 == 0 ? 1.00m : 1.02m)).ToArray();
        var query = new OptionComparisonQuery("اخابر", OptionComparisonView.Call);

        var withRate = await new OptionComparisonUseCase(new FakeReader(snapshot, closes), new OptionAnswersOptions { RiskFreeAnnualRate = 0.415m })
            .ExecuteAsync(query, default);
        var withoutRate = await new OptionComparisonUseCase(new FakeReader(snapshot, closes), Options).ExecuteAsync(query, default);
        var fewCloses = await new OptionComparisonUseCase(new FakeReader(snapshot, closes.Take(10).ToArray()), new OptionAnswersOptions { RiskFreeAnnualRate = 0.415m })
            .ExecuteAsync(query, default);

        var row = Rows(withRate.Comparison!, OptionComparisonView.Call).Single();
        Assert.Equal(0.415m, withRate.Comparison!.TheoreticalInputs!.RiskFreeAnnualRate);
        Assert.Equal(90, withRate.Comparison.TheoreticalInputs.VolatilityWindowDays);
        Assert.NotNull(row.TheoreticalPrice);
        Assert.NotNull(row.ImpliedVolatility);
        Assert.Null(withoutRate.Comparison!.TheoreticalInputs);
        Assert.Null(Rows(withoutRate.Comparison, OptionComparisonView.Call).Single().TheoreticalPrice);
        Assert.Null(fewCloses.Comparison!.TheoreticalInputs);
    }

    [Fact]
    public async Task UseCase_ReportsWhyThereIsNoComparison()
    {
        var query = new OptionComparisonQuery("اخابر", OptionComparisonView.Call);
        var noPrice = Snapshot(1489, Share, Now, Call("ض1", 1500, [])) with { Underlying = null };
        var noExpiry = Snapshot(1489, Share, Now) with { AvailableExpiryDates = [Today.AddDays(11)] };

        Assert.Equal(OptionComparisonFailure.NoOptionContracts,
            (await new OptionComparisonUseCase(new FakeReader(null, []), Options).ExecuteAsync(query, default)).Failure);
        Assert.Equal(OptionComparisonFailure.NoUnderlyingPrice,
            (await new OptionComparisonUseCase(new FakeReader(noPrice, []), Options).ExecuteAsync(query, default)).Failure);
        var unlisted = await new OptionComparisonUseCase(new FakeReader(noExpiry, []), Options).ExecuteAsync(query, default);
        Assert.Equal(OptionComparisonFailure.ExpiryNotListed, unlisted.Failure);
        Assert.Equal([Today.AddDays(11)], unlisted.ListedExpiryDates);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new OptionComparisonUseCase(new FakeReader(null, []), Options).ExecuteAsync(query with { QuantityInContracts = 0 }, default));
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    public void StarterCard_NeedsTheCapabilityTheFlagAndItsOwnKey(
        bool enabled, bool capabilityAvailable, bool starterCardEnabled, bool shown)
    {
        // Change «option-starter-card-switch»: off by default; never shown while the capability is off.
        Assert.False(Options.StarterCardEnabled);
        var options = new OptionAnswersOptions { Enabled = enabled, StarterCardEnabled = starterCardEnabled };
        Assert.Equal(shown, options.ShowsStarterCard(capabilityAvailable));
    }

    [Fact]
    public void Settings_DefaultToTheNamedFeeSource_AndRejectBadValues()
    {
        Assert.Equal(new OptionFeeRates(0.001m, 0.0005m, 0.005m), Options.Fees);
        Assert.False(Options.Enabled);
        Assert.Null(Options.RiskFreeAnnualRate);
        Assert.True(Options.IsValid(out _));
        Assert.False(new OptionAnswersOptions { TradeFeeRate = 0.06m }.IsValid(out _));
        Assert.False(new OptionAnswersOptions { AnnualizationFloorDays = -1 }.IsValid(out _));
        Assert.False(new OptionAnswersOptions { MaxExpiries = 0 }.IsValid(out _));
        Assert.False(new OptionAnswersOptions { RiskFreeAnnualRate = -0.1m }.IsValid(out _));
        Assert.Equal(0m, Options.Fees.TransferTaxFor(OptionUnderlyingClass.FundUnit));
        Assert.Equal(0.005m, Options.Fees.TransferTaxFor(OptionUnderlyingClass.Unknown));
    }

    // ── Intent rules (AC-06, AC-15, AC-16) ───────────────────────────────────

    [Theory]
    [InlineData("اختیار خرید اخابر", OptionComparisonView.Call, "اخابر")]
    [InlineData("زنجیره اختیار اخابر", OptionComparisonView.Call, "اخابر")]
    [InlineData("اختیار معامله اخابر را نشان بده", OptionComparisonView.Call, "اخابر")]
    [InlineData("آپشن‌های اخابر", OptionComparisonView.Call, "اخابر")]
    [InlineData("اختیار خریدهای اخابر را برای اعمال مقایسه کن", OptionComparisonView.Call, "اخابر")]
    [InlineData("خرید اخابر از مسیر اختیار خرید چقدر تمام می‌شود؟", OptionComparisonView.Call, "اخابر")]
    [InlineData("اختیار فروش اخابر", OptionComparisonView.Put, "اخابر")]
    [InlineData("کاورد کال اخابر", OptionComparisonView.CoveredCall, "اخابر")]
    [InlineData("بازده فروش اختیار خرید اخابر", OptionComparisonView.CoveredCall, "اخابر")]
    [InlineData("اختیار خرید", OptionComparisonView.Call, null)]
    [InlineData("اختیار خرید ضمخا9021", OptionComparisonView.Call, "ضمخا9021")]
    public void OptionQuestions_AreMatchedWithTheirView(string message, OptionComparisonView view, string? company)
    {
        var request = OptionComparisonIntentRules.TryMatch(message);

        Assert.NotNull(request);
        Assert.Equal(view, request.View);
        Assert.Equal(company, request.CompanyText);
        Assert.Equal(1, request.Quantity);
    }

    [Theory]
    [InlineData("اخابر بخرم؟")]
    [InlineData("اختیار خرید اخابر بخرم؟")]
    [InlineData("توصیه برای اختیار خرید اخابر")]
    [InlineData("تحلیل اختیار خرید اخابر")]
    [InlineData("وقتی اختیار خرید اخابر زیر قیمت سهم شد خبر بده")]
    [InlineData("اختیار خرید اخابر یا فولاد")]
    [InlineData("قیمت اخابر")]
    [InlineData("خرید اخابر")]
    [InlineData("")]
    public void OtherQuestions_AreNotMatched(string message) =>
        Assert.Null(OptionComparisonIntentRules.TryMatch(message));

    [Fact]
    public void QuantityAndExpiry_AreReadFromTheQuestion()
    {
        var month = OptionComparisonIntentRules.TryMatch("اختیار خرید اخابر ۵ قرارداد سررسید دی");
        var date = OptionComparisonIntentRules.TryMatch("اختیار خرید اخابر سررسید 1405/09/18");
        var nearest = OptionComparisonIntentRules.TryMatch("اختیار فروش اخابر نزدیک‌ترین سررسید");

        Assert.Equal((5, 10, "اخابر"), (month!.Quantity, month.ExpiryMonth!.Value, month.CompanyText));
        Assert.Equal(new DateOnly(2026, 12, 9), date!.ExpiryDate);
        Assert.True(nearest!.NearestExpiryOnly);
        Assert.Equal(OptionComparisonView.Put, nearest.View);
        Assert.Null(OptionComparisonIntentRules.TryMatch("اختیار خرید اخابر 0 قرارداد"));
        Assert.Null(OptionComparisonIntentRules.TryMatch("اختیار خرید اخابر 200000 قرارداد"));
        Assert.Equal(10, OptionComparisonIntentRules.JalaliMonthOf(new DateOnly(2027, 1, 13)));
    }

    [Fact]
    public void TheRequestSurvivesTheTaskStateSlot()
    {
        var request = new OptionComparisonRequest(OptionComparisonView.CoveredCall, "اخابر", 5, 10, new DateOnly(2026, 12, 9), true);

        var parsed = OptionComparisonRequest.ParseSlot(request.SlotValue, "اخابر");

        Assert.Equal(request, parsed);
        Assert.Equal(new OptionComparisonRequest(OptionComparisonView.Call, null), OptionComparisonRequest.ParseSlot(null, null));
        Assert.Equal(1, OptionComparisonRequest.ParseSlot("view=put;qty=0;month=13;junk", "x").Quantity);
    }

    // ── Text and wording (AC-10, AC-11) ──────────────────────────────────────

    [Fact]
    public void Text_HasTitlePriceRowsHighlightsNotesAndDataTime()
    {
        var comparison = Build(1489, Share,
            Call("ضمخا9019", 800, Levels((0, 0, 730, 5))),
            Call("ضمخا9021", 1000, Levels((0, 0, 544, 10))),
            Call("ضمخا9027", 1600, []));

        var text = OptionComparisonTextComposer.Compose(comparison);

        Assert.StartsWith("مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید — اخابر", text);
        Assert.Contains("قیمت سهم: 1,489 ریال (1405/09/07)", text);
        Assert.Contains("سررسید 1405/09/18 (11 روز)، برای 1 قرارداد", text);
        Assert.Contains("• ضمخا9021 | اعمال 1,000 | عرضه 544 | هزینهٔ مؤثر 1,545 (3.8٪ بالاتر نسبت به قیمت سهم) | ارزش زمانی 55", text);
        Assert.Contains("کمترین هزینهٔ مؤثر: ضمخا9019", text);
        Assert.Contains("بدون سفارش یا با حجم ناکافی: 1 قرارداد", text);
        Assert.Contains(OptionComparisonWording.CallNote, text);
        Assert.Contains("کارمزدها: معامله 0.10٪، اعمال 0.05٪.", text);
        Assert.Contains("زمان عمق سفارش: 1405/09/07 10:00", text);
        Assert.EndsWith(OptionComparisonWording.AdviceNote, text);
    }

    [Fact]
    public void Text_ShowsAtMostEightRowsNearestToTheMoney_AndTheAskedView()
    {
        var calls = Enumerable.Range(0, 12)
            .Select(index => Call($"ض{index:00}", 900 + index * 100, Levels((0, 0, 100, 10))))
            .Append(Put("ط1", 1400, Levels((0, 0, 20, 10))))
            .ToArray();

        var callText = OptionComparisonTextComposer.Compose(Build(1489, Share, calls));
        var putText = OptionComparisonTextComposer.Compose(Build(1489, Share, view: OptionComparisonView.Put, contracts: calls));

        Assert.Equal(OptionComparisonTextComposer.MaxRows, callText.Split('\n').Count(line => line.StartsWith("• ")));
        Assert.Contains("• ض06", callText);
        Assert.DoesNotContain("• ض11", callText);
        Assert.StartsWith(OptionComparisonWording.PutTitle, putText);
        Assert.Contains("مالیات نقل‌وانتقال 0.50٪", putText);
        Assert.Contains(OptionComparisonWording.PutNote, putText);
    }

    [Fact]
    public void Text_SaysWhenTheOrderBookIsNotCurrent_AndWhyThereIsNoAnswer()
    {
        var stale = Build(1489, Share, orderBookObservedAt: Now.AddDays(-1),
            contracts: Call("ضمخا9021", 1000, Levels((0, 0, 544, 10))) with { LastPrice = 540 });

        var text = OptionComparisonTextComposer.Compose(stale);

        Assert.Contains(OptionComparisonWording.OrderBookNotCurrent, text);
        Assert.Contains("• ضمخا9021 | اعمال 1,000 | آخرین معامله 540 | موقعیت باز 7", text);
        Assert.DoesNotContain("هزینهٔ مؤثر", text);
        Assert.Equal("برای فولاد قرارداد اختیار معاملهٔ فعالی ثبت نشده است.",
            OptionComparisonTextComposer.Unavailable(new("فولاد", OptionComparisonFailure.NoOptionContracts, [])));
        Assert.Contains("سررسیدهای موجود: 1405/09/18",
            OptionComparisonTextComposer.Unavailable(new("اخابر", OptionComparisonFailure.ExpiryNotListed, [new DateOnly(2026, 12, 9)])));
        Assert.Contains("قیمت اخابر در دسترس نیست",
            OptionComparisonTextComposer.Unavailable(new("اخابر", OptionComparisonFailure.NoUnderlyingPrice, [])));
    }

    [Fact]
    public void Wording_NeverRecommends()
    {
        string[] banned = ["بهترین", "مناسب", "پیشنهاد", "فرصت"];
        var texts = OptionComparisonWording.All
            .Append(OptionComparisonTextComposer.Compose(Build(1489, Share,
                Call("ض1", 1000, Levels((500, 10, 544, 10))), Call("ض2", 1100, Levels((400, 10, 445, 10))),
                Put("ط1", 1400, Levels((0, 0, 20, 10))), Put("ط2", 1500, Levels((0, 0, 60, 10))))))
            .Append(OptionComparisonTextComposer.Unavailable(new("اخابر", OptionComparisonFailure.NoOptionContracts, [])));

        Assert.All(texts, text => Assert.All(banned, word => Assert.DoesNotContain(word, text)));
    }

    [Fact]
    public void Billing_ChargesOneCreditForAnAnsweredComparisonOnly()
    {
        Assert.Equal(1m, OptionComparisonBillingPolicy.FixedCredits(OptionComparisonBillingPolicy.AnsweredStatus));
        Assert.Null(OptionComparisonBillingPolicy.FixedCredits("Completed"));
        Assert.Null(OptionComparisonBillingPolicy.FixedCredits(null));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private const string Share = "IRO1MKBT0001";

    private static OptionComparison Build(decimal price, string isin, params OptionMarketContract[] contracts) =>
        Build(price, isin, 1, null, OptionComparisonView.Call, Now, contracts);

    private static OptionComparison Build(
        decimal price, string isin, int quantity = 1, string? requested = null,
        OptionComparisonView view = OptionComparisonView.Call, DateTimeOffset? orderBookObservedAt = null,
        params OptionMarketContract[] contracts) =>
        OptionComparisonPolicy.Build(
            Snapshot(price, isin, orderBookObservedAt ?? Now, contracts),
            new OptionComparisonQuery("اخابر", view, quantity, null, requested), Options, null);

    private static OptionMarketSnapshot Snapshot(
        decimal price, string isin, DateTimeOffset orderBookObservedAt, params OptionMarketContract[] contracts) =>
        new("اخابر", isin, new OptionUnderlyingQuote(price, Today, Now),
            contracts.Select(contract => contract.ExpiryDate).Distinct().Order().ToArray(), contracts, orderBookObservedAt, Now);

    private static OptionComparisonRow[] Rows(OptionComparison comparison, OptionComparisonView view) =>
        comparison.Views.Single(item => item.View == view).Expiries.SelectMany(expiry => expiry.Rows).ToArray();

    private static OptionMarketContract Call(string symbol, decimal strike, IReadOnlyList<OptionOrderBookLevel> levels) =>
        new(symbol, OptionSide.Call, strike, Today.AddDays(11), 1000, 7, null, levels);

    private static OptionMarketContract Put(string symbol, decimal strike, IReadOnlyList<OptionOrderBookLevel> levels) =>
        new(symbol, OptionSide.Put, strike, Today.AddDays(11), 1000, 7, null, levels);

    private static OptionOrderBookLevel[] Levels(params (decimal Bid, long BidVolume, decimal Ask, long AskVolume)[] levels) =>
        levels.Select((level, index) =>
        {
            Assert.True(OptionOrderBookLevel.TryCreate(index + 1, level.Bid, level.BidVolume, 1, level.Ask, level.AskVolume, 1, out var created));
            return created!;
        }).ToArray();

    private sealed class FakeReader(OptionMarketSnapshot? snapshot, IReadOnlyList<decimal> closes) : IOptionMarketSnapshotReader
    {
        public Task<OptionMarketSnapshot?> GetAsync(string underlyingSymbol, DateOnly? expiryDate, int maxExpiries, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);

        public Task<OptionContractReference?> FindContractAsync(string contractSymbol, CancellationToken cancellationToken) =>
            Task.FromResult<OptionContractReference?>(null);

        public Task<IReadOnlyList<string>> ListUnderlyingSymbolsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<decimal>> GetUnderlyingDailyClosesAsync(string underlyingSymbol, int days, CancellationToken cancellationToken) =>
            Task.FromResult(closes);
    }
}
