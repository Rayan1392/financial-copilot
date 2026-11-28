using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.ValuationGauges;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Change «valuation-gauge-forward-ratio»: Sapio's forward estimate on the P/E and P/S cards (use case, projection,
/// payload and text; tasks T3 and T4). The estimator itself is covered by <see cref="ForwardRatioEstimatorTests"/>.
/// </summary>
public sealed class ValuationGaugeForwardEstimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // شاراک P/E as stored locally: start 2.106, min 3.348, average 5.88, max 9.696, end 59.61.
    private static RatioGaugeSnapshot Ratio(string close) =>
        new(close, "2.106", "3.348", "5.88", "9.696", "59.61", [120, 340, 410, 260, 150, 20], 3, Now.AddHours(-3));

    private static ValuationGaugeSnapshotSet Set(string peClose = "6.4291", string psClose = "2.5") =>
        new(ValuationGaugeMatchOutcome.Matched, "شاراک", "IRO1PARK0001", new ValuationGaugeCatalogFlags(true, true),
            Pe: Ratio(peClose), Ps: Ratio(psClose));

    /// <summary>
    /// Growth 1.5, forecast sales 2 100, margin 0.1, revenue 2 000 and profit 200 over the last 4 quarters. With P/E
    /// 6.4291 the forward P/E is 6.4291 × 200 / 210 = 6.12; with P/S 2.5 the forward P/S is 2.5 × 2 000 / 2 100 = 2.38.
    /// </summary>
    private static ForwardRatioInputs Inputs(string? industry = "1", decimal netProfit = 50m)
    {
        var lastMonth = new DateOnly(2026, 10, 22);
        var months = Enumerable.Range(0, 16)
            .Select(index => new ForwardMonthlySales(lastMonth.AddMonths(index - 15), index >= 12 ? 150m : 100m)).ToArray();
        var quarters = Enumerable.Range(0, 8)
            .Select(index => new ForwardQuarter(new DateOnly(2026, 9, 22).AddMonths(3 * (index - 7)), 500m, index >= 4 ? 50m : netProfit))
            .ToArray();
        return new ForwardRatioInputs(months, quarters, industry);
    }

    // ---- AC-01: the flag off is today's payload and text ------------------------------------------------------------

    [Fact]
    public async Task WithTheFlagOffThePayloadAndTextAreExactlyToday()
    {
        var reader = new FakeForwardReader(Inputs());
        var today = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);
        var flagOff = await UseCase(Set(), forward: new ValuationGaugeForwardOptions { Enabled = false }, reader).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(JsonSerializer.Serialize(today, Json), JsonSerializer.Serialize(flagOff, Json));
        Assert.DoesNotContain("forward", JsonSerializer.Serialize(flagOff, Json), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ValuationGaugeTextComposer.Compose(today), ValuationGaugeTextComposer.Compose(flagOff));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task WithTheFlagOnButNoReaderRegisteredTheCardsAreToday()
    {
        var today = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);
        var noReader = await UseCase(Set(), forward: Enabled(), reader: null).ExecuteAsync(Query(), CancellationToken.None);
        Assert.Equal(JsonSerializer.Serialize(today, Json), JsonSerializer.Serialize(noReader, Json));
    }

    // ---- AC-02, AC-08: values, needle and pin -----------------------------------------------------------------------

    [Fact]
    public async Task WithTheFlagOnTheRatioCardsCarryTheEstimateOnTheirOwnAxis()
    {
        var observer = new RecordingObserver();
        var reader = new FakeForwardReader(Inputs());
        var result = await UseCase(Set(), Enabled(), reader, observer).ExecuteAsync(Query(), CancellationToken.None);

        var pe = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward;
        Assert.NotNull(pe);
        Assert.Equal("6.12", pe!.Value);
        Assert.Equal(ValuationGaugeNeedleSide.Inside, pe.Side);
        // Between the average 5.88 (90°) and the split point 7.788 (120°).
        Assert.Equal(decimal.Round(90m + (6.12m - 5.88m) / (7.788m - 5.88m) * 30m, 4, MidpointRounding.AwayFromZero), pe.Angle);
        Assert.Equal(ForwardRatioEstimator.MethodVersion, pe.MethodVersion);
        Assert.Equal("1.5000", pe.SalesGrowthFactor);
        Assert.Equal("10.0", pe.MarginPercent);
        Assert.Equal(new DateOnly(2026, 10, 22), pe.LatestSalesMonthEnd);
        Assert.Equal(new DateOnly(2026, 9, 22), pe.LatestQuarterEnd);

        var ps = result.Cards.Single(card => card.Kind == ValuationGaugeKind.Ps).Forward;
        Assert.NotNull(ps);
        Assert.Equal("2.38", ps!.Value);
        Assert.Null(ps.MarginPercent); // the P/S card does not use the margin
        Assert.Equal(1, reader.Calls);  // one read for both cards
        Assert.Equal(new[] { (ValuationGaugeKind.Pe, true, "none"), (ValuationGaugeKind.Ps, true, "none") },
            observer.Forward.Select(call => (call.Kind, call.Shown, call.Reason)));
    }

    [Fact]
    public async Task TheEstimateChangesNothingElseOnTheCard()
    {
        var today = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);
        var withForward = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs())).ExecuteAsync(Query(), CancellationToken.None);

        for (var index = 0; index < today.Cards.Count; index++)
        {
            Assert.NotNull(withForward.Cards[index].Forward);
            Assert.Equal(JsonSerializer.Serialize(today.Cards[index], Json),
                JsonSerializer.Serialize(withForward.Cards[index] with { Forward = null }, Json));
        }
    }

    [Theory]
    [InlineData("100", ValuationGaugeNeedleSide.PinnedAbove, 180)] // forward P/E 95.24 > end 59.61
    [InlineData("1.5", ValuationGaugeNeedleSide.PinnedBelow, 0)]   // forward P/E 1.43 < start 2.106
    public async Task AForwardValueOutsideTheAxisPinsAtTheEnd(string peClose, ValuationGaugeNeedleSide side, int angle)
    {
        var today = await UseCase(Set(peClose), forward: null, reader: null).ExecuteAsync(Query([ValuationGaugeKind.Pe]), CancellationToken.None);
        var result = await UseCase(Set(peClose), Enabled(), new FakeForwardReader(Inputs())).ExecuteAsync(Query([ValuationGaugeKind.Pe]), CancellationToken.None);

        var card = result.Cards.Single();
        Assert.Equal(side, card.Forward!.Side);
        Assert.Equal((decimal)angle, card.Forward.Angle);
        // The solid needle is the same as without the estimate.
        Assert.Equal(JsonSerializer.Serialize(today.Cards.Single().Geometry, Json), JsonSerializer.Serialize(card.Geometry, Json));
    }

    // ---- AC-05: a non-positive margin keeps the forward P/S ---------------------------------------------------------

    [Fact]
    public async Task ANonPositiveMarginHidesOnlyTheForwardPe()
    {
        // Margins -0.3 (×4) and 0.1 (×4): median -0.1, while the last four quarters still earn 200.
        var observer = new RecordingObserver();
        var result = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs(netProfit: -150m)), observer)
            .ExecuteAsync(Query(), CancellationToken.None);

        Assert.Null(result.Cards.Single(card => card.Kind == ValuationGaugeKind.Pe).Forward);
        Assert.Equal("2.38", result.Cards.Single(card => card.Kind == ValuationGaugeKind.Ps).Forward!.Value);
        Assert.Contains((ValuationGaugeKind.Pe, false, ForwardRatioReasonCodes.NonPositiveMargin),
            observer.Forward.Select(call => (call.Kind, call.Shown, call.Reason)));
    }

    // ---- AC-04, AC-06: guards give no estimate and today's card ---------------------------------------------------

    [Fact]
    public async Task AnExcludedIndustryGetsTodaysCards()
    {
        var observer = new RecordingObserver();
        var today = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);
        var result = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs(industry: "31")), observer)
            .ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(JsonSerializer.Serialize(today, Json), JsonSerializer.Serialize(result, Json));
        Assert.All(observer.Forward, call => Assert.Equal((false, ForwardRatioReasonCodes.ExcludedIndustry), (call.Shown, call.Reason)));
    }

    [Fact]
    public async Task AnUnmatchedCompanyGetsTodaysCards()
    {
        var observer = new RecordingObserver();
        var result = await UseCase(Set(), Enabled(), new FakeForwardReader(null), observer).ExecuteAsync(Query(), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.Null(card.Forward));
        Assert.All(observer.Forward, call => Assert.Equal(ForwardRatioReasonCodes.CompanyUnmatched, call.Reason));
    }

    [Fact]
    public async Task ACardWithoutACurrentValueIsNotReadForAnEstimate()
    {
        var observer = new RecordingObserver();
        var reader = new FakeForwardReader(Inputs());
        var set = Set() with { Pe = Ratio("6.4291") with { Close = null }, Ps = null };
        var result = await UseCase(set, Enabled(), reader, observer).ExecuteAsync(Query([ValuationGaugeKind.Pe, ValuationGaugeKind.Ps]), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.Null(card.Forward));
        Assert.Equal(0, reader.Calls);
        Assert.All(observer.Forward, call => Assert.Equal(ForwardRatioReasonCodes.CardNotDisplayable, call.Reason));
    }

    [Fact]
    public async Task AnEquilibriumOnlyRequestNeverReadsTheInputs()
    {
        var reader = new FakeForwardReader(Inputs());
        var observer = new RecordingObserver();
        await UseCase(Set(), Enabled(), reader, observer).ExecuteAsync(Query([ValuationGaugeKind.Equilibrium]), CancellationToken.None);
        Assert.Equal(0, reader.Calls);
        Assert.Empty(observer.Forward);
    }

    // ---- AC-07: read timeout, failure and caller cancellation -------------------------------------------------------

    [Fact]
    public async Task AReadTimeoutDropsOnlyTheEstimate()
    {
        var observer = new RecordingObserver();
        var today = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);
        var reader = new FakeForwardReader(Inputs()) { Delay = Timeout.InfiniteTimeSpan };
        var result = await UseCase(Set(), Enabled(readTimeout: 100), reader, observer).ExecuteAsync(Query(), CancellationToken.None);

        Assert.Equal(JsonSerializer.Serialize(today, Json), JsonSerializer.Serialize(result, Json));
        Assert.Equal(new[] { ForwardRatioReasonCodes.ReadTimedOut }, observer.ReadFailures.Select(call => call.Reason));
        Assert.All(observer.Forward, call => Assert.Equal(ForwardRatioReasonCodes.ReadTimedOut, call.Reason));
    }

    [Fact]
    public async Task AReadFailureDropsOnlyTheEstimateAndIsReportedOnce()
    {
        var observer = new RecordingObserver();
        var failure = new InvalidOperationException("database unavailable");
        var result = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs()) { Failure = failure }, observer)
            .ExecuteAsync(Query(), CancellationToken.None);

        Assert.All(result.Cards, card => Assert.Null(card.Forward));
        var reported = Assert.Single(observer.ReadFailures);
        Assert.Equal(ForwardRatioReasonCodes.ReadFailed, reported.Reason);
        Assert.Same(failure, reported.Error);
    }

    [Fact]
    public async Task CallerCancellationDuringTheInputReadIsRethrown()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new FakeForwardReader(Inputs()) { Delay = Timeout.InfiniteTimeSpan, OnRead = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UseCase(Set(), Enabled(readTimeout: 10_000), reader).ExecuteAsync(Query(), cancellation.Token));
    }

    // ---- AC-09: text and Telegram ----------------------------------------------------------------------------------

    [Fact]
    public async Task TheTextShowsTheForwardLineOnlyWhenTheValueExists()
    {
        var with = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs())).ExecuteAsync(Query(), CancellationToken.None);
        var without = await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None);

        var text = ValuationGaugeTextComposer.Compose(with);
        Assert.Contains("P/E: نسبت فعلی ۶٫۴۳ — بین میانگین و حد بالا\nP/E آینده‌نگر (برآورد ساپیو): ۶٫۱۲\nحد پایین", text.Replace("\r\n", "\n"));
        Assert.Contains("P/S آینده‌نگر (برآورد ساپیو): ۲٫۳۸", text);
        Assert.Contains("منبع: تحلیل‌اپ", text);
        Assert.DoesNotContain("آینده‌نگر", ValuationGaugeTextComposer.Compose(without));
        // Only the estimate lines are added.
        var added = text.Replace("\r\n", "\n").Split('\n').Except(ValuationGaugeTextComposer.Compose(without).Replace("\r\n", "\n").Split('\n'));
        Assert.All(added, line => Assert.Contains("(برآورد ساپیو)", line));
    }

    // ---- AC-10: stored cards ----------------------------------------------------------------------------------------

    [Fact]
    public async Task ACardStoredBeforeThisChangeStillReadsAndRenders()
    {
        var stored = JsonSerializer.Serialize(await UseCase(Set(), forward: null, reader: null).ExecuteAsync(Query(), CancellationToken.None), Json);
        var restored = JsonSerializer.Deserialize<ValuationGaugeCardsResult>(stored, Json)!;

        Assert.All(restored.Cards, card => Assert.Null(card.Forward));
        Assert.Equal(stored, JsonSerializer.Serialize(restored, Json));
    }

    [Fact]
    public async Task ACardWithAnEstimateRoundTripsThroughJson()
    {
        var result = await UseCase(Set(), Enabled(), new FakeForwardReader(Inputs())).ExecuteAsync(Query(), CancellationToken.None);
        var json = JsonSerializer.Serialize(result, Json);
        var restored = JsonSerializer.Deserialize<ValuationGaugeCardsResult>(json, Json)!;

        Assert.Contains("\"forward\":{\"value\":\"6.12\"", json);
        Assert.Contains("\"side\":\"Inside\"", json);
        Assert.Contains("\"latestSalesMonthEnd\":\"2026-10-22\"", json);
        Assert.Equal(ValuationGaugeTextComposer.Compose(result), ValuationGaugeTextComposer.Compose(restored));
        Assert.Equal(result.Cards[0].Forward, restored.Cards[0].Forward);
    }

    // ---- Configuration -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 4, 75, 1000)]
    [InlineData(4, 4, 75, 1000)]
    [InlineData(0.5, 21, 75, 1000)]
    [InlineData(0.5, 4, 0, 1000)]
    [InlineData(0.5, 4, 366, 1000)]
    [InlineData(0.5, 4, 75, 99)]
    [InlineData(0.5, 4, 75, 30001)]
    public void InvalidForwardSettingsFailValidation(double min, double max, int age, int timeout)
    {
        var options = new ValuationGaugeCardsOptions
        {
            ForwardEstimate = new ValuationGaugeForwardOptions
            {
                MinGrowthFactor = (decimal)min, MaxGrowthFactor = (decimal)max, MaxMonthlyReportAgeDays = age, ReadTimeoutMilliseconds = timeout
            }
        };
        Assert.False(options.IsValid(out var error));
        Assert.StartsWith("ForwardEstimate", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultsAreValidAndOff()
    {
        var options = new ValuationGaugeCardsOptions();
        Assert.True(options.IsValid(out _));
        Assert.False(options.ForwardEstimate.Enabled);
        Assert.Equal(new[] { "31" }, options.ForwardEstimate.ExcludedIndustryExternalIds);
        Assert.False(new ValuationGaugeCardsOptions { ForwardEstimate = new ValuationGaugeForwardOptions { ExcludedIndustryExternalIds = [" "] } }.IsValid(out _));
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static ValuationGaugeForwardOptions Enabled(int readTimeout = 1000) => new() { Enabled = true, ReadTimeoutMilliseconds = readTimeout };

    private static ValuationGaugeCardsQuery Query(IReadOnlyList<ValuationGaugeKind>? kinds = null) =>
        new(new ValuationGaugeSecurity("شاراک", Guid.NewGuid(), null, "پارس"), kinds ?? [ValuationGaugeKind.Pe, ValuationGaugeKind.Ps]);

    private static ValuationGaugeCardsUseCase UseCase(
        ValuationGaugeSnapshotSet set,
        ValuationGaugeForwardOptions? forward,
        IForwardRatioInputsReader? reader,
        IValuationGaugeObserver? observer = null) =>
        new(new FakeSnapshotReader(set), new NullQuoteReader(),
            new ValuationGaugeCardsOptions { ForwardEstimate = forward ?? new ValuationGaugeForwardOptions() },
            new FixedTime(Now), observer, reader);

    private sealed class FakeForwardReader(ForwardRatioInputs? inputs) : IForwardRatioInputsReader
    {
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public Exception? Failure { get; init; }
        public Action? OnRead { get; init; }
        public int Calls { get; private set; }

        public async Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity security, string vendorTicker, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("شاراک", vendorTicker);
            OnRead?.Invoke();
            if (Delay != TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            if (Failure is not null) throw Failure;
            return inputs;
        }
    }

    private sealed class RecordingObserver : IValuationGaugeObserver
    {
        public List<(ValuationGaugeKind Kind, bool Shown, string Reason)> Forward { get; } = [];
        public List<(string Reason, Exception? Error)> ReadFailures { get; } = [];
        public void CardProjected(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, TimeSpan? fetchAge) { }
        public void Completed(TimeSpan elapsed, int displayableCards, string matchOutcome) { }
        public void ForwardEstimated(ValuationGaugeKind kind, bool shown, string reasonCode, TimeSpan? readElapsed) => Forward.Add((kind, shown, reasonCode));
        public void ForwardReadFailed(string reasonCode, TimeSpan elapsed, Exception? error) => ReadFailures.Add((reasonCode, error));
        public void ProviderForwardProjected(ValuationGaugeKind kind, bool shown, string reasonCode) { }
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
