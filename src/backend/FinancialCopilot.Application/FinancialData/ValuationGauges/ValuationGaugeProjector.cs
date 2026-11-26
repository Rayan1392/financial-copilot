using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

/// <summary>
/// Pure, deterministic projection of Feature 148 snapshots into gauge cards. Ratio dials put the provider
/// <c>start</c>, <c>min</c>, <c>avg</c>, <c>max</c> and <c>end</c> at 0°, 30°, 90°, 150° and 180°, so the real
/// average sits at the top even when it is not midway between min and max; the six day-count sectors are equal
/// 30° slices. Equilibrium dials put lower, midpoint and upper at 0°, 90° and 180°. Nothing is invented: a missing
/// current value means no needle, and an axis that is not strictly increasing falls back to text.
/// </summary>
public static class ValuationGaugeProjector
{
    public const string Source = "CyclicalWavesV2";

    /// <summary>Provider timestamps this far in the future are treated as untrustworthy (stale).</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    private static readonly decimal[] RatioAnchorAngles = [0m, 30m, 60m, 90m, 120m, 150m, 180m];
    private static readonly decimal[] EquilibriumAnchorAngles = [0m, 90m, 180m];

    public static bool IsStale(DateTimeOffset? fetchedAtUtc, DateTimeOffset now, TimeSpan maxAge) =>
        fetchedAtUtc is not { } fetched || now - fetched > maxAge || fetched - now > FutureTolerance;

    public static ValuationGaugeCard Unavailable(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, DateTimeOffset? fetchedAtUtc = null) =>
        new(kind, state, reasonCode, Source, fetchedAtUtc);

    public static ValuationGaugeCard ProjectRatio(
        ValuationGaugeKind kind,
        RatioGaugeSnapshot snapshot,
        DateTimeOffset now,
        TimeSpan maxAge,
        int percentageDecimals)
    {
        if (kind == ValuationGaugeKind.Equilibrium) throw new ArgumentOutOfRangeException(nameof(kind));
        if (snapshot.DayCounts.Count != 6 || snapshot.DayCounts.Any(count => count < 0))
            return Unavailable(kind, ValuationGaugeCardState.NoData, ValuationGaugeReasonCodes.SnapshotMissing, snapshot.FetchedAtUtc);
        if (IsStale(snapshot.FetchedAtUtc, now, maxAge))
            return Unavailable(kind, ValuationGaugeCardState.Stale, ValuationGaugeReasonCodes.StaleSnapshot, snapshot.FetchedAtUtc);

        var total = snapshot.DayCounts.Sum();
        var shares = total > 0 ? LargestRemainderPercentages.Calculate(snapshot.DayCounts, percentageDecimals) : null;
        IReadOnlyList<string>? percentages = shares?
            .Select(value => value.ToString("F" + percentageDecimals, CultureInfo.InvariantCulture)).ToArray();

        var start = Parse(snapshot.Start);
        var min = Parse(snapshot.Min);
        var average = Parse(snapshot.Average);
        var max = Parse(snapshot.Max);
        var end = Parse(snapshot.End);
        var current = Parse(snapshot.Close);
        var renderable = start is { } s && min is { } mn && average is { } av && max is { } mx && end is { } e &&
                         s < mn && mn < av && av < mx && mx < e;

        ValuationGaugeNeedleSide? side = current is { } value && start is { } lowEdge && end is { } highEdge
            ? value < lowEdge ? ValuationGaugeNeedleSide.PinnedBelow
            : value > highEdge ? ValuationGaugeNeedleSide.PinnedAbove
            : ValuationGaugeNeedleSide.Inside
            : null;

        ValuationGaugeGeometry? geometry = null;
        if (renderable)
        {
            var anchors = new[] { start!.Value, min!.Value, (min.Value + average!.Value) / 2m, average.Value, (average.Value + max!.Value) / 2m, max.Value, end!.Value };
            // Provider strings for the five provider anchors; the two split points are display geometry.
            var edges = new[] { snapshot.Start, snapshot.Min, Format(anchors[2]), snapshot.Average, Format(anchors[4]), snapshot.Max, snapshot.End };
            var sectors = Enumerable.Range(0, 6).Select(order => new ValuationGaugeSector(
                order,
                order * 30m,
                (order + 1) * 30m,
                snapshot.DayCounts[order],
                percentages?[order],
                edges[order],
                edges[order + 1])).ToArray();
            var markers = new[]
            {
                new ValuationGaugeMarker("start", 0m, snapshot.Start),
                new ValuationGaugeMarker("min", 30m, snapshot.Min),
                new ValuationGaugeMarker("average", 90m, snapshot.Average),
                new ValuationGaugeMarker("max", 150m, snapshot.Max),
                new ValuationGaugeMarker("end", 180m, snapshot.End)
            };
            var needle = current is { } needleValue && snapshot.Close is { } closeText
                ? new ValuationGaugeNeedle(Angle(needleValue, anchors, RatioAnchorAngles), closeText, side!.Value)
                : null;
            geometry = new ValuationGaugeGeometry(sectors, markers, needle);
        }

        var facts = new ValuationRatioGaugeFacts(
            snapshot.Close,
            snapshot.Start,
            snapshot.Min,
            snapshot.Average,
            snapshot.Max,
            snapshot.End,
            snapshot.DayCounts,
            percentages,
            snapshot.ProviderStatus,
            PositionFor(snapshot.ProviderStatus),
            side);

        var (state, reason) = !renderable
            ? (ValuationGaugeCardState.Partial, ValuationGaugeReasonCodes.AxisNotRenderable)
            : snapshot.Close is null || current is null
                ? (ValuationGaugeCardState.Partial, ValuationGaugeReasonCodes.NoCurrentValue)
                : percentages is null
                    ? (ValuationGaugeCardState.Partial, ValuationGaugeReasonCodes.NoDistribution)
                    : (ValuationGaugeCardState.Available, ValuationGaugeReasonCodes.None);
        return new ValuationGaugeCard(kind, state, reason, Source, snapshot.FetchedAtUtc, Ratio: facts, Geometry: geometry,
            Insights: RatioInsights(current, average, geometry?.Needle, shares, percentageDecimals));
    }

