using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.ValuationGauges;

// Standalone valuation gauge cards (Feature 159 change). The result below is persisted in the assistant
// message payload and replayed from history, so its JSON shape is a compatibility contract: add optional
// members only, keep enum names, and keep provider facts as exact strings.

/// <summary>The gauge kinds, in the fixed presentation order P/E, P/S, equilibrium.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValuationGaugeKind>))]
public enum ValuationGaugeKind { Pe, Ps, Equilibrium }

[JsonConverter(typeof(JsonStringEnumConverter<ValuationGaugeCardState>))]
public enum ValuationGaugeCardState
{
    /// <summary>All facts needed for the card are present.</summary>
    Available,
    /// <summary>Usable facts with a documented gap (no current value, no distribution, text-only axis, range without quote).</summary>
    Partial,
    /// <summary>No snapshot, no vendor match or not in the vendor catalog.</summary>
    NoData,
    /// <summary>A snapshot exists but is older than the configured fetch age; values are suppressed.</summary>
    Stale,
    /// <summary>The read exceeded its time budget.</summary>
    TemporarilyUnavailable
}

/// <summary>Provider status bucket for the current ratio; <see cref="Unranked"/> when the provider gave none.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValuationRatioPosition>))]
public enum ValuationRatioPosition { BelowMin, MinToAverage, AverageToMax, AboveMax, Unranked }

[JsonConverter(typeof(JsonStringEnumConverter<ValuationEquilibriumPosition>))]
public enum ValuationEquilibriumPosition { BelowRange, LowerHalf, AtMidpoint, UpperHalf, AboveRange }

[JsonConverter(typeof(JsonStringEnumConverter<ValuationGaugeNeedleSide>))]
public enum ValuationGaugeNeedleSide { Inside, PinnedBelow, PinnedAbove }

[JsonConverter(typeof(JsonStringEnumConverter<ValuationGaugeQuoteState>))]
public enum ValuationGaugeQuoteState { Available, Unavailable, TimedOut }

public static class ValuationGaugeReasonCodes
{
    public const string None = "none";
    public const string CoverageMiss = "coverage_miss";
    public const string AmbiguousMatch = "ambiguous_match";
    public const string NotInCatalog = "not_in_catalog";
    public const string SnapshotMissing = "snapshot_missing";
    public const string StaleSnapshot = "stale_snapshot";
    public const string NoCurrentValue = "no_current_value";
    public const string NoDistribution = "no_distribution";
    public const string AxisNotRenderable = "axis_not_renderable";
    public const string RangeMissing = "range_missing";
    public const string QuoteUnavailable = "quote_unavailable";
    public const string QuoteTimedOut = "quote_timed_out";
    public const string ReadTimedOut = "read_timed_out";
}

/// <summary>Identity of the requested security as resolved by the conversation (canonical id and/or vendor id).</summary>
public sealed record ValuationGaugeSecurity(
    string DisplaySymbol,
    Guid? CanonicalId,
    string? ExternalCompanyId,
    string? CompanyName);

public sealed record ValuationGaugeCardsQuery(
    ValuationGaugeSecurity Security,
    IReadOnlyList<ValuationGaugeKind> Kinds);

public sealed record ValuationGaugeCardsResult(
    int Version,
    string Symbol,
    string? CompanyName,
    string? VendorTicker,
    IReadOnlyList<ValuationGaugeKind> RequestedKinds,
    IReadOnlyList<ValuationGaugeCard> Cards,
    DateTimeOffset GeneratedAtUtc,
    int MaxFetchAgeHours)
{
    public const int CurrentVersion = 1;

    public bool HasDisplayableData() => Cards.Any(card => card.IsDisplayable());

    public bool IsComplete() => Cards.Count > 0 && Cards.All(card => card.State == ValuationGaugeCardState.Available);
}

