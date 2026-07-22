namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

/// <summary>
/// Instrument dimension record from TSETMC Instrument(flow) ASMX method.
/// Field names mirror the DataSet columns returned by TsePublicV2.
/// </summary>
public sealed record TsetmcInstrumentRecord(
    long InsCode,
    string InstrumentId,     // InstrumentID (ISIN-like 12-char code)
    string Symbol,           // CValMne (5-char symbol)
    string SymbolFull,       // LVal18AFC (18-char Persian symbol)
    string CompanyName,      // LSoc30 (Persian company name)
    string MarketCode,       // YMarNSC
    string InstrumentKind,   // CGdSVal (single char)
    string InstrumentGroupCode, // CGrValCot
    bool Valid,
    DateOnly ListingDate,
    decimal TotalShares);

/// <summary>
/// Intraday trade snapshot from TSETMC TradeLastDay(flow) ASMX method.
/// Called repeatedly throughout the trading session.
/// </summary>
public sealed record TsetmcIntradayTradeRecord(
    long InsCode,
    DateOnly TradingDate,    // DEven (yyyyMMdd int)
    TimeOnly TradingTime,    // HEven (HHmmss int)
    decimal TotalTransactions,   // ZTotTran
    decimal Volume,              // QTotTran5J
    decimal TotalCapital,        // QTotCap
    decimal ClosingPrice,        // PClosing
    decimal LastTradedPrice,     // PDrCotVal
    decimal PriceChange,         // PriceChange
    decimal PriceMin,            // PriceMin
    decimal PriceMax,            // PriceMax
    decimal PriceFirst,          // PriceFirst
    decimal PriceYesterday);     // PriceYesterday

/// <summary>
/// Daily historical trade record from TSETMC TradeOneDay(SelDate, flow) ASMX method.
/// One record per instrument per date.
/// </summary>
public sealed record TsetmcDailyTradeRecord(
    long InsCode,
    string Symbol,               // LVal18AFC (Persian symbol)
    DateOnly TradingDate,        // DEven
    decimal ClosingPrice,        // PClosing
    decimal LastTradedPrice,     // PDrCotVal
    decimal PriceYesterday,      // PriceYesterday
    decimal PriceFirst,          // PriceFirst
    decimal PriceMin,            // PriceMin
    decimal PriceMax,            // PriceMax
    decimal PriceChange,         // PriceChange
    decimal TotalTransactions,   // ZTotTran
    decimal Volume,              // QTotTran5J
    decimal TotalCapital);       // QTotCap

/// <summary>
/// Daily index record from TSETMC IndexB2(DEven) ASMX method.
/// Returns all index instruments for a given date.
/// </summary>
public sealed record TsetmcDailyIndexRecord(
    long InsCode,
    DateOnly IndexDate,          // Deven
    decimal Value,               // xNivInuClMresIbs (closing value)
    decimal? High,               // xNivInuPhMresIbs
    decimal? Low,                // xNivInuPbMresIbs
    decimal? ChangePercent);     // XVarDrInuClV (change vs previous day)

/// <summary>
/// Intraday index snapshot from TSETMC IndexB1LastDayLastData(flow) ASMX method.
/// </summary>
public sealed record TsetmcIntradayIndexRecord(
    long InsCode,
    DateOnly IndexDate,          // DEven
    TimeOnly IndexTime,          // HEven
    decimal Value,               // XDrNivJIdx004 (current value)
    decimal? ChangePercent);     // XVarIdxJRfV (close-to-close percentage change)