    /// <summary>
    /// Distance to the average needs a current value and a positive average. The band figures need an inside needle
    /// and a day distribution: a pinned needle has no band, and "below today's band" is the only honest claim because
    /// days inside one band are not ordered.
    /// </summary>
    private static ValuationGaugeInsights? RatioInsights(
        decimal? current,
        decimal? average,
        ValuationGaugeNeedle? needle,
        IReadOnlyList<decimal>? shares,
        int percentageDecimals)
    {
        var distance = current is { } value && average is > 0m and var avg ? Percent((value - avg) / avg) : null;
        int? band = needle is { Side: ValuationGaugeNeedleSide.Inside } inside && shares is not null
            ? Math.Min(6, (int)Math.Floor(inside.Angle / 30m) + 1)
            : null;
        string? below = null, within = null;
        if (band is { } order)
        {
            below = shares!.Take(order - 1).Sum().ToString("F" + percentageDecimals, CultureInfo.InvariantCulture);
            within = shares![order - 1].ToString("F" + percentageDecimals, CultureInfo.InvariantCulture);
        }

        return distance is null && band is null ? null : new ValuationGaugeInsights(distance, band, below, within);
    }

    /// <summary>A ratio as a percentage with one decimal, rounded half away from zero.</summary>
    private static string Percent(decimal ratio) =>
        decimal.Round(ratio * 100m, 1, MidpointRounding.AwayFromZero).ToString("F1", CultureInfo.InvariantCulture);