public sealed record ValuationGaugeCard(
    ValuationGaugeKind Kind,
    ValuationGaugeCardState State,
    string ReasonCode,
    string Source,
    DateTimeOffset? FetchedAtUtc,
    ValuationRatioGaugeFacts? Ratio = null,
    ValuationEquilibriumFacts? Equilibrium = null,
    ValuationGaugeGeometry? Geometry = null,
    ValuationGaugeInsights? Insights = null,
    // Omitted from JSON when absent, so cards without an estimate keep exactly their previous payload (AC-01).
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ValuationGaugeForwardEstimate? Forward = null)
{
    public bool IsDisplayable() => State is ValuationGaugeCardState.Available or ValuationGaugeCardState.Partial;
}

/// <summary>
/// Sapio's forward estimate of this card's ratio (change «valuation-gauge-forward-ratio»), never the provider's.
/// <see cref="Value"/> has two decimals; <see cref="Angle"/> and <see cref="Side"/> are on the card's own axis with the
/// same pin rule as the current needle. <see cref="SalesGrowthFactor"/> is the 4-month year-over-year factor (4
/// decimals); <see cref="MarginPercent"/> is the median net margin in percent (1 decimal), present on the P/E card only.
/// </summary>
public sealed record ValuationGaugeForwardEstimate(
    string Value,
    decimal Angle,
    ValuationGaugeNeedleSide Side,
    string MethodVersion,
    string SalesGrowthFactor,
    string? MarginPercent,
    DateOnly LatestSalesMonthEnd,
    DateOnly LatestQuarterEnd);

/// <summary>Provider facts for a P/E or P/S gauge. Values are the provider's exact decimals as invariant strings.</summary>
public sealed record ValuationRatioGaugeFacts(
    string? Current,
    string Start,
    string Min,
    string Average,
    string Max,
    string End,
    IReadOnlyList<long> DayCounts,
    IReadOnlyList<string>? DayPercentages,
    int? ProviderStatus,
    ValuationRatioPosition Position,
    ValuationGaugeNeedleSide? CurrentSide);

/// <summary>Equilibrium price range in rials with the market quote kept separate from the range fetch time.</summary>
public sealed record ValuationEquilibriumFacts(
    string Lower,
    string Midpoint,
    string Upper,
    ValuationGaugeQuoteState QuoteState,
    ValuationGaugeQuoteFacts? Quote,
    ValuationEquilibriumPosition? Position);

public sealed record ValuationGaugeQuoteFacts(
    string Price,
    string ProviderName,
    string SourceLabel,
    DateTimeOffset AsOf,
    DateOnly? TradingDate);

/// <summary>Deterministic dial geometry: 0° is the left end of the semicircle and 180° the right end.</summary>
public sealed record ValuationGaugeGeometry(
    IReadOnlyList<ValuationGaugeSector> Sectors,
    IReadOnlyList<ValuationGaugeMarker> Markers,
    ValuationGaugeNeedle? Needle);

/// <summary>
/// One dial sector. <see cref="From"/> and <see cref="To"/> are the exact value range the sector covers (ratio dials
/// only; absent on cards stored before the visual refresh).
/// </summary>
public sealed record ValuationGaugeSector(
    int Order,
    decimal StartAngle,
    decimal EndAngle,
    long? DayCount,
    string? Percentage,
    string? From = null,
    string? To = null);

/// <summary>
/// Figures derived from the same card's exact facts (Feature 159 change «valuation-gauge-cards-visual-refresh»).
/// Every member is optional and absent when its inputs are missing, so web and text show only what the data supports.
/// Percentages are invariant decimal strings. <see cref="CurrentBand"/> is the 1-based sector of an inside needle;
/// the day shares are sums of the displayed largest-remainder sector percentages, so they match the dial exactly.
/// </summary>
public sealed record ValuationGaugeInsights(
    string? DistanceToAveragePercent = null,
    int? CurrentBand = null,
    string? DaysBelowBandPercent = null,
    string? DaysInBandPercent = null,
    string? PriceVsMidpointPercent = null,
    string? PriceVsLowerPercent = null);

