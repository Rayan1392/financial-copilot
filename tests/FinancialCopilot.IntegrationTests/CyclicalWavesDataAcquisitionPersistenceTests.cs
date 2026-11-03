using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class CyclicalWavesDataAcquisitionPersistenceTests(PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateOnly CycleDate = new(2026, 8, 14);
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 2, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task CompanySource_ReadsOnlyNoavaranEligibleCompaniesView()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var eligibleCompanyId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.Companies.AddRange(
                new NormalizedCompanyRow
                {
                    Id = eligibleCompanyId,
                    ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
                    ExternalCompanyId = "271",
                    Name = "کالسیمین",
                    CompanySymbol = "فاسمین",
                    SymbolIsin = "IRO1KSIM0001",
                    MarketId = NoavaranCompanyScope.BourseMarketId,
                    PrecedencyRight = 0,
                    LastSynchronizedAt = Now
                },
                new NormalizedCompanyRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = NadpcoApiCompanyNormalizer.NadpcoApiProviderName,
                    ExternalCompanyId = "272",
                    Name = "Ineligible Company",
                    CompanySymbol = "نامعتبر",
                    SymbolIsin = "IRO1INVL0001",
                    MarketId = NoavaranCompanyScope.BourseMarketId,
                    PrecedencyRight = 1,
                    LastSynchronizedAt = Now
                });
            await context.SaveChangesAsync();
        }

        await using var queryContext = database.CreateContext();
        var companies = await new CyclicalWavesAcquisitionCompanySource(queryContext)
            .GetCompaniesAsync(CancellationToken.None);

        var company = Assert.Single(companies);
        Assert.Equal(eligibleCompanyId, company.CompanyId);
        Assert.Equal("271", company.ExternalCompanyId);
        Assert.Equal("فاسمین", company.CompanySymbol);
        Assert.Equal("IRO1KSIM0001", company.SymbolIsin);
    }

    [SkippableFact]
    public async Task AcceptedResponses_PreserveRawText_DetectChanges_AndPreserveReversions()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var companyId = await SeedCompanyAsync(database);
        var hasher = new CanonicalJsonHasher();

        await using (var context = database.CreateContext())
        {
            var repository = new CyclicalWavesDataAcquisitionRepository(
                context,
                new FixedTimeProvider(Now.AddMinutes(1)));

            var firstRaw = "{ \"b\": 2, \"a\": 1.00, \"unknown\": \"نگهداری\" }";
            var first = await repository.PersistAcceptedAsync(
                Accepted(companyId, firstRaw, hasher.ComputeHash(firstRaw), Now),
                CancellationToken.None);
            var unchangedRaw = "{\"unknown\":\"نگهداری\",\"a\":1,\"b\":2.0}";
            var unchanged = await repository.PersistAcceptedAsync(
                Accepted(companyId, unchangedRaw, hasher.ComputeHash(unchangedRaw), Now.AddMinutes(1)),
                CancellationToken.None);
            var secondRaw = "{\"a\":2,\"b\":2,\"unknown\":\"نگهداری\"}";
            var second = await repository.PersistAcceptedAsync(
                Accepted(companyId, secondRaw, hasher.ComputeHash(secondRaw), Now.AddMinutes(2)),
                CancellationToken.None);
            var reverted = await repository.PersistAcceptedAsync(
                Accepted(companyId, firstRaw, hasher.ComputeHash(firstRaw), Now.AddMinutes(3)),
                CancellationToken.None);

            Assert.Equal(CyclicalWavesAcquisitionResult.Changed, first.Result);
            Assert.Equal(CyclicalWavesAcquisitionResult.NoChange, unchanged.Result);
            Assert.Equal(first.SnapshotId, unchanged.SnapshotId);
            Assert.Equal(CyclicalWavesAcquisitionResult.Changed, second.Result);
            Assert.Equal(CyclicalWavesAcquisitionResult.Changed, reverted.Result);
        }

        await using var verify = database.CreateContext();
        var snapshots = await verify.CyclicalWavesMetricSnapshots
            .OrderBy(row => row.AcquisitionDateUtc)
            .ToListAsync();
        var checks = await verify.CyclicalWavesAcquisitionChecks.ToListAsync();

        Assert.Equal(3, snapshots.Count);
        Assert.Equal(4, checks.Count);
        Assert.Equal("{ \"b\": 2, \"a\": 1.00, \"unknown\": \"نگهداری\" }", snapshots[0].RawResponseJson);
        Assert.Null(snapshots[0].PreviousSnapshotId);
        Assert.Equal(snapshots[0].Id, snapshots[1].PreviousSnapshotId);
        Assert.Equal(snapshots[1].Id, snapshots[2].PreviousSnapshotId);
        Assert.True(await new CyclicalWavesDataAcquisitionRepository(verify, new FixedTimeProvider(Now))
            .HasSuccessfulCheckAsync(CycleDate, companyId, CyclicalWavesMetricType.PS, CancellationToken.None));
    }

    [SkippableFact]
    public async Task FailedCheck_DoesNotReplacePreviousValidSnapshot()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var companyId = await SeedCompanyAsync(database);
        var hasher = new CanonicalJsonHasher();

        await using (var context = database.CreateContext())
        {
            var repository = new CyclicalWavesDataAcquisitionRepository(context, new FixedTimeProvider(Now));
            const string raw = "{\"value\":1}";
            await repository.PersistAcceptedAsync(
                Accepted(companyId, raw, hasher.ComputeHash(raw), Now),
                CancellationToken.None);
            await repository.PersistFailedAsync(
                new CyclicalWavesFailedAcquisition(
                    CycleDate,
                    companyId,
                    "IRO1TEST0001",
                    CyclicalWavesMetricType.PE,
                    Now,
                    Now,
                    Now.AddSeconds(1),
                    "pe/circle-chart-data/IRO1TEST0001",
                    503,
                    3,
                    CyclicalWavesAcquisitionFailureCodes.ProviderServerError,
                    "Provider returned HTTP status 503."),
                CancellationToken.None);
        }

        await using var verify = database.CreateContext();
        Assert.Single(await verify.CyclicalWavesMetricSnapshots.ToListAsync());
        var failed = await verify.CyclicalWavesAcquisitionChecks
            .SingleAsync(row => row.Result == nameof(CyclicalWavesAcquisitionResult.Failed));
        Assert.Null(failed.SnapshotId);
        Assert.Null(failed.ResponseHash);
    }

    private static CyclicalWavesAcceptedAcquisition Accepted(
        Guid companyId,
        string raw,
        string hash,
        DateTimeOffset acquisitionDate) =>
        new(
            CycleDate,
            companyId,
            "IRO1TEST0001",
            CyclicalWavesMetricType.PS,
            raw,
            hash,
            acquisitionDate,
            acquisitionDate,
            acquisitionDate,
            acquisitionDate.AddSeconds(1),
            "ps/circle-chart-data/IRO1TEST0001",
            200,
            1);

    private static async Task<Guid> SeedCompanyAsync(PostgreSqlTestDatabase database)
    {
        var companyId = Guid.NewGuid();
        await using var context = database.CreateContext();
        context.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            ProviderName = "Test",
            ExternalCompanyId = $"company-{companyId:N}",
            Name = "Test Company",
            SymbolIsin = "IRO1TEST0001",
            LastSynchronizedAt = Now
        });
        await context.SaveChangesAsync();
        return companyId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
