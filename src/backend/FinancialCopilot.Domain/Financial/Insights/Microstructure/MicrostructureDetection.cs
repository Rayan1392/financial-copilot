using System.Globalization;

namespace FinancialCopilot.Domain.Financial.Insights.Microstructure;

public enum MicrostructureSignalKind
{
    LargeTrade,
    BuyerPower,
    SellerPower,
    RetailMoneyInflow,
    RetailMoneyOutflow,
    BuyQueueFormation,
    BuyQueueStrengthening,
    BuyQueueWeakening,
    BuyQueueRelease,
    BuyQueueCollection,
    SellQueueFormation,
    SellQueueStrengthening,
    SellQueueWeakening,
    SellQueueRelease,
    SellQueueCollection,
    VolumeAnomaly,
    TradingValueAnomaly
}

public enum MicrostructureTradeSide
{
    Unknown,
    Buy,
    Sell
}

public sealed record MicrostructureEvidence(string Label, string Value);

public sealed record MicrostructureSignal(
    string DetectorCode,
    string DetectorVersion,
    MicrostructureSignalKind Kind,
    decimal MagnitudeScore,
    decimal RarityScore,
    decimal EvidenceCompleteness,
    string Title,
    string Summary,
    string Reason,
    IReadOnlyList<MicrostructureEvidence> Evidence);

public sealed record MicrostructureDetectionOutcome(
    IReadOnlyList<MicrostructureSignal> Signals,
    string? SuppressionReason = null)
{
    public static MicrostructureDetectionOutcome Suppressed(string reason) => new([], reason);
    public static MicrostructureDetectionOutcome Empty() => new([]);
    public static MicrostructureDetectionOutcome One(MicrostructureSignal signal) => new([signal]);
}

/// <summary>
/// Provider-neutral evidence presented to the deterministic detector policies. Nullable fields
/// are deliberate: a detector must suppress a signal when its required canonical evidence is
/// unavailable instead of inferring provider-specific facts.
/// </summary>
public sealed record MarketMicrostructureObservation(
    string ExternalCompanyId,
    string Symbol,
    string? IndustryCode,
    string MarketSegment,
    string ProviderName,
    string InstrumentIdentity,
    string SourceEventIdentity,
    DateOnly TradingDate,
    string Window,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset SourceSyncedAtUtc,
    DateTimeOffset EvaluatedAtUtc,
    bool IsTradingSession,
    bool IsCorrection = false,
    string? SupersedesSourceEventIdentity = null,
    decimal? Volume = null,
    decimal? TradingValue = null,
    decimal? Transactions = null,
    decimal? LargestTradeVolume = null,
    decimal? LargestTradeValue = null,
    MicrostructureTradeSide LargestTradeSide = MicrostructureTradeSide.Unknown,
    decimal? RealBuyVolume = null,
    decimal? RealSellVolume = null,
    decimal? RealBuyValue = null,
    decimal? RealSellValue = null,
    decimal? InstitutionalBuyValue = null,
    decimal? InstitutionalSellValue = null,
    int? RealBuyerCount = null,
    int? RealSellerCount = null,
    decimal? AllowedMinimumPrice = null,
    decimal? AllowedMaximumPrice = null,
    decimal? BuyQueueVolume = null,
    decimal? BuyQueueValue = null,
    decimal? SellQueueVolume = null,
    decimal? SellQueueValue = null,
    int? BuyQueueDurationSeconds = null,
    int? SellQueueDurationSeconds = null,
    decimal? PreviousBuyQueueValue = null,
    decimal? PreviousSellQueueValue = null,
    bool? BuyQueueCollectionConfirmed = null,
    bool? SellQueueCollectionConfirmed = null,
    IReadOnlyList<decimal>? BaselineVolumes = null,
    IReadOnlyList<decimal>? BaselineTradingValues = null);

