using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161ReaderTests
{
    [Fact]
    public async Task Reader_ScopesHeadersAndValuesAndNeverReturnsExcludedOrConsolidatedData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var included = Statement("NoavaranCurrentApi", "30", "CashFlow", "ThreeMonths", false, new DateOnly(2026, 6, 30));
        var consolidated = Statement("NoavaranCurrentApi", "30", "CashFlow", "ThreeMonths", true, new DateOnly(2026, 6, 30));
        db.FinancialStatements.AddRange(included, consolidated,
            Statement("NoavaranCurrentApi", "31", "CashFlow", "ThreeMonths", false, new DateOnly(2026, 6, 30)),
            Statement("OtherProvider", "30", "CashFlow", "ThreeMonths", false, new DateOnly(2026, 6, 30)),
            Statement("NoavaranCurrentApi", "30", "IncomeStatement", "ThreeMonths", false, new DateOnly(2026, 6, 30)));
        var total = Catalog("CashFlow", 128);
        var forbidden = Catalog("CashFlow", 58);
        var wrongProvider = Catalog("CashFlow", 10, "OtherProvider");
        var unregistered = Catalog("CashFlow", 999);
        db.FinancialStatementSourceItems.AddRange(total, forbidden, wrongProvider, unregistered);
        db.FinancialStatementLineItems.AddRange(
            Line(included.Id, total.Id, 500m), Line(included.Id, forbidden.Id, 900m),
            Line(included.Id, wrongProvider.Id, 700m), Line(included.Id, unregistered.Id, 12m),
            Line(consolidated.Id, total.Id, 800m));
        await db.SaveChangesAsync();

        var reader = new EfCoreCashFlowStatementReader(db);
        var headers = await reader.ReadHeadersAsync("NoavaranCurrentApi", "30");
        var lines = await reader.ReadLineItemsAsync("NoavaranCurrentApi", [included.Id, consolidated.Id], [128, 58, 10]);
        var allLines = await reader.ReadAllLineItemsAsync("NoavaranCurrentApi", [included.Id, consolidated.Id]);
        var consolidatedAvailable = await reader.ConsolidatedExistsAsync("NoavaranCurrentApi", "30");

        Assert.Single(headers.Statements);
        Assert.Equal(included.Id, headers.Statements[0].StatementId);
        Assert.Single(headers.FiscalEvidence);
        Assert.True(consolidatedAvailable);
        Assert.Equal([128], lines[included.Id].Select(x => x.SourceItemId));
        Assert.Equal(500m, lines[included.Id][0].Value);
        Assert.DoesNotContain(consolidated.Id, lines.Keys);
        Assert.True(allLines.IsComplete);
        Assert.Equal([128, 999], allLines.Items[included.Id].Select(x => x.SourceItemId).Order());
        Assert.DoesNotContain(consolidated.Id, allLines.Items.Keys);
    }

    [Fact]
    public async Task Reader_AllLineItemsFailsClosedWhenHardBoundIsExceeded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var statement = Statement("NoavaranCurrentApi", "30", "CashFlow", "ThreeMonths", false, new DateOnly(2026, 6, 30));
        db.FinancialStatements.Add(statement);
        var catalogs = Enumerable.Range(0, CashFlowReportContract.MaxCashFlowSourceLineItems + 1)
            .Select(index => Catalog("CashFlow", 2000 + index)).ToArray();
        db.FinancialStatementSourceItems.AddRange(catalogs);
        db.FinancialStatementLineItems.AddRange(catalogs.Select(catalog => Line(statement.Id, catalog.Id, 1m)));
        await db.SaveChangesAsync();

        var result = await new EfCoreCashFlowStatementReader(db).ReadAllLineItemsAsync("NoavaranCurrentApi", [statement.Id]);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Items);
        Assert.Equal(CashFlowReportContract.MaxCashFlowSourceLineItems + 1, result.ObservedLineCount);
    }

    [Fact]
    public async Task Reader_IncomeCompanionIsExactPeriodStandaloneScaleAndAtMostTwoTopVersions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var date = new DateOnly(2026, 6, 30);
        var cash = Statement("NoavaranCurrentApi", "30", "CashFlow", "ThreeMonths", false, date);
        var income = Statement("NoavaranCurrentApi", "30", "IncomeStatement", "ThreeMonths", false, date);
        income.IsAudited = true;
        var consolidated = Statement("NoavaranCurrentApi", "30", "IncomeStatement", "ThreeMonths", true, date);
        var mismatched = Statement("NoavaranCurrentApi", "30", "IncomeStatement", "SixMonths", false, date);
        db.FinancialStatements.AddRange(cash, income, consolidated, mismatched);
        var revenue = Catalog("IncomeStatement", 15);
        var profit = Catalog("IncomeStatement", 143);
        var cashFlowWrong = Catalog("CashFlow", 143);
        db.FinancialStatementSourceItems.AddRange(revenue, profit, cashFlowWrong);
        db.FinancialStatementLineItems.AddRange(Line(income.Id, revenue.Id, 1000m), Line(income.Id, profit.Id, 100m),
            Line(income.Id, cashFlowWrong.Id, 999m));
        await db.SaveChangesAsync();

        var reader = new EfCoreCashFlowStatementReader(db);
        var headers = await reader.ReadHeadersAsync("NoavaranCurrentApi", "30");
        var result = await reader.ReadIncomeStatementsAsync("NoavaranCurrentApi", "30", headers.Statements);

        var selected = Assert.Single(result);
        Assert.Equal(income.Id, selected.Header.StatementId);
        Assert.Equal([15, 143], selected.Items.Select(x => x.SourceItemId).Order());
    }

    private static NormalizedFinancialStatementRow Statement(string provider, string company, string type,
        string periodType, bool composing, DateOnly end) => new()
    {
        Id = Guid.NewGuid(), ProviderName = provider, ExternalCompanyId = company,
        ExternalStatementId = Guid.NewGuid().ToString("N"), StatementType = type, PeriodType = periodType,
        PeriodStart = end.AddMonths(-3).AddDays(1), PeriodEnd = end, SourcePayloadChecksum = Guid.NewGuid().ToString("N"),
        LastSynchronizedAt = DateTimeOffset.UtcNow, IsComposing = composing, WarningsJson = """
            [
              { "code": "JalaliFiscalYearEnd", "evidence": "1405/12/29" },
              { "assumedScale": "MillionRials", "sourceIsComposing": false }
            ]
            """
    };

    private static FinancialStatementSourceItemCatalogRow Catalog(string type, int sourceItemId, string provider = "NoavaranCurrentApi") => new()
    {
        Id = Guid.NewGuid(), ProviderName = provider, StatementType = type, SourceItemId = sourceItemId,
        TitleFa = $"item-{sourceItemId}", Unit = "MillionRials", LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static NormalizedFinancialStatementLineItemRow Line(Guid statementId, Guid catalogId, decimal value) => new()
    {
        Id = Guid.NewGuid(), FinancialStatementId = statementId, SourceItemCatalogId = catalogId, Value = value
    };
}
