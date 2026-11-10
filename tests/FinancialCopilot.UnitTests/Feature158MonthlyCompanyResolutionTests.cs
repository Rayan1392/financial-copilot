using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 158 D5: monthly trend reads the Noavaran row for the symbol on V2. Rows are invented.</summary>
public sealed class Feature158MonthlyCompanyResolutionTests
{
    private const string Symbol = "کگل";
    private const string Noavaran = "NoavaranCurrentApi";
    private const string NoavaranId = "100";

    [Fact]
    public async Task NoavaranRowWithOtherProviderTicker_ReadsNoavaranSnapshot()
    {
        await using var db = CreateDb();
        db.Companies.Add(Eligible(Symbol, NoavaranId));
        db.Companies.Add(new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "CyclicalWaves", ExternalCompanyId = "CW-1", Name = "x", Ticker = Symbol });
        SeedSnapshot(db, NoavaranId);
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("q", Symbol));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task NoavaranRowWithOtherProviderTseSymbol_ReadsNoavaranSnapshot()
    {
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "CyclicalWaves", ExternalCompanyId = "CW-1", Name = "x", TseSymbol = Symbol });
        await db.SaveChangesAsync();
        db.Companies.Add(Eligible(Symbol, NoavaranId));
        SeedSnapshot(db, NoavaranId);
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("q", Symbol));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task EligibleAndNonEligibleNoavaranRows_ReadEligibleSnapshot()
    {
        await using var db = CreateDb();
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = Noavaran, ExternalCompanyId = "9100", Name = "حق تقدم",
            TseSymbol = Symbol, CompanySymbol = Symbol, PrecedencyRight = 1, MarketId = NoavaranCompanyScope.BourseMarketId
        });
        await db.SaveChangesAsync();
        db.Companies.Add(Eligible(Symbol, NoavaranId));
        SeedSnapshot(db, NoavaranId);
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("q", Symbol));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task TwoEligibleRowsWithOneSymbol_ReturnNull()
    {
        await using var db = CreateDb();
        db.Companies.Add(Eligible(Symbol, NoavaranId));
        db.Companies.Add(Eligible(Symbol, "200"));
        SeedSnapshot(db, NoavaranId);
        await db.SaveChangesAsync();

        var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("q", Symbol));

        Assert.Null(result);
    }

    [Fact]
    public async Task FrozenV1_KeepsResolveBySymbolBehaviour()
    {
        await using var db = CreateDb();
        db.Companies.Add(Eligible(Symbol, NoavaranId));
        db.Companies.Add(new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "CyclicalWaves", ExternalCompanyId = "CW-1", Name = "x", Ticker = Symbol });
        SeedSnapshot(db, NoavaranId);
        await db.SaveChangesAsync();

        using (Feature154RoutingScope.EnterFrozenV1())
        {
            var result = await CreateUseCase(db).ExecuteAsync(new MonthlyActivityTrendQuery("q", Symbol));

            // V1 still reads the Ticker-first row of the other provider, which has no snapshot.
            Assert.Null(result);
        }
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NormalizedCompanyRow Eligible(string symbol, string externalId) => new()
    {
        Id = Guid.NewGuid(), ProviderName = Noavaran, ExternalCompanyId = externalId, Name = "شرکت " + externalId,
        TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId
    };

    private static void SeedSnapshot(FinancialIngestionDbContext db, string externalCompanyId) =>
        db.CompanyMonthlyActivityTrendSnapshots.Add(new CompanyMonthlyActivityTrendSnapshotRow
        {
            Id = Guid.NewGuid(),
            ExternalCompanyId = externalCompanyId,
            CompanySymbol = Symbol,
            CompanyName = "شرکت",
            ReportYear = 1405,
            ReportMonth = 4,
            MonthlySalesAmount = 1000m,
            CurrentMonthOutputType = 0,
            SourceProviderName = Noavaran,
            CalculatedAtUtc = DateTimeOffset.UtcNow
        });

    private static MonthlyActivityTrendQueryUseCase CreateUseCase(FinancialIngestionDbContext db)
    {
        var resolver = new CompanyResolverService(db, NullLogger<CompanyResolverService>.Instance);
        return new(
            resolver,
            new CanonicalMonthlyCompanyResolver(db, resolver),
            new EfCoreCompanyMonthlyActivityTrendSnapshotRepository(db),
            NullLogger<MonthlyActivityTrendQueryUseCase>.Instance);
    }
}