public sealed record MicrostructureDetectionPolicy(
    string Version,
    string EvidenceSchemaVersion,
    TimeSpan MaximumSourceAge,
    int BaselineLookback,
    int MinimumBaselineObservations,
    decimal LargeTradeAbsoluteValue,
    decimal LargeTradeRelativeToMedian,
    decimal BuyerSellerPowerRatio,
    decimal MoneyFlowAbsoluteValue,
    decimal MoneyFlowRelativeToMedian,
    decimal QueueMinimumValue,
    int QueueMinimumDurationSeconds,
    decimal QueueChangeRatio,
    decimal AnomalyRatio)
{
    public static MicrostructureDetectionPolicy Default { get; } = new(
        Version: "microstructure-v1",
        EvidenceSchemaVersion: "microstructure-evidence-v1",
        MaximumSourceAge: TimeSpan.FromMinutes(15),
        BaselineLookback: 20,
        MinimumBaselineObservations: 10,
        LargeTradeAbsoluteValue: 50_000_000_000m,
        LargeTradeRelativeToMedian: 0.05m,
        BuyerSellerPowerRatio: 1.5m,
        MoneyFlowAbsoluteValue: 20_000_000_000m,
        MoneyFlowRelativeToMedian: 0.02m,
        QueueMinimumValue: 10_000_000_000m,
        QueueMinimumDurationSeconds: 120,
        QueueChangeRatio: 0.20m,
        AnomalyRatio: 2m);
}

public interface IMicrostructureSignalDetector
{
    string DetectorCode { get; }
    string Version { get; }
    MicrostructureDetectionOutcome Detect(MarketMicrostructureObservation observation, MicrostructureDetectionPolicy policy);
}

public abstract class MicrostructureSignalDetectorBase : IMicrostructureSignalDetector
{
    public abstract string DetectorCode { get; }
    public virtual string Version => "1";

    public MicrostructureDetectionOutcome Detect(MarketMicrostructureObservation observation, MicrostructureDetectionPolicy policy)
    {
        if (!observation.IsTradingSession) return MicrostructureDetectionOutcome.Suppressed("outside-trading-session");
        if (observation.EvaluatedAtUtc - observation.SourceSyncedAtUtc > policy.MaximumSourceAge)
            return MicrostructureDetectionOutcome.Suppressed("stale-source");
        return DetectCurrent(observation, policy);
    }

    protected abstract MicrostructureDetectionOutcome DetectCurrent(
        MarketMicrostructureObservation observation,
        MicrostructureDetectionPolicy policy);

    protected MicrostructureSignal Signal(
        MicrostructureSignalKind kind,
        decimal magnitude,
        decimal rarity,
        decimal completeness,
        string title,
        string summary,
        string reason,
        MicrostructureDetectionPolicy policy,
        params MicrostructureEvidence[] evidence) =>
        new(DetectorCode, $"{policy.Version}/{DetectorCode}/{Version}", kind,
            Math.Clamp(magnitude, 0m, 100m), Math.Clamp(rarity, 0m, 100m),
            Math.Clamp(completeness, 0m, 100m), title, summary, reason,
            [new("evidence_schema", policy.EvidenceSchemaVersion), .. evidence]);

    protected static decimal Median(IReadOnlyList<decimal> values)
    {
        var ordered = values.Where(value => value > 0m).Order().ToArray();
        if (ordered.Length == 0) return 0m;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    protected static decimal RatioScore(decimal ratio, decimal threshold) =>
        threshold <= 0m ? 0m : Math.Min(100m, 50m + ((ratio / threshold) - 1m) * 50m);

    protected static string Number(decimal value, string format = "0.####") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    protected static string Number(decimal? value, string format = "0.####") =>
        value.HasValue ? Number(value.Value, format) : "unavailable";
}

public sealed class LargeTradeSignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "LARGE_TRADE";

    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p)
    {
        if (!o.LargestTradeValue.HasValue || o.LargestTradeValue <= 0m)
            return MicrostructureDetectionOutcome.Suppressed("missing-largest-trade-value");
        var baseline = Median(o.BaselineTradingValues ?? []);
        var threshold = Math.Max(p.LargeTradeAbsoluteValue, baseline * p.LargeTradeRelativeToMedian);
        if (o.LargestTradeValue < threshold) return MicrostructureDetectionOutcome.Empty();
        var ratio = threshold == 0m ? 0m : o.LargestTradeValue.Value / threshold;
        return MicrostructureDetectionOutcome.One(Signal(
            MicrostructureSignalKind.LargeTrade, RatioScore(ratio, 1m), Math.Min(100m, ratio * 50m),
            o.LargestTradeSide == MicrostructureTradeSide.Unknown ? 85m : 100m,
            "Large trade detected",
            FormattableString.Invariant($"A {o.LargestTradeSide.ToString().ToLowerInvariant()}-side trade with value {o.LargestTradeValue.Value:0.##} met the governed threshold."),
            "The largest canonical trade value exceeded both the absolute and relative controls.", p,
            new("largest_trade_value", Number(o.LargestTradeValue.Value)),
            new("largest_trade_volume", Number(o.LargestTradeVolume)),
            new("trade_side", o.LargestTradeSide.ToString()), new("threshold", Number(threshold)),
            new("absolute_threshold", Number(p.LargeTradeAbsoluteValue)),
            new("relative_threshold", Number(p.LargeTradeRelativeToMedian)),
            new("baseline_median_value", Number(baseline)),
            new("baseline_observations", (o.BaselineTradingValues?.Count ?? 0).ToString(CultureInfo.InvariantCulture))));
    }
}