    public static ValuationGaugeCard ProjectEquilibrium(
        EquilibriumRangeSnapshot? snapshot,
        ValuationGaugeQuote? quote,
        ValuationGaugeQuoteState quoteState,
        DateTimeOffset now,
        TimeSpan maxAge)
    {
        const ValuationGaugeKind kind = ValuationGaugeKind.Equilibrium;
        if (snapshot is null)
            return Unavailable(kind, ValuationGaugeCardState.NoData, ValuationGaugeReasonCodes.SnapshotMissing);
        if (!snapshot.HasRange || snapshot.Lower is null || snapshot.Midpoint is null || snapshot.Upper is null)
            return Unavailable(kind, ValuationGaugeCardState.NoData, ValuationGaugeReasonCodes.RangeMissing, snapshot.FetchedAtUtc);
        if (IsStale(snapshot.FetchedAtUtc, now, maxAge))
            return Unavailable(kind, ValuationGaugeCardState.Stale, ValuationGaugeReasonCodes.StaleSnapshot, snapshot.FetchedAtUtc);

        var lower = Parse(snapshot.Lower);
        var midpoint = Parse(snapshot.Midpoint);
        var upper = Parse(snapshot.Upper);
        var renderable = lower is { } lo && midpoint is { } mid && upper is { } up && lo < mid && mid < up;
        if (quote is null && quoteState == ValuationGaugeQuoteState.Available)
            quoteState = ValuationGaugeQuoteState.Unavailable;

        ValuationEquilibriumPosition? position = null;
        ValuationGaugeNeedleSide? side = null;
        if (quote is not null && lower is { } l && midpoint is { } m && upper is { } u && l <= m && m <= u)
        {
            var price = quote.Price;
            position = price < l ? ValuationEquilibriumPosition.BelowRange
                : price > u ? ValuationEquilibriumPosition.AboveRange
                : price < m ? ValuationEquilibriumPosition.LowerHalf
                : price > m ? ValuationEquilibriumPosition.UpperHalf
                : ValuationEquilibriumPosition.AtMidpoint;
            side = price < l ? ValuationGaugeNeedleSide.PinnedBelow
                : price > u ? ValuationGaugeNeedleSide.PinnedAbove
                : ValuationGaugeNeedleSide.Inside;
        }

        var quoteFacts = quote is null
            ? null
            : new ValuationGaugeQuoteFacts(Format(quote.Price), quote.ProviderName, quote.SourceLabel, quote.AsOf, quote.TradingDate);

        ValuationGaugeGeometry? geometry = null;
        if (renderable)
        {
            var anchors = new[] { lower!.Value, midpoint!.Value, upper!.Value };
            geometry = new ValuationGaugeGeometry(
                [new ValuationGaugeSector(0, 0m, 90m, null, null), new ValuationGaugeSector(1, 90m, 180m, null, null)],
                [
                    new ValuationGaugeMarker("lower", 0m, snapshot.Lower),
                    new ValuationGaugeMarker("midpoint", 90m, snapshot.Midpoint),
                    new ValuationGaugeMarker("upper", 180m, snapshot.Upper)
                ],
                quote is null ? null : new ValuationGaugeNeedle(Angle(quote.Price, anchors, EquilibriumAnchorAngles), quoteFacts!.Price, side!.Value));
        }

        var facts = new ValuationEquilibriumFacts(snapshot.Lower, snapshot.Midpoint, snapshot.Upper, quoteState, quoteFacts, position);
        var (state, reason) = !renderable
            ? (ValuationGaugeCardState.Partial, ValuationGaugeReasonCodes.AxisNotRenderable)
            : quote is null
                ? (ValuationGaugeCardState.Partial, quoteState == ValuationGaugeQuoteState.TimedOut
                    ? ValuationGaugeReasonCodes.QuoteTimedOut
                    : ValuationGaugeReasonCodes.QuoteUnavailable)
                : (ValuationGaugeCardState.Available, ValuationGaugeReasonCodes.None);
        ValuationGaugeInsights? insights = null;
        if (quote is not null)
        {
            var vsMid = midpoint is { } m0 && m0 != 0m ? Percent((quote.Price - m0) / Math.Abs(m0)) : null;
            var vsLow = lower is { } l0 && l0 != 0m ? Percent((quote.Price - l0) / Math.Abs(l0)) : null;
            if (vsMid is not null || vsLow is not null)
                insights = new ValuationGaugeInsights(PriceVsMidpointPercent: vsMid, PriceVsLowerPercent: vsLow);
        }

        return new ValuationGaugeCard(kind, state, reason, Source, snapshot.FetchedAtUtc, Equilibrium: facts, Geometry: geometry, Insights: insights);
    }

    public static ValuationRatioPosition PositionFor(int? providerStatus) => providerStatus switch
    {
        1 => ValuationRatioPosition.BelowMin,
        2 => ValuationRatioPosition.MinToAverage,
        3 => ValuationRatioPosition.AverageToMax,
        4 => ValuationRatioPosition.AboveMax,
        _ => ValuationRatioPosition.Unranked
    };

    /// <summary>Piecewise-linear angle between strictly increasing anchors, pinned to the ends outside the axis.</summary>
    private static decimal Angle(decimal value, IReadOnlyList<decimal> anchors, IReadOnlyList<decimal> angles)
    {
        if (value <= anchors[0]) return angles[0];
        if (value >= anchors[^1]) return angles[^1];
        for (var index = 1; index < anchors.Count; index++)
        {
            if (value > anchors[index]) continue;
            var fraction = (value - anchors[index - 1]) / (anchors[index] - anchors[index - 1]);
            var angle = angles[index - 1] + fraction * (angles[index] - angles[index - 1]);
            return decimal.Round(Math.Clamp(angle, angles[0], angles[^1]), 4, MidpointRounding.AwayFromZero);
        }

        return angles[^1];
    }

    /// <summary>Exact provider decimal to <see cref="decimal"/>; null when absent or not representable.</summary>
    private static decimal? Parse(string? value) =>
        value is not null && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
