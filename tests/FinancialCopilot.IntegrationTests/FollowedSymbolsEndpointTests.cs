using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class FollowedSymbolsEndpointTests : IClassFixture<FollowedSymbolsApiFactory>
{
    private readonly FollowedSymbolsApiFactory _factory;

    public FollowedSymbolsEndpointTests(FollowedSymbolsApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
        factory.EnsureSeeded();
        factory.ResetFollowedSymbols();
    }

    [Fact]
    public async Task FollowThenList_ReturnsCanonicalCompanyShape()
    {
        using var client = UserClient();

        using var followResponse = await client.PostAsync("/api/v1/followed-symbols/me/100", null, CancellationToken.None);
        using var listResponse = await client.GetAsync("/api/v1/followed-symbols/me", CancellationToken.None);
        using var document = await ReadJsonAsync(listResponse);
        var symbol = Assert.Single(document.RootElement.GetProperty("symbols").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal("100", symbol.GetProperty("externalCompanyId").GetString());
        Assert.Equal("FOO", symbol.GetProperty("symbol").GetString());
        Assert.Equal("Foo Company", symbol.GetProperty("companyName").GetString());
        Assert.True(symbol.TryGetProperty("followedAtUtc", out _));
    }

    [Fact]
    public async Task Follow_IsScopedToAuthenticatedActor()
    {
        using var owner = UserClient();
        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var followResponse = await owner.PostAsync("/api/v1/followed-symbols/me/100", null, CancellationToken.None);
        using var peerResponse = await apiClient.GetAsync("/api/v1/followed-symbols/me", CancellationToken.None);
        using var document = await ReadJsonAsync(peerResponse);

        Assert.Equal(HttpStatusCode.OK, followResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, peerResponse.StatusCode);
        Assert.Empty(document.RootElement.GetProperty("symbols").EnumerateArray());
    }

    [Fact]
    public async Task Follow_RejectsUnknownCompany()
    {
        using var client = UserClient();

        using var response = await client.PostAsync("/api/v1/followed-symbols/me/missing", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceThenUnfollow_UsesIdempotentActorSet()
    {
        using var client = UserClient();

        using var replaceResponse = await client.PutAsJsonAsync(
            "/api/v1/followed-symbols/me",
            new { externalCompanyIds = new[] { "100", "200", "200" } },
            CancellationToken.None);
        using var unfollowResponse = await client.DeleteAsync("/api/v1/followed-symbols/me/100", CancellationToken.None);
        using var document = await ReadJsonAsync(unfollowResponse);
        var remaining = Assert.Single(document.RootElement.GetProperty("symbols").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, replaceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unfollowResponse.StatusCode);
        Assert.Equal("200", remaining.GetProperty("externalCompanyId").GetString());
    }

    [Fact]
    public async Task SymbolMetadata_IncludesExternalCompanyId_ForCanonicalFollowActions()
    {
        using var client = UserClient();

        using var response = await client.GetAsync("/api/ai/v1/metadata/symbols?search=FOO&limit=5", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var symbol = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("100", symbol.GetProperty("externalCompanyId").GetString());
        Assert.Equal("FOO", symbol.GetProperty("symbolCode").GetString());
    }

    [Theory]
    [InlineData("/api/v1/followed-symbols/me")]
    public async Task FollowedSymbolsEndpoints_WithoutCredentials_ReturnUnauthorized(string path)
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

public sealed class FollowedSymbolsApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"followed-symbols-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(DateTimeOffset.Parse("2026-07-10T08:00:00Z")));
        });
    }

    public void EnsureSeeded()
    {
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            db.Database.EnsureCreated();
            db.Companies.AddRange(
                Company("100", "FOO", "Foo Company", "Foo Company English"),
                Company("200", "BAR", "Bar Company", null));
            db.SaveChanges();
            _seeded = true;
        }
    }

    public void ResetFollowedSymbols()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.FollowedSymbols.RemoveRange(db.FollowedSymbols);
        db.SaveChanges();
    }

    private static NormalizedCompanyRow Company(
        string externalCompanyId,
        string symbol,
        string name,
        string? nameEnglish) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = externalCompanyId,
            Name = name,
            NameEnglish = nameEnglish,
            Ticker = symbol,
            TseSymbol = symbol,
            CompanySymbol = symbol,
            LastSynchronizedAt = DateTimeOffset.Parse("2026-07-10T08:00:00Z")
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
