using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
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
        var calculation = await db.IndustryRelativeValuationCalculations.SingleAsync();
        Assert.Equal(industryId, calculation.IndustryId);
        Assert.NotNull(calculation.GroupId);
        var read = await new IndustryRelativeValuationReadRepository(db).ReadAsync(
            new(calculation.GroupId, [companies[0]], "symbol_vs_industry_relative_valuation"));
        Assert.NotNull(read);
        Assert.Equal(calculation.GroupId, read.GroupId);
        Assert.Equal("Test Group", read.GroupTitle);
        Assert.Equal(2, read.Members.Count);
        Assert.Contains(read.Members, member => member.CompanyId == companies[0]);
        Assert.Contains(read.Members, member => member.CompanyId == companies[1]);
        Assert.Empty(await db.IndustryRelativeValuationSourceFacts.ToArrayAsync());
        Assert.Equal(
            [typeof(FinancialIngestionDbContext)],
            typeof(CyclicalWavesMetricSnapshotReader).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task Reader_AppendsRequestedCompany_WhenItFallsOutsideTopN()
    {
        await using var db = CreateDb();
        var (_, companies) = await SeedCatalogAsync(db, 3);
        foreach (var companyId in companies)
            AddCompleteSnapshotSet(db, companyId, 50m + Array.IndexOf(companies, companyId));
        await db.SaveChangesAsync();

        var reader = new CyclicalWavesMetricSnapshotReader(db);
        var input = Assert.Single(await new IndustryRelativeValuationCalculationInputBuilder(db, reader)
            .BuildAsync(ProviderSources.NoavaranCurrentApiName, Now, TimeSpan.FromHours(26), CancellationToken.None));
        var write = await new IndustryRelativeValuationCalculationSnapshotWriter(db)
            .WriteAsync(DateOnly.FromDateTime(Now.UtcDateTime), input, Now, CancellationToken.None);
        Assert.Equal("Published", write.Status);

        var result = await new IndustryRelativeValuationReadRepository(db).ReadAsync(
            new(input.GroupId, [companies[2]], "symbol_vs_industry_relative_valuation", Limit: 2));

        Assert.NotNull(result);
        Assert.Equal(3, result.Members.Count);
        Assert.Equal(companies[2], result.Members[^1].CompanyId);
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
    public async Task InputBuilder_UsesEligibleUniverse_ExcludesZeroMetricCompanies_AndRetainsPartialMembers()
    {
        await using var db = CreateDb();
        var (_, companies) = await SeedCatalogAsync(db, 6, eligibleCompanyCount: 5);
        for (var index = 0; index < 3; index++)
        {
            AddSnapshot(db, companies[index], CyclicalWavesMetricType.PE,
                $"{{\"close\":{50 + index},\"avg\":100}}", new string('a', 64), Now.AddMinutes(-1));
            AddSnapshot(db, companies[index], CyclicalWavesMetricType.PS,
                $"{{\"close\":{60 + index},\"avg\":100}}", new string('b', 64), Now.AddMinutes(-1));
        }
        for (var index = 0; index < 2; index++)
            AddSnapshot(db, companies[index], CyclicalWavesMetricType.Equilibrium,
                $"{{\"close\":{70 + index},\"balance\":100}}", new string('c', 64), Now.AddMinutes(-1));
        AddCompleteSnapshotSet(db, companies[5], 1m);
        await db.SaveChangesAsync();

        var input = Assert.Single(await new IndustryRelativeValuationCalculationInputBuilder(
                db, new CyclicalWavesMetricSnapshotReader(db))
            .BuildAsync(ProviderSources.NoavaranCurrentApiName, Now, TimeSpan.FromHours(26), CancellationToken.None));

        Assert.Equal(companies.Take(3).Order(), input.Members.Select(member => member.CompanyId).Order());
        Assert.DoesNotContain(input.Result.Companies, company => company.CompanyId == companies[3]);
        Assert.DoesNotContain(input.Result.Companies, company => company.CompanyId == companies[4]);
        Assert.DoesNotContain(input.Result.Companies, company => company.CompanyId == companies[5]);
        Assert.Equal(8, input.SourceBarrier.Selections.Count);
        Assert.Equal(8, input.SourceBarrier.RequiredSelectionCount);
        Assert.True(input.SourceBarrier.IsComplete);
        Assert.All(input.Result.Benchmarks, benchmark => Assert.True(benchmark.IsAvailable));
        var write = await new IndustryRelativeValuationCalculationSnapshotWriter(db)
            .WriteAsync(DateOnly.FromDateTime(Now.UtcDateTime), input, Now, CancellationToken.None);
        Assert.Equal("Published", write.Status);
        Assert.Equal(3, await db.CompanyIndustryRelativeValuations.CountAsync());
    }

    [Fact]
    public async Task InputBuilder_IsolatesGroupsThatShareTheSameIndustry()
    {
        await using var db = CreateDb();
        var (_, companies) = await SeedCatalogAsync(db, 4);
        var firstGroupId = (await db.NoavaranEligibleCompanies.FindAsync(companies[0]))!.GroupId!.Value;
        var secondGroupId = Guid.NewGuid();
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = secondGroupId,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalId = "group-20",
            Name = "Second Test Group",
            LastSynchronizedAt = Now
        });
        foreach (var companyId in companies.Skip(2))
        {
            (await db.Companies.FindAsync(companyId))!.GroupId = secondGroupId;
            (await db.NoavaranEligibleCompanies.FindAsync(companyId))!.GroupId = secondGroupId;
        }
        for (var index = 0; index < companies.Length; index++)
            AddCompleteSnapshotSet(db, companies[index], index < 2 ? 10m + index : 90m + index);
        await db.SaveChangesAsync();

        var inputs = await new IndustryRelativeValuationCalculationInputBuilder(
                db, new CyclicalWavesMetricSnapshotReader(db))
            .BuildAsync(ProviderSources.NoavaranCurrentApiName, Now, TimeSpan.FromHours(26), CancellationToken.None);

        Assert.Equal(2, inputs.Count);
        var first = Assert.Single(inputs, input => input.GroupId == firstGroupId);
        var second = Assert.Single(inputs, input => input.GroupId == secondGroupId);
        Assert.Equal(companies.Take(2).Order(), first.Members.Select(member => member.CompanyId).Order());
        Assert.Equal(companies.Skip(2).Order(), second.Members.Select(member => member.CompanyId).Order());
        Assert.All(first.Result.Companies, company => Assert.Equal(firstGroupId, company.GroupId));
        Assert.All(second.Result.Companies, company => Assert.Equal(secondGroupId, company.GroupId));
        Assert.True(first.Result.Benchmarks.Single(benchmark => benchmark.Metric == RelativeValuationMetric.Pe).CleanAverage < 20m);
        Assert.True(second.Result.Benchmarks.Single(benchmark => benchmark.Metric == RelativeValuationMetric.Pe).CleanAverage > 80m);
    }

    [Fact]
    public async Task InputBuilder_EmitsNoIndustryWhenEligibleCompaniesHaveNoUsableMetric()
    {
        await using var db = CreateDb();
        var (_, companies) = await SeedCatalogAsync(db, 1);
        AddSnapshot(db, companies[0], CyclicalWavesMetricType.PE,
            "{\"close\":0,\"avg\":100}", new string('a', 64), Now.AddMinutes(-1));
        await db.SaveChangesAsync();

        var inputs = await new IndustryRelativeValuationCalculationInputBuilder(
                db, new CyclicalWavesMetricSnapshotReader(db))
            .BuildAsync(ProviderSources.NoavaranCurrentApiName, Now, TimeSpan.FromHours(26), CancellationToken.None);

        Assert.Empty(inputs);
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
        int companyCount,
        int? eligibleCompanyCount = null)
    {
        var industryId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.Industries.Add(new NormalizedIndustryRow
        {
            Id = industryId,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalId = "10",
            Name = "Test Industry",
            LastSynchronizedAt = Now
        });
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = groupId,
            ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalId = "group-10",
            Name = "Test Group",
            LastSynchronizedAt = Now
        });
        var companies = Enumerable.Range(1, companyCount).Select(_ => Guid.NewGuid()).ToArray();
        for (var index = 0; index < companies.Length; index++)
        {
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = companies[index],
                ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalCompanyId = (index + 1).ToString(),
                Name = $"Company {index + 1}",
                IndustryId = industryId,
                GroupId = groupId,
                SymbolIsin = $"IRTEST{index + 1:000}",
                LastSynchronizedAt = Now
            });
            if (index < (eligibleCompanyCount ?? companyCount))
                db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
                {
                    Id = companies[index],
                    ProviderName = ProviderSources.NoavaranCurrentApiName,
                    ExternalCompanyId = (index + 1).ToString(),
                    Name = $"Company {index + 1}",
                    IndustryId = industryId,
                    GroupId = groupId,
                    SymbolIsin = $"IRTEST{index + 1:000}"
                });
        }
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
