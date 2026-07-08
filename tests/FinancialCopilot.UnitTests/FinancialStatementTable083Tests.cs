using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialStatementTable083Tests
{
    [Fact]
    public void BuildQuery_IncomeStatementAlias_ExtractsStatementTypeAndCompany()
    {
        var query = FinancialStatementTableIntentRules.BuildQuery("آخرین صورت سود و زیان کگل");

        Assert.Equal(FinancialStatementType.IncomeStatement, query.StatementType);
        Assert.Equal("کگل", query.CompanyQuery);
    }

    [Fact]
    public void BuildQuery_BalanceSheetAlias_ParsesPeriodAndVariantFlags()
    {
        var query = FinancialStatementTableIntentRules.BuildQuery(
            "ترازنامه ۱۲ ماهه حسابرسی شده تلفیقی کگل را نشان بده");

        Assert.Equal(FinancialStatementType.BalanceSheet, query.StatementType);
        Assert.Equal(12, query.PeriodMonths);
        Assert.True(query.IsAudited);
        Assert.True(query.IsComposing);
        Assert.Equal("کگل", query.CompanyQuery);
    }

    [Fact]
    public void LooksLikeFinancialStatementTableQuery_AnalysisQuestion_ReturnsFalse()
    {
        Assert.False(FinancialStatementTableIntentRules.LooksLikeFinancialStatementTableQuery(
            "صورت مالی غالبر را تحلیل کن"));
    }

    [Fact]
    public async Task Repository_FiltersByConfiguredProviderAndLoadsCatalogBackedLineItems()
    {
        await using var db = CreateDb();
        var selectedStatementId = Guid.NewGuid();
        SeedCompany(db, "NoavaranCurrentApi");
        SeedStatement(db, selectedStatementId, "NoavaranCurrentApi", FinancialStatementType.IncomeStatement, "SixMonths", false);
        SeedStatement(db, Guid.NewGuid(), "OtherProvider", FinancialStatementType.IncomeStatement, "SixMonths", false);
        SeedLineItem(db, selectedStatementId, 10, "درآمدهای عملیاتی", "REVENUE", 1000m);
        await db.SaveChangesAsync();

        var repository = new EfCoreFinancialStatementTableRepository(
            db,
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }));

        var source = await repository.FindLatestStatementAsync(
            new FinancialStatementTableSelection(
                "30",
                FinancialStatementType.IncomeStatement,
                "NoavaranCurrentApi",
                PeriodMonths: 6,
                IsAudited: null,
                IsRepresented: null,
                IsComposing: false),
            CancellationToken.None);
        var items = await repository.GetStatementLineItemsAsync(selectedStatementId, CancellationToken.None);

        Assert.NotNull(source);
        Assert.Equal("NoavaranCurrentApi", source.ProviderName);
        var item = Assert.Single(items);
        Assert.Equal("درآمدهای عملیاتی", item.TitleFa);
        Assert.Equal("REVENUE", item.MetricCode);
        Assert.Equal(1000m, item.Value);
    }

    [Fact]
    public async Task Repository_ClassifiesBalanceSheetSidesFromMetricCodeAndTitle()
    {
        await using var db = CreateDb();
        var statementId = Guid.NewGuid();
        SeedCompany(db, "NoavaranCurrentApi");
        SeedStatement(db, statementId, "NoavaranCurrentApi", FinancialStatementType.BalanceSheet, "TwelveMonths", false);
        SeedLineItem(db, statementId, 1, "دارایی های جاری", "CURRENT_ASSETS", 2000m);
        SeedLineItem(db, statementId, 2, "بدهی های جاری", "CURRENT_LIABILITIES", 900m);
        await db.SaveChangesAsync();

        var repository = new EfCoreFinancialStatementTableRepository(
            db,
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }));

        var items = await repository.GetStatementLineItemsAsync(statementId, CancellationToken.None);

        Assert.Contains(items, item => item.Side == FinancialStatementTableSide.Assets);
        Assert.Contains(items, item => item.Side == FinancialStatementTableSide.LiabilitiesAndEquity);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static void SeedCompany(FinancialIngestionDbContext db, string providerName)
    {
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = providerName,
            ExternalCompanyId = "30",
            Name = "گل گهر",
            CompanySymbol = "کگل",
            Ticker = "کگل",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        });
    }

    private static void SeedStatement(
        FinancialIngestionDbContext db,
        Guid id,
        string providerName,
        FinancialStatementType statementType,
        string periodType,
        bool isComposing)
    {
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = id,
            ProviderName = providerName,
            ExternalCompanyId = "30",
            ExternalStatementId = id.ToString("N"),
            StatementType = statementType.ToString(),
            PeriodType = periodType,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 6, 30),
            SourcePayloadChecksum = id.ToString("N"),
            LastSynchronizedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            IsAudited = false,
            IsRepresented = false,
            IsComposing = isComposing,
            WarningsJson = """
                [
                  { "code": "JalaliPeriodEnd", "evidence": "1405/04/09" },
                  { "code": "JalaliFiscalYearEnd", "evidence": "1405/12/29" },
                  { "code": "JalaliAnnouncementDate", "evidence": "1405/04/10" },
                  { "code": "AnnouncementDate", "evidence": "2026-07-01T00:00:00Z" }
                ]
                """
        });
    }

    private static void SeedLineItem(
        FinancialIngestionDbContext db,
        Guid statementId,
        int sourceItemId,
        string titleFa,
        string metricCode,
        decimal value)
    {
        var catalogId = Guid.NewGuid();
        db.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
        {
            Id = catalogId,
            ProviderName = "NoavaranCurrentApi",
            StatementType = FinancialStatementType.IncomeStatement.ToString(),
            SourceItemId = sourceItemId,
            TitleFa = titleFa,
            Unit = "میلیون ریال",
            LastSynchronizedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        });
        db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
        {
            Id = Guid.NewGuid(),
            FinancialStatementId = statementId,
            SourceItemCatalogId = catalogId,
            MetricCode = metricCode,
            Value = value
        });
    }
}
