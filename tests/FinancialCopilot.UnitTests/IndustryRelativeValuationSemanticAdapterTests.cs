using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationSemanticAdapterTests
{
    [Fact]
    public async Task Adapter_UsesFeature119CanonicalResultsAndDerivesSameIndustry()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var group = Guid.NewGuid();
        var company = Guid.NewGuid();
        db.Industries.Add(new NormalizedIndustryRow { Id = industry, ProviderName = "NoavaranCurrentApi", ExternalId = "7", Name = "Steel" });
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow { Id = group, ProviderName = "NoavaranCurrentApi", ExternalId = "70", Name = "Steel Makers" });
        db.Companies.Add(new NormalizedCompanyRow { Id = company, ProviderName = "NoavaranCurrentApi", IndustryId = industry, GroupId = group, Ticker = "AAA", Name = "Alpha" });
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow { Id = company, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "1", IndustryId = industry, GroupId = group, Name = "Alpha" });
        await db.SaveChangesAsync();

        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(new EntityResolutionResult.Resolved(new(company, "AAA", "Alpha", "Company", "feature119"), new("exact_ticker", 1m))),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);

        var result = await adapter.ResolveAsync("symbol_vs_industry_relative_valuation", Interpretation("AAA"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Resolved, result.Status);
        Assert.Equal(group, result.GroupId);
        Assert.Equal("Steel Makers", result.GroupTitle);
        Assert.Equal(industry, result.IndustryId);
        Assert.Equal([company], result.CompanyIds);
    }

    [Fact]
    public async Task Adapter_PreservesCanonicalAmbiguityAndCandidateIds()
    {
        await using var db = CreateDb();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var ambiguous = new EntityResolutionResult.Ambiguous([
            new(new(first, "AAA", "Alpha", "Company", "feature119"), 1m, "ambiguous"),
            new(new(second, "AAB", "Alpha B", "Company", "feature119"), 1m, "ambiguous")]);
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(ambiguous),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);

        var result = await adapter.ResolveAsync("symbol_vs_industry_relative_valuation", Interpretation("AAA"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Ambiguous, result.Status);
        Assert.Equal([first, second], result.CandidateIds);
    }

    [Fact]
    public async Task Adapter_DoesNotResolveIndustryReferenceAsACompanyWhenExactTickerIsPresent()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var group = Guid.NewGuid();
        var company = Guid.NewGuid();
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = group, ProviderName = "NoavaranCurrentApi", ExternalId = "70", Name = "Iron Ore Producers"
        });
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = company, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "1",
            IndustryId = industry, GroupId = group, Name = "Gohar"
        });
        await db.SaveChangesAsync();

        var exactTicker = new EntityResolutionResult.Resolved(
            new(company, "کگهر", "Gohar", "Company", "exact_ticker"),
            new("exact_ticker", 1m));
        var noiseAmbiguity = new EntityResolutionResult.Ambiguous([
            new(new(Guid.NewGuid(), "AAA", "Industry Services", "Company", "fuzzy_candidate"), 0.62m, "fuzzy_candidate"),
            new(new(Guid.NewGuid(), "AAB", "Industrial Group", "Company", "fuzzy_candidate"), 0.60m, "fuzzy_candidate")]);
        var companyResolver = new FakeCompanyResolver(exactTicker, noiseAmbiguity);
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            companyResolver,
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);
        var interpretation = new QueryInterpretation(
            "کگهر را با صنعت خودش مقایسه کن",
            "کگهر را با صنعت خودش مقایسه کن",
            "fa",
            [],
            [
                new EntityMention("کگهر", 0, 4, QueryValueProvenance.UserExplicit, "company"),
                new EntityMention("صنعت خودش", 13, 9, QueryValueProvenance.ModelProposed, "industry", "industry")
            ],
            [], null, null, null, [], [], 1m, [], 1);

        var result = await adapter.ResolveAsync("symbol_vs_industry_relative_valuation", interpretation);

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Resolved, result.Status);
        Assert.Equal([company], result.CompanyIds);
        Assert.Equal(["کگهر"], companyResolver.Mentions);
    }

    [Fact]
    public async Task Adapter_StillClarifiesAnExplicitlyTypedAmbiguousCompanyBesideAnExactTicker()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var group = Guid.NewGuid();
        var company = Guid.NewGuid();
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = group, ProviderName = "NoavaranCurrentApi", ExternalId = "70", Name = "Iron Ore Producers"
        });
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
        {
            Id = company, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "1",
            IndustryId = industry, GroupId = group, Name = "Gohar"
        });
        await db.SaveChangesAsync();

        var ambiguous = new EntityResolutionResult.Ambiguous([
            new(new(Guid.NewGuid(), "AB1", "Ambiguous Company A", "Company", "exact_company_name"), 1m, "exact_company_name"),
            new(new(Guid.NewGuid(), "AB2", "Ambiguous Company B", "Company", "exact_company_name"), 1m, "exact_company_name")]);
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(
                new EntityResolutionResult.Resolved(new(company, "کگهر", "Gohar", "Company", "exact_ticker"), new("exact_ticker", 1m)),
                ambiguous),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);
        var interpretation = new QueryInterpretation(
            "کگهر compare Alpha",
            "کگهر compare Alpha",
            "fa",
            [],
            [
                new EntityMention("کگهر", 0, 4, QueryValueProvenance.UserExplicit, "company"),
                new EntityMention("Alpha", 14, 5, QueryValueProvenance.ModelProposed, "company")
            ],
            [], null, null, null, [], [], 1m, [], 1);

        var result = await adapter.ResolveAsync("symbol_vs_industry_relative_valuation", interpretation);

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(2, result.CandidateIds!.Count);
    }

    [Fact]
    public async Task Adapter_ResolvesIndustryOnlyThroughCanonicalIndustryAuthority()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var group = Guid.NewGuid();
        var company = Guid.NewGuid();
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow { Id = group, ProviderName = "NoavaranCurrentApi", ExternalId = "70", Name = "Steel Makers" });
        db.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow { Id = company, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "1", IndustryId = industry, GroupId = group, Name = "Alpha" });
        await db.SaveChangesAsync();
        var canonicalIndustry = new CanonicalQueryIndustry(industry, "steel", "Steel", "NoavaranCurrentApi", "feature119");
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(new EntityResolutionResult.NotFound("steel")),
            new FakeIndustryResolver(new IndustryResolutionResult.Resolved(canonicalIndustry, new("exact_industry", 1m))),
            db);

        var result = await adapter.ResolveAsync("industry_relative_valuation_ranking", Interpretation("steel"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Resolved, result.Status);
        Assert.Equal(group, result.GroupId);
        Assert.Equal(industry, result.IndustryId);
        Assert.Empty(result.CompanyIds!);
    }

    [Fact]
    public async Task Adapter_RejectsDifferentGroupsEvenWhenIndustryMatches()
    {
        await using var db = CreateDb();
        var firstIndustry = Guid.NewGuid();
        var secondIndustry = firstIndustry;
        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        db.IndustryGroups.AddRange(
            new NormalizedIndustryGroupRow { Id = firstGroup, ProviderName = "NoavaranCurrentApi", ExternalId = "1", Name = "Group A" },
            new NormalizedIndustryGroupRow { Id = secondGroup, ProviderName = "NoavaranCurrentApi", ExternalId = "2", Name = "Group B" });
        db.Companies.AddRange(
            new NormalizedCompanyRow { Id = first, ProviderName = "NoavaranCurrentApi", IndustryId = firstIndustry, GroupId = firstGroup, Ticker = "AAA" },
            new NormalizedCompanyRow { Id = second, ProviderName = "NoavaranCurrentApi", IndustryId = secondIndustry, GroupId = secondGroup, Ticker = "BBB" });
        db.NoavaranEligibleCompanies.AddRange(
            new NoavaranEligibleCompanyRow { Id = first, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "1", IndustryId = firstIndustry, GroupId = firstGroup },
            new NoavaranEligibleCompanyRow { Id = second, ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "2", IndustryId = secondIndustry, GroupId = secondGroup });
        await db.SaveChangesAsync();
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(new EntityResolutionResult.Resolved(new(first, "AAA", "A", "Company", "feature119"), new("exact_ticker", 1m)),
                new EntityResolutionResult.Resolved(new(second, "BBB", "B", "Company", "feature119"), new("exact_ticker", 1m))),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);

        var result = await adapter.ResolveAsync("symbol_pair_within_industry", Interpretation("AAA", "BBB"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.DifferentIndustries, result.Status);
        Assert.Null(result.IndustryId);
    }

    [Fact]
    public async Task Adapter_PairCapabilityRequiresExactlyTwoCanonicalCompanies()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var companies = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        db.Companies.AddRange(companies.Select((id, index) => new NormalizedCompanyRow
        {
            Id = id,
            ProviderName = "NoavaranCurrentApi",
            IndustryId = industry,
            Ticker = $"S{index}"
        }));
        await db.SaveChangesAsync();
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(companies.Select((id, index) =>
                (EntityResolutionResult)new EntityResolutionResult.Resolved(
                    new(id, $"S{index}", $"Company {index}", "Company", "feature119"),
                    new("exact_ticker", 1m))).ToArray()),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);

        var result = await adapter.ResolveAsync(
            "symbol_pair_within_industry",
            Interpretation("S0", "S1", "S2"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Missing, result.Status);
    }

    private static QueryInterpretation Interpretation(params string[] mentions) =>
        new("relative", "relative", "en", [], mentions.Select((text, index) => new EntityMention(text, index * 4, text.Length)).ToArray(), [], null, null, null, [], [], 1m, [], 1);

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeCompanyResolver(params EntityResolutionResult[] results) : ICanonicalQueryEntityResolver
    {
        private int index;
        public List<string?> Mentions { get; } = [];
        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default)
        {
            Mentions.Add(mention);
            return Task.FromResult(results[Math.Min(index++, results.Length - 1)]);
        }
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult(results[0]);
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([]);
    }

    private sealed class FakeIndustryResolver(IndustryResolutionResult result) : ICanonicalQueryIndustryResolver
    {
        public Task<IndustryResolutionResult> ResolveIndustryMentionAsync(string? mention, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
