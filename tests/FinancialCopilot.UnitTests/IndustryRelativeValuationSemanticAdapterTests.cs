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
        var company = Guid.NewGuid();
        db.Industries.Add(new NormalizedIndustryRow { Id = industry, ProviderName = "NoavaranCurrentApi", ExternalId = "7", Name = "Steel" });
        db.Companies.Add(new NormalizedCompanyRow { Id = company, ProviderName = "NoavaranCurrentApi", IndustryId = industry, Ticker = "AAA", Name = "Alpha" });
        await db.SaveChangesAsync();

        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(new EntityResolutionResult.Resolved(new(company, "AAA", "Alpha", "Company", "feature119"), new("exact_ticker", 1m))),
            new FakeIndustryResolver(new IndustryResolutionResult.Missing("Industry")),
            db);

        var result = await adapter.ResolveAsync("symbol_vs_industry_relative_valuation", Interpretation("AAA"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Resolved, result.Status);
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
    public async Task Adapter_ResolvesIndustryOnlyThroughCanonicalIndustryAuthority()
    {
        await using var db = CreateDb();
        var industry = Guid.NewGuid();
        var canonicalIndustry = new CanonicalQueryIndustry(industry, "steel", "Steel", "NoavaranCurrentApi", "feature119");
        var adapter = new IndustryRelativeValuationSemanticAdapter(
            new FakeCompanyResolver(new EntityResolutionResult.NotFound("steel")),
            new FakeIndustryResolver(new IndustryResolutionResult.Resolved(canonicalIndustry, new("exact_industry", 1m))),
            db);

        var result = await adapter.ResolveAsync("industry_relative_valuation_ranking", Interpretation("steel"));

        Assert.Equal(IndustryRelativeValuationResolutionStatus.Resolved, result.Status);
        Assert.Equal(industry, result.IndustryId);
        Assert.Empty(result.CompanyIds!);
    }

    [Fact]
    public async Task Adapter_ReturnsDifferentIndustryWithoutComparison()
    {
        await using var db = CreateDb();
        var firstIndustry = Guid.NewGuid();
        var secondIndustry = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        db.Companies.AddRange(
            new NormalizedCompanyRow { Id = first, ProviderName = "NoavaranCurrentApi", IndustryId = firstIndustry, Ticker = "AAA" },
            new NormalizedCompanyRow { Id = second, ProviderName = "NoavaranCurrentApi", IndustryId = secondIndustry, Ticker = "BBB" });
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

    private static QueryInterpretation Interpretation(params string[] mentions) =>
        new("relative", "relative", "en", [], mentions.Select((text, index) => new EntityMention(text, index * 4, text.Length)).ToArray(), [], null, null, null, [], [], 1m, [], 1);

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FakeCompanyResolver(params EntityResolutionResult[] results) : ICanonicalQueryEntityResolver
    {
        private int index;
        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) =>
            Task.FromResult(results[Math.Min(index++, results.Length - 1)]);
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult(results[0]);
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([]);
    }

    private sealed class FakeIndustryResolver(IndustryResolutionResult result) : ICanonicalQueryIndustryResolver
    {
        public Task<IndustryResolutionResult> ResolveIndustryMentionAsync(string? mention, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
