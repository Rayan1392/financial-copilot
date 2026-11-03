using FinancialCopilot.Application.FinancialData;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class FinancialStatementValueSearch131PostgreSqlTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private const string Provider = "NoavaranCurrentApi";
    private const string GrossProfitTitle = "سود ناخالص";
    private const string RevenueTitle = "فروش خالص و درآمد ارائه خدمات";

    [SkippableFact]
    public async Task ProductionLikeFixture_MatchesBothCluesAndPreservesEvidence()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var companyId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        var grossProfitSource = Guid.NewGuid();
        var revenueSource = Guid.NewGuid();
        db.Markets.Add(Market());
        db.Companies.Add(EligibleCompany("15624", "آترا زیست آری", "داترا"));
        db.FinancialStatements.Add(new()
        {
            Id = statementId, ProviderName = Provider, ExternalCompanyId = "15624",
            ExternalStatementId = "548219", StatementType = nameof(FinancialStatementType.IncomeStatement),
            PeriodType = "ThreeMonths", PeriodStart = new(2026, 3, 21), PeriodEnd = new(2026, 6, 21),
            PublishedAt = new(2026, 7, 22), LastSynchronizedAt = new(2026, 7, 23, 8, 0, 0, TimeSpan.Zero),
            CompanyId = companyId
        });
        // The local company is intentionally present so the fixture proves the stronger path.
        db.Companies.Add(new() { Id = companyId, ProviderName = Provider, ExternalCompanyId = "local-15624", Name = "آترا زیست آری", Ticker = "داترا" });
        db.FinancialStatementSourceItems.AddRange(
            Source(grossProfitSource, GrossProfitTitle, 1), Source(revenueSource, RevenueTitle, 2));
        db.FinancialStatementSourceItemMetricMappings.AddRange(
            Mapping(grossProfitSource, "GROSS_PROFIT"), Mapping(revenueSource, "REVENUE"));
        db.FinancialStatementLineItems.AddRange(
            Line(statementId, 2_580_407m, "GROSS_PROFIT", grossProfitSource),
            Line(statementId, 3_300_508m, "REVENUE", revenueSource),
            Line(statementId, 3_300_508m, "GROSS_PROFIT", null));
        await db.SaveChangesAsync();

        var result = await Service(db).SearchAsync(new(Provider, FinancialStatementType.IncomeStatement,
            [new(2_580_407m, SourceTitle: GrossProfitTitle), new(3_300_508m, SourceTitle: RevenueTitle)]));

        var match = Assert.Single(result.Matches);
        Assert.Equal("داترا", match.Symbol);
        Assert.Equal("آترا زیست آری", match.CompanyName);
        Assert.Equal("548219", match.ExternalStatementId);
        Assert.Equal(["GROSS_PROFIT", "REVENUE"], match.Items.Select(i => i.MetricCode!).OrderBy(x => x).ToArray());
        Assert.Contains(match.Items, i => i.Value == 2_580_407m && i.SourceTitle == GrossProfitTitle);
        Assert.Contains(match.Items, i => i.Value == 3_300_508m && i.SourceTitle == RevenueTitle);
    }

    [SkippableFact]
    public async Task NullLocalCompanyId_UsesProviderMapping_AndLocalIdWinsWhenConflicting()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var localId = Guid.NewGuid();
        db.Markets.Add(Market());
        db.Companies.Add(new() { Id = localId, ProviderName = Provider, Name = "Local", Ticker = "LOCAL" });
        db.Companies.AddRange(
            EligibleCompany("fallback", "Fallback", "FALLBACK"),
            EligibleCompany("conflict", "Mapped", "MAPPED"));
        var fallback = Statement("fallback", "fallback-statement", null, new(2026, 7, 1));
        var conflict = Statement("conflict", "conflict-statement", localId, new(2026, 8, 1));
        db.FinancialStatements.AddRange(fallback, conflict);
        db.FinancialStatementLineItems.AddRange(Line(fallback.Id, 11m, "REVENUE", null), Line(conflict.Id, 22m, "REVENUE", null));
        await db.SaveChangesAsync();

        var service = Service(db);
        var mapped = Assert.Single((await service.SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(11m, "REVENUE")]))).Matches);
        var local = Assert.Single((await service.SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(22m, "REVENUE")]))).Matches);
        Assert.Equal("FALLBACK", mapped.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.ProviderExternalMapping, mapped.ResolutionStatus);
        Assert.Equal("LOCAL", local.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.LocalCompanyId, local.ResolutionStatus);
    }

    [SkippableFact]
    public async Task ExactDecimalEquality_DoesNotMatchRoundedValue()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var statement = Statement("decimal", "decimal-statement", null, new(2026, 8, 1));
        db.Markets.Add(Market());
        db.Companies.Add(EligibleCompany("decimal", "Decimal", "DEC"));
        db.FinancialStatements.Add(statement);
        db.FinancialStatementLineItems.Add(Line(statement.Id, 123.456789m, "REVENUE", null));
        await db.SaveChangesAsync();

        var result = await Service(db).SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(123.456788m, "REVENUE")]));
        Assert.Empty(result.Matches);
        Assert.Equal(FinancialStatementValueSearchOutcome.NoMatch, result.Outcome);
    }

    [SkippableFact]
    public async Task LatestStatementIsSelectedBeforeMatching_AndSplitStatementsDoNotCombine()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var older = Statement("history", "older", null, new(2026, 7, 1));
        var latest = Statement("history", "latest", null, new(2026, 8, 1));
        db.Markets.Add(Market());
        db.Companies.Add(EligibleCompany("history", "History", "HIS"));
        db.FinancialStatements.AddRange(older, latest);
        db.FinancialStatementLineItems.AddRange(Line(older.Id, 1m, "REVENUE", null), Line(latest.Id, 2m, "GROSS_PROFIT", null));
        await db.SaveChangesAsync();

        var result = await Service(db).SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(1m), new(2m)]));
        Assert.Empty(result.Matches);
    }

    [SkippableFact]
    public async Task DuplicateRepresentations_ReturnOneCanonicalEvidenceItem()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var statement = Statement("duplicate", "duplicate-statement", null, new(2026, 8, 1));
        var source = Guid.NewGuid();
        db.Markets.Add(Market());
        db.Companies.Add(EligibleCompany("duplicate", "Duplicate", "DUP"));
        db.FinancialStatements.Add(statement);
        db.FinancialStatementSourceItems.Add(Source(source, RevenueTitle, 3));
        db.FinancialStatementLineItems.AddRange(Line(statement.Id, 44m, "REVENUE", null), Line(statement.Id, 44m, null, source));
        await db.SaveChangesAsync();

        var item = Assert.Single(Assert.Single((await Service(db).SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(44m)]))).Matches).Items);
        Assert.Equal("REVENUE", item.MetricCode);
        Assert.Single(item.DuplicateLineItemIds);
    }

    [SkippableFact]
    public async Task MatchingUnresolvedStatement_IsReportedWithoutAguessedSymbol()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var statement = Statement("unresolved", "unresolved-statement", null, new(2026, 8, 1));
        db.FinancialStatements.Add(statement);
        db.FinancialStatementLineItems.Add(Line(statement.Id, 55m, "REVENUE", null));
        await db.SaveChangesAsync();

        var match = Assert.Single((await Service(db).SearchAsync(new(Provider, FinancialStatementType.IncomeStatement, [new(55m, "REVENUE")]))).Matches);
        Assert.Null(match.Symbol);
        Assert.Equal(FinancialStatementCompanyResolutionStatus.Unresolved, match.ResolutionStatus);
    }

    private static FinancialStatementValueSearchService Service(FinancialIngestionDbContext db)
    {
        var definitions = new[] { Definition("REVENUE"), Definition("GROSS_PROFIT") };
        var registry = new FinancialMetricRegistry(definitions, []);
        return new(db, new MetricAliasResolver(registry), registry);
    }

    private static FinancialMetricDefinition Definition(string code) => new(
        new MetricCode(code), new MetricVersion("v1"), code, code, MetricCategory.Profitability,
        new MetricUnit("amount", "Amount"), new DateOnly(2020, 1, 1), null, [], [], [], []);

    private static NormalizedFinancialStatementRow Statement(string externalCompanyId, string externalStatementId, Guid? companyId, DateOnly periodEnd) => new()
    {
        Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = externalCompanyId,
        ExternalStatementId = externalStatementId, StatementType = nameof(FinancialStatementType.IncomeStatement),
        PeriodType = "ThreeMonths", PeriodStart = periodEnd.AddMonths(-3), PeriodEnd = periodEnd,
        LastSynchronizedAt = new(periodEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), CompanyId = companyId
    };

    private static NormalizedCompanyRow EligibleCompany(string externalCompanyId, string name, string symbol) => new()
    {
        Id = Guid.NewGuid(), ProviderName = Provider, ExternalCompanyId = externalCompanyId,
        Name = name, CompanySymbol = symbol, MarketId = Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428"),
        PrecedencyRight = 0, LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static NormalizedMarketRow Market() => new()
    {
        Id = Guid.Parse("037c69ad-f519-419f-ae62-59003b6b2428"), ProviderName = Provider,
        ExternalId = "bourse", Name = "Bourse", LastSynchronizedAt = DateTimeOffset.UtcNow
    };

    private static NormalizedFinancialStatementLineItemRow Line(Guid statementId, decimal value, string? metricCode, Guid? sourceId) => new()
        { Id = Guid.NewGuid(), FinancialStatementId = statementId, Value = value, MetricCode = metricCode, SourceItemCatalogId = sourceId };

    private static FinancialStatementSourceItemCatalogRow Source(Guid id, string title, int sourceItemId) => new()
        { Id = id, ProviderName = Provider, StatementType = nameof(FinancialStatementType.IncomeStatement), SourceItemId = sourceItemId, TitleFa = title, LastSynchronizedAt = DateTimeOffset.UtcNow };

    private static FinancialStatementSourceItemMetricMappingRow Mapping(Guid sourceId, string metricCode) => new()
        { Id = Guid.NewGuid(), SourceItemCatalogId = sourceId, MetricCode = metricCode };
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FinancialStatementValueSearch131PostgreSqlCollection :
    ICollectionFixture<FinancialStatementValueSearch131PostgreSqlFixture>
{
    public const string Name = "Feature 131 local PostgreSQL";
}

public sealed class FinancialStatementValueSearch131PostgreSqlFixture : IAsyncLifetime
{
    private const string ConnectionStringVariable = "FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING";
    private NpgsqlConnectionStringBuilder? admin;

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            UnavailableReason = $"Set {ConnectionStringVariable} to an isolated local PostgreSQL admin connection string.";
            return;
        }

        try
        {
            admin = new NpgsqlConnectionStringBuilder(configured);
            if (string.IsNullOrWhiteSpace(admin.Database) ||
                admin.Database.Equals("financial_copilot", StringComparison.OrdinalIgnoreCase) ||
                admin.Database.StartsWith("financial_copilot_", StringComparison.OrdinalIgnoreCase))
            {
                UnavailableReason = "Feature 131 requires an isolated PostgreSQL administrative database, not the application database.";
                admin = null;
                return;
            }

            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
        }
        catch (Exception exception)
        {
            UnavailableReason = $"Local PostgreSQL is unavailable: {exception.GetType().Name}: {exception.Message}";
            admin = null;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<LocalPostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        Skip.If(UnavailableReason is not null, UnavailableReason);
        var databaseName = $"feature131_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(admin!.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var database = new LocalPostgreSqlTestDatabase(admin.ConnectionString, databaseName, target.ConnectionString);
        await using (var context = database.CreateProviderContext())
            await context.Database.MigrateAsync();
        await using (var context = database.CreateContext())
            await context.Database.MigrateAsync();
        return database;
    }
}

public sealed class LocalPostgreSqlTestDatabase(
    string adminConnectionString,
    string databaseName,
    string connectionString) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public FinancialIngestionDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseNpgsql(ConnectionString).Options);

    public FinancialProviderDbContext CreateProviderContext() =>
        new(new DbContextOptionsBuilder<FinancialProviderDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using (var terminate = connection.CreateCommand())
        {
            terminate.CommandText = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()";
            terminate.Parameters.AddWithValue("database", databaseName);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = connection.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\"";
        await drop.ExecuteNonQueryAsync();
    }
}
