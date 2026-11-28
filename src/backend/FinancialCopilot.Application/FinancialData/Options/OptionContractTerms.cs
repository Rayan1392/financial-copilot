using System.Text.Json.Serialization;

namespace FinancialCopilot.Application.FinancialData.Options;

/// <summary>The right an option contract gives its holder.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OptionSide>))]
public enum OptionSide
{
    /// <summary>اختیار خرید: the right to buy the underlying at the strike price.</summary>
    Call,

    /// <summary>اختیار فروش: the right to sell the underlying at the strike price.</summary>
    Put
}

/// <summary>Why a provider row could not become an option contract.</summary>
public enum OptionContractRejection
{
    None,
    NonPositiveStrike,
    NonPositiveContractSize,
    ExpiryBeforeBegin
}

/// <summary>
/// The fixed terms of one listed call or put option contract. Terms are validated at construction, so an
/// instance always describes a contract that can be exercised.
/// </summary>
public sealed record OptionContractTerms
{
    private OptionContractTerms(
        OptionSide side, decimal strikePrice, DateOnly expiryDate, DateOnly beginDate, int contractSize)
    {
        Side = side;
        StrikePrice = strikePrice;
        ExpiryDate = expiryDate;
        BeginDate = beginDate;
        ContractSize = contractSize;
    }

    public OptionSide Side { get; }

    public decimal StrikePrice { get; }

    public DateOnly ExpiryDate { get; }

    public DateOnly BeginDate { get; }

    /// <summary>Number of underlying shares one contract covers.</summary>
    public int ContractSize { get; }

    public static OptionContractRejection TryCreate(
        OptionSide side,
        decimal strikePrice,
        DateOnly expiryDate,
        DateOnly beginDate,
        int contractSize,
        out OptionContractTerms? terms)
    {
        terms = null;
        if (strikePrice <= 0) return OptionContractRejection.NonPositiveStrike;
        if (contractSize <= 0) return OptionContractRejection.NonPositiveContractSize;
        if (expiryDate < beginDate) return OptionContractRejection.ExpiryBeforeBegin;

        terms = new OptionContractTerms(side, strikePrice, expiryDate, beginDate, contractSize);
        return OptionContractRejection.None;
    }
}

/// <summary>
/// One price level of an option contract's order book. A side with no resting order is absent
/// (null), never zero. A level whose ask is below its bid is not a valid book state.
/// </summary>
public sealed record OptionOrderBookLevel
{
    private OptionOrderBookLevel(
        int level, decimal? bidPrice, long? bidVolume, int? bidOrderCount,
        decimal? askPrice, long? askVolume, int? askOrderCount)
    {
        Level = level;
        BidPrice = bidPrice;
        BidVolume = bidVolume;
        BidOrderCount = bidOrderCount;
        AskPrice = askPrice;
        AskVolume = askVolume;
        AskOrderCount = askOrderCount;
    }

    public const int MinLevel = 1;
    public const int MaxLevel = 5;

    public int Level { get; }
    public decimal? BidPrice { get; }
    public long? BidVolume { get; }
    public int? BidOrderCount { get; }
    public decimal? AskPrice { get; }
    public long? AskVolume { get; }
    public int? AskOrderCount { get; }

    public bool IsEmpty => BidPrice is null && AskPrice is null;

    /// <summary>
    /// Builds a level from raw quantities where zero means "no order". Returns false when the
    /// level number is out of range or the ask is below the bid.
    /// </summary>
    public static bool TryCreate(
        int level, decimal bidPrice, long bidVolume, int bidOrderCount,
        decimal askPrice, long askVolume, int askOrderCount,
        out OptionOrderBookLevel? result)
    {
        result = null;
        if (level is < MinLevel or > MaxLevel) return false;

        var hasBid = bidPrice > 0 && bidVolume > 0;
        var hasAsk = askPrice > 0 && askVolume > 0;
        if (hasBid && hasAsk && askPrice < bidPrice) return false;

        result = new OptionOrderBookLevel(
            level,
            hasBid ? bidPrice : null, hasBid ? bidVolume : null, hasBid ? bidOrderCount : null,
            hasAsk ? askPrice : null, hasAsk ? askVolume : null, hasAsk ? askOrderCount : null);
        return true;
    }
}
