namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

/// <summary>
/// Typed client for the TSETMC ASMX web service (TsePublicV2).
/// Endpoint: http://service.tsetmc.com/WebService/TsePublicV2.asmx
/// All methods are credential-authenticated and must not be called from query-time paths.
/// </summary>
public interface ITsetmcWebServiceClient
{
    /// <summary>
    /// Fetches the instrument dimension for the given market flow.
    /// Calls Instrument(UserName, Password, flow).
    /// </summary>
    Task<IReadOnlyList<TsetmcInstrumentRecord>> GetInstrumentsAsync(
        byte flow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches today's intraday trade snapshots for all instruments in the given market flow.
    /// Calls TradeLastDay(UserName, Password, flow).
    /// </summary>
    Task<IReadOnlyList<TsetmcIntradayTradeRecord>> GetIntradayTradesAsync(
        byte flow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches all daily trade records for a specific date and market flow.
    /// Calls TradeOneDay(UserName, Password, SelDate, flow) where SelDate is yyyyMMdd.
    /// </summary>
    Task<IReadOnlyList<TsetmcDailyTradeRecord>> GetDailyTradesAsync(
        DateOnly date,
        byte flow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches daily index values for all index instruments on a specific date.
    /// Calls IndexB2(UserName, Password, DEven) where DEven is yyyyMMdd.
    /// </summary>
    Task<IReadOnlyList<TsetmcDailyIndexRecord>> GetDailyIndicesAsync(
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches today's intraday index snapshots for the given market flow.
    /// Calls IndexB1LastDayLastData(UserName, Password, flow).
    /// </summary>
    Task<IReadOnlyList<TsetmcIntradayIndexRecord>> GetIntradayIndicesAsync(
        byte flow,
        CancellationToken cancellationToken);
}
