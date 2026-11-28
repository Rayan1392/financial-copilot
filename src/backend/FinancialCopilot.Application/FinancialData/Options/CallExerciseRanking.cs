namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>Why a call option is left out of the exercise ranking.</summary>
public enum CallExerciseIneligibility
{
    /// <summary>Nobody is offering the contract.</summary>
    NoAsk,

    /// <summary>The best offer is smaller than the requested number of contracts.</summary>
    InsufficientAskVolume,

    /// <summary>The order book was last observed before the underlying's latest trading session.</summary>
    StaleQuote
}

/// <param name="EffectivePurchaseCost">Cost per share of buying the call at its best ask and exercising it: strike + ask.</param>
/// <param name="PremiumOverSpot">(effective purchase cost − underlying price) ÷ underlying price. Negative when exercise is cheaper than the market.</param>
public sealed record RankedCallOption(
    int Rank,
    OptionQuote Option,
    decimal EffectivePurchaseCost,
    decimal PremiumOverSpot);

public sealed record IneligibleCallOption(OptionQuote Option, CallExerciseIneligibility Reason);

public sealed record CallExerciseRankingResult(
    string UnderlyingSymbol,
    OptionUnderlyingQuote Underlying,
    DateOnly ExpiryDate,
    IReadOnlyList<DateOnly> AvailableExpiryDates,
    int QuantityInContracts,
    IReadOnlyList<RankedCallOption> Ranked,
    IReadOnlyList<IneligibleCallOption> Ineligible,
    DateTimeOffset? OrderBookObservedAt,
    DateTimeOffset? OpenInterestObservedAt);

/// <summary>
/// Ranks the call options of one underlying and expiry by the cost of acquiring the share through
/// exercise at expiry. The cheapest effective purchase cost ranks first.
/// </summary>
public static class CallExerciseRankingPolicy
{
    // Iran Standard Time, fixed offset (no daylight saving).
    private static readonly TimeSpan MarketOffset = TimeSpan.FromHours(3.5);

    public static CallExerciseRankingResult Rank(OptionChain chain, int quantityInContracts)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentOutOfRangeException.ThrowIfLessThan(quantityInContracts, 1);
        if (chain.Underlying is not { LastPrice: > 0 } underlying)
            throw new InvalidOperationException("Ranking needs a positive underlying price.");

        var bookIsStale = chain.OrderBookObservedAt is not { } observedAt ||
            DateOnly.FromDateTime(observedAt.ToOffset(MarketOffset).DateTime) < underlying.TradingDate;

        var eligible = new List<(OptionQuote Option, decimal Cost)>();
        var ineligible = new List<IneligibleCallOption>();
        foreach (var call in chain.Strikes.Select(strike => strike.Call).OfType<OptionQuote>())
        {
            if (call.AskPrice is not { } ask || call.AskVolume is not { } askVolume)
                ineligible.Add(new IneligibleCallOption(call, CallExerciseIneligibility.NoAsk));
            else if (bookIsStale)
                ineligible.Add(new IneligibleCallOption(call, CallExerciseIneligibility.StaleQuote));
            else if (askVolume < quantityInContracts)
                ineligible.Add(new IneligibleCallOption(call, CallExerciseIneligibility.InsufficientAskVolume));
            else
                eligible.Add((call, call.StrikePrice + ask));
        }

        var ranked = eligible
            .OrderBy(item => item.Cost)
            .ThenByDescending(item => item.Option.AskVolume)
            .ThenBy(item => item.Option.Symbol, StringComparer.Ordinal)
            .Select((item, index) => new RankedCallOption(
                index + 1,
                item.Option,
                item.Cost,
                (item.Cost - underlying.LastPrice) / underlying.LastPrice))
            .ToArray();

        return new CallExerciseRankingResult(
            chain.UnderlyingSymbol,
            underlying,
            chain.ExpiryDate,
            chain.AvailableExpiryDates,
            quantityInContracts,
            ranked,
            ineligible.OrderBy(item => item.Option.StrikePrice).ToArray(),
            chain.OrderBookObservedAt,
            chain.OpenInterestObservedAt);
    }
}

/// <summary>Outcome of a ranking request that could not be answered.</summary>
public enum CallExerciseRankingFailure
{
    None,
    NoOptionContracts,
    NoUnderlyingPrice
}

/// <summary>Use case: rank the calls of one underlying for exercise at expiry.</summary>
public sealed class CallExerciseRankingUseCase(IOptionChainReader chainReader)
{
    public async Task<(CallExerciseRankingResult? Result, CallExerciseRankingFailure Failure)> ExecuteAsync(
        string underlyingSymbol,
        DateOnly? expiryDate,
        int quantityInContracts,
        CancellationToken cancellationToken)
    {
        var chain = await chainReader.GetChainAsync(underlyingSymbol, expiryDate, cancellationToken);
        if (chain is null)
            return (null, CallExerciseRankingFailure.NoOptionContracts);
        if (chain.Underlying is not { LastPrice: > 0 })
            return (null, CallExerciseRankingFailure.NoUnderlyingPrice);

        return (CallExerciseRankingPolicy.Rank(chain, quantityInContracts), CallExerciseRankingFailure.None);
    }
}
