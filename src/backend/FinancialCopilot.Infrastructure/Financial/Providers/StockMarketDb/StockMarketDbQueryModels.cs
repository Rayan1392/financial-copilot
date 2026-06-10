namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed record StockMarketInstrumentRecord(
    Guid Id,
    long InsCode,
    string InstrumentId,
    string Symbol,
    string Name,
    string MarketCode,
    string InstrumentKind,
    DateTimeOffset ChangeTime,
    bool? Valid,
    bool? IsDeleted);

public sealed record StockMarketIntradayTradeRecord(
    Guid Id,
    Guid InstrumentRef,
    DateOnly TradeDate,
    decimal TotalTransactions,
    decimal VolumeOfTradedShares,
    decimal TotalCapital,
    decimal ClosingPrice,
    decimal ClosingPriceChange,
    decimal LastTradedPrice,
    decimal PriceChange,
    decimal PriceMin,
    decimal PriceMax,
    decimal PriceFirst,
    decimal PriceYesterday,
    TimeOnly? TradeTime,
    DateTimeOffset ReceiveDate);

public sealed record StockMarketDailyTradeRecord(
    Guid Id,
    Guid InstrumentRef,
    DateOnly TradeDate,
    decimal ClosingPrice,
    decimal LastTradedPrice,
    decimal TotalTransactions,
    decimal VolumeOfTradedShares,
    decimal TotalCapital,
    decimal PriceChange,
    decimal PriceMin,
    decimal PriceMax,
    decimal PriceYesterday,
    decimal PriceFirst,
    decimal MarketValue,
    DateTimeOffset ChangeTime);

public sealed record StockMarketIntradayIndexRecord(
    Guid Id,
    Guid InstrumentRef,
    long? InsCode,
    DateOnly IndexDate,
    TimeOnly? IndexTime,
    decimal Value,
    decimal? ChangePercent,
    DateTimeOffset ChangeTime);

public sealed record StockMarketHistoricalDailyIndexRecord(
    Guid Id,
    Guid InstrumentRef,
    DateOnly IndexDate,
    decimal? Value,
    decimal? High,
    decimal? Low,
    decimal? ChangePercent,
    DateTimeOffset? ChangeTime);