public sealed class BuyerSellerPowerSignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "BUYER_SELLER_POWER";

    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p)
    {
        if (!o.RealBuyVolume.HasValue || !o.RealSellVolume.HasValue ||
            o.RealBuyerCount is null or <= 0 || o.RealSellerCount is null or <= 0)
            return MicrostructureDetectionOutcome.Suppressed("incomplete-real-person-count-or-volume");
        var buyAverage = o.RealBuyVolume.Value / o.RealBuyerCount.Value;
        var sellAverage = o.RealSellVolume.Value / o.RealSellerCount.Value;
        if (buyAverage <= 0m || sellAverage <= 0m)
            return MicrostructureDetectionOutcome.Suppressed("zero-buyer-or-seller-denominator");
        var power = buyAverage / sellAverage;
        var sellerThreshold = 1m / p.BuyerSellerPowerRatio;
        if (power < p.BuyerSellerPowerRatio && power > sellerThreshold) return MicrostructureDetectionOutcome.Empty();
        var kind = power >= p.BuyerSellerPowerRatio ? MicrostructureSignalKind.BuyerPower : MicrostructureSignalKind.SellerPower;
        var directionalRatio = kind == MicrostructureSignalKind.BuyerPower ? power : 1m / power;
        return MicrostructureDetectionOutcome.One(Signal(kind, RatioScore(directionalRatio, p.BuyerSellerPowerRatio),
            Math.Min(100m, directionalRatio * 40m), 100m,
            kind == MicrostructureSignalKind.BuyerPower ? "Buyer power detected" : "Seller power detected",
            FormattableString.Invariant($"Real-person average buy-to-sell volume ratio is {power:0.####}."),
            "The ratio crossed the governed buyer/seller power boundary; this is descriptive, not investment advice.", p,
            new("buyer_power_ratio", Number(power)),
            new("buyer_power_threshold", Number(p.BuyerSellerPowerRatio)),
            new("real_buy_average_volume", Number(buyAverage)),
            new("real_sell_average_volume", Number(sellAverage)),
            new("real_buyer_count", o.RealBuyerCount.Value.ToString(CultureInfo.InvariantCulture)),
            new("real_seller_count", o.RealSellerCount.Value.ToString(CultureInfo.InvariantCulture))));
    }
}

