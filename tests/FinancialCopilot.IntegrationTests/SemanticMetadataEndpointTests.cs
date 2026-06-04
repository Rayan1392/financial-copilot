using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class SemanticMetadataEndpointTests : IClassFixture<AssistedMetadataApiFactory>
{
    private readonly AssistedMetadataApiFactory _factory;

    public SemanticMetadataEndpointTests(AssistedMetadataApiFactory factory)
    {
        _factory = factory;
        factory.EnsureMetadataSeeded();
    }

    [Fact]
    public async Task MetricsMetadata_RequiresAuthenticatedFacadeActor()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/ai/v1/metadata/metrics", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MetricsMetadata_ReturnsRegisteredVersionedSemanticDefinitions()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync("/api/ai/v1/metadata/metrics", CancellationToken.None);
        await using var body = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: CancellationToken.None);
        var pe = document.RootElement.GetProperty("metrics")
            .EnumerateArray()
            .Single(metric => metric.GetProperty("metricCode").GetString() == "PE_TTM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("v1", pe.GetProperty("metricVersion").GetString());
        Assert.Contains(
            pe.GetProperty("aliases").EnumerateArray(),
            alias => alias.GetProperty("language").GetString() == "fa-IR");
        Assert.Contains(
            pe.GetProperty("calculationPolicyVersions").EnumerateArray(),
            policy => policy.GetString() == "vendor-pe-ratio-passthrough-v1");
    }

    [Fact]
    public async Task PeriodMetadata_ReturnsBackendOwnedPersianAndEnglishLabels()
    {
        using var client = UserClient();

        using var response = await client.GetAsync("/api/ai/v1/metadata/periods", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var latestQuarter = document.RootElement
            .EnumerateArray()
            .Single(period => period.GetProperty("code").GetString() == "LatestQuarter");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Latest quarter", latestQuarter.GetProperty("displayName").GetString());
        Assert.Equal("آخرین فصل", latestQuarter.GetProperty("displayNameFa").GetString());
    }

    [Fact]
    public async Task SymbolMetadata_SearchesNormalizedProjection_AndHonorsLimit()
    {
        using var client = UserClient();

        using var response = await client.GetAsync(
            "/api/ai/v1/metadata/symbols?search=Alpha&limit=1",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var symbols = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(symbols);
        Assert.Equal("ALPHA", symbols[0].GetProperty("symbolCode").GetString());
        Assert.Equal("Technology", symbols[0].GetProperty("industryName").GetString());
    }

    [Fact]
    public async Task IndustryMetadata_SearchesNormalizedProjection()
    {
        using var client = UserClient();

        using var response = await client.GetAsync(
            "/api/ai/v1/metadata/industries?search=tech&limit=20",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var industry = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Technology", industry.GetProperty("displayName").GetString());
    }

    [Theory]
    [InlineData("/api/ai/v1/metadata/symbols?limit=0")]
    [InlineData("/api/ai/v1/metadata/symbols?limit=51")]
    [InlineData("/api/ai/v1/metadata/industries?limit=0")]
    public async Task DiscoveryMetadata_RejectsOutOfBoundsLimit(string path)
    {
        using var client = UserClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DiscoveryMetadata_RejectsOverlongSearch()
    {
        using var client = UserClient();

        using var response = await client.GetAsync(
            $"/api/ai/v1/metadata/symbols?search={new string('a', 101)}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/ai/v1/metadata/periods")]
    [InlineData("/api/ai/v1/metadata/symbols")]
    [InlineData("/api/ai/v1/metadata/industries")]
    public async Task DiscoveryMetadata_RequiresAuthenticatedFacadeActor(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class AssistedMetadataApiFactory : AiFacadeApiFactory
{
    private bool _metadataSeeded;
    private readonly object _metadataSeedLock = new();

    public void EnsureMetadataSeeded()
    {
        if (_metadataSeeded) return;

        lock (_metadataSeedLock)
        {
            if (_metadataSeeded) return;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            db.Database.EnsureCreated();
            var industry = new NormalizedIndustryRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "Test",
                ExternalId = "TECH",
                Name = "Technology",
                LastSynchronizedAt = DateTimeOffset.UtcNow
            };
            var alpha = Company("1", "Alpha Company", industry.Id);
            var beta = Company("2", "Beta Company", industry.Id);
            db.Industries.Add(industry);
            db.Companies.AddRange(alpha, beta);
            db.Symbols.AddRange(Symbol(alpha.Id, "ALPHA"), Symbol(beta.Id, "BETA"));
            db.SaveChanges();
            _metadataSeeded = true;
        }
    }

    private static NormalizedCompanyRow Company(string id, string name, Guid industryId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "Test",
            ExternalCompanyId = id,
            Name = name,
            NameEnglish = name,
            IndustryId = industryId,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        };

    private static NormalizedSymbolRow Symbol(Guid companyId, string symbolCode) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = "Test",
            ExternalSymbolId = symbolCode,
            SymbolCode = symbolCode,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        };
}
