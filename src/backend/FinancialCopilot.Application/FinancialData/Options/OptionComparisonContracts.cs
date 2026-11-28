using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>Which trader's question a comparison answers.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OptionComparisonView>))]
public enum OptionComparisonView
{
    /// <summary>Buying a call to own the share through exercise.</summary>
    Call,

    /// <summary>Buying a put to sell the share through exercise.</summary>
    Put,

    /// <summary>Writing a call against a held share.</summary>
    CoveredCall
}

/// <summary>Whether the order book can serve the requested quantity on the side the view needs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OptionRowState>))]
public enum OptionRowState
{
    Fillable,

    /// <summary>There are orders, but fewer contracts than requested.</summary>
    NotFillable,

    NoOrder
}

public static class OptionHighlights
{
    public const string LowestEffectivePurchaseCost = "lowest_effective_purchase_cost";
    public const string LowestImpliedAnnualRate = "lowest_implied_annual_rate";
    public const string HighestEffectiveSalePrice = "highest_effective_sale_price";
    public const string HighestAnnualizedYieldIfExercised = "highest_annualized_yield_if_exercised";
}

/// <summary>
/// One contract in a comparison. All money figures are per share and include fees. A measure is
/// null when it does not belong to the row's view, when the row is not fillable, when the order
/// book is not current, or when its own condition fails (for example the annualization floor).
/// </summary>
public sealed record OptionComparisonRow(
    string Symbol,
    decimal StrikePrice,
    int ContractSize,
    long OpenInterest,
    OptionRowState State,
    decimal? FillPrice,
    long AvailableVolume,
    decimal? LastPrice,
    bool IsNearestToMoney,
    bool IsRequestedContract,
    IReadOnlyList<string> Highlights,
    // Call bought for exercise.
    decimal? EffectivePurchaseCost = null,
    decimal? BreakEvenDistance = null,
    decimal? IntrinsicValue = null,
    decimal? TimeValue = null,
    decimal? ImpliedAnnualRate = null,
    decimal? Leverage = null,
    decimal? CashNow = null,
    decimal? CashAtExpiry = null,
    // Put bought for exercise.
    decimal? EffectiveSalePrice = null,
    decimal? DistanceFromMarket = null,
    decimal? ProtectionCost = null,
    decimal? ProtectionCostAnnualized = null,
    // Call written against a held share.
    decimal? PremiumIncome = null,
    decimal? YieldIfExercised = null,
    decimal? YieldIfExercisedAnnualized = null,
    decimal? YieldIfNotExercised = null,
    decimal? YieldIfNotExercisedAnnualized = null,
    decimal? DownsideCushion = null,
    // Theoretical (Black–Scholes) price and the volatility implied by the fill price.
    decimal? TheoreticalPrice = null,
    decimal? ImpliedVolatility = null);

public sealed record OptionComparisonExpiry(
    DateOnly ExpiryDate,
    int DaysToExpiry,
    IReadOnlyList<OptionComparisonRow> Rows);

public sealed record OptionComparisonViewResult(
    OptionComparisonView View,
    IReadOnlyList<OptionComparisonExpiry> Expiries);

/// <summary>Inputs of the theoretical price, stated so the figure is never read as a target.</summary>
public sealed record OptionTheoreticalInputs(
    decimal RiskFreeAnnualRate,
    decimal HistoricalVolatility,
    int VolatilityWindowDays);