public sealed class RealMoneyFlowSignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "REAL_MONEY_FLOW";

    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p)
    {
        if (!o.RealBuyValue.HasValue || !o.RealSellValue.HasValue)
            return MicrostructureDetectionOutcome.Suppressed("incomplete-real-person-buy-or-sell-value");
        var net = o.RealBuyValue.Value - o.RealSellValue.Value;
        var baseline = Median(o.BaselineTradingValues ?? []);
        var threshold = Math.Max(p.MoneyFlowAbsoluteValue, baseline * p.MoneyFlowRelativeToMedian);
        if (Math.Abs(net) < threshold) return MicrostructureDetectionOutcome.Empty();
        var ratio = threshold == 0m ? 0m : Math.Abs(net) / threshold;
        var kind = net > 0m ? MicrostructureSignalKind.RetailMoneyInflow : MicrostructureSignalKind.RetailMoneyOutflow;
        return MicrostructureDetectionOutcome.One(Signal(kind, RatioScore(ratio, 1m), Math.Min(100m, ratio * 45m), 100m,
            net > 0m ? "Retail money inflow detected" : "Retail money outflow detected",
            FormattableString.Invariant($"Real-person net traded value is {net:0.##}."),
            "Canonical real-person buy and sell values crossed the governed net-flow threshold; no smart-money claim is made.", p,
            new("real_buy_value", Number(o.RealBuyValue.Value)),
            new("real_sell_value", Number(o.RealSellValue.Value)),
            new("net_real_money_flow", Number(net)), new("threshold", Number(threshold)),
            new("absolute_threshold", Number(p.MoneyFlowAbsoluteValue)),
            new("relative_threshold", Number(p.MoneyFlowRelativeToMedian)),
            new("baseline_median_value", Number(baseline)),
            new("baseline_observations", (o.BaselineTradingValues?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
            new("institutional_buy_value", Number(o.InstitutionalBuyValue)),
            new("institutional_sell_value", Number(o.InstitutionalSellValue))));
    }
}

public sealed class OrderQueueSignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "ORDER_QUEUE";

    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p)
    {
        if (!o.AllowedMinimumPrice.HasValue || !o.AllowedMaximumPrice.HasValue)
            return MicrostructureDetectionOutcome.Suppressed("missing-allowed-price-bounds");
        var signals = new List<MicrostructureSignal>();
        AddQueueSignal(signals, true, o.BuyQueueValue, o.BuyQueueVolume, o.BuyQueueDurationSeconds,
            o.PreviousBuyQueueValue, o.BuyQueueCollectionConfirmed, p);
        AddQueueSignal(signals, false, o.SellQueueValue, o.SellQueueVolume, o.SellQueueDurationSeconds,
            o.PreviousSellQueueValue, o.SellQueueCollectionConfirmed, p);
        return signals.Count == 0 ? MicrostructureDetectionOutcome.Empty() : new(signals);
    }

    private void AddQueueSignal(List<MicrostructureSignal> signals, bool buy, decimal? value, decimal? volume,
        int? duration, decimal? previous, bool? collectionConfirmed, MicrostructureDetectionPolicy p)
    {
        if (!value.HasValue || !volume.HasValue || !duration.HasValue) return;
        var wasMaterial = previous >= p.QueueMinimumValue;
        var isMaterial = value >= p.QueueMinimumValue && duration >= p.QueueMinimumDurationSeconds;
        MicrostructureSignalKind? kind = null;
        if (wasMaterial && !isMaterial)
            kind = collectionConfirmed == true
                ? (buy ? MicrostructureSignalKind.BuyQueueCollection : MicrostructureSignalKind.SellQueueCollection)
                : (buy ? MicrostructureSignalKind.BuyQueueRelease : MicrostructureSignalKind.SellQueueRelease);
        else if (isMaterial && (!previous.HasValue || previous <= 0m))
            kind = buy ? MicrostructureSignalKind.BuyQueueFormation : MicrostructureSignalKind.SellQueueFormation;
        else if (isMaterial && previous > 0m && value >= previous * (1m + p.QueueChangeRatio))
            kind = buy ? MicrostructureSignalKind.BuyQueueStrengthening : MicrostructureSignalKind.SellQueueStrengthening;
        else if (isMaterial && previous > 0m && value <= previous * (1m - p.QueueChangeRatio))
            kind = buy ? MicrostructureSignalKind.BuyQueueWeakening : MicrostructureSignalKind.SellQueueWeakening;
        if (!kind.HasValue) return;
        var ratio = p.QueueMinimumValue == 0m ? 0m : Math.Max(value.Value, previous ?? 0m) / p.QueueMinimumValue;
        signals.Add(Signal(kind.Value, RatioScore(ratio, 1m), Math.Min(100m, ratio * 40m), 100m,
            $"{(buy ? "Buy" : "Sell")} queue {QueueVerb(kind.Value)}",
            FormattableString.Invariant($"The {(buy ? "buy" : "sell")} queue value is {value.Value:0.##} after {duration.Value} seconds."),
            "Allowed-price queue evidence crossed a governed value, duration, or change boundary.", p,
            new("queue_side", buy ? "Buy" : "Sell"), new("queue_value", Number(value.Value)),
            new("queue_volume", Number(volume.Value)), new("duration_seconds", duration.Value.ToString(CultureInfo.InvariantCulture)),
            new("previous_queue_value", Number(previous)),
            new("collection_confirmed", collectionConfirmed?.ToString() ?? "unavailable"),
            new("minimum_queue_value", Number(p.QueueMinimumValue)),
            new("minimum_duration_seconds", p.QueueMinimumDurationSeconds.ToString(CultureInfo.InvariantCulture)),
            new("hysteresis_ratio", Number(p.QueueChangeRatio))));
    }

    private static string QueueVerb(MicrostructureSignalKind kind) => kind switch
    {
        MicrostructureSignalKind.BuyQueueFormation or MicrostructureSignalKind.SellQueueFormation => "formed",
        MicrostructureSignalKind.BuyQueueStrengthening or MicrostructureSignalKind.SellQueueStrengthening => "strengthened",
        MicrostructureSignalKind.BuyQueueWeakening or MicrostructureSignalKind.SellQueueWeakening => "weakened",
        MicrostructureSignalKind.BuyQueueCollection or MicrostructureSignalKind.SellQueueCollection => "collected",
        _ => "released"
    };
}

