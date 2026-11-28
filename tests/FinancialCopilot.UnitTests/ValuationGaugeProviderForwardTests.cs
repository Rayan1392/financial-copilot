using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «valuation-gauge-provider-forward»: the provider's own forward ratio on the P/E and P/S cards, its priority
/// over Sapio's estimate, the kill switch, the payload and the text.
/// </summary>
public sealed class ValuationGaugeProviderForwardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Axis: start 2.106, min 3.348, average 5.88, max 9.696, end 59.61.
    private static RatioGaugeSnapshot Ratio(string? close, string? forward) =>
        new(close, "2.106", "3.348", "5.88", "9.696", "59.61", [120, 340, 410, 260, 150, 20], 3, Now.AddHours(-3), forward);

    private static ValuationGaugeSnapshotSet Set(string? peForward, string? psForward, string? peClose = "6.4291") =>
        new(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001", new ValuationGaugeCatalogFlags(true, true),
            Pe: Ratio(peClose, peForward), Ps: Ratio("2.5", psForward));

    [Fact]
    public async Task TheRatioCardsCarryTheProviderValueOnTheirOwnAxis()
    {
        var observer = new RecordingObserver();
        var result = await UseCase(Set("3.8198454018903374", "4.614"), observer: observer).ExecuteAsync(Query(), CancellationToken.None);

        var pe = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward!;
        Assert.Equal("3.82", pe.Value);
        Assert.Equal(ValuationGaugeNeedleSide.Inside, pe.Side);
        // Between min 3.348 (30°) and the split point 4.614 (60°).
        Assert.Equal(decimal.Round(30m + (3.82m - 3.348m) / (4.614m - 3.348m) * 30m, 4, MidpointRounding.AwayFromZero), pe.Angle);
        Assert.True(pe.IsProviderValue());
        Assert.Equal((null, null, null, null, null),
            (pe.MethodVersion, pe.SalesGrowthFactor, pe.MarginPercent, pe.LatestSalesMonthEnd, pe.LatestQuarterEnd));

        var ps = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Ps).Forward!;
        Assert.Equal(("4.61", 60m), (ps.Value, decimal.Round(ps.Angle, 0)));
        Assert.Equal(new[] { (ValuationGaugeKind.Pe, true, "none"), (ValuationGaugeKind.Ps, true, "none") }, observer.Provider);
    }

    [Fact]
    public async Task TheProviderValueChangesNothingElseOnTheCard()
    {
        var without = await UseCase(Set(null, null)).ExecuteAsync(Query(), CancellationToken.None);
        var with = await UseCase(Set("3.82", "0.86")).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(
            JsonSerializer.Serialize(without, Json),
            JsonSerializer.Serialize(with with { Cards = with.Cards.Select(card => card with { Forward = null }).ToArray() }, Json));
    }

    [Theory]
    [InlineData("1.5", ValuationGaugeNeedleSide.PinnedBelow, 0)]
    [InlineData("75", ValuationGaugeNeedleSide.PinnedAbove, 180)]
    public async Task AProviderValueOutsideTheAxisPinsAtTheEnd(string forward, ValuationGaugeNeedleSide side, int angle)
    {
        var result = await UseCase(Set(forward, null)).ExecuteAsync(Query(), CancellationToken.None);
        var card = result.Cards.Single(item => item.Kind == ValuationGaugeKind.Pe);

        Assert.Equal((side, (decimal)angle), (card.Forward!.Side, card.Forward.Angle));
        Assert.Equal(ValuationGaugeNeedleSide.Inside, card.Geometry!.Needle!.Side); // the solid needle is unaffected
    }

    [Theory]
    [InlineData(null, "provider_forward_missing")]
    [InlineData(" ", "provider_forward_missing")]
    [InlineData("0", "provider_forward_not_positive")]
    [InlineData("-4.2", "provider_forward_not_positive")]
    [InlineData("123456789012345678901234567890123456789", "provider_forward_out_of_range")]
    public async Task AMissingNonPositiveOrUnusableProviderValueIsNotDrawn(string? forward, string reason)
    {
        var observer = new RecordingObserver();
        var result = await UseCase(Set(forward, "0.86"), observer: observer).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Null(result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward);
        Assert.NotNull(result.Cards.Single(card => card.Kind == ValuationGaugeKind.Ps).Forward);
        Assert.Contains((ValuationGaugeKind.Pe, false, reason), observer.Provider);
    }

    [Fact]
    public async Task ACardWithoutACurrentValueStillShowsTheProviderValue()
    {
        // A loss-making company: no current P/E, but the provider has a forward one.
        var result = await UseCase(Set("7.5", null, peClose: null)).ExecuteAsync(Query(), CancellationToken.None);
        var card = result.Cards.Single(item => item.Kind == ValuationGaugeKind.Pe);

        Assert.Null(card.Ratio!.Current);
        Assert.Equal("7.50", card.Forward!.Value);
    }

    [Fact]
    public async Task AStaleSnapshotShowsNoProviderValue()
    {
        var stale = Ratio("6.4291", "3.82") with { FetchedAtUtc = Now.AddHours(-49) };
        var set = new ValuationGaugeSnapshotSet(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001",
            new ValuationGaugeCatalogFlags(true, true), Pe: stale, Ps: stale);
        var observer = new RecordingObserver();

        var result = await UseCase(set, observer: observer).ExecuteAsync(Query(), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.Null(card.Forward));
        Assert.All(observer.Provider, call => Assert.Equal("card_not_displayable", call.Reason));
    }

    [Fact]
    public async Task WithTheKillSwitchOffTheCardsAreAsBeforeTheProviderValue()
    {
        var observer = new RecordingObserver();
        var off = await UseCase(Set("3.82", "0.86"), providerEnabled: false, observer: observer).ExecuteAsync(Query(), CancellationToken.None);
        var none = await UseCase(Set(null, null)).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(JsonSerializer.Serialize(none, Json), JsonSerializer.Serialize(off, Json));
        Assert.DoesNotContain("آینده‌نگر", ValuationGaugeTextComposer.Compose(off));
        Assert.Empty(observer.Provider);
    }

    // ---- Source order: provider value first, Sapio's estimate only fills in ----------------------------------------

    [Fact]
    public async Task TheProviderValueWinsAndNoEstimateInputIsReadWhenEveryRatioCardHasOne()
    {
        var reader = new FakeForwardReader(Inputs());
        var observer = new RecordingObserver();
        var result = await UseCase(Set("3.82", "0.86"), estimate: Estimate(), reader: reader, observer: observer)
            .ExecuteAsync(Query(), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.True(card.Forward!.IsProviderValue()));
        Assert.Equal(0, reader.Calls);
        Assert.Empty(observer.Estimates);
    }

    [Fact]
    public async Task TheSapioEstimateFillsInOnlyForTheCardWithoutAProviderValue()
    {
        var reader = new FakeForwardReader(Inputs());
        var observer = new RecordingObserver();
        var result = await UseCase(Set(null, "0.86"), estimate: Estimate(), reader: reader, observer: observer)
            .ExecuteAsync(Query(), CancellationToken.None);

        var pe = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward!;
        var ps = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Ps).Forward!;
        Assert.Equal(("6.12", ValuationGaugeForwardSources.Sapio, false), (pe.Value, pe.Source, pe.IsProviderValue()));
        Assert.Equal(("0.86", true), (ps.Value, ps.IsProviderValue()));
        Assert.Equal(1, reader.Calls);
        Assert.Equal(new[] { (ValuationGaugeKind.Pe, true, "none") }, observer.Estimates);
    }

    [Fact]
    public async Task WithTheEstimateFlagOffACardWithoutAProviderValueShowsNoForwardValue()
    {
        var reader = new FakeForwardReader(Inputs());
        var result = await UseCase(Set(null, "0.86"), reader: reader).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Null(result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward);
        Assert.Equal(0, reader.Calls);
    }

    // ---- Payload and text -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheProviderValueRoundTripsThroughJsonWithItsSourceAndWithoutMethodMembers()
    {
        var result = await UseCase(Set("3.82", null)).ExecuteAsync(Query(), CancellationToken.None);
        var json = JsonSerializer.Serialize(result, Json);
        var restored = JsonSerializer.Deserialize<ValuationGaugeCardsResult>(json, Json)!;

        Assert.Contains("\"forward\":{\"value\":\"3.82\"", json);
        Assert.Contains("\"source\":\"provider\"", json);
        Assert.DoesNotContain("methodVersion", json);
        Assert.DoesNotContain("salesGrowthFactor", json);
        Assert.DoesNotContain("latestSalesMonthEnd", json);
        Assert.Equal(result.Cards[0].Forward, restored.Cards[0].Forward);
        Assert.Equal(ValuationGaugeTextComposer.Compose(result), ValuationGaugeTextComposer.Compose(restored));
    }

    [Fact]
    public void AStoredForwardValueWithoutASourceIsASapioEstimate()
    {
        // The payload written while only the estimate existed.
        const string stored = """{"value":"6.12","angle":93.7736,"side":"Inside","methodVersion":"sapio-1","salesGrowthFactor":"1.5000","marginPercent":"10.0","latestSalesMonthEnd":"2026-10-22","latestQuarterEnd":"2026-09-22"}""";

        var forward = JsonSerializer.Deserialize<ValuationGaugeForwardEstimate>(stored, Json)!;

        Assert.Null(forward.Source);
        Assert.False(forward.IsProviderValue());
        Assert.Equal(("sapio-1", new DateOnly(2026, 10, 22)), (forward.MethodVersion, forward.LatestSalesMonthEnd));
    }

    [Fact]
    public async Task TheTextNamesTheProviderValueWithoutAQualifierAndTheEstimateAsSapios()
    {
        var result = await UseCase(Set(null, "0.86"), estimate: Estimate(), reader: new FakeForwardReader(Inputs()))
            .ExecuteAsync(Query(), CancellationToken.None);
        var text = ValuationGaugeTextComposer.Compose(result);

        Assert.Contains("P/S آینده‌نگر: ۰٫۸۶", text);
        Assert.Contains("P/E آینده‌نگر (برآورد ساپیو): ۶٫۱۲", text);
        Assert.DoesNotContain("P/S آینده‌نگر (برآورد ساپیو)", text);
    }

    [Fact]
    public void TheKillSwitchIsOnByDefault()
    {
        var options = new ValuationGaugeCardsOptions();

        Assert.True(options.IsValid(out _));
        Assert.True(options.ProviderForward.Enabled);
        Assert.False(options.ForwardEstimate.Enabled);
        Assert.False(new ValuationGaugeCardsOptions { ProviderForward = null! }.IsValid(out var error));
        Assert.StartsWith("ProviderForward", error, StringComparison.Ordinal);
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------------

    private static ValuationGaugeForwardOptions Estimate() => new() { Enabled = true };

    /// <summary>Growth 1.5, margin 0.1: with P/E 6.4291 the estimate is 6.12 (see ValuationGaugeForwardEstimateTests).</summary>
    private static ForwardRatioInputs Inputs()
    {
        var lastMonth = new DateOnly(2026, 10, 22);
        var months = Enumerable.Range(0, 16)
            .Select(index => new ForwardMonthlySales(lastMonth.AddMonths(index - 15), index >= 12 ? 150m : 100m)).ToArray();
        var quarters = Enumerable.Range(0, 8)
            .Select(index => new ForwardQuarter(new DateOnly(2026, 9, 22).AddMonths(3 * (index - 7)), 500m, 50m)).ToArray();
        return new ForwardRatioInputs(months, quarters, "1");
    }

    private static ValuationGaugeCardsQuery Query() =>
        new(new ValuationGaugeSecurity("شاراک", Guid.NewGuid(), null, "پارس"), [ValuationGaugeKind.Pe, ValuationGaugeKind.Ps]);

    private static ValuationGaugeCardsUseCase UseCase(
        ValuationGaugeSnapshotSet set,
        bool providerEnabled = true,
        ValuationGaugeForwardOptions? estimate = null,
        IForwardRatioInputsReader? reader = null,
        IValuationGaugeObserver? observer = null) =>
        new(new FakeSnapshotReader(set), new NullQuoteReader(),
            new ValuationGaugeCardsOptions
            {
                ProviderForward = new ValuationGaugeProviderForwardOptions { Enabled = providerEnabled },
                ForwardEstimate = estimate ?? new ValuationGaugeForwardOptions()
            },
            new FixedTime(Now), observer, reader);

    private sealed class FakeForwardReader(ForwardRatioInputs? inputs) : IForwardRatioInputsReader
    {
        public int Calls { get; private set; }

        public Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity security, string vendorTicker, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(inputs);
        }
    }

    private sealed class RecordingObserver : IValuationGaugeObserver
    {
        public List<(ValuationGaugeKind Kind, bool Shown, string Reason)> Provider { get; } = [];
        public List<(ValuationGaugeKind Kind, bool Shown, string Reason)> Estimates { get; } = [];
        public void CardProjected(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, TimeSpan? fetchAge) { }
        public void Completed(TimeSpan elapsed, int displayableCards, string matchOutcome) { }
        public void ForwardEstimated(ValuationGaugeKind kind, bool shown, string reasonCode, TimeSpan? readElapsed) => Estimates.Add((kind, shown, reasonCode));
        public void ForwardReadFailed(string reasonCode, TimeSpan elapsed, Exception? error) { }
        public void ProviderForwardProjected(ValuationGaugeKind kind, bool shown, string reasonCode) => Provider.Add((kind, shown, reasonCode));
    }

    private sealed class FakeSnapshotReader(ValuationGaugeSnapshotSet set) : IValuationGaugeSnapshotReader
    {
        public Task<ValuationGaugeSnapshotSet> ReadAsync(ValuationGaugeSecurity security, IReadOnlyCollection<ValuationGaugeKind> kinds, CancellationToken cancellationToken) =>
            Task.FromResult(set);
    }

    private sealed class NullQuoteReader : IValuationGaugeQuoteReader
    {
        public Task<ValuationGaugeQuote?> ReadAsync(string isin, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<ValuationGaugeQuote?>(null);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
