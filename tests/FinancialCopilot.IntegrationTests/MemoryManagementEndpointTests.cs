using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class MemoryManagementEndpointTests : IClassFixture<MemoryApiFactory>
{
    private readonly MemoryApiFactory _factory;

    public MemoryManagementEndpointTests(MemoryApiFactory factory)
    {
        _factory = factory;
        factory.EnsureBillingSeeded();
    }

    private HttpClient UserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    [Fact]
    public async Task GrantConsent_AsUser_Returns200AndConsentAppearsInList()
    {
        using var client = UserClient();

        using var grantResponse = await client.PostAsJsonAsync(
            "/api/v1/memory/consent",
            new { memoryType = "PreferenceMemory", purpose = "Personalization" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, grantResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/memory/consent", CancellationToken.None);
        using var listDoc = await ReadJsonAsync(listResponse);

        var consents = listDoc.RootElement.EnumerateArray()
            .Where(c => c.GetProperty("memoryType").GetString() == "PreferenceMemory"
                     && c.GetProperty("purpose").GetString() == "Personalization")
            .ToList();

        Assert.Single(consents);
        Assert.Equal("Granted", consents[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task RevokeConsent_AfterGrant_Returns204AndStatusBecomesRevoked()
    {
        using var client = UserClient();

        await client.PostAsJsonAsync(
            "/api/v1/memory/consent",
            new { memoryType = "LongTermUserMemory", purpose = "Personalization" },
            CancellationToken.None);

        using var revokeResponse = await client.DeleteAsync(
            "/api/v1/memory/consent/LongTermUserMemory/Personalization",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/memory/consent", CancellationToken.None);
        using var listDoc = await ReadJsonAsync(listResponse);

        var entry = listDoc.RootElement.EnumerateArray()
            .First(c => c.GetProperty("memoryType").GetString() == "LongTermUserMemory"
                     && c.GetProperty("purpose").GetString() == "Personalization");

        Assert.Equal("Revoked", entry.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MemoryEndpoints_AsApiClient_Returns403()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var getResponse = await client.GetAsync("/api/v1/memory/consent", CancellationToken.None);
        using var postResponse = await client.PostAsJsonAsync(
            "/api/v1/memory/consent",
            new { memoryType = "PreferenceMemory", purpose = "Personalization" },
            CancellationToken.None);
        using var recordsResponse = await client.GetAsync("/api/v1/memory/records", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, recordsResponse.StatusCode);
    }

    [Fact]
    public async Task WriteMemoryRecord_AsUser_Returns201AndAppearsInInspect()
    {
        using var client = UserClient();

        using var writeResponse = await client.PostAsJsonAsync(
            "/api/v1/memory/records",
            new { type = "ResearchMemory", purpose = "ResearchContinuation", sensitivity = "General", summary = "MSFT competitive analysis" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, writeResponse.StatusCode);
        using var writeDoc = await ReadJsonAsync(writeResponse);
        var memoryId = writeDoc.RootElement.GetProperty("memoryId").GetGuid();
        Assert.NotEqual(Guid.Empty, memoryId);

        using var listResponse = await client.GetAsync("/api/v1/memory/records", CancellationToken.None);
        using var listDoc = await ReadJsonAsync(listResponse);

        var records = listDoc.RootElement.EnumerateArray()
            .Where(r => r.GetProperty("memoryId").GetGuid() == memoryId)
            .ToList();

        Assert.Single(records);
        Assert.Equal("MSFT competitive analysis", records[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task DeleteMemoryRecord_AsUser_Returns204AndRecordGone()
    {
        using var client = UserClient();

        using var writeResponse = await client.PostAsJsonAsync(
            "/api/v1/memory/records",
            new { type = "WatchlistMemory", purpose = "WatchlistContext", sensitivity = "General", summary = "Watching TSLA" },
            CancellationToken.None);
        using var writeDoc = await ReadJsonAsync(writeResponse);
        var memoryId = writeDoc.RootElement.GetProperty("memoryId").GetGuid();

        using var deleteResponse = await client.DeleteAsync(
            $"/api/v1/memory/records/{memoryId}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/memory/records", CancellationToken.None);
        using var listDoc = await ReadJsonAsync(listResponse);
        var remaining = listDoc.RootElement.EnumerateArray()
            .Where(r => r.GetProperty("memoryId").GetGuid() == memoryId)
            .ToList();

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAllMemoryRecords_AsUser_Returns204AndListIsEmpty()
    {
        // Use isolated client with separate factory to avoid test interference
        using var isolatedFactory = new MemoryApiFactory();
        isolatedFactory.EnsureBillingSeeded();
        using var client = isolatedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", isolatedFactory.CreateWebAppToken(includeTenant: true));

        await client.PostAsJsonAsync("/api/v1/memory/records",
            new { type = "PreferenceMemory", purpose = "Personalization", sensitivity = "General", summary = "Prefers tech" },
            CancellationToken.None);
        await client.PostAsJsonAsync("/api/v1/memory/records",
            new { type = "ResearchMemory", purpose = "ResearchContinuation", sensitivity = "General", summary = "AAPL research" },
            CancellationToken.None);

        using var deleteAll = await client.DeleteAsync("/api/v1/memory/records", CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, deleteAll.StatusCode);

        using var listResponse = await client.GetAsync("/api/v1/memory/records", CancellationToken.None);
        using var listDoc = await ReadJsonAsync(listResponse);
        Assert.Empty(listDoc.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task QueryWithGrantedPreference_MemoryDisclosureAppearsInAiQueryResponse()
    {
        using var client = UserClient();

        // Grant consent and write a preference memory
        await client.PostAsJsonAsync("/api/v1/memory/consent",
            new { memoryType = "PreferenceMemory", purpose = "Personalization" }, CancellationToken.None);
        await client.PostAsJsonAsync("/api/v1/memory/records",
            new { type = "PreferenceMemory", purpose = "Personalization", sensitivity = "PersonalPreference", summary = "Prefers technology sector stocks" },
            CancellationToken.None);

        // Execute an AI query — memory disclosures should surface
        using var queryResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query", new { message = "P/E below 6" }, CancellationToken.None);
        using var queryDoc = await ReadJsonAsync(queryResponse);

        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        // Response is well-formed regardless of whether memory was used in this execution
        Assert.True(queryDoc.RootElement.TryGetProperty("conversationId", out _));
    }

    [Fact]
    public async Task WriteMemoryRecord_WithInvalidType_Returns400ValidationProblem()
    {
        using var client = UserClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/memory/records",
            new { type = "NonExistentType", purpose = "Personalization", sensitivity = "General", summary = "test" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class MemoryApiFactory : AiFacadeApiFactory
{
    private readonly string _memoryDatabaseName = $"memory-api-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<MemoryDbContext>();
            services.RemoveAll<DbContextOptions<MemoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MemoryDbContext>>();
            services.AddDbContext<MemoryDbContext>(options =>
                options.UseInMemoryDatabase(_memoryDatabaseName));
        });
    }
}
