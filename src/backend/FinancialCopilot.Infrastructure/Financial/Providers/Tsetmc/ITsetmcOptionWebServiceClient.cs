namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

/// <summary>
/// Option-market operations of the TSETMC TsePublicV2 web service. Responses are read as a
/// stream because the order book is several megabytes per call.
/// </summary>
public interface ITsetmcOptionWebServiceClient
{
    /// <summary>Calls Option(UserName, Password): terms and open positions of every option contract.</summary>
    Task<IReadOnlyList<TsetmcOptionRecord>> GetOptionContractsAsync(CancellationToken cancellationToken);

    /// <summary>Calls BestLimitsAllIns(UserName, Password, flow): up to five order-book levels per instrument.</summary>
    Task<IReadOnlyList<TsetmcBestLimitRecord>> GetBestLimitsAsync(byte flow, CancellationToken cancellationToken);
}

/// <summary>Row of TSETMC Option(). BuyOP and SellOP are always equal; one value is kept.</summary>
public sealed record TsetmcOptionRecord(
    long InsCode,
    string InstrumentId,          // InstrumentID (ISIN)
    long OpenPositions,           // BuyOP
    long YesterdayOpenPositions,  // YesterdayOP
    int ContractSize,             // ContractSize
    decimal StrikePrice,          // StrikePrice
    long UnderlyingInsCode,       // UAInsCode
    DateOnly BeginDate,           // BeginDate (yyyyMMdd)
    DateOnly EndDate);            // EndDate (yyyyMMdd)

/// <summary>Row of TSETMC BestLimitsAllIns(flow). Zero price or volume means no order on that side.</summary>
public sealed record TsetmcBestLimitRecord(
    long InsCode,
    int Level,                    // number (1..5)
    long BidVolume,               // QTitMeDem
    int BidOrderCount,            // ZOrdMeDem
    decimal BidPrice,             // PMeDem
    decimal AskPrice,             // PMeOf
    int AskOrderCount,            // ZOrdMeOf
    long AskVolume,               // QTitMeOf
    TimeOnly Time);               // HEven (HHmmss)
