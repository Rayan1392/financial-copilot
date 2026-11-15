using System.Data.Common;
using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace FinancialCopilot.IntegrationTests;

[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class CashFlowReport161PostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ScalarExecutingAsync(command, eventData, result, cancellationToken); }
        public void Reset() => Commands = 0;
    }

    [SkippableFact]
    public async Task Reader_ScopesFixtureValuesAndStaysWithinQueryAndStatementBudgets()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var now = DateTimeOffset.UtcNow;
        var companyId = "161-pg-" + Guid.NewGuid().ToString("N");
        var date = new DateOnly(2026, 9, 22);
        var catalog = new[] { 10, 48, 128, 121, 122, 123, 124, 33, 35, 125, 58, 15, 143, 313 }
            .Distinct().ToDictionary(id => id, id => new FinancialStatementSourceItemCatalogRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi",
                StatementType = id is 15 or 143 or 313 ? "IncomeStatement" : "CashFlow",
                SourceItemId = id, TitleFa = $"item-{id}", Unit = "MillionRials", LastSynchronizedAt = now
            });
        Guid bankAnchor;
        await using (var db = database.CreateContext())
        {
            db.FinancialStatementSourceItems.AddRange(catalog.Values);
            var statements = new List<NormalizedFinancialStatementRow>();
            for (var i = 0; i < 13; i++)
            {
                var statement = new NormalizedFinancialStatementRow
                {
                    Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = companyId,
                    ExternalStatementId = $"bank-{i}", StatementType = "CashFlow", PeriodType = "SixMonths",
                    PeriodStart = date.AddMonths(-6), PeriodEnd = date, LastSynchronizedAt = now,
                    SourcePayloadChecksum = $"bank-{i}", IsAudited = i < 12,
                    WarningsJson = """[{"code":"NadpcoApiStatementSelection","isComposing":false,"jalaliFiscalYearEnd":"1405/12/29","assumedScale":"MillionRials"}]"""
                };
                statements.Add(statement);
            }
            bankAnchor = statements[0].Id;
            db.FinancialStatements.AddRange(statements);
            var income = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = companyId,
                ExternalStatementId = "bank-income", StatementType = "IncomeStatement", PeriodType = "SixMonths",
                PeriodStart = date.AddMonths(-6), PeriodEnd = date, LastSynchronizedAt = now,
                SourcePayloadChecksum = "bank-income",
                WarningsJson = """[{"code":"NadpcoApiStatementSelection","isComposing":false,"jalaliFiscalYearEnd":"1405/12/29","assumedScale":"MillionRials"}]"""
            };
            db.FinancialStatements.Add(income);
            db.FinancialStatements.Add(new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = companyId,
                ExternalStatementId = "bank-consolidated", StatementType = "CashFlow", PeriodType = "SixMonths",
                PeriodStart = date.AddMonths(-6), PeriodEnd = date, LastSynchronizedAt = now,
                SourcePayloadChecksum = "bank-consolidated", IsComposing = true
            });
            db.FinancialStatementLineItems.AddRange(
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = bankAnchor, SourceItemCatalogId = catalog[128].Id, Value = 386_206_543m },
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = bankAnchor, SourceItemCatalogId = catalog[58].Id, Value = 900m },
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = bankAnchor, SourceItemCatalogId = catalog[10].Id, Value = -43_082_895m },
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = income.Id, SourceItemCatalogId = catalog[15].Id, Value = 100m },
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = income.Id, SourceItemCatalogId = catalog[143].Id, Value = 20m },
                new NormalizedFinancialStatementLineItemRow { Id = Guid.NewGuid(), FinancialStatementId = income.Id, SourceItemCatalogId = catalog[313].Id, Value = 19m });
            await db.SaveChangesAsync();
        }

        var counter = new CommandCounter();
        await using var context = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(counter).Options);
        var reader = new EfCoreCashFlowStatementReader(context);
        var headers = await reader.ReadHeadersAsync("NoavaranCurrentApi", companyId);
        var exact = await reader.ReadHeadersAtDatesAsync("NoavaranCurrentApi", companyId, [date]);
        var lines = await reader.ReadLineItemsAsync("NoavaranCurrentApi", [bankAnchor], CashFlowLineRegistry.ReadableSourceItemIds);
        var incomeRows = await reader.ReadIncomeStatementsAsync("NoavaranCurrentApi", companyId, [headers.Statements[0]]);
        var consolidated = await reader.ConsolidatedExistsAsync("NoavaranCurrentApi", companyId);

        Assert.True(counter.Commands <= 7, $"Reader exceeded 7 round trips: {counter.Commands}");
        Assert.True(headers.Statements.Count <= CashFlowReportContract.HeaderWindow);
        Assert.True(exact.Count <= CashFlowReportContract.HeaderWindow);
        Assert.True(lines.Count <= CashFlowReportContract.MaxCashFlowStatements);
        Assert.DoesNotContain(lines[bankAnchor], item => CashFlowLineRegistry.ExcludedValueItems.Contains(item.SourceItemId));
        Assert.Equal(386_206_543m, Assert.Single(lines[bankAnchor], item => item.SourceItemId == 128).Value);
        Assert.True(consolidated);
        Assert.Single(incomeRows);
        Assert.Equal(new[] { 15, 143, 313 }, incomeRows[0].Items.Select(x => x.SourceItemId).Order().ToArray());
    }
}
