using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSnapshotConsumptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reader_ReturnsLatestValidSnapshotPerCompanyAndMetric()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var ps = AddSnapshot(db, companyId, CyclicalWavesMetricType.PS, "{\"close\":4,\"avg\":2}",
            new string('a', 64), Now.AddHours(-2), Guid.Parse("00000000-0000-0000-0000-000000000001"));
        AddSnapshot(db, companyId, CyclicalWavesMetricType.PE, "{\"close\":5,\"avg\":10}",
            new string('b', 64), Now.AddHours(-2));
        AddSnapshot(db, companyId, CyclicalWavesMetricType.Equilibrium,
            "{\"close\":100,\"balance\":125}", new string('c', 64), Now.AddHours(-2));
        AddSnapshot(db, otherCompanyId, CyclicalWavesMetricType.PS, "{\"close\":99,\"avg\":1}",
            new string('d', 64), Now.AddMinutes(-1));

        var winningCheckId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = winningCheckId,
            CycleDateUtc = DateOnly.FromDateTime(Now.UtcDateTime),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            MetricType = CyclicalWavesMetricType.PS.ToString(),
            CompletedAtUtc = Now.AddHours(-1),
            CreatedAtUtc = Now.AddMinutes(-50),
            ResponseHash = ps.ResponseHash,
            Result = CyclicalWavesAcquisitionResult.NoChange.ToString(),
            SnapshotId = ps.Id,
            SourceEndpoint = "ps",
            AttemptCount = 1
        });
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = DateOnly.FromDateTime(Now.UtcDateTime),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            MetricType = CyclicalWavesMetricType.PS.ToString(),
            CompletedAtUtc = Now,
            CreatedAtUtc = Now,
            Result = CyclicalWavesAcquisitionResult.Failed.ToString(),
            SourceEndpoint = "ps",
            AttemptCount = 1,
            FailureCode = "Timeout"
        });
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = DateOnly.FromDateTime(Now.UtcDateTime),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            MetricType = CyclicalWavesMetricType.PS.ToString(),
            CompletedAtUtc = Now.AddMinutes(-1),
            CreatedAtUtc = Now.AddMinutes(-1),
            ResponseHash = new string('e', 64),
            Result = CyclicalWavesAcquisitionResult.NoChange.ToString(),
            SnapshotId = ps.Id,
            SourceEndpoint = "ps",
            AttemptCount = 1
        });
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = Guid.NewGuid(),
            CycleDateUtc = DateOnly.FromDateTime(Now.UtcDateTime),
            CompanyId = companyId,
            ProviderName = "OtherProvider",
            MetricType = CyclicalWavesMetricType.PS.ToString(),
            CompletedAtUtc = Now.AddMinutes(-1),
            CreatedAtUtc = Now.AddMinutes(-1),
            ResponseHash = ps.ResponseHash,
            Result = CyclicalWavesAcquisitionResult.NoChange.ToString(),
            SnapshotId = ps.Id,
            SourceEndpoint = "ps",
            AttemptCount = 1
        });
        await db.SaveChangesAsync();

        var snapshots = await new CyclicalWavesMetricSnapshotReader(db)
            .ReadLatestAsync([companyId], CancellationToken.None);

        Assert.Equal(3, snapshots.Count);
        Assert.All(snapshots, snapshot => Assert.Equal(companyId, snapshot.CompanyId));
        var selectedPs = snapshots.Single(snapshot => snapshot.MetricType == CyclicalWavesMetricType.PS);
        Assert.Equal(ps.Id, selectedPs.SnapshotId);
        Assert.Equal(winningCheckId, selectedPs.AcquisitionCheckId);
        Assert.Equal(Now.AddHours(-1), selectedPs.CompletedAtUtc);
    }

    [Fact]
    public async Task CalculationPath_DoesNotResolveOrCallProvider()
    {
        await using var db = CreateDb();
        var (industryId, companies) = await SeedCatalogAsync(db, 2);
        foreach (var companyId in companies)
            AddCompleteSnapshotSet(db, companyId, companyId == companies[0] ? 50m : 90m);
        await db.SaveChangesAsync();

        var providerSpy = new RecordingProvider();
        var reader = new CyclicalWavesMetricSnapshotReader(db);
        var service = new IndustryRelativeValuationOrchestrationService(
            new IndustryRelativeValuationCalculationInputBuilder(db, reader),
            new IndustryRelativeValuationCalculationSnapshotWriter(db),
            Options.Create(new IndustryRelativeValuationOptions { Enabled = true, SourceFreshnessHours = 26 }),
            Options.Create(new IndustryRelativeValuationSourceOptions { CanonicalProviderName = ProviderSources.NoavaranCurrentApiName }),
            new FixedTimeProvider(Now),
            NullLogger<IndustryRelativeValuationOrchestrationService>.Instance);

        var result = await service.RunAsync("snapshot-test", CancellationToken.None);

        Assert.Equal(0, providerSpy.CallCount);
        Assert.Equal(2, result.CompaniesConsidered);
        Assert.Equal(1, result.PublishedSnapshots);
        Assert.Equal(industryId, (await db.IndustryRelativeValuationCalculations.SingleAsync()).IndustryId);
        Assert.Empty(await db.IndustryRelativeValuationSourceFacts.ToArrayAsync());
        Assert.Equal(
            [typeof(FinancialIngestionDbContext)],
            typeof(CyclicalWavesMetricSnapshotReader).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task InputBuilder_UsesPersistedPsSnapshotInsteadOfVisualizationTable()
    {
        await using var db = CreateDb();
        var (_, companies) = await SeedCatalogAsync(db, 1);
        var companyId = companies.Single();
        db.CompanyPsGaugeSnapshots.Add(new CompanyPsGaugeSnapshotRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = "CyclicalWaves",
            SourceCompanyIsin = "IRTEST001",
            ObservationDate = DateOnly.FromDateTime(Now.UtcDateTime),
            GaugeClose = 400m,
            GaugeAverage = 200m,
            BoundaryAverage = 999m,
            GaugeFetchedAtUtc = Now,
            CurrentValuesFetchedAtUtc = Now,
            LastSyncedAtUtc = Now,
            FirstSeenAtUtc = Now
        });
        AddSnapshot(db, companyId, CyclicalWavesMetricType.PS, "{\"close\":4,\"avg\":2}",
            new string('a', 64), Now.AddMinutes(-1));
        await db.SaveChangesAsync();

        var inputs = await new IndustryRelativeValuationCalculationInputBuilder(
                db,
                new CyclicalWavesMetricSnapshotReader(db))
            .BuildAsync(ProviderSources.NoavaranCurrentApiName, Now, TimeSpan.FromHours(26), CancellationToken.None);

        var ps = Assert.Single(inputs).SourceBarrier.Selections
            .Single(selection => selection.Metric == RelativeValuationMetric.Ps).Fact;
        Assert.Equal(4m, ps.CurrentValue);
        Assert.Equal(2m, ps.ReferenceValue);
        Assert.NotEqual(400m, ps.CurrentValue);
        Assert.NotEqual(999m, ps.ReferenceValue);
    }

    [Fact]
    public void SnapshotMapping_PreservesNormalizationBenchmarksAndRanking()
    {
        var industryId = Guid.NewGuid();
        var companies = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var members = companies.Select(companyId =>
            new CanonicalIndustryMember(companyId, industryId, "10", "Test Industry")).ToArray();
        var legacyFacts = new List<RelativeValuationSourceFact>();
        var snapshotFacts = new List<RelativeValuationSourceFact>();

        for (var index = 0; index < companies.Length; index++)
        {
            var percent = index == 0 ? 50m : 90m;
            foreach (var metric in Enum.GetValues<RelativeValuationMetric>())
            {
                var (metricType, current, reference, json) = metric switch
                {
                    RelativeValuationMetric.Pe => (CyclicalWavesMetricType.PE, percent, 100m,
                        $"{{\"close\":{percent},\"avg\":100}}"),
                    RelativeValuationMetric.Ps => (CyclicalWavesMetricType.PS, percent, 100m,
                        $"{{\"close\":{percent},\"avg\":100}}"),
                    _ => (CyclicalWavesMetricType.Equilibrium, percent, 100m,
                        $"{{\"close\":{percent},\"balance\":100}}")
                };
                legacyFacts.Add(new RelativeValuationSourceFact(companies[index], metric, current, reference));
                snapshotFacts.Add(IndustryRelativeValuationSourceMapper.Map(
                    Snapshot(companies[index], metricType, json, (char)('a' + index))));
            }
        }

        var legacyNormalized = legacyFacts.Select(fact => IndustryRelativeValuationEngine.Normalize(fact)).ToArray();
        var snapshotNormalized = snapshotFacts.Select(fact => IndustryRelativeValuationEngine.Normalize(fact)).ToArray();
        Assert.Equal(legacyNormalized, snapshotNormalized);

        var context = new RelativeValuationCalculationContext(
            ProviderSources.NoavaranCurrentApiName,
            Now,
            TimeSpan.FromHours(26));
        var legacyResult = IndustryRelativeValuationEngine.Calculate(members, legacyFacts, context);
        var snapshotResult = IndustryRelativeValuationEngine.Calculate(members, snapshotFacts, context);

        Assert.Equal(JsonSerializer.Serialize(legacyResult), JsonSerializer.Serialize(snapshotResult));
        Assert.Equal(1, snapshotResult.Companies.Single(company => company.CompanyId == companies[0]).GlobalRank);
        Assert.Equal(2, snapshotResult.Companies.Single(company => company.CompanyId == companies[1]).GlobalRank);
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<(Guid IndustryId, Guid[] Companies)> SeedCatalogAsync(
        FinancialIngestionDbContext db,
        int companyCount)
    {
        var industryId = Guid.NewGuid();
        db.Industries.Add(new NormalizedIndustryRow
        {
            Id = industryId,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalId = "10",
            Name = "Test Industry",
            LastSynchronizedAt = Now
        });
        var companies = Enumerable.Range(1, companyCount).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < companies.Length; index++)
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companies[index],
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalCompanyId = (index + 1).ToString(),
                Name = $"Company {index + 1}",
                IndustryId = industryId,
                SymbolIsin = $"IRTEST{index + 1:000}",
                LastSynchronizedAt = Now
            });
        await db.SaveChangesAsync();
        return (industryId, companies);
    }

    private static void AddCompleteSnapshotSet(FinancialIngestionDbContext db, Guid companyId, decimal current)
    {
        AddSnapshot(db, companyId, CyclicalWavesMetricType.PE,
            $"{{\"close\":{current},\"avg\":100}}", new string('a', 64), Now.AddMinutes(-1));
        AddSnapshot(db, companyId, CyclicalWavesMetricType.PS,
            $"{{\"close\":{current},\"avg\":100}}", new string('b', 64), Now.AddMinutes(-1));
        AddSnapshot(db, companyId, CyclicalWavesMetricType.Equilibrium,
            $"{{\"close\":{current},\"balance\":100}}", new string('c', 64), Now.AddMinutes(-1));
    }

    private static CyclicalWavesMetricSnapshotRow AddSnapshot(
        FinancialIngestionDbContext db,
        Guid companyId,
        CyclicalWavesMetricType metricType,
        string json,
        string hash,
        DateTimeOffset completedAtUtc,
        Guid? checkId = null)
    {
        var snapshot = new CyclicalWavesMetricSnapshotRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            SymbolIsin = "IRTEST001",
            ProviderName = "CyclicalWaves",
            MetricType = metricType.ToString(),
            RawResponseJson = json,
            ResponseHash = hash,
            AcquisitionDateUtc = completedAtUtc,
            SourceEndpoint = metricType.ToString(),
            CreatedAtUtc = completedAtUtc
        };
        db.CyclicalWavesMetricSnapshots.Add(snapshot);
        db.CyclicalWavesAcquisitionChecks.Add(new CyclicalWavesAcquisitionCheckRow
        {
            Id = checkId ?? Guid.NewGuid(),
            CycleDateUtc = DateOnly.FromDateTime(completedAtUtc.UtcDateTime),
            CompanyId = companyId,
            SymbolIsin = snapshot.SymbolIsin,
            ProviderName = snapshot.ProviderName,
            MetricType = snapshot.MetricType,
            CheckedAtUtc = completedAtUtc,
            RequestedAtUtc = completedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ResponseHash = hash,
            Result = CyclicalWavesAcquisitionResult.Changed.ToString(),
            SnapshotId = snapshot.Id,
            SourceEndpoint = snapshot.SourceEndpoint,
            HttpStatusCode = 200,
            AttemptCount = 1,
            CreatedAtUtc = completedAtUtc
        });
        return snapshot;
    }

    private static CyclicalWavesMetricSnapshot Snapshot(
        Guid companyId,
        CyclicalWavesMetricType metricType,
        string json,
        char hashCharacter) =>
        new(
            Guid.NewGuid(),
            companyId,
            "CyclicalWaves",
            metricType,
            json,
            new string(hashCharacter, 64),
            Now,
            Now,
            Guid.NewGuid(),
            Now,
            Now);

    private sealed class RecordingProvider : ICyclicalWavesRelativeValuationProviderClient
    {
        public int CallCount { get; private set; }

        public Task<RelativeValuationProviderResult> GetPeGaugeAsync(string isin, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("The snapshot calculation path must not call the provider.");
        }

        public Task<RelativeValuationProviderResult> GetEquilibriumGaugeAsync(string isin, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("The snapshot calculation path must not call the provider.");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
