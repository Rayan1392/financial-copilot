using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 159 change «standalone-valuation-gauge-cards»: recognition, availability, projection and text.</summary>
public sealed class ValuationGaugeCardsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);

    // شاراک P/E as stored locally (evidence/feature148-local-sample-output.txt): asymmetric average.
    private static RatioGaugeSnapshot SharakPe(string? close = "6.4291", int? status = 3, long[]? counts = null, DateTimeOffset? fetched = null) =>
        new(close, "2.106", "3.348", "5.88", "9.696", "59.61", counts ?? [120, 340, 410, 260, 150, 20], status, fetched ?? Now.AddHours(-3));

    private static EquilibriumRangeSnapshot AkhaberRange(DateTimeOffset? fetched = null) =>
        new(true, "2488.15", "4713.35", "6938.56", fetched ?? Now.AddHours(-2));

    private static ValuationGaugeQuote Quote(decimal price) =>
        new(price, "StockMarketDb", "IntradayToday", Now.AddMinutes(-5), new DateOnly(2026, 11, 26));

    // ---- Phrase recognition (OQ-130) -----------------------------------------------------------------------

    [Theory]
    [InlineData("گیج P/E شاراک", "pe", "شاراک")]
    [InlineData("گیج P/S کگهر", "ps", "کگهر")]
    [InlineData("گیج‌های ارزش‌گذاری شاراک", "pe,ps,equilibrium", "شاراک")]
    [InlineData("گیج ارزش گذاری شاراک", "pe,ps,equilibrium", "شاراک")]
    [InlineData("بازۀ قیمت تعادلی اخابر", "equilibrium", "اخابر")]
    [InlineData("بازهٔ قیمت تعادلی اخابر", "equilibrium", "اخابر")]
    [InlineData("گیج قیمت تعادلی اخابر", "equilibrium", "اخابر")]
    [InlineData("گیج pe نماد شاراک را نشان بده", "pe", "شاراک")]
    [InlineData("P/E gauge شاراک", "pe", "شاراک")]
    public void ExplicitGaugeRequestsAreRecognized(string message, string kinds, string company)
    {
        var match = ValuationGaugeIntentRules.TryMatch(message);

        Assert.NotNull(match);
        Assert.Equal(kinds, match!.KindsSlotValue);
        Assert.Equal(company, match.CompanyText);
    }

    [Theory]
    [InlineData("قیمت تعادلی اخابر")] // OQ-130: the bare phrase keeps its current route.
    [InlineData("P/E شاراک")]
    [InlineData("P/E شاراک چقدر است؟")]
    [InlineData("سهام با P/E زیر ۵")]
    [InlineData("گیج P/E کمتر از ۵")]
    [InlineData("گیج P/E شاراک و کگهر")]
    [InlineData("مقایسه گیج P/E شاراک با صنعت")]
    [InlineData("گیج P/E شاراک بخرم؟")]
    [InlineData("روند گیج P/E شاراک")]
    [InlineData("گیج شاراک")]
    [InlineData("بازه P/E شاراک")]
    [InlineData("تحلیل گیج ارزش گذاری شاراک")]
    [InlineData("")]
    public void OtherMessagesAreNotGaugeRequests(string message) =>
        Assert.Null(ValuationGaugeIntentRules.TryMatch(message));

    [Fact]
    public void AGaugeRequestWithoutSymbolHasNoCompanyText()
    {
        var match = ValuationGaugeIntentRules.TryMatch("گیج‌های ارزش‌گذاری");
        Assert.NotNull(match);
        Assert.Null(match!.CompanyText);
    }

    [Theory]
    [InlineData("equilibrium,pe", new[] { ValuationGaugeKind.Pe, ValuationGaugeKind.Equilibrium })]
    [InlineData("", new[] { ValuationGaugeKind.Pe, ValuationGaugeKind.Ps, ValuationGaugeKind.Equilibrium })]
    [InlineData("unknown", new[] { ValuationGaugeKind.Pe, ValuationGaugeKind.Ps, ValuationGaugeKind.Equilibrium })]
    public void KindsAreParsedInPresentationOrder(string value, ValuationGaugeKind[] expected) =>
        Assert.Equal(expected, ValuationGaugeIntentRules.ParseKinds(value));

    // ---- Availability and source map (AC-07) ---------------------------------------------------------------

    [Theory]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, null, false, false)]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, null, true, false)]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, "CyclicalWaves", false, false)]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, "CyclicalWaves", true, true)]
    [InlineData(FinancialDataChatProvider.CyclicalWaves, null, false, false)]
    [InlineData(FinancialDataChatProvider.CyclicalWaves, null, true, true)]
    public void TheRouteNeedsTheFlagAndACyclicalWavesSource(FinancialDataChatProvider mode, string? mapping, bool enabled, bool available)
    {
        var options = new FinancialDataOptions { ChatProvider = mode };
        if (mapping is not null) options.CapabilitySources[CapabilityExecutionRoutes.ValuationGauges] = mapping;
        var availability = new ChatCapabilityAvailability(mode, ChatCapabilitySourceMap.Create(options), valuationGaugeCardsEnabled: enabled);

        var outcome = availability.EvaluateForV2(CapabilityExecutionRoutes.ValuationGauges, ValuationGaugeCapability.Code);

        Assert.Equal(available, outcome.IsAvailable);
        if (!available)
            Assert.Equal(ChatCapabilityAvailability.ValuationGaugesUnavailableReasonCode, outcome.ReasonCode);
    }

    [Theory]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, "NoavaranAmin")]
    [InlineData(FinancialDataChatProvider.NoavaranAmin, "Bogus")]
    [InlineData(FinancialDataChatProvider.CyclicalWaves, "CyclicalWaves")]
    public void InvalidValuationGaugeMappingsFailComposition(FinancialDataChatProvider mode, string mapping)
    {
        var options = new FinancialDataOptions { ChatProvider = mode };
        options.CapabilitySources[CapabilityExecutionRoutes.ValuationGauges] = mapping;
        Assert.Throws<InvalidOperationException>(() => ChatCapabilitySourceMap.Create(options));
    }

    [Fact]
    public void AnEmptyValuationGaugeMappingIsNotAnOverride()
    {
        var options = new FinancialDataOptions { ChatProvider = FinancialDataChatProvider.NoavaranAmin };
        options.CapabilitySources[CapabilityExecutionRoutes.ValuationGauges] = "";
        Assert.Empty(ChatCapabilitySourceMap.Create(options).Overrides);
    }

    // ---- Billing (AC-08) -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(ValuationGaugeCapability.Code, null)]
    [InlineData(null, "گیج‌های ارزش‌گذاری شاراک")]
    [InlineData(null, "گیج P/E شاراک")]
    public void GaugeTurnsReserveTheMinimumOperation(string? capability, string? message) =>
        Assert.Equal(AiQueryBillingOperationSelector.DefaultOperationCode, AiQueryBillingOperationSelector.Select(capability, message));

    // ---- Ratio projection (AC-03) --------------------------------------------------------------------------

    [Fact]
    public void RatioAnchorsUseTheProviderAverageAtNinetyDegrees()
    {
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, SharakPe(), Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Available, card.State);
        var geometry = Assert.IsType<ValuationGaugeGeometry>(card.Geometry);
        Assert.Equal([0m, 30m, 90m, 150m, 180m], geometry.Markers.Select(marker => marker.Angle));
        Assert.Equal(["2.106", "3.348", "5.88", "9.696", "59.61"], geometry.Markers.Select(marker => marker.Value));
        Assert.Equal(6, geometry.Sectors.Count);
        Assert.All(geometry.Sectors, sector => Assert.Equal(30m, sector.EndAngle - sector.StartAngle));
        // 6.4291 lies between avg 5.88 (90°) and (avg+max)/2 = 7.788 (120°).
        var expected = decimal.Round(90m + (6.4291m - 5.88m) / (7.788m - 5.88m) * 30m, 4, MidpointRounding.AwayFromZero);
        Assert.Equal(expected, geometry.Needle!.Angle);
        Assert.Equal("6.4291", geometry.Needle.Value);
        Assert.Equal(ValuationRatioPosition.AverageToMax, card.Ratio!.Position);
    }

    [Fact]
    public void DayPercentagesUseLargestRemainderAndTotalExactlyOneHundred()
    {
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Ps, SharakPe(counts: [1, 1, 1, 0, 0, 0]), Now, MaxAge, 1);

        var percentages = card.Ratio!.DayPercentages!.Select(decimal.Parse).ToArray();
        Assert.Equal(100m, percentages.Sum());
        Assert.Equal([33.4m, 33.3m, 33.3m, 0m, 0m, 0m], percentages);
        Assert.Equal(LargestRemainderPercentages.Calculate([1, 1, 1, 0, 0, 0], 1), percentages);
    }

    [Fact]
    public void ExtractedRoundingKeepsThePsGaugeCalculatorResults()
    {
        var (bands, _) = PsGaugeCalculator.Calculate([7, 3, 5, 11, 2, 1], 0.1m, 0.5m, 2.5m, 4m, 1.2m, 2);
        Assert.Equal(LargestRemainderPercentages.Calculate([7, 3, 5, 11, 2, 1], 2), bands.Select(band => band.DisplayPercentage));
        Assert.Equal(100m, bands.Sum(band => band.DisplayPercentage));
    }

    [Fact]
    public void NullCloseHidesTheNeedleAndNullStatusStaysUnranked()
    {
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, SharakPe(close: null, status: null), Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Partial, card.State);
        Assert.Equal(ValuationGaugeReasonCodes.NoCurrentValue, card.ReasonCode);
        Assert.NotNull(card.Geometry);
        Assert.Null(card.Geometry!.Needle);
        Assert.Equal(ValuationRatioPosition.Unranked, card.Ratio!.Position);
        Assert.Null(card.Ratio.CurrentSide);
    }

    [Theory]
    [InlineData("1.5", 0, ValuationGaugeNeedleSide.PinnedBelow)]
    [InlineData("80.25", 180, ValuationGaugeNeedleSide.PinnedAbove)]
    public void OutOfAxisValuesPinTheNeedleAndKeepTheValue(string close, int angle, ValuationGaugeNeedleSide side)
    {
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, SharakPe(close: close, status: 4), Now, MaxAge, 1);

        Assert.Equal((decimal)angle, card.Geometry!.Needle!.Angle);
        Assert.Equal(side, card.Geometry.Needle.Side);
        Assert.Equal(close, card.Geometry.Needle.Value);
        Assert.Equal(close, card.Ratio!.Current);
        Assert.Equal(side, card.Ratio.CurrentSide);
    }

    [Fact]
    public void ZeroDayTotalOmitsTheDistribution()
    {
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, SharakPe(counts: [0, 0, 0, 0, 0, 0]), Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Partial, card.State);
        Assert.Equal(ValuationGaugeReasonCodes.NoDistribution, card.ReasonCode);
        Assert.Null(card.Ratio!.DayPercentages);
        Assert.All(card.Geometry!.Sectors, sector => Assert.Null(sector.Percentage));
        Assert.NotNull(card.Geometry.Needle);
    }

    [Theory]
    [InlineData("2", "2", "3", "4", "5")] // degenerate: start == min
    [InlineData("1", "3", "2", "4", "5")] // average below min
    public void InvalidAxisFallsBackToText(string start, string min, string average, string max, string end)
    {
        var snapshot = new RatioGaugeSnapshot("3", start, min, average, max, end, [1, 1, 1, 1, 1, 1], 2, Now.AddHours(-1));
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, snapshot, Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Partial, card.State);
        Assert.Equal(ValuationGaugeReasonCodes.AxisNotRenderable, card.ReasonCode);
        Assert.Null(card.Geometry);
        Assert.Equal(min, card.Ratio!.Min);
    }

    [Fact]
    public void SignedBoundsArePreservedExactly()
    {
        var snapshot = new RatioGaugeSnapshot("-12.5", "-9000.125", "-4312.68", "-10.5", "3.25", "40.0000001", [5, 4, 3, 2, 1, 1], 2, Now.AddHours(-1));
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, snapshot, Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Available, card.State);
        Assert.Equal("-4312.68", card.Ratio!.Min);
        Assert.Equal("40.0000001", card.Ratio.End);
        Assert.InRange(card.Geometry!.Needle!.Angle, 30m, 90m);
    }

    [Fact]
    public void StaleSnapshotsSuppressValuesAndKeepTheFetchTime()
    {
        var fetched = Now.AddHours(-49);
        var card = ValuationGaugeProjector.ProjectRatio(ValuationGaugeKind.Pe, SharakPe(fetched: fetched), Now, MaxAge, 1);

        Assert.Equal(ValuationGaugeCardState.Stale, card.State);
        Assert.Equal(fetched, card.FetchedAtUtc);
        Assert.Null(card.Ratio);
        Assert.Null(card.Geometry);
        Assert.False(card.IsDisplayable());
    }

    [Fact]
    public void FutureFetchTimesAreNotTrusted() =>
        Assert.True(ValuationGaugeProjector.IsStale(Now.AddHours(1), Now, MaxAge));

    // ---- Equilibrium projection (AC-04) --------------------------------------------------------------------

    [Theory]
    [InlineData(1000, ValuationEquilibriumPosition.BelowRange, ValuationGaugeNeedleSide.PinnedBelow, 0)]
    [InlineData(3600.75, ValuationEquilibriumPosition.LowerHalf, ValuationGaugeNeedleSide.Inside, null)]
    [InlineData(4713.35, ValuationEquilibriumPosition.AtMidpoint, ValuationGaugeNeedleSide.Inside, 90)]
    [InlineData(6000, ValuationEquilibriumPosition.UpperHalf, ValuationGaugeNeedleSide.Inside, null)]
    [InlineData(9000, ValuationEquilibriumPosition.AboveRange, ValuationGaugeNeedleSide.PinnedAbove, 180)]
    public void EquilibriumNeedleUsesTheQuoteAndPinsOutsideTheRange(double price, ValuationEquilibriumPosition position,
        ValuationGaugeNeedleSide side, int? angle)
    {
        var card = ValuationGaugeProjector.ProjectEquilibrium(AkhaberRange(), Quote((decimal)price), ValuationGaugeQuoteState.Available, Now, MaxAge);

        Assert.Equal(ValuationGaugeCardState.Available, card.State);
        Assert.Equal(position, card.Equilibrium!.Position);
        Assert.Equal(side, card.Geometry!.Needle!.Side);
        if (angle is { } expected) Assert.Equal((decimal)expected, card.Geometry.Needle.Angle);
        Assert.Equal([0m, 90m, 180m], card.Geometry.Markers.Select(marker => marker.Angle));
        Assert.Equal("StockMarketDb", card.Equilibrium.Quote!.ProviderName);
        Assert.NotEqual(card.FetchedAtUtc, card.Equilibrium.Quote.AsOf);
    }

    [Theory]
    [InlineData(ValuationGaugeQuoteState.Unavailable, ValuationGaugeReasonCodes.QuoteUnavailable)]
    [InlineData(ValuationGaugeQuoteState.TimedOut, ValuationGaugeReasonCodes.QuoteTimedOut)]
    public void AMissingQuoteKeepsTheRangeUsable(ValuationGaugeQuoteState quoteState, string reason)
    {
        var card = ValuationGaugeProjector.ProjectEquilibrium(AkhaberRange(), null, quoteState, Now, MaxAge);

        Assert.Equal(ValuationGaugeCardState.Partial, card.State);
        Assert.Equal(reason, card.ReasonCode);
        Assert.True(card.IsDisplayable());
        Assert.Null(card.Geometry!.Needle);
        Assert.Equal("2488.15", card.Equilibrium!.Lower);
        Assert.Null(card.Equilibrium.Position);
    }

    [Fact]
    public void DegenerateOrSignedRangesShowFactsAsText()
    {
        var degenerate = ValuationGaugeProjector.ProjectEquilibrium(
            new EquilibriumRangeSnapshot(true, "100", "100", "100", Now.AddHours(-1)), Quote(100), ValuationGaugeQuoteState.Available, Now, MaxAge);
        Assert.Equal(ValuationGaugeReasonCodes.AxisNotRenderable, degenerate.ReasonCode);
        Assert.Null(degenerate.Geometry);

        var signed = ValuationGaugeProjector.ProjectEquilibrium(
            new EquilibriumRangeSnapshot(true, "-4312.68", "120.5", "900", Now.AddHours(-1)), Quote(50), ValuationGaugeQuoteState.Available, Now, MaxAge);
        Assert.Equal(ValuationGaugeCardState.Available, signed.State);
        Assert.Equal("-4312.68", signed.Equilibrium!.Lower);
        Assert.Equal(ValuationEquilibriumPosition.LowerHalf, signed.Equilibrium.Position);
    }

    [Fact]
    public void AMissingRangeIsNoData()
    {
        var card = ValuationGaugeProjector.ProjectEquilibrium(new EquilibriumRangeSnapshot(false, null, null, null, Now), null,
            ValuationGaugeQuoteState.Unavailable, Now, MaxAge);
        Assert.Equal(ValuationGaugeCardState.NoData, card.State);
        Assert.Equal(ValuationGaugeReasonCodes.RangeMissing, card.ReasonCode);
    }

    // ---- Use case (AC-01, AC-02, AC-05) --------------------------------------------------------------------

    [Fact]
    public async Task AllKindsComeInFixedOrderWithPerKindAvailability()
    {
        var reader = new FakeSnapshotReader(new ValuationGaugeSnapshotSet(
            ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001", new ValuationGaugeCatalogFlags(true, false),
            Pe: SharakPe(), Ps: SharakPe(), Equilibrium: AkhaberRange()));
        var quotes = new FakeQuoteReader(Quote(4000));
        var result = await UseCase(reader, quotes).ExecuteAsync(Query([ValuationGaugeKind.Equilibrium, ValuationGaugeKind.Ps, ValuationGaugeKind.Pe]), CancellationToken.None);

        Assert.Equal([ValuationGaugeKind.Pe, ValuationGaugeKind.Ps, ValuationGaugeKind.Equilibrium], result.Cards.Select(card => card.Kind));
        Assert.Equal(ValuationGaugeCardState.Available, result.Cards[0].State);
        // P/S is not in the vendor catalog: its row is never exposed.
        Assert.Equal(ValuationGaugeCardState.NoData, result.Cards[1].State);
        Assert.Equal(ValuationGaugeReasonCodes.NotInCatalog, result.Cards[1].ReasonCode);
        Assert.Null(result.Cards[1].Ratio);
        Assert.Equal(ValuationGaugeCardState.Available, result.Cards[2].State);
        Assert.Equal("IRO1PARK0001", quotes.LastIsin);
        Assert.True(result.HasDisplayableData());
        Assert.False(result.IsComplete());
        Assert.Equal(Now, result.GeneratedAtUtc);
    }

    [Theory]
    [InlineData(ValuationGaugeMatchOutcome.Missing, ValuationGaugeReasonCodes.CoverageMiss)]
    [InlineData(ValuationGaugeMatchOutcome.Ambiguous, ValuationGaugeReasonCodes.AmbiguousMatch)]
    public async Task NoUniqueVendorMatchIsACoverageMiss(ValuationGaugeMatchOutcome match, string reason)
    {
        var result = await UseCase(new FakeSnapshotReader(new ValuationGaugeSnapshotSet(match)), new FakeQuoteReader(null))
            .ExecuteAsync(Query([ValuationGaugeKind.Pe]), CancellationToken.None);

        var card = Assert.Single(result.Cards);
        Assert.Equal(ValuationGaugeCardState.NoData, card.State);
        Assert.Equal(reason, card.ReasonCode);
    }

    [Fact]
    public async Task ASnapshotReadOverItsBudgetIsTemporarilyUnavailable()
    {
        var reader = new FakeSnapshotReader(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Missing)) { Delay = Timeout.InfiniteTimeSpan };
        var result = await UseCase(reader, new FakeQuoteReader(null), readTimeout: 100).ExecuteAsync(Query([ValuationGaugeKind.Pe, ValuationGaugeKind.Ps]), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.Equal(ValuationGaugeCardState.TemporarilyUnavailable, card.State));
        Assert.False(result.HasDisplayableData());
    }

    [Fact]
    public async Task CallerCancellationIsNotTurnedIntoATimeout()
    {
        var reader = new FakeSnapshotReader(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Missing)) { Delay = Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UseCase(reader, new FakeQuoteReader(null), readTimeout: 10_000).ExecuteAsync(Query([ValuationGaugeKind.Pe]), cancellation.Token));
    }

    [Fact]
    public async Task AQuoteOverItsBudgetLeavesARangeOnlyCard()
    {
        var reader = new FakeSnapshotReader(new ValuationGaugeSnapshotSet(
            ValuationGaugeMatchOutcome.Matched, "اخابر", "IRO1MKBT0001", Equilibrium: AkhaberRange()));
        var quotes = new FakeQuoteReader(Quote(4000)) { Delay = Timeout.InfiniteTimeSpan };

        var result = await UseCase(reader, quotes, quoteTimeout: 100).ExecuteAsync(Query([ValuationGaugeKind.Equilibrium]), CancellationToken.None);

        var card = Assert.Single(result.Cards);
        Assert.Equal(ValuationGaugeCardState.Partial, card.State);
        Assert.Equal(ValuationGaugeReasonCodes.QuoteTimedOut, card.ReasonCode);
        Assert.Equal(ValuationGaugeQuoteState.TimedOut, card.Equilibrium!.QuoteState);
    }

    [Fact]
    public async Task AStaleRangeDoesNotReadTheQuote()
    {
        var reader = new FakeSnapshotReader(new ValuationGaugeSnapshotSet(
            ValuationGaugeMatchOutcome.Matched, "اخابر", "IRO1MKBT0001", Equilibrium: AkhaberRange(Now.AddDays(-4))));
        var quotes = new FakeQuoteReader(Quote(4000));

        var result = await UseCase(reader, quotes).ExecuteAsync(Query([ValuationGaugeKind.Equilibrium]), CancellationToken.None);

        Assert.Equal(ValuationGaugeCardState.Stale, Assert.Single(result.Cards).State);
        Assert.Null(quotes.LastIsin);
    }

    // ---- Executor status (AC-08 billing statuses) ----------------------------------------------------------

    [Fact]
    public async Task ExecutorStatusesFollowUsability()
    {
        var complete = await Execute(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001",
            new ValuationGaugeCatalogFlags(true, true), Pe: SharakPe()), "pe");
        Assert.Equal(CapabilityExecutionStatus.Executed, complete.Status);
        Assert.Equal("Completed", SemanticBillingCompletionStatus.For(complete.Status));

        var partial = await Execute(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001",
            new ValuationGaugeCatalogFlags(true, false), Pe: SharakPe()), "pe,ps");
        Assert.Equal(CapabilityExecutionStatus.Partial, partial.Status);
        Assert.Equal("Completed", SemanticBillingCompletionStatus.For(partial.Status));

        var none = await Execute(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Missing), "pe");
        Assert.Equal(CapabilityExecutionStatus.NoData, none.Status);
        Assert.NotEqual("Completed", SemanticBillingCompletionStatus.For(none.Status));
        Assert.IsType<ValuationGaugeCardsResult>(none.Payload);

        var stale = await Execute(new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001",
            new ValuationGaugeCatalogFlags(true, true), Pe: SharakPe(fetched: Now.AddDays(-3))), "pe");
        Assert.Equal(CapabilityExecutionStatus.NoData, stale.Status);
        Assert.Equal(DialogueOutcomeReasonCodes.DataStaleOrIneligible, stale.ReasonCode);
    }

    [Fact]
    public async Task ExecutorWithoutSymbolAsksForIt()
    {
        var executor = new ValuationGaugeCapabilityExecutor(UseCase(new FakeSnapshotReader(new(ValuationGaugeMatchOutcome.Missing)), new FakeQuoteReader(null)), new NullResolver());
        var result = await executor.ExecuteAsync(Frame(null, "pe"), Context(), CancellationToken.None);
        Assert.Equal(CapabilityExecutionStatus.ClarificationRequired, result.Status);
    }

    // ---- Text and persistence (AC-06) ----------------------------------------------------------------------

    [Fact]
    public async Task TextShowsExactFactsSourceAndGaps()
    {
        var result = await UseCase(new FakeSnapshotReader(new ValuationGaugeSnapshotSet(
                ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001", new ValuationGaugeCatalogFlags(true, false),
                Pe: SharakPe(), Equilibrium: AkhaberRange())), new FakeQuoteReader(null))
            .ExecuteAsync(Query([]), CancellationToken.None);

        var text = ValuationGaugeTextComposer.Compose(result);

        Assert.StartsWith("گیج‌های ارزش‌گذاری شاراک", text);
        Assert.Contains("نسبت فعلی ۶٫۴۲۹۱", text);
        Assert.Contains("میانگین ۵٫۸۸", text);
        Assert.Contains("بین میانگین و حد بالا", text);
        Assert.Contains("۲٬۴۸۸٫۱۵", text);
        Assert.Contains("قیمت بازار در دسترس نیست", text);
        Assert.Contains("منبع: CyclicalWaves", text);
        Assert.Contains("در دسترس نیست: P/S", text);
        Assert.True(text.IndexOf("P/E", StringComparison.Ordinal) < text.IndexOf("بازهٔ قیمت تعادلی:", StringComparison.Ordinal));
    }

    [Fact]
    public void NoUsableCardIsOneOrdinaryMessage()
    {
        var result = new ValuationGaugeCardsResult(1, "شاراک", null, null, [ValuationGaugeKind.Pe],
            [new ValuationGaugeCard(ValuationGaugeKind.Pe, ValuationGaugeCardState.Stale, ValuationGaugeReasonCodes.StaleSnapshot, "CyclicalWavesV2", Now.AddDays(-3))],
            Now, 48);

        var text = ValuationGaugeTextComposer.Compose(result);

        Assert.StartsWith("برای گیج P/E شاراک دادهٔ قابل نمایش وجود ندارد.", text);
        Assert.Contains("دادهٔ منبع قدیمی است", text);
    }

    [Fact]
    public async Task TheResultRoundTripsThroughJsonWithStringEnumsAndExactStrings()
    {
        var result = await UseCase(new FakeSnapshotReader(new ValuationGaugeSnapshotSet(
                ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001", new ValuationGaugeCatalogFlags(true, true),
                Pe: SharakPe(), Equilibrium: AkhaberRange())), new FakeQuoteReader(Quote(4100)))
            .ExecuteAsync(Query([ValuationGaugeKind.Pe, ValuationGaugeKind.Equilibrium]), CancellationToken.None);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(result, options);
        var restored = JsonSerializer.Deserialize<ValuationGaugeCardsResult>(json, options)!;

        Assert.Contains("\"kind\":\"Pe\"", json);
        Assert.Contains("\"current\":\"6.4291\"", json);
        Assert.Contains("\"state\":\"Available\"", json);
        Assert.Equal(ValuationGaugeTextComposer.Compose(result), ValuationGaugeTextComposer.Compose(restored));
        Assert.Equal(result.Cards[0].Geometry!.Needle!.Angle, restored.Cards[0].Geometry!.Needle!.Angle);
    }

    [Fact]
    public void TelegramRendersTheSameTextFromTheTypedResult()
    {
        var result = new ValuationGaugeCardsResult(1, "اخابر", null, "اخابر", [ValuationGaugeKind.Equilibrium],
            [ValuationGaugeProjector.ProjectEquilibrium(AkhaberRange(), Quote(4000), ValuationGaugeQuoteState.Available, Now, MaxAge)],
            Now, 48);
        var response = new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.ValuationGauges,
            null, null, null, null, null, "unused text", false, null, null, ValuationGaugeCards: result);
        var renderer = new TelegramAssistantResponseRenderer(new TelegramMonthlyTrendChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance);

        var text = string.Concat(renderer.Render(response, "fa-IR").Select(message => message.Text)).Replace("\\", string.Empty);

        Assert.Contains("بازهٔ قیمت تعادلی: حد پایین ۲٬۴۸۸٫۱۵", text);
        Assert.Contains("در نیمهٔ پایین بازه", text);
        Assert.DoesNotContain("unused text", text);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private static ValuationGaugeCardsQuery Query(IReadOnlyList<ValuationGaugeKind> kinds) =>
        new(new ValuationGaugeSecurity("شاراک", Guid.NewGuid(), null, "پارس"), kinds);

    private static ValuationGaugeCardsUseCase UseCase(IValuationGaugeSnapshotReader reader, IValuationGaugeQuoteReader quotes,
        int readTimeout = 2000, int quoteTimeout = 1500) =>
        new(reader, quotes, new ValuationGaugeCardsOptions { ReadTimeoutMilliseconds = readTimeout, QuoteTimeoutMilliseconds = quoteTimeout },
            new FixedTime(Now));

    private static Task<CapabilityExecutionResult> Execute(ValuationGaugeSnapshotSet set, string kinds) =>
        new ValuationGaugeCapabilityExecutor(UseCase(new FakeSnapshotReader(set), new FakeQuoteReader(null)), new NullResolver())
            .ExecuteAsync(Frame("شاراک", kinds), Context(), CancellationToken.None);

    private static ValidatedQueryFrame Frame(string? symbol, string kinds)
    {
        var interpretation = ValuationGaugeIntentRules.BuildInterpretation("گیج", new ValuationGaugeRequest([], symbol), 1);
        var slots = new List<ResolvedQuerySlot>
        {
            new(QuerySlotType.MetricSet, kinds, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, ValuationGaugeCapability.Code)
        };
        if (symbol is not null)
            slots.Add(new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, symbol, QueryValueProvenance.UserExplicit, 1m,
                QuerySlotValidationState.Valid, ValuationGaugeCapability.Code,
                CanonicalEntity: new CanonicalQueryEntity(Guid.NewGuid(), symbol, "پارس", "company", "test")));
        return new ValidatedQueryFrame(ValuationGaugeCapability.Code, 1, slots, interpretation);
    }

    private static QueryExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "corr", "fa", Now, IsAgentWorkflowV2: true);

    private sealed class FakeSnapshotReader(ValuationGaugeSnapshotSet set) : IValuationGaugeSnapshotReader
    {
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public async Task<ValuationGaugeSnapshotSet> ReadAsync(ValuationGaugeSecurity security, IReadOnlyCollection<ValuationGaugeKind> kinds, CancellationToken cancellationToken)
        {
            if (Delay != TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return set;
        }
    }

    private sealed class FakeQuoteReader(ValuationGaugeQuote? quote) : IValuationGaugeQuoteReader
    {
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public string? LastIsin { get; private set; }

        public async Task<ValuationGaugeQuote?> ReadAsync(string isin, DateTimeOffset now, CancellationToken cancellationToken)
        {
            LastIsin = isin;
            if (Delay != TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return quote;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullResolver : ICanonicalQueryEntityResolver
    {
        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) =>
            Task.FromResult<EntityResolutionResult>(new EntityResolutionResult.Missing("CompanyOrSymbol"));

        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) =>
            ResolveMentionAsync(text, cancellationToken);

        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            ResolveMentionAsync(null, cancellationToken);

        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([]);
    }
}
