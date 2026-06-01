using Microsoft.Data.SqlClient;

namespace FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;

public sealed class SqlStockMarketDbQueryExecutor(
    StockMarketDbConnectionFactory connectionFactory,
    StockMarketDbSqlResilience resilience) : IStockMarketDbQueryExecutor
{
    public Task<IReadOnlyList<StockMarketInstrumentRecord>> QueryInstrumentsAsync(
        StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) =>
        QueryAsync(
            """
            SELECT TOP (@take) Id, InsCode, InstrumentID, Instrument18LetterPersianCode,
                   Instrument30LetterPersianName, MarketCode, InstrumentKind, ChangeTime, Valid, IsDeleted
            FROM Tse.Instrument
            WHERE @after IS NULL OR ChangeTime > @after OR (ChangeTime = @after AND Id > @lastGuidId)
            ORDER BY ChangeTime, Id;
            """,
            cursor,
            take,
            reader => new StockMarketInstrumentRecord(
                reader.GetGuid(reader.GetOrdinal("Id")),
                Convert.ToInt64(reader["InsCode"]),
                Str(reader["InstrumentID"]),
                Str(reader["Instrument18LetterPersianCode"]),
                Str(reader["Instrument30LetterPersianName"]),
                Str(reader["MarketCode"]),
                Str(reader["InstrumentKind"]),
                Date(reader["ChangeTime"]),
                Bool(reader["Valid"]),
                Bool(reader["IsDeleted"])),
            cancellationToken);

    public Task<IReadOnlyList<StockMarketIntradayTradeRecord>> QueryIntradayTradesAsync(
        StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) =>
        QueryAsync(
            """
            SELECT TOP (@take) Id, InstrumentRef, TradeDate, TotalTransactions, VolumeOfTradedShares,
                   TotalCapital, ClosingPrice, ClosingPriceChange, LastTradedPrice, PriceChange,
                   PriceMin, PriceMax, PriceFirst, PriceYesterday, TradeTime, ReceiveDate
            FROM Tse.Trade
            WHERE @after IS NULL OR ReceiveDate > @after OR (ReceiveDate = @after AND Id > @lastGuidId)
            ORDER BY ReceiveDate, Id;
            """,
            cursor,
            take,
            reader => new StockMarketIntradayTradeRecord(
                reader.GetGuid(reader.GetOrdinal("Id")),
                reader.GetGuid(reader.GetOrdinal("InstrumentRef")),
                DateOnly.FromDateTime(Convert.ToDateTime(reader["TradeDate"])),
                Decimal(reader["TotalTransactions"]),
                Decimal(reader["VolumeOfTradedShares"]),
                Decimal(reader["TotalCapital"]),
                Decimal(reader["ClosingPrice"]),
                Decimal(reader["ClosingPriceChange"]),
                Decimal(reader["LastTradedPrice"]),
                Decimal(reader["PriceChange"]),
                Decimal(reader["PriceMin"]),
                Decimal(reader["PriceMax"]),
                Decimal(reader["PriceFirst"]),
                Decimal(reader["PriceYesterday"]),
                Time(reader["TradeTime"]),
                Date(reader["ReceiveDate"])),
            cancellationToken);

    public Task<IReadOnlyList<StockMarketDailyTradeRecord>> QueryDailyTradesAsync(
        StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) =>
        QueryAsync(
            """
            SELECT TOP (@take) Id, InstrumentRef, InsCode, TradeDateTime, PClosing, PDrCotVal,
                   ZTotTran, QTotTran5J, QTotCap, PriceChange, PriceMin, PriceMax, PriceYesterday,
                   PriceFirst, MarketValue, InsertDateTime
            FROM Tse.InstTrade
            WHERE @after IS NULL OR InsertDateTime > @after OR (InsertDateTime = @after AND Id > @lastLongId)
            ORDER BY InsertDateTime, Id;
            """,
            cursor,
            take,
            reader => new StockMarketDailyTradeRecord(
                Convert.ToInt64(reader["Id"]),
                reader.GetGuid(reader.GetOrdinal("InstrumentRef")),
                Convert.ToInt64(reader["InsCode"]),
                DateOnly.FromDateTime(Convert.ToDateTime(reader["TradeDateTime"])),
                Decimal(reader["PClosing"]),
                Decimal(reader["PDrCotVal"]),
                Decimal(reader["ZTotTran"]),
                Decimal(reader["QTotTran5J"]),
                Decimal(reader["QTotCap"]),
                Decimal(reader["PriceChange"]),
                Decimal(reader["PriceMin"]),
                Decimal(reader["PriceMax"]),
                Decimal(reader["PriceYesterday"]),
                Decimal(reader["PriceFirst"]),
                Decimal(reader["MarketValue"]),
                Date(reader["InsertDateTime"])),
            cancellationToken);

    public Task<IReadOnlyList<StockMarketIntradayIndexRecord>> QueryIntradayIndicesAsync(
        StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) =>
        QueryAsync(
            """
            SELECT TOP (@take) Id, InstrumentRef, InsCode, IndexDate, IndexTime, XDrNivJIdx004,
                   XVarIdxJRfV, ChangeTime
            FROM Tse.IndexB1LastDay
            WHERE @after IS NULL OR ChangeTime > @after OR (ChangeTime = @after AND Id > @lastGuidId)
            ORDER BY ChangeTime, Id;
            """,
            cursor,
            take,
            reader => new StockMarketIntradayIndexRecord(
                reader.GetGuid(reader.GetOrdinal("Id")),
                reader.GetGuid(reader.GetOrdinal("InstrumentRef")),
                Long(reader["InsCode"]),
                DateOnly.FromDateTime(Convert.ToDateTime(reader["IndexDate"])),
                Time(reader["IndexTime"]),
                Decimal(reader["XDrNivJIdx004"]),
                NullableDecimal(reader["XVarIdxJRfV"]),
                Date(reader["ChangeTime"])),
            cancellationToken);

    public Task<IReadOnlyList<StockMarketHistoricalDailyIndexRecord>> QueryHistoricalDailyIndicesAsync(
        StockMarketPageCursor cursor, int take, CancellationToken cancellationToken) =>
        QueryAsync(
            """
            SELECT TOP (@take) Id, InstrumentRef, IndexDate, XNivInuClMresIbs, XNivInuPhMresIbs,
                   XNivInuPbMresIbs, XVarIdxPhJClV, ChangeTime
            FROM Tse.IndexNew2
            WHERE @after IS NULL OR ChangeTime > @after OR (ChangeTime = @after AND Id > @lastGuidId)
            ORDER BY ChangeTime, Id;
            """,
            cursor,
            take,
            reader => new StockMarketHistoricalDailyIndexRecord(
                reader.GetGuid(reader.GetOrdinal("Id")),
                reader.GetGuid(reader.GetOrdinal("InstrumentRef")),
                DateOnly.FromDateTime(Convert.ToDateTime(reader["IndexDate"])),
                NullableDecimal(reader["XNivInuClMresIbs"]),
                NullableDecimal(reader["XNivInuPhMresIbs"]),
                NullableDecimal(reader["XNivInuPbMresIbs"]),
                NullableDecimal(reader["XVarIdxPhJClV"]),
                NullableDate(reader["ChangeTime"])),
            cancellationToken);

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        StockMarketPageCursor cursor,
        int take,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken)
    => await resilience.ExecuteAsync("query trading statistics", async ct =>
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection)
        {
            CommandTimeout = connectionFactory.CommandTimeoutSeconds
        };
        command.Parameters.AddWithValue("@take", Math.Max(1, take));
        command.Parameters.AddWithValue("@after", (object?)cursor.After?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@lastGuidId", (object?)cursor.LastGuidId ?? Guid.Empty);
        command.Parameters.AddWithValue("@lastLongId", (object?)cursor.LastLongId ?? long.MinValue);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
            rows.Add(map(reader));
        return rows;
    }, cancellationToken);

    private static string Str(object value) => value is DBNull ? string.Empty : Convert.ToString(value)!.Trim();
    private static bool? Bool(object value) => value is DBNull ? null : Convert.ToBoolean(value);
    private static long? Long(object value) => value is DBNull ? null : Convert.ToInt64(value);
    private static decimal Decimal(object value) => Convert.ToDecimal(value);
    private static decimal? NullableDecimal(object value) => value is DBNull ? null : Convert.ToDecimal(value);
    private static DateTimeOffset Date(object value) => new(DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc));
    private static DateTimeOffset? NullableDate(object value) => value is DBNull ? null : Date(value);
    private static TimeOnly? Time(object value) => value is DBNull ? null : TimeOnly.FromTimeSpan((TimeSpan)value);
}