/// <summary>
/// The structured option comparison answer. Persisted with the message and replayed from
/// history, so this JSON is a compatibility contract: bump <see cref="Version"/> and add only
/// optional members.
/// </summary>
public sealed record OptionComparison(
    int Version,
    string UnderlyingSymbol,
    OptionUnderlyingClass UnderlyingClass,
    decimal UnderlyingPrice,
    DateOnly UnderlyingTradingDate,
    DateTimeOffset UnderlyingObservedAt,
    int QuantityInContracts,
    OptionComparisonView RequestedView,
    IReadOnlyList<OptionComparisonViewResult> Views,
    IReadOnlyList<DateOnly> AvailableExpiryDates,
    OptionFeeRates Fees,
    bool OrderBookIsCurrent,
    DateTimeOffset? OrderBookObservedAt,
    DateTimeOffset? OpenInterestObservedAt,
    OptionTheoreticalInputs? TheoreticalInputs = null)
{
    public const int CurrentVersion = 1;

    /// <summary>True when at least one row of any view exists; an answer without rows is not charged.</summary>
    public bool HasRows() => Views.Any(view => view.Expiries.Any(expiry => expiry.Rows.Count > 0));

    public OptionComparisonViewResult? ViewOrDefault(OptionComparisonView view) =>
        Views.FirstOrDefault(item => item.View == view && item.Expiries.Any(expiry => expiry.Rows.Count > 0));
}

/// <summary>What the caller asked for.</summary>
/// <param name="ExpiryDate">Null means the nearest expiries, up to the configured maximum.</param>
/// <param name="ContractSymbol">Set when the question named one contract; its row is marked.</param>
public sealed record OptionComparisonQuery(
    string UnderlyingSymbol,
    OptionComparisonView View,
    int QuantityInContracts = 1,
    DateOnly? ExpiryDate = null,
    string? ContractSymbol = null);

public enum OptionComparisonFailure
{
    None,

    /// <summary>The symbol is unknown or has no unexpired listed option.</summary>
    NoOptionContracts,

    /// <summary>The requested expiry is not listed; the listed ones are returned.</summary>
    ExpiryNotListed,

    NoUnderlyingPrice
}

public sealed record OptionComparisonOutcome(
    OptionComparison? Comparison,
    OptionComparisonFailure Failure,
    IReadOnlyList<DateOnly> ListedExpiryDates);

public interface IOptionComparisonUseCase
{
    Task<OptionComparisonOutcome> ExecuteAsync(OptionComparisonQuery query, CancellationToken cancellationToken);
}

// ── Stored market state the comparison is computed from ───────────────────────

/// <summary>One stored contract with its full order book (levels 1 to 5, best first).</summary>
public sealed record OptionMarketContract(
    string Symbol,
    OptionSide Side,
    decimal StrikePrice,
    DateOnly ExpiryDate,
    int ContractSize,
    long OpenInterest,
    decimal? LastPrice,
    IReadOnlyList<OptionOrderBookLevel> Levels);

public sealed record OptionMarketSnapshot(
    string UnderlyingSymbol,
    string UnderlyingIsin,
    OptionUnderlyingQuote? Underlying,
    IReadOnlyList<DateOnly> AvailableExpiryDates,
    IReadOnlyList<OptionMarketContract> Contracts,
    DateTimeOffset? OrderBookObservedAt,
    DateTimeOffset? OpenInterestObservedAt);

/// <summary>A contract found by its own symbol (for example «ضمخا9021»).</summary>
public sealed record OptionContractReference(
    string ContractSymbol,
    string UnderlyingSymbol,
    OptionSide Side,
    DateOnly ExpiryDate);

public interface IOptionMarketSnapshotReader
{
    /// <summary>
    /// Unexpired listed contracts of an underlying with their order books. With
    /// <paramref name="expiryDate"/> only that expiry; otherwise the nearest
    /// <paramref name="maxExpiries"/>. Null when the symbol is unknown or has no unexpired contract.
    /// </summary>
    Task<OptionMarketSnapshot?> GetAsync(
        string underlyingSymbol, DateOnly? expiryDate, int maxExpiries, CancellationToken cancellationToken);

    Task<OptionContractReference?> FindContractAsync(string contractSymbol, CancellationToken cancellationToken);

    /// <summary>Symbols of underlyings that have at least one unexpired listed option.</summary>
    Task<IReadOnlyList<string>> ListUnderlyingSymbolsAsync(CancellationToken cancellationToken);

    /// <summary>Daily closing prices of the underlying, oldest first, at most <paramref name="days"/> trading days.</summary>
    Task<IReadOnlyList<decimal>> GetUnderlyingDailyClosesAsync(
        string underlyingSymbol, int days, CancellationToken cancellationToken);
}
