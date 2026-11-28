namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>Reads the stored option chain of one underlying symbol.</summary>
public interface IOptionChainReader
{
    /// <summary>
    /// Returns the chain for <paramref name="underlyingSymbol"/>, or null when the symbol is unknown
    /// or has no unexpired listed option contract. Without <paramref name="expiryDate"/> the nearest
    /// unexpired expiry is used.
    /// </summary>
    Task<OptionChain?> GetChainAsync(
        string underlyingSymbol,
        DateOnly? expiryDate,
        CancellationToken cancellationToken);
}

/// <summary>Last known price of the underlying share.</summary>
/// <param name="TradingDate">The trading session the price belongs to.</param>
public sealed record OptionUnderlyingQuote(decimal LastPrice, DateOnly TradingDate, DateTimeOffset ObservedAt);

/// <summary>One side (call or put) of a strike, with its market state.</summary>
/// <param name="ClosingPrice">Volume-weighted closing price of the session of <paramref name="LastTradeDate"/>; null when the contract did not trade in it.</param>
/// <param name="OrderBookChangedAt">When the stored level-1 row last changed; null when the contract has no order.</param>
public sealed record OptionQuote(
    string Symbol,
    string Name,
    OptionSide Side,
    decimal StrikePrice,
    DateOnly ExpiryDate,
    int ContractSize,
    decimal? LastPrice,
    decimal? ClosingPrice,
    DateOnly? LastTradeDate,
    decimal? BidPrice,
    long? BidVolume,
    decimal? AskPrice,
    long? AskVolume,
    long OpenInterest,
    DateTimeOffset? OrderBookChangedAt);

public sealed record OptionChainStrike(decimal StrikePrice, OptionQuote? Call, OptionQuote? Put);

/// <param name="OrderBookObservedAt">Completion time of the last successful order-book sync; null when none has run.</param>
/// <param name="OpenInterestObservedAt">Latest open-interest observation among the returned contracts.</param>
public sealed record OptionChain(
    string UnderlyingSymbol,
    OptionUnderlyingQuote? Underlying,
    DateOnly ExpiryDate,
    IReadOnlyList<DateOnly> AvailableExpiryDates,
    IReadOnlyList<OptionChainStrike> Strikes,
    DateTimeOffset? OrderBookObservedAt,
    DateTimeOffset? OpenInterestObservedAt);
