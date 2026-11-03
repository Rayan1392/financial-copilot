using FinancialCopilot.Domain.Financial.RelativeValuation;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationEngineTests
{
    private static readonly Guid Industry = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Membership_UsesProviderScopedIndustryAndRetainsNoFactMembers()
    {
        var companies = new[]
        {
            new RelativeValuationCatalogCompany(Guid.NewGuid(), "NADPCO", Industry),
            new RelativeValuationCatalogCompany(Guid.NewGuid(), "NADPCO", null),
            new RelativeValuationCatalogCompany(Guid.NewGuid(), "Other", Industry),
            new RelativeValuationCatalogCompany(Guid.NewGuid(), "NADPCO", Industry, false)
        };
        var industries = new[] { new RelativeValuationCatalogGroup(Industry, "NADPCO", "42", "Display") };
        var result = IndustryRelativeValuationEngine.ResolveCanonicalMembership(companies, industries, "NADPCO");
        Assert.Single(result);
        Assert.Equal("42", result[0].GroupExternalId);
    }

    [Fact]
    public void MembershipSnapshot_HashIsStableForSameCanonicalSet()
    {
        var company = new RelativeValuationCatalogCompany(Guid.NewGuid(), "NADPCO", Industry);
        var industry = new RelativeValuationCatalogGroup(Industry, "NADPCO", "42", "Display");
        var first = IndustryRelativeValuationEngine.ResolveCanonicalMembershipSnapshot(new[] { company }, new[] { industry }, "NADPCO");
        var second = IndustryRelativeValuationEngine.ResolveCanonicalMembershipSnapshot(new[] { company }, new[] { industry }, "NADPCO");
        Assert.Equal(first.MembershipHash, second.MembershipHash);
    }

    [Fact]
    public void Normalize_UsesDecimalFormulaWithoutRounding()
    {
        var result = IndustryRelativeValuationEngine.Normalize(new(Guid.NewGuid(), RelativeValuationMetric.Pe, 1.23456789m, 2.34567891m));
        Assert.Equal(52.631580764820023896621042650m, result.Percent);
    }

    [Fact]
    public void Normalize_DistinguishesMissingZeroAndNegative()
    {
        Assert.Equal(RelativeValuationQuality.Missing, IndustryRelativeValuationEngine.Normalize(null).Quality);
        Assert.Equal(RelativeValuationQuality.InvalidNonPositiveInput, Quality(0, 2));
        Assert.Equal(RelativeValuationQuality.InvalidNonPositiveInput, Quality(-1, 2));
        Assert.Equal(RelativeValuationQuality.InvalidNonPositiveInput, Quality(2, -1));
    }

    [Fact]
    public void R7_UsesLinearInterpolationForTwoThreeAndFourValues()
    {
        var result = CalculateWithPe(10, 20, 30, 40);
        var benchmark = result.Benchmarks.Single(x => x.Metric == RelativeValuationMetric.Pe);
        Assert.Equal(17.5m, benchmark.Q1);
        Assert.Equal(32.5m, benchmark.Q3);

        result = CalculateWithPe(10, 20, 30);
        benchmark = result.Benchmarks.Single(x => x.Metric == RelativeValuationMetric.Pe);
        Assert.Equal(15m, benchmark.Q1);
        Assert.Equal(25m, benchmark.Q3);
    }

    [Fact]
    public void Benchmark_UsesInclusiveBoundsAndRemovesOutliers()
    {
        var result = CalculateWithPe(100, 100, 100, 1000);
        var benchmark = result.Benchmarks.Single(x => x.Metric == RelativeValuationMetric.Pe);
        Assert.Equal(100m, benchmark.CleanAverage);
        Assert.Equal(1, benchmark.OutlierCount);
        Assert.Contains(result.Companies, x => x.Metrics.Single(m => m.Metric == RelativeValuationMetric.Pe).IsOutlier);
    }

    [Fact]
    public void Benchmark_RequiresTwoCleanValuesAndZeroIqrKeepsEqualValuesClean()
    {
        var one = CalculateWithPe(100);
        Assert.False(one.Benchmarks.Single(x => x.Metric == RelativeValuationMetric.Pe).IsAvailable);

        var equal = CalculateWithPe(100, 100, 100);
        var benchmark = equal.Benchmarks.Single(x => x.Metric == RelativeValuationMetric.Pe);
        Assert.True(benchmark.IsAvailable);
        Assert.Equal(0m, benchmark.LowerBound - benchmark.UpperBound);
        Assert.All(equal.Companies, x => Assert.False(x.Metrics.Single(m => m.Metric == RelativeValuationMetric.Pe).IsOutlier));
    }

    [Fact]
    public void Classification_EqualityIsGreenAndInvalidNonPositiveIsRed()
    {
        var result = CalculateWithPe(100, 100, 100);
        Assert.All(result.Companies, x => Assert.Equal(RelativeValuationClassification.Green,
            x.Metrics.Single(m => m.Metric == RelativeValuationMetric.Pe).Classification));
        var invalid = result.Companies[0] with
        {
            Metrics = result.Companies[0].Metrics.Select(x => x.Metric == RelativeValuationMetric.Ps
                ? x with { Quality = RelativeValuationQuality.InvalidNonPositiveInput, Classification = RelativeValuationClassification.Red }
                : x).ToArray()
        };
        Assert.Equal(RelativeValuationClassification.Red, invalid.Metrics.Single(x => x.Metric == RelativeValuationMetric.Ps).Classification);
    }

    [Fact]
    public void Ranking_IsFullIndustryDeterministicAndTopNIsAppliedAfterRanking()
    {
        var ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000003"), Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var members = ids.Select(id => new CanonicalIndustryMember(id, Industry, "42", "Industry"));
        var facts = ids.SelectMany((id, i) => new[]
        {
            new RelativeValuationSourceFact(id, RelativeValuationMetric.Pe, 100 + i, 100),
            new RelativeValuationSourceFact(id, RelativeValuationMetric.Ps, 100, 100),
            new RelativeValuationSourceFact(id, RelativeValuationMetric.Equilibrium, 100, 100)
        });
        var result = IndustryRelativeValuationEngine.Calculate(members, facts, new("NADPCO", Now, TimeSpan.FromHours(26)));
        Assert.Equal(new[] { ids[0], ids[1], ids[2] }, result.Companies.Where(x => x.GlobalRank is not null).OrderBy(x => x.GlobalRank).Select(x => x.CompanyId).ToArray());
        Assert.Equal(ids[0], IndustryRelativeValuationEngine.TopN(result, Industry, 1).Single().CompanyId);
    }

    [Fact]
    public void Ranking_0Of0IsVisibleButDoesNotConsumeRank()
    {
        var noData = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var data = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var dataTwo = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var members = new[] { new CanonicalIndustryMember(noData, Industry, "42", "Industry"), new CanonicalIndustryMember(data, Industry, "42", "Industry"), new CanonicalIndustryMember(dataTwo, Industry, "42", "Industry") };
        var facts = new[] { data, dataTwo }.SelectMany(id => Enum.GetValues<RelativeValuationMetric>().Select(metric => new RelativeValuationSourceFact(id, metric, 1, 1)));
        var result = IndustryRelativeValuationEngine.Calculate(members, facts, new("NADPCO", Now, TimeSpan.FromHours(26)));
        Assert.Null(result.Companies.Single(x => x.CompanyId == noData).GlobalRank);
        Assert.Equal(1, result.Companies.Single(x => x.CompanyId == data).GlobalRank);
    }

    [Fact]
    public void DuplicateFacts_SelectNewestObservationRegardlessOfInputOrder()
    {
        var company = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var older = Fact(company, 100, new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero), new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), "old");
        var newer = Fact(company, 200, new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero), new(2026, 8, 11, 0, 0, 0, TimeSpan.Zero), "new");

        var first = CalculateWithFacts(new[] { older, newer });
        var second = CalculateWithFacts(new[] { newer, older });

        Assert.Equal(200m, Pe(first, company).Percent);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }

    [Fact]
    public void DuplicateFacts_SameTimestampUsesNewestPersistedObservation()
    {
        var company = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var timestamp = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var olderPersisted = Fact(company, 100, timestamp, timestamp.AddHours(1), "same-time-old-persist");
        var newerPersisted = Fact(company, 200, timestamp, timestamp.AddHours(2), "same-time-new-persist");

        var result = CalculateWithFacts(new[] { olderPersisted, newerPersisted });

        Assert.Equal(200m, Pe(result, company).Percent);
    }

    [Fact]
    public void DuplicateFacts_SameTimestampAndPersistedUsesDescendingObservationId()
    {
        var company = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var timestamp = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var lowId = Fact(company, 100, timestamp, timestamp, "A");
        var highId = Fact(company, 200, timestamp, timestamp, "B");

        var result = CalculateWithFacts(new[] { highId, lowId });

        Assert.Equal(200m, Pe(result, company).Percent);
    }

    [Fact]
    public void DuplicateFacts_CompleteMetadataTieUsesCanonicalValueTieBreaker()
    {
        var company = Guid.Parse("00000000-0000-0000-0000-000000000013");
        var timestamp = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var lowValue = Fact(company, 100, timestamp, timestamp, "same");
        var highValue = Fact(company, 200, timestamp, timestamp, "same");

        var first = CalculateWithFacts(new[] { lowValue, highValue });
        var second = CalculateWithFacts(new[] { highValue, lowValue });

        Assert.Equal(200m, Pe(first, company).Percent);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
    }

    [Fact]
    public void DuplicateFacts_CollectionOrderingCannotChangeRanking()
    {
        var ids = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000021"),
            Guid.Parse("00000000-0000-0000-0000-000000000022")
        };
        var members = ids.Select(id => new CanonicalIndustryMember(id, Industry, "42", "Industry")).ToArray();
        var timestamp = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var facts = ids.SelectMany((id, index) => new[]
        {
            Fact(id, index == 0 ? 100 : 200, timestamp, timestamp, index == 0 ? "A" : "B"),
            Fact(id, index == 0 ? 300 : 400, timestamp, timestamp, index == 0 ? "A" : "B")
        }).ToArray();

        var first = IndustryRelativeValuationEngine.Calculate(members, facts, new("NADPCO", Now, TimeSpan.FromHours(26)));
        var second = IndustryRelativeValuationEngine.Calculate(members.Reverse(), facts.Reverse(), new("NADPCO", Now, TimeSpan.FromHours(26)));

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first), System.Text.Json.JsonSerializer.Serialize(second));
        Assert.Equal(ids[0], first.Companies.Single(x => x.GlobalRank == 1).CompanyId);
    }

    [Fact]
    public void TopN_RejectsInvalidLimits()
    {
        var result = CalculateWithPe(100, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryRelativeValuationEngine.TopN(result, Industry, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndustryRelativeValuationEngine.TopN(result, Industry, 101));
    }

    private static RelativeValuationQuality Quality(decimal? current, decimal? reference) =>
        IndustryRelativeValuationEngine.Normalize(new(Guid.NewGuid(), RelativeValuationMetric.Pe, current, reference)).Quality;

    private static IndustryRelativeValuationResult CalculateWithPe(params decimal[] values)
    {
        var members = values.Select((_, i) => new CanonicalIndustryMember(Guid.NewGuid(), Industry, "42", "Industry")).ToArray();
        var facts = members.SelectMany((member, i) => new[]
        {
            new RelativeValuationSourceFact(member.CompanyId, RelativeValuationMetric.Pe, values[i], 100),
            new RelativeValuationSourceFact(member.CompanyId, RelativeValuationMetric.Ps, 100, 100),
            new RelativeValuationSourceFact(member.CompanyId, RelativeValuationMetric.Equilibrium, 100, 100)
        });
        return IndustryRelativeValuationEngine.Calculate(members, facts, new("NADPCO", Now, TimeSpan.FromHours(26)));
    }

    private static IndustryRelativeValuationResult CalculateWithFacts(IEnumerable<RelativeValuationSourceFact> facts)
    {
        var company = facts.First().CompanyId;
        var member = new CanonicalIndustryMember(company, Industry, "42", "Industry");
        return IndustryRelativeValuationEngine.Calculate(new[] { member }, facts, new("NADPCO", Now, TimeSpan.FromHours(26)));
    }

    private static RelativeValuationSourceFact Fact(
        Guid companyId,
        decimal current,
        DateTimeOffset sourceTimestamp,
        DateTimeOffset persistedAt,
        string sourceObservationId) =>
        new(companyId, RelativeValuationMetric.Pe, current, 100, true, true, true,
            sourceTimestamp, persistedAt, sourceObservationId);

    private static CompanyRelativeMetric Pe(IndustryRelativeValuationResult result, Guid companyId) =>
        result.Companies.Single(x => x.CompanyId == companyId).Metrics.Single(x => x.Metric == RelativeValuationMetric.Pe);
}