public sealed class VolumeAnomalySignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "VOLUME_ANOMALY";
    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p) =>
        DetectAnomaly(o.Volume, o.BaselineVolumes, MicrostructureSignalKind.VolumeAnomaly, "volume", p);

    private MicrostructureDetectionOutcome DetectAnomaly(decimal? current, IReadOnlyList<decimal>? samples,
        MicrostructureSignalKind kind, string label, MicrostructureDetectionPolicy p)
    {
        var usable = (samples ?? []).Where(value => value > 0m).Take(p.BaselineLookback).ToArray();
        if (!current.HasValue) return MicrostructureDetectionOutcome.Suppressed($"missing-current-{label}");
        if (usable.Length < p.MinimumBaselineObservations) return MicrostructureDetectionOutcome.Suppressed("insufficient-baseline");
        var median = Median(usable);
        if (median <= 0m) return MicrostructureDetectionOutcome.Suppressed("zero-baseline-median");
        var ratio = current.Value / median;
        if (ratio < p.AnomalyRatio) return MicrostructureDetectionOutcome.Empty();
        var percentile = usable.Count(value => value <= current.Value) * 100m / usable.Length;
        return MicrostructureDetectionOutcome.One(Signal(kind, RatioScore(ratio, p.AnomalyRatio), percentile, 100m,
            "Trading volume anomaly detected", FormattableString.Invariant($"Current volume is {ratio:0.####} times its historical median."),
            "The current canonical volume crossed the governed robust-median anomaly boundary.", p,
            new("current_volume", Number(current.Value)), new("baseline_median", Number(median)),
            new("ratio", Number(ratio)), new("baseline_observations", usable.Length.ToString(CultureInfo.InvariantCulture)),
            new("baseline_lookback", p.BaselineLookback.ToString(CultureInfo.InvariantCulture)),
            new("minimum_baseline_observations", p.MinimumBaselineObservations.ToString(CultureInfo.InvariantCulture)),
            new("threshold_ratio", Number(p.AnomalyRatio)),
            new("rarity_percentile", Number(percentile))));
    }
}

public sealed class TradingValueAnomalySignalDetector : MicrostructureSignalDetectorBase
{
    public override string DetectorCode => "TRADING_VALUE_ANOMALY";

    protected override MicrostructureDetectionOutcome DetectCurrent(MarketMicrostructureObservation o, MicrostructureDetectionPolicy p)
    {
        var usable = (o.BaselineTradingValues ?? []).Where(value => value > 0m).Take(p.BaselineLookback).ToArray();
        if (!o.TradingValue.HasValue) return MicrostructureDetectionOutcome.Suppressed("missing-current-trading-value");
        if (usable.Length < p.MinimumBaselineObservations) return MicrostructureDetectionOutcome.Suppressed("insufficient-baseline");
        var median = Median(usable);
        if (median <= 0m) return MicrostructureDetectionOutcome.Suppressed("zero-baseline-median");
        var ratio = o.TradingValue.Value / median;
        if (ratio < p.AnomalyRatio) return MicrostructureDetectionOutcome.Empty();
        var percentile = usable.Count(value => value <= o.TradingValue.Value) * 100m / usable.Length;
        return MicrostructureDetectionOutcome.One(Signal(MicrostructureSignalKind.TradingValueAnomaly,
            RatioScore(ratio, p.AnomalyRatio), percentile, 100m,
            "Trading value anomaly detected", FormattableString.Invariant($"Current trading value is {ratio:0.####} times its historical median."),
            "The current canonical traded value crossed the governed robust-median anomaly boundary.", p,
            new("current_trading_value", Number(o.TradingValue.Value)),
            new("baseline_median", Number(median)), new("ratio", Number(ratio)),
            new("baseline_observations", usable.Length.ToString(CultureInfo.InvariantCulture)),
            new("baseline_lookback", p.BaselineLookback.ToString(CultureInfo.InvariantCulture)),
            new("minimum_baseline_observations", p.MinimumBaselineObservations.ToString(CultureInfo.InvariantCulture)),
            new("threshold_ratio", Number(p.AnomalyRatio)), new("rarity_percentile", Number(percentile))));
    }
}
