using FinancialCopilot.Application.FinancialData;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialStatementValueSearch131Tests
{
    [Fact]
    public async Task MatchesAllValuesOnlyInTheSelectedLatestStatement()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var olderId = Guid.NewGuid();
        var latestId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = "Provider", ExternalCompanyId = "1", Name = "Company", Ticker = "SYM" });
        db.FinancialStatements.AddRange(
            Statement(olderId, companyId, new DateOnly(2026, 1, 1), "older"),
            Statement(latestId, companyId, new DateOnly(2026, 2, 1), "latest"));
        db.FinancialStatementLineItems.AddRange(
            new() { Id = Guid.NewGuid(), FinancialStatementId = olderId, MetricCode = "REVENUE", Value = 100m },
            new() { Id = Guid.NewGuid(), FinancialStatementId = latestId, MetricCode = "REVENUE", Value = 200m },
            new() { Id = Guid.NewGuid(), FinancialStatementId = latestId, MetricCode = "GROSS_PROFIT", Value = 300m });
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var result = await service.SearchAsync(new("Provider", FinancialStatementType.IncomeStatement,
            [new(200m, "REVENUE"), new(300m, "GROSS_PROFIT")]));

        var match = Assert.Single(result.Matches);
        Assert.Equal("SYM", match.Symbol);
        Assert.Equal("latest", match.ExternalStatementId);
        Assert.Equal(2, match.Items.Count);
    }

    [Fact]
    public async Task DoesNotCombineValuesFromDifferentStatements()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        db.Companies.Add(new NormalizedCompanyRow { Id = companyId, ProviderName = "Provider", ExternalCompanyId = "1", Name = "Company", Ticker = "SYM" });
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        db.FinancialStatements.AddRange(Statement(first, companyId, new DateOnly(2026, 1, 1), "first"), Statement(second, companyId, new DateOnly(2026, 2, 1), "second"));
        db.FinancialStatementLineItems.AddRange(
            new() { Id = Guid.NewGuid(), FinancialStatementId = first, MetricCode = "REVENUE", Value = 100m },
            new() { Id = Guid.NewGuid(), FinancialStatementId = second, MetricCode = "GROSS_PROFIT", Value = 300m });
        await db.SaveChangesAsync();

        var result = await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement,
            [new(100m, "REVENUE"), new(300m, "GROSS_PROFIT")]));

        Assert.Empty(result.Matches);
        Assert.Equal(FinancialStatementValueSearchOutcome.NoMatch, result.Outcome);
    }

    [Fact]
    public async Task UsesProviderExternalMappingWhenLocalCompanyIsMissing()
    {
        await using var db = CreateDb();
        var mappedId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = mappedId, ProviderName = "Provider", ExternalCompanyId = "42", Name = "Mapped Company", CompanySymbol = "MAP"
        });
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = statementId, ProviderName = "Provider", ExternalCompanyId = "42", ExternalStatementId = "mapped-statement",
            StatementType = nameof(FinancialStatementType.IncomeStatement), PeriodType = "ThreeMonths",
            PeriodStart = new(2026, 1, 1), PeriodEnd = new(2026, 3, 31), LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        db.FinancialStatementLineItems.Add(new() { Id = Guid.NewGuid(), FinancialStatementId = statementId, MetricCode = "REVENUE", Value = 123.456789m });
        await db.SaveChangesAsync();

        var result = await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement,
            [new(123.456789m, "REVENUE")]));

        var match = Assert.Single(result.Matches);
        Assert.Equal("MAP", match.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.ProviderExternalMapping, match.ResolutionStatus);
        Assert.Equal(123.456789m, Assert.Single(match.Items).Value);
    }

    [Fact]
    public async Task AppliesSourceTitleAndValueToTheSameLineItem()
    {
        await using var db = CreateDb();
        var statementId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        db.NoavaranEligibleCompanies.Add(new() { Id = Guid.NewGuid(), ProviderName = "Provider", ExternalCompanyId = "7", Name = "Company", CompanySymbol = "TITLE" });
        var statement = Statement(statementId, Guid.Empty, new DateOnly(2026, 3, 1), "title");
        statement.CompanyId = null;
        statement.ExternalCompanyId = "7";
        db.FinancialStatements.Add(statement);
        db.FinancialStatementSourceItems.Add(new() { Id = sourceId, ProviderName = "Provider", StatementType = "IncomeStatement", SourceItemId = 10, TitleFa = "Revenue title" });
        db.FinancialStatementSourceItemMetricMappings.Add(new() { Id = Guid.NewGuid(), SourceItemCatalogId = sourceId, MetricCode = "REVENUE" });
        db.FinancialStatementLineItems.AddRange(
            new() { Id = Guid.NewGuid(), FinancialStatementId = statementId, MetricCode = "REVENUE", SourceItemCatalogId = sourceId, Value = 500m },
            new() { Id = Guid.NewGuid(), FinancialStatementId = statementId, MetricCode = "GROSS_PROFIT", Value = 500m });
        await db.SaveChangesAsync();

        var result = await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement,
            [new(500m, SourceTitle: "Revenue title")]));

        var item = Assert.Single(Assert.Single(result.Matches).Items);
        Assert.Equal("REVENUE", item.MetricCode);
        Assert.Equal("Revenue title", item.SourceTitle);
    }

    [Fact]
    public async Task LocalCompanyIdTakesPrecedenceOverConflictingProviderMapping()
    {
        await using var db = CreateDb();
        var localId = Guid.NewGuid();
        db.Companies.Add(new() { Id = localId, ProviderName = "Provider", ExternalCompanyId = "local", Name = "Local", Ticker = "LOCAL" });
        db.NoavaranEligibleCompanies.Add(new() { Id = Guid.NewGuid(), ProviderName = "Provider", ExternalCompanyId = "9", Name = "Mapped", CompanySymbol = "MAPPED" });
        var statement = Statement(Guid.NewGuid(), localId, new DateOnly(2026, 3, 1), "precedence");
        statement.ExternalCompanyId = "9";
        db.FinancialStatements.Add(statement);
        db.FinancialStatementLineItems.Add(new() { Id = Guid.NewGuid(), FinancialStatementId = statement.Id, MetricCode = "REVENUE", Value = 9m });
        await db.SaveChangesAsync();

        var match = Assert.Single((await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement, [new(9m, "REVENUE")]))).Matches);
        Assert.Equal("LOCAL", match.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.LocalCompanyId, match.ResolutionStatus);
    }

    [Fact]
    public async Task CanonicalizesDuplicateSourceRepresentationsAndRetainsDiagnostics()
    {
        await using var db = CreateDb();
        var statementId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        db.NoavaranEligibleCompanies.Add(new() { Id = Guid.NewGuid(), ProviderName = "Provider", ExternalCompanyId = "11", Name = "Company", CompanySymbol = "DUP" });
        var statement = Statement(statementId, Guid.Empty, new DateOnly(2026, 3, 1), "duplicates");
        statement.ExternalCompanyId = "11";
        db.FinancialStatements.Add(statement);
        db.FinancialStatementSourceItems.Add(new() { Id = sourceId, ProviderName = "Provider", StatementType = "IncomeStatement", SourceItemId = 11, TitleFa = "Revenue" });
        db.FinancialStatementLineItems.AddRange(
            new() { Id = Guid.NewGuid(), FinancialStatementId = statementId, MetricCode = "REVENUE", Value = 11m },
            new() { Id = Guid.NewGuid(), FinancialStatementId = statementId, SourceItemCatalogId = sourceId, Value = 11m });
        await db.SaveChangesAsync();

        var item = Assert.Single(Assert.Single((await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement, [new(11m)]))).Matches).Items);
        Assert.Equal("REVENUE", item.MetricCode);
        Assert.Single(item.DuplicateLineItemIds);
    }

    [Fact]
    public async Task ReturnsMatchingStatementAsUnresolvedWhenIdentityCannotBeResolved()
    {
        await using var db = CreateDb();
        var statement = Statement(Guid.NewGuid(), Guid.Empty, new DateOnly(2026, 3, 1), "unresolved");
        statement.CompanyId = null;
        statement.ExternalCompanyId = "missing";
        db.FinancialStatements.Add(statement);
        db.FinancialStatementLineItems.Add(new() { Id = Guid.NewGuid(), FinancialStatementId = statement.Id, MetricCode = "REVENUE", Value = 77m });
        await db.SaveChangesAsync();

        var match = Assert.Single((await CreateService(db).SearchAsync(new("Provider", FinancialStatementType.IncomeStatement, [new(77m, "REVENUE")]))).Matches);
        Assert.Null(match.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.Unresolved, match.ResolutionStatus);
    }

    private static NormalizedFinancialStatementRow Statement(Guid id, Guid companyId, DateOnly periodEnd, string externalId) => new()
    {
        Id = id, CompanyId = companyId, ProviderName = "Provider", ExternalCompanyId = "1", ExternalStatementId = externalId,
        StatementType = nameof(FinancialStatementType.IncomeStatement), PeriodType = "ThreeMonths", PeriodStart = periodEnd.AddMonths(-3),
        PeriodEnd = periodEnd, LastSynchronizedAt = new DateTimeOffset(periodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
    };

    private static FinancialStatementValueSearchService CreateService(FinancialIngestionDbContext db)
    {
        var definitions = new[] { Definition("REVENUE"), Definition("GROSS_PROFIT") };
        var registry = new FinancialMetricRegistry(definitions, []);
        return new(db, new MetricAliasResolver(registry), registry);
    }

    private static FinancialMetricDefinition Definition(string code) => new(
        new MetricCode(code), new MetricVersion("v1"), code, code, MetricCategory.Profitability,
        new MetricUnit("amount", "Amount"), new DateOnly(2020, 1, 1), null, [], [], [], []);

    private static FinancialIngestionDbContext CreateDb() => new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
