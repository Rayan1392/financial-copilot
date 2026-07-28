using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class CompanyDisclosureFeed112Tests
{
    [Fact]
    public async Task QueryAsync_ReturnsAllFourDisclosureTypesWithCanonicalCompanyLinkage()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1", "FOOLAD", "Foolad Mobarakeh");
        AddMonthly(db, "monthly", company: company);
        AddStatement(db, "income", "IncomeStatement", company, statementTitle: "Foolad Mobarakeh income statement");
        AddStatement(db, "balance", "BalanceSheet", company);
        AddStatement(db, "cash", "CashFlow", company);
        await db.SaveChangesAsync();

        var page = await Repository(db).QueryAsync(new CompanyDisclosureFeedQuery(ConsolidationScope: DisclosureConsolidationScope.Both));

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(
            [CompanyDisclosureType.MonthlyProductionSales, CompanyDisclosureType.IncomeStatement,
             CompanyDisclosureType.BalanceSheet, CompanyDisclosureType.CashFlowStatement],
            page.Items.Select(item => item.Type).Order());
        Assert.All(page.Items, item =>
        {
            Assert.Equal(company.Id, item.CompanyId);
            Assert.Equal("FOOLAD", item.Symbol);
            Assert.Equal("Foolad Mobarakeh", item.CompanyName);
            Assert.Equal(DisclosureCoverageStatus.Complete, item.CoverageStatus);
        });
        var monthly = Assert.Single(page.Items, item => item.Type == CompanyDisclosureType.MonthlyProductionSales);
        Assert.Contains("دوره منتهی به", monthly.Title);
        Assert.DoesNotContain("2026/07/31", monthly.Title);
        var incomeStatement = Assert.Single(page.Items, item => item.Type == CompanyDisclosureType.IncomeStatement);
        Assert.StartsWith("صورت مالی دوره منتهی به ", incomeStatement.Title);
        Assert.DoesNotContain("Foolad Mobarakeh", incomeStatement.Title);
        Assert.Equal("Quarterly", incomeStatement.ReportingPeriodType);
    }

    [Fact]
    public async Task QueryAsync_ExposesUnmappedRowsWithoutInventingCompanyIdentity()
    {
        await using var db = CreateDb();
        AddMonthly(db, "orphan", externalCompanyId: "missing-company");
        await db.SaveChangesAsync();

        var page = await Repository(db).QueryAsync(new CompanyDisclosureFeedQuery());

        var item = Assert.Single(page.Items);
        Assert.Null(item.CompanyId);
        Assert.Null(item.Symbol);
        Assert.Null(item.CompanyName);
        Assert.Equal(DisclosureCoverageStatus.UnmappedCompany, item.CoverageStatus);
        Assert.Equal(DisclosureCoverageStatus.UnmappedCompany, page.CoverageStatus);
    }

    [Fact]
    public async Task QueryAsync_UsesNonConsolidatedByDefaultAndHonorsConsolidationScope()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1");
        AddStatement(db, "standalone", "IncomeStatement", company, isComposing: false);
        AddStatement(db, "consolidated", "IncomeStatement", company, isComposing: true);
        await db.SaveChangesAsync();

        var repository = Repository(db);
        var defaultPage = await repository.QueryAsync(new CompanyDisclosureFeedQuery());
        var consolidated = await repository.QueryAsync(new CompanyDisclosureFeedQuery(ConsolidationScope: DisclosureConsolidationScope.Consolidated));
        var both = await repository.QueryAsync(new CompanyDisclosureFeedQuery(ConsolidationScope: DisclosureConsolidationScope.Both));

        Assert.Equal(["standalone"], defaultPage.Items.Select(item => item.SourceRecordId));
        Assert.Equal(["consolidated"], consolidated.Items.Select(item => item.SourceRecordId));
        Assert.Equal(2, both.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_SelectsNewestSuccessfulRevisionAndDoesNotDuplicateCorrectedDisclosure()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1");
        AddMonthly(db, "old", company: company, receivedAt: At(1), reportType: "ProductSales");
        AddMonthly(db, "correction", company: company, receivedAt: At(3), reportType: "ProductSales");
        db.SyncRuns.AddRange(
            new DataSyncRunRow { Id = Guid.NewGuid(), IdempotencyKey = "failed-newer", Dataset = "MonthlyProductionSales", ExternalReference = "1", Status = "Failed", RequestedAt = At(4), ErrorCount = 1 },
            new DataSyncRunRow { Id = Guid.NewGuid(), IdempotencyKey = "running-newest", Dataset = "MonthlyProductionSales", ExternalReference = "1", Status = "Running", RequestedAt = At(5) });
        await db.SaveChangesAsync();

        var page = await Repository(db).QueryAsync(new CompanyDisclosureFeedQuery());

        var item = Assert.Single(page.Items);
        Assert.Equal("correction", item.SourceRecordId);
        Assert.True(item.IsRevised);
        Assert.Equal(2, item.RevisionNumber);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task QueryAsync_LeavesPublicationDateUnknownAndUsesReceiptThenStableIdOrdering()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1");
        AddMonthly(db, "dated", company: company, receivedAt: At(1), reportType: "Dated", publishedAt: new DateOnly(2026, 8, 1));
        AddMonthly(db, "b", company: company, receivedAt: At(2), reportType: "B");
        AddMonthly(db, "a", company: company, receivedAt: At(2), reportType: "A");
        AddMonthly(db, "older", company: company, receivedAt: At(1), reportType: "Older");
        await db.SaveChangesAsync();

        var repository = Repository(db);
        var page = await repository.QueryAsync(new CompanyDisclosureFeedQuery());
        var publicationRange = await repository.QueryAsync(new CompanyDisclosureFeedQuery(
            PublishedFrom: new DateOnly(2026, 8, 1), PublishedTo: new DateOnly(2026, 8, 1)));

        Assert.Equal(new DateOnly(2026, 8, 1), page.Items[0].PublishedAt);
        Assert.All(page.Items.Skip(1), item => Assert.Null(item.PublishedAt));
        Assert.Equal(["monthly:Provider:dated", "monthly:Provider:a", "monthly:Provider:b", "monthly:Provider:older"],
            page.Items.Select(item => item.DisclosureId));
        Assert.Equal(["dated"], publicationRange.Items.Select(item => item.SourceRecordId));
    }

    [Fact]
    public async Task QueryAsync_AppliesTypeAndProviderAsOrWithinFilterAndAndAcrossFilters()
    {
        await using var db = CreateDb();
        var first = AddCompany(db, "1", "ALPHA");
        var second = AddCompany(db, "2", "BETA");
        AddMonthly(db, "a-month", "ProviderA", first, reportType: "Monthly");
        AddStatement(db, "a-income", "IncomeStatement", first, provider: "ProviderA");
        AddStatement(db, "b-income", "IncomeStatement", second, provider: "ProviderB");
        await db.SaveChangesAsync();

        var page = await Repository(db).QueryAsync(new CompanyDisclosureFeedQuery(
            [CompanyDisclosureType.MonthlyProductionSales, CompanyDisclosureType.IncomeStatement],
            SymbolOrCompany: "ALPHA",
            ProviderNames: ["ProviderA", "ProviderB"]));

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.Equal("ProviderA", item.ProviderName));
        Assert.Equal([CompanyDisclosureType.MonthlyProductionSales, CompanyDisclosureType.IncomeStatement],
            page.Items.Select(item => item.Type).Order());
    }

    [Fact]
    public async Task QueryAsync_IncludesReceiptRangeBoundariesAndPaginatesWithoutOmissions()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1");
        AddMonthly(db, "first", company: company, receivedAt: At(1), reportType: "First");
        AddMonthly(db, "middle", company: company, receivedAt: At(2), reportType: "Middle");
        AddMonthly(db, "last", company: company, receivedAt: At(3), reportType: "Last");
        await db.SaveChangesAsync();

        var repository = Repository(db);
        var query = new CompanyDisclosureFeedQuery(ReceivedFrom: At(1), ReceivedTo: At(3), PageSize: 2);
        var firstPage = await repository.QueryAsync(query);
        var secondPage = await repository.QueryAsync(query with { Page = 2 });

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(3, firstPage.Items.Concat(secondPage.Items).Select(item => item.DisclosureId).Distinct().Count());
        Assert.Contains(firstPage.Items.Concat(secondPage.Items), item => item.SourceRecordId == "first");
        Assert.Contains(firstPage.Items.Concat(secondPage.Items), item => item.SourceRecordId == "last");
    }

    [Fact]
    public async Task QueryAsync_ReportsPersistedFreshnessForNonEmptyAndCompleteCoverageForEmptyPage()
    {
        await using var db = CreateDb();
        AddMonthly(db, "unmapped", externalCompanyId: "missing-company");
        await db.SaveChangesAsync();

        var repository = Repository(db);
        var nonEmpty = await repository.QueryAsync(new CompanyDisclosureFeedQuery());
        var empty = await repository.QueryAsync(new CompanyDisclosureFeedQuery(SymbolOrCompany: "does-not-exist"));

        Assert.Equal(DisclosureCoverageStatus.UnmappedCompany, nonEmpty.CoverageStatus);
        Assert.Equal("PersistedNormalizedRecord", Assert.Single(nonEmpty.Items).FreshnessReasonCode);
        Assert.Empty(empty.Items);
        Assert.Equal(DisclosureCoverageStatus.Complete, empty.CoverageStatus);
    }

    [Fact]
    public async Task QueryAsync_ReadsOnlyPersistedRowsAndRequiresNoProviderClient()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "1");
        AddMonthly(db, "persisted", company: company);
        await db.SaveChangesAsync();

        var result = await Repository(db).QueryAsync(new CompanyDisclosureFeedQuery());

        Assert.Single(result.Items);
    }

    private static CompanyDisclosureFeedRepository Repository(FinancialIngestionDbContext db) => new(db);

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static NormalizedCompanyRow AddCompany(FinancialIngestionDbContext db, string externalId, string symbol = "COMP", string? name = null)
    {
        var company = new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "Provider", ExternalCompanyId = externalId,
            Name = name ?? symbol, Ticker = symbol, LastSynchronizedAt = At(10)
        };
        db.Companies.Add(company);
        return company;
    }

    private static void AddMonthly(FinancialIngestionDbContext db, string id, string provider = "Provider",
        NormalizedCompanyRow? company = null, string? externalCompanyId = null, DateTimeOffset? receivedAt = null,
        string reportType = "ProductSales", DateOnly? publishedAt = null) => db.MonthlyReports.Add(new NormalizedMonthlyReportRow
    {
        Id = Guid.NewGuid(), ProviderName = provider, ExternalCompanyId = externalCompanyId ?? company?.ExternalCompanyId ?? "1",
        ExternalReportId = id, CompanyId = company?.Id, PeriodStart = new DateOnly(2026, 7, 1),
        PeriodEnd = new DateOnly(2026, 7, 31), VendorPeriodDate = new DateOnly(2026, 7, 31),
        ReportType = reportType, SourcePayloadChecksum = id, LastSynchronizedAt = receivedAt ?? At(1), PublishedAt = publishedAt
    });

    private static void AddStatement(FinancialIngestionDbContext db, string id, string type, NormalizedCompanyRow company,
        string provider = "Provider", bool isComposing = false, string? statementTitle = null) => db.FinancialStatements.Add(new NormalizedFinancialStatementRow
    {
        Id = Guid.NewGuid(), ProviderName = provider, ExternalCompanyId = company.ExternalCompanyId,
        ExternalStatementId = id, CompanyId = company.Id, StatementType = type, PeriodType = "Quarterly",
        PeriodStart = new DateOnly(2026, 4, 1), PeriodEnd = new DateOnly(2026, 6, 30),
        SourcePayloadChecksum = id, LastSynchronizedAt = At(1), IsComposing = isComposing, StatementTitle = statementTitle
    });

    private static DateTimeOffset At(int day) => new(2026, 8, day, 0, 0, 0, TimeSpan.FromHours(3.5));
}
