namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

/// <summary>Terms and open interest of one listed option contract (Feature 167).</summary>
public sealed class OptionContractRow
{
    public Guid Id { get; set; }

    /// <summary>The contract's own instrument.</summary>
    public Guid TradingInstrumentId { get; set; }

    public Guid UnderlyingInstrumentId { get; set; }

    /// <summary>"Call" or "Put".</summary>
    public string Side { get; set; } = string.Empty;

    public decimal StrikePrice { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public DateOnly BeginDate { get; set; }

    public int ContractSize { get; set; }

    public long OpenInterest { get; set; }

    public long PreviousOpenInterest { get; set; }

    public DateTimeOffset OpenInterestObservedAt { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSynchronizedAt { get; set; }
}

/// <summary>Latest state of one order-book level of an option contract. No history is kept.</summary>
public sealed class OptionOrderBookLevelRow
{
    public Guid TradingInstrumentId { get; set; }

    /// <summary>1 (best) to 5.</summary>
    public short Level { get; set; }

    public decimal? BidPrice { get; set; }

    public long? BidVolume { get; set; }

    public int? BidOrderCount { get; set; }

    public decimal? AskPrice { get; set; }

    public long? AskVolume { get; set; }

    public int? AskOrderCount { get; set; }

    /// <summary>Provider's time of the last change of this level (market local time).</summary>
    public TimeOnly ProviderTime { get; set; }

    /// <summary>When this row last changed in our store.</summary>
    public DateTimeOffset ObservedAt { get; set; }
}
