using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class IndustryComparisonRoutingEndpointTests : IClassFixture<IndustryComparisonApiFactory>
{
    private readonly IndustryComparisonApiFactory factory;

    public IndustryComparisonRoutingEndpointTests(IndustryComparisonApiFactory factory)
    {
        this.factory = factory;
        factory.EnsureComparisonSeeded();
    }

    [Theory]
    [InlineData("کگهر را با صنعت خودش مقایسه کن")]
    [InlineData("کگهر رو با صنعتش مقایسه کن")]
    [InlineData("مقایسه کگهر با صنعت")]
    [InlineData("کگهر نسبت به شرکت‌های هم‌گروه چطوره؟")]
    [InlineData("وضعیت کگهر در مقایسه با هم‌صنعتی‌ها")]
    [InlineData("کگهر را با شرکت‌های صنعت خودش مقایسه کن")]
    public async Task Api_ResolvesExactTickerAndReturnsExistingIndustryValuationTable(string message)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message },
            CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(CancellationToken.None),
            cancellationToken: CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("symbol_vs_industry_relative_valuation", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        var answer = root.GetProperty("textAnswer").GetString()!;
        Assert.True(answer.Contains("کگهر", StringComparison.Ordinal), root.ToString());
        Assert.Contains("P/E", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("P/S", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("قیمت به تعادلی", answer);
        Assert.Contains("میانگین صنعت", answer);
        Assert.DoesNotContain("مطلوب‌تر از معیار گروه", answer);
        Assert.Contains("AAA", answer);
    }
}

public sealed class IndustryComparisonApiFactory : V2Feature128CanaryApiFactory
{
    public static readonly Guid CompanyId = Guid.Parse("52000000-0000-0000-0000-000000000101");
    public static readonly Guid PeerId = Guid.Parse("52000000-0000-0000-0000-000000000102");
    public static readonly Guid GroupId = Guid.Parse("52000000-0000-0000-0000-000000000103");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SemanticRouting:DefaultMode"] = "Canary",
                ["SemanticRouting:Capabilities:symbol_vs_industry_relative_valuation"] = "Canary",
                ["SemanticRouting:CanaryPercentage"] = "100"
            }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICanonicalQueryEntityResolver>();
            services.RemoveAll<ICanonicalQueryIndustryResolver>();
            var resolver = new TestCanonicalResolver();
            services.AddSingleton<ICanonicalQueryEntityResolver>(resolver);
            services.AddSingleton<ICanonicalQueryIndustryResolver>(resolver);
            services.RemoveAll<IIndustryRelativeValuationReadRepository>();
            services.AddSingleton<IIndustryRelativeValuationReadRepository>(new TestValuationRepository());
        });
    }

    public void EnsureComparisonSeeded()
    {
        EnsureSeeded();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        if (db.IndustryGroups.Any(group => group.Id == GroupId)) return;

        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = GroupId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "97",
            Name = "Iron Ore Producers"
        });
        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = CompanyId,
                ProviderName = "NoavaranCurrentApi",
                Ticker = "کگهر",
                Name = "Gohar Mining",
                GroupId = GroupId
            },
            new NormalizedCompanyRow
            {
                Id = PeerId,
                ProviderName = "NoavaranCurrentApi",
                Ticker = "AAA",
                Name = "Peer Mining",
                GroupId = GroupId
            });
        db.NoavaranEligibleCompanies.AddRange(
            new NoavaranEligibleCompanyRow
            {
                Id = CompanyId,
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "kegahr-test",
                Name = "Gohar Mining",
                IndustryId = Guid.Parse("52000000-0000-0000-0000-000000000104"),
                GroupId = GroupId,
                CompanySymbol = "کگهر"
            },
            new NoavaranEligibleCompanyRow
            {
                Id = PeerId,
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = "peer-test",
                Name = "Peer Mining",
                IndustryId = Guid.Parse("52000000-0000-0000-0000-000000000104"),
                GroupId = GroupId,
                CompanySymbol = "AAA"
            });
        db.SaveChanges();
    }

    private sealed class TestCanonicalResolver : ICanonicalQueryEntityResolver, ICanonicalQueryIndustryResolver
    {
        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) =>
            Task.FromResult<EntityResolutionResult>(string.Equals(mention, "کگهر", StringComparison.Ordinal)
                ? new EntityResolutionResult.Resolved(
                    new CanonicalQueryEntity(CompanyId, "کگهر", "Gohar Mining", "Company", "exact_ticker"),
                    new EntityResolutionEvidence("exact_ticker", 1m))
                : new EntityResolutionResult.Ambiguous([
                    new(new CanonicalQueryEntity(Guid.NewGuid(), "AAA", "Ambiguous A", "Company", "fuzzy_candidate"), 0.6m, "fuzzy_candidate"),
                    new(new CanonicalQueryEntity(Guid.NewGuid(), "AAB", "Ambiguous B", "Company", "fuzzy_candidate"), 0.59m, "fuzzy_candidate")]));

        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            ResolveMentionAsync("کگهر", cancellationToken);

        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) =>
            ResolveMentionAsync("کگهر", cancellationToken);

        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([
                new(new CanonicalQueryEntity(CompanyId, "کگهر", "Gohar Mining", "Company", "exact_ticker"), new("exact_ticker", 1m))]);

        public Task<IndustryResolutionResult> ResolveIndustryMentionAsync(string? mention, CancellationToken cancellationToken = default) =>
            Task.FromResult<IndustryResolutionResult>(new IndustryResolutionResult.NotFound(mention ?? string.Empty));
    }

    private sealed class TestValuationRepository : IIndustryRelativeValuationReadRepository
    {
        public Task<IndustryRelativeValuationReadModel?> ReadAsync(IndustryRelativeValuationReadRequest request, CancellationToken cancellationToken = default)
        {
            var pe = Metric("PE", 42.5m, "Green");
            var ps = Metric("PS", 67.5m, "Green");
            var equilibrium = Metric("Equilibrium", 12.5m, "Green");
            var model = new IndustryRelativeValuationReadModel(
                request.CapabilityCode,
                GroupId,
                "97",
                "Iron Ore Producers",
                new DateOnly(2026, 10, 6),
                Guid.NewGuid(),
                1,
                "Published",
                DateTimeOffset.UtcNow,
                "IQR-v1",
                "rank-v1",
                2,
                2,
                [
                    new(CompanyId, "کگهر", "Gohar Mining", 1, 2, pe, ps, equilibrium),
                    new(PeerId, "AAA", "Peer Mining", 2, 2, pe, ps, equilibrium)
                ],
                [Metric("PE", 50m, "Green"), Metric("PS", 70m, "Green"), Metric("Equilibrium", 20m, "Green")],
                "Published");
            return Task.FromResult<IndustryRelativeValuationReadModel?>(model);
        }

        private static RelativeValuationMetricReadModel Metric(string kind, decimal percent, string classification) =>
            new(percent, percent, classification, false, string.Empty, "Available", kind, "Ready", 2, 2);
    }
}
