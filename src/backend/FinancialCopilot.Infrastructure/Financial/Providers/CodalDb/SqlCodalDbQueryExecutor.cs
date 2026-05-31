using Microsoft.Data.SqlClient;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// <see cref="ICodalDbQueryExecutor"/> backed by <c>Microsoft.Data.SqlClient</c>. All queries are
/// parameterized and read-only, use the configured command timeout, and run under
/// <see cref="CodalDbSqlResilience"/> (transient retry + terminal-failure mapping). No writes/DDL.
/// Soft-deleted statements (<c>isDeleted</c>) are filtered out.
/// </summary>
public sealed class SqlCodalDbQueryExecutor(
    CodalDbConnectionFactory connectionFactory,
    CodalDbSqlResilience resilience) : ICodalDbQueryExecutor
{
    public Task<IReadOnlyList<CodalDbCompanyRecord>> QueryCompaniesAsync(CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query companies", async ct =>
        {
            const string sql = """
                SELECT CoID, CoName, CoNameEnglish, CompanySymbol, CoTSESymbol, GroupID, GroupName,
                       IndustryID, IndustryName, InstCode, TseCIsinCode, TseSIsinCode, MarketID,
                       MarketName, InstrumentRef, ModifiedDateTime
                FROM Companies;
                """;

            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = CreateCommand(connection, sql);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var rows = new List<CodalDbCompanyRecord>();
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new CodalDbCompanyRecord(
                    CoID: Convert.ToInt32(reader["CoID"]),
                    CoName: Str(reader["CoName"]) ?? string.Empty,
                    CoNameEnglish: Str(reader["CoNameEnglish"]),
                    CompanySymbol: Str(reader["CompanySymbol"]),
                    CoTSESymbol: Str(reader["CoTSESymbol"]),
                    GroupID: NInt(reader["GroupID"]),
                    GroupName: Str(reader["GroupName"]),
                    IndustryID: NInt(reader["IndustryID"]),
                    IndustryName: Str(reader["IndustryName"]),
                    InstCode: Str(reader["InstCode"]),
                    TseCIsinCode: Str(reader["TseCIsinCode"]),
                    TseSIsinCode: Str(reader["TseSIsinCode"]),
                    MarketID: NInt(reader["MarketID"]),
                    MarketName: Str(reader["MarketName"]),
                    InstrumentRef: reader["InstrumentRef"] is DBNull ? null : reader["InstrumentRef"].ToString(),
                    ModifiedDateTime: NDateTimeOffset(reader["ModifiedDateTime"])));
            }

            return (IReadOnlyList<CodalDbCompanyRecord>)rows;
        }, cancellationToken);

    public Task<IReadOnlyList<CodalStatementRow>> QueryStatementsAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query statements", async ct =>
        {
            await using var connection = await connectionFactory.OpenAsync(ct);

            const string headerSql = """
                SELECT Id, StmtId, CompanyId, PeriodType, FiscalYearEnd, FiscalYearEndJalali, PeriodEnd,
                       PeriodEndJalali, AnnouncementDate, IsAudited, IsRepresented, IsComposing, ModifiedDateTime
                FROM Statements
                WHERE CompanyId = @companyId AND (isDeleted = 0 OR isDeleted IS NULL);
                """;

            var headers = new List<(CodalStatementRow Row, long Id)>();
            await using (var command = CreateCommand(connection, headerSql))
            {
                command.Parameters.AddWithValue("@companyId", companyId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = Convert.ToInt64(reader["Id"]);
                    headers.Add((new CodalStatementRow(
                        Id: id,
                        StmtId: Convert.ToInt64(reader["StmtId"]),
                        CompanyId: Convert.ToInt32(reader["CompanyId"]),
                        PeriodType: Convert.ToByte(reader["PeriodType"]),
                        FiscalYearEnd: ReqDateTimeOffset(reader["FiscalYearEnd"]),
                        FiscalYearEndJalali: Str(reader["FiscalYearEndJalali"]),
                        PeriodEnd: ReqDateTimeOffset(reader["PeriodEnd"]),
                        PeriodEndJalali: Str(reader["PeriodEndJalali"]),
                        AnnouncementDate: ReqDateTimeOffset(reader["AnnouncementDate"]),
                        IsAudited: NBool(reader["IsAudited"]),
                        IsRepresented: NBool(reader["IsRepresented"]),
                        IsComposing: NBool(reader["IsComposing"]),
                        ModifiedDateTime: NDateTimeOffset(reader["ModifiedDateTime"]),
                        IncomeItems: [],
                        BalanceItems: []), id));
                }
            }

            var income = await QueryLineItemsAsync(connection, companyId, isIncome: true, ct);
            var balance = await QueryLineItemsAsync(connection, companyId, isIncome: false, ct);

            var rows = headers
                .Select(h => h.Row with
                {
                    IncomeItems = OrderedItems(income, h.Id),
                    BalanceItems = OrderedItems(balance, h.Id)
                })
                .ToList();

            return (IReadOnlyList<CodalStatementRow>)rows;
        }, cancellationToken);

    public Task<IReadOnlyList<CodalMonthlyActivityRow>> QueryMonthlyActivityAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query monthly activity", async ct =>
        {
            await using var connection = await connectionFactory.OpenAsync(ct);

            const string headerSql = """
                SELECT Id, CompanyId, Month, Year, FiscalYearEnd, ModifiedDateTime
                FROM MonthlyActivity
                WHERE CompanyId = @companyId;
                """;

            var headers = new List<(CodalMonthlyActivityRow Row, long Id)>();
            await using (var command = CreateCommand(connection, headerSql))
            {
                command.Parameters.AddWithValue("@companyId", companyId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var id = Convert.ToInt64(reader["Id"]);
                    headers.Add((new CodalMonthlyActivityRow(
                        Id: id,
                        CompanyId: Convert.ToInt32(reader["CompanyId"]),
                        Month: Convert.ToByte(reader["Month"]),
                        Year: Convert.ToInt32(reader["Year"]),
                        FiscalYearEnd: Str(reader["FiscalYearEnd"]),
                        ModifiedDateTime: NDateTimeOffset(reader["ModifiedDateTime"]),
                        Products: []), id));
                }
            }

            const string amountSql = """
                SELECT maa.MonthlyActivityId, maa.ProductId, maa.ProductTitle, maa.ProductProduceAmount,
                       maa.ProductSaleAmount, maa.ProductSaleRate, maa.ProductSaleValue, maa.ProductUnit
                FROM MonthlyActivityAmounts maa
                JOIN MonthlyActivity ma ON ma.Id = maa.MonthlyActivityId
                WHERE ma.CompanyId = @companyId;
                """;

            var amounts = new List<(long ActivityId, CodalMonthlyActivityAmount Amount)>();
            await using (var command = CreateCommand(connection, amountSql))
            {
                command.Parameters.AddWithValue("@companyId", companyId);
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    amounts.Add((Convert.ToInt64(reader["MonthlyActivityId"]), new CodalMonthlyActivityAmount(
                        ProductId: Convert.ToInt32(reader["ProductId"]),
                        ProductTitle: Str(reader["ProductTitle"]),
                        ProductProduceAmount: Convert.ToInt64(reader["ProductProduceAmount"]),
                        ProductSaleAmount: Convert.ToInt64(reader["ProductSaleAmount"]),
                        ProductSaleRate: Convert.ToDecimal(reader["ProductSaleRate"]),
                        ProductSaleValue: Convert.ToInt64(reader["ProductSaleValue"]),
                        ProductUnit: Str(reader["ProductUnit"]))));
                }
            }

            var rows = headers
                .Select(h => h.Row with
                {
                    Products = amounts
                        .Where(a => a.ActivityId == h.Id)
                        .Select(a => a.Amount)
                        .OrderBy(p => p.ProductId)
                        .ToList()
                })
                .ToList();

            return (IReadOnlyList<CodalMonthlyActivityRow>)rows;
        }, cancellationToken);

    public Task<IReadOnlyList<CodalRatioRow>> QueryFinancialRatiosAsync(
        int companyId,
        IReadOnlyCollection<int> mappedItemIds,
        CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query financial ratios", async ct =>
        {
            if (mappedItemIds.Count == 0)
                return (IReadOnlyList<CodalRatioRow>)[];

            // Build a parameterized IN list; bind each id individually to avoid SQL injection.
            var paramNames = mappedItemIds.Select((_, i) => $"@item{i}").ToList();
            var sql = $"""
                SELECT fr.Id, fr.CompanyId, fr.FiscalYearEnd, fr.JalaliFiscalYearEnd,
                       fr.PeriodEnd, fr.JalaliPeriodEnd, fr.PeriodType,
                       fr.IsAudited, fr.IsRepresented, fr.IsComposing,
                       fr.ItemID, fr.ItemValue, fr.ModifiedDateTime
                FROM FinancialRatios fr
                WHERE fr.CompanyId = @companyId
                  AND fr.ItemID IN ({string.Join(", ", paramNames)})
                ORDER BY fr.PeriodEnd DESC, fr.ItemID;
                """;

            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("@companyId", companyId);
            var ids = mappedItemIds.ToList();
            for (var i = 0; i < ids.Count; i++)
                command.Parameters.AddWithValue(paramNames[i], ids[i]);

            var rows = new List<CodalRatioRow>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new CodalRatioRow(
                    Id: Convert.ToInt64(reader["Id"]),
                    CompanyId: Convert.ToInt32(reader["CompanyId"]),
                    FiscalYearEnd: ReqDateTimeOffset(reader["FiscalYearEnd"]),
                    JalaliFiscalYearEnd: Str(reader["JalaliFiscalYearEnd"]),
                    PeriodEnd: ReqDateTimeOffset(reader["PeriodEnd"]),
                    JalaliPeriodEnd: Str(reader["JalaliPeriodEnd"]),
                    PeriodType: Convert.ToInt32(reader["PeriodType"]),
                    IsAudited: NBool(reader["IsAudited"]),
                    IsRepresented: NBool(reader["IsRepresented"]),
                    IsComposing: NBool(reader["IsComposing"]),
                    ItemId: Convert.ToInt32(reader["ItemID"]),
                    ItemValue: Convert.ToDouble(reader["ItemValue"]),
                    ModifiedDateTime: NDateTimeOffset(reader["ModifiedDateTime"])));
            }

            return (IReadOnlyList<CodalRatioRow>)rows;
        }, cancellationToken);

    public Task<IReadOnlyList<int>> QueryChangedCompanyIdsAsync(
        DateTimeOffset? since,
        CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query changed company ids", async ct =>
        {
            // UNION across the four mutable tables; DISTINCT companies whose source ModifiedDateTime
            // is newer than the watermark. When @since is NULL this returns every company present in
            // any of the source tables (full-reload mode).
            const string sql = """
                SELECT DISTINCT CoID AS CompanyId
                FROM Companies WHERE @since IS NULL OR ModifiedDateTime > @since
                UNION
                SELECT DISTINCT CompanyId
                FROM Statements
                WHERE (isDeleted = 0 OR isDeleted IS NULL)
                  AND (@since IS NULL OR ModifiedDateTime > @since)
                UNION
                SELECT DISTINCT CompanyId
                FROM MonthlyActivity
                WHERE @since IS NULL OR ModifiedDateTime > @since
                UNION
                SELECT DISTINCT CompanyId
                FROM FinancialRatios
                WHERE @since IS NULL OR ModifiedDateTime > @since;
                """;

            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = CreateCommand(connection, sql);
            command.Parameters.AddWithValue("@since", (object?)since ?? DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(ct);

            var ids = new List<int>();
            while (await reader.ReadAsync(ct))
            {
                ids.Add(Convert.ToInt32(reader["CompanyId"]));
            }

            return (IReadOnlyList<int>)ids;
        }, cancellationToken);

    public Task<DateTimeOffset?> QueryMaxModifiedDateTimeAsync(CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("query max modified", async ct =>
        {
            const string sql = """
                SELECT MAX(m) FROM (
                    SELECT MAX(ModifiedDateTime) AS m FROM Companies
                    UNION ALL SELECT MAX(ModifiedDateTime) FROM Statements WHERE (isDeleted = 0 OR isDeleted IS NULL)
                    UNION ALL SELECT MAX(ModifiedDateTime) FROM MonthlyActivity
                    UNION ALL SELECT MAX(ModifiedDateTime) FROM FinancialRatios
                ) t;
                """;

            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = CreateCommand(connection, sql);
            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : (DateTimeOffset?)ReqDateTimeOffset(result);
        }, cancellationToken);

    public Task<CodalDbHealthProbe> ProbeAsync(CancellationToken cancellationToken) =>
        resilience.ExecuteAsync("health probe", async ct =>
        {
            await using var connection = await connectionFactory.OpenAsync(ct);
            await using var command = CreateCommand(connection, "SELECT COUNT(*) FROM Companies;");
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            return new CodalDbHealthProbe(Reachable: true, CompanyCount: count, Detail: null);
        }, cancellationToken);

    private async Task<List<(long StatementId, CodalStatementLineItem Item)>> QueryLineItemsAsync(
        SqlConnection connection,
        int companyId,
        bool isIncome,
        CancellationToken cancellationToken)
    {
        var sql = isIncome
            ? """
              SELECT ia.StatementId, ia.ItemId, ii.ItemTitleEn, ia.Amount
              FROM IncomeItemAmounts ia
              JOIN Statements s ON s.Id = ia.StatementId
              JOIN IncomeItems ii ON ii.ItemId = ia.ItemId
              WHERE s.CompanyId = @companyId AND (s.isDeleted = 0 OR s.isDeleted IS NULL);
              """
            : """
              SELECT ba.StatementId, ba.ItemId, bi.ItemTitleEn, ba.Amount
              FROM BalanceSheetItemAmounts ba
              JOIN Statements s ON s.Id = ba.StatementId
              JOIN BalanceSheetItems bi ON bi.ItemId = ba.ItemId
              WHERE s.CompanyId = @companyId AND (s.isDeleted = 0 OR s.isDeleted IS NULL);
              """;

        var items = new List<(long, CodalStatementLineItem)>();
        await using var command = CreateCommand(connection, sql);
        command.Parameters.AddWithValue("@companyId", companyId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add((Convert.ToInt64(reader["StatementId"]), new CodalStatementLineItem(
                ItemId: Convert.ToInt32(reader["ItemId"]),
                ItemTitleEn: Str(reader["ItemTitleEn"]),
                Amount: Convert.ToDecimal(reader["Amount"]))));
        }

        return items;
    }

    private static IReadOnlyList<CodalStatementLineItem> OrderedItems(
        List<(long StatementId, CodalStatementLineItem Item)> all,
        long statementId) =>
        all.Where(x => x.StatementId == statementId)
            .Select(x => x.Item)
            .OrderBy(i => i.ItemId)
            .ToList();

    private SqlCommand CreateCommand(SqlConnection connection, string sql) =>
        new(sql, connection) { CommandTimeout = connectionFactory.CommandTimeoutSeconds };

    private static string? Str(object value) => value is DBNull ? null : Convert.ToString(value);

    private static int? NInt(object value) => value is DBNull ? null : Convert.ToInt32(value);

    private static bool? NBool(object value) => value is DBNull ? null : Convert.ToBoolean(value);

    private static DateTimeOffset? NDateTimeOffset(object value) =>
        value is DBNull ? null : ReqDateTimeOffset(value);

    // CodalDB stores fiscal dates without a time zone; treat as UTC for a stable, comparable value.
    private static DateTimeOffset ReqDateTimeOffset(object value) =>
        new(DateTime.SpecifyKind(Convert.ToDateTime(value), DateTimeKind.Utc));
}
