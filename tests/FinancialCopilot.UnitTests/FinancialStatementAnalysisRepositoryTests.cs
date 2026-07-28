using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialStatementAnalysisRepositoryTests
{
    [Fact]
    public async Task ListCompanyStatementsAsync_ReadsCurrentNadpcoEvidenceShapeAndStructuralVariantFlags()
    {
        await using var db = CreateDb();
        var statementId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "30",
            Name = "Sample",
            CompanySymbol = "TEST",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-30T08:00:00Z")
        });
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = statementId,
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "30",
            ExternalStatementId = "123",
            StatementType = FinancialStatementType.IncomeStatement.ToString(),
            StatementTitle = "Sample statement",
            PeriodType = "TwelveMonths",
            PeriodStart = new DateOnly(2025, 3, 21),
            PeriodEnd = new DateOnly(2026, 3, 20),
            SourcePayloadChecksum = "checksum",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-30T08:00:00Z"),
            IsAudited = true,
            IsRepresented = true,
            IsComposing = true,
            WarningsJson = """
                [
                  {
                    "code": "NadpcoApiStatementSelection",
                    "jalaliFiscalYearEnd": "1404/12/29",
                    "jalaliPeriodEnd": "1404/12/29",
                    "jalaliAnouncementDate": "1405/04/09 09:23:24",
                    "anouncementDate": "2026-06-30T08:00:00Z",
                    "isAudited": false,
                    "isRepresented": false,
                    "isComposing": false
                  }
                ]
                """
        });
        db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
        {
            Id = Guid.NewGuid(),
            FinancialStatementId = statementId,
            MetricCode = "REVENUE",
            Value = 42m
        });
        await db.SaveChangesAsync();

        var snapshot = Assert.Single(
            await new EfCoreFinancialStatementAnalysisRepository(db)
                .ListCompanyStatementsAsync("30", CancellationToken.None));

        Assert.Equal("1404/12/29", snapshot.JalaliFiscalYearEnd);
        Assert.Equal("1404/12/29", snapshot.JalaliPeriodEnd);
        Assert.Equal("1405/04/09 09:23:24", snapshot.JalaliAnnouncementDate);
        Assert.Equal(DateTimeOffset.Parse("2026-06-30T08:00:00Z"), snapshot.AnnouncementDate);
        Assert.True(snapshot.IsAudited);
        Assert.True(snapshot.IsRepresented);
        Assert.True(snapshot.IsComposing);
        Assert.Equal(42m, snapshot.LineItems["REVENUE"]);
    }

    [Fact]
    public async Task ListCompanyStatementsAsync_CollapsesDuplicateMetricCodesWithinOneStatement()
    {
        await using var db = CreateDb();
        var statementId = Guid.NewGuid();

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "30",
            Name = "Sample",
            CompanySymbol = "TEST",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-30T08:00:00Z")
        });
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = statementId,
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = "30",
            ExternalStatementId = "123",
            StatementType = FinancialStatementType.IncomeStatement.ToString(),
            StatementTitle = "Sample statement",
            PeriodType = "TwelveMonths",
            PeriodStart = new DateOnly(2025, 3, 21),
            PeriodEnd = new DateOnly(2026, 3, 20),
            SourcePayloadChecksum = "checksum",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-06-30T08:00:00Z")
        });
        db.FinancialStatementLineItems.AddRange(
            new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(),
                FinancialStatementId = statementId,
                SourceItemCatalogId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                MetricCode = "FINANCE_COSTS",
                Value = null
            },
            new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(),
                FinancialStatementId = statementId,
                SourceItemCatalogId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                MetricCode = "FINANCE_COSTS",
                Value = 125m
            });
        await db.SaveChangesAsync();

        var snapshot = Assert.Single(
            await new EfCoreFinancialStatementAnalysisRepository(db)
                .ListCompanyStatementsAsync("30", CancellationToken.None));

        Assert.Single(snapshot.LineItems);
        Assert.Equal(125m, snapshot.LineItems["FINANCE_COSTS"]);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