/// <summary>Role is one of start, min, average, max, end (ratio) or lower, midpoint, upper (equilibrium).</summary>
public sealed record ValuationGaugeMarker(string Role, decimal Angle, string Value);

public sealed record ValuationGaugeNeedle(decimal Angle, string Value, ValuationGaugeNeedleSide Side);

// ---- Ports owned by the use case -------------------------------------------------------------------------

public enum ValuationGaugeMatchOutcome { Matched, Missing, Ambiguous }

public sealed record ValuationGaugeCatalogFlags(bool HasPe, bool HasPs);

public sealed record RatioGaugeSnapshot(
    string? Close,
    string Start,
    string Min,
    string Average,
    string Max,
    string End,
    IReadOnlyList<long> DayCounts,
    int? ProviderStatus,
    DateTimeOffset FetchedAtUtc);

public sealed record EquilibriumRangeSnapshot(
    bool HasRange,
    string? Lower,
    string? Midpoint,
    string? Upper,
    DateTimeOffset? FetchedAtUtc);

public sealed record ValuationGaugeSnapshotSet(
    ValuationGaugeMatchOutcome Match,
    string? VendorTicker = null,
    string? Isin = null,
    ValuationGaugeCatalogFlags? Catalog = null,
    RatioGaugeSnapshot? Pe = null,
    RatioGaugeSnapshot? Ps = null,
    EquilibriumRangeSnapshot? Equilibrium = null);

/// <summary>Read-only latest-state Feature 148 snapshots for one security, matched by unique vendor identity or ISIN.</summary>
public interface IValuationGaugeSnapshotReader
{
    Task<ValuationGaugeSnapshotSet> ReadAsync(
        ValuationGaugeSecurity security,
        IReadOnlyCollection<ValuationGaugeKind> kinds,
        CancellationToken cancellationToken);
}

public sealed record ValuationGaugeQuote(
    decimal Price,
    string ProviderName,
    string SourceLabel,
    DateTimeOffset AsOf,
    DateOnly? TradingDate);

/// <summary>Canonical market quote for the same instrument (keyed by normalized ISIN).</summary>
public interface IValuationGaugeQuoteReader
{
    Task<ValuationGaugeQuote?> ReadAsync(string isin, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Low-cardinality diagnostics; never participates in decisions.</summary>
public interface IValuationGaugeObserver
{
    void CardProjected(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, TimeSpan? fetchAge);
    void Completed(TimeSpan elapsed, int displayableCards, string matchOutcome);

    /// <summary>Per ratio card when the forward estimate is enabled: shown or not, the reason, and the input read latency.</summary>
    void ForwardEstimated(ValuationGaugeKind kind, bool shown, string reasonCode, TimeSpan? readElapsed);

    /// <summary>Once per request when the estimator input read timed out or failed; only the estimate is dropped.</summary>
    void ForwardReadFailed(string reasonCode, TimeSpan elapsed, Exception? error);
}

public sealed class NullValuationGaugeObserver : IValuationGaugeObserver
{
    public static readonly NullValuationGaugeObserver Instance = new();
    public void CardProjected(ValuationGaugeKind kind, ValuationGaugeCardState state, string reasonCode, TimeSpan? fetchAge) { }
    public void Completed(TimeSpan elapsed, int displayableCards, string matchOutcome) { }
    public void ForwardEstimated(ValuationGaugeKind kind, bool shown, string reasonCode, TimeSpan? readElapsed) { }
    public void ForwardReadFailed(string reasonCode, TimeSpan elapsed, Exception? error) { }
}

public interface IValuationGaugeCardsUseCase
{
    Task<ValuationGaugeCardsResult> ExecuteAsync(ValuationGaugeCardsQuery query, CancellationToken cancellationToken);
}
