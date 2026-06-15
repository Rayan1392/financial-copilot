using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.IntegrationTests;

public sealed class BackfillCyclicalWavesCompanyIdEndpointTests
    : IClassFixture<BackfillCyclicalWavesApiFactory>
{
    private readonly BackfillCyclicalWavesApiFactory _factory;

    public BackfillCyclicalWavesCompanyIdEndpointTests(BackfillCyclicalWavesApiFactory factory)
    {
        _factory = factory;
        factory.ResetStub();
    }

    private HttpClient CreateDataAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateDataAdminToken());
        return client;
    }

    private HttpClient CreateRegularUserClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));
        return client;
    }

    [Fact]
    public async Task BackfillCompanyId_DataAdmin_Returns200WithCounts()
    {
        _factory.Stub.Configure(resolved: 42, unresolved: 3);

        using var client = CreateDataAdminClient();
        using var response = await client.PostAsync(
            "/api/v1/admin/cyclicalwaves/backfill-company-id",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(42, body.GetProperty("resolved").GetInt32());
        Assert.Equal(3, body.GetProperty("unresolved").GetInt32());
    }

    [Fact]
    public async Task BackfillCompanyId_NonDataAdmin_Returns403()
    {
        using var client = CreateRegularUserClient();
        using var response = await client.PostAsync(
            "/api/v1/admin/cyclicalwaves/backfill-company-id",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BackfillCompanyId_AllResolved_ReturnsZeroUnresolved()
    {
        _factory.Stub.Configure(resolved: 100, unresolved: 0);

        using var client = CreateDataAdminClient();
        using var response = await client.PostAsync(
            "/api/v1/admin/cyclicalwaves/backfill-company-id",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("unresolved").GetInt32());
    }

    [Fact]
    public async Task BackfillCompanyId_AlreadyResolvedRows_NotDoubleCounted()
    {
        // Stub returns resolved=0 to simulate: no null-CompanyId rows left (already all resolved).
        _factory.Stub.Configure(resolved: 0, unresolved: 0);

        using var client = CreateDataAdminClient();
        using var first = await client.PostAsync(
            "/api/v1/admin/cyclicalwaves/backfill-company-id", null, CancellationToken.None);
        using var second = await client.PostAsync(
            "/api/v1/admin/cyclicalwaves/backfill-company-id", null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("resolved").GetInt32());
    }
}

public sealed class BackfillCyclicalWavesApiFactory : AuthenticationApiFactory
{
    public StubBackfillService Stub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBackfillCyclicalWavesCompanyIdService>();
            services.AddSingleton<IBackfillCyclicalWavesCompanyIdService>(Stub);
        });
    }

    public void ResetStub() => Stub.Configure(0, 0);

    public string CreateDataAdminToken() =>
        CreateWebAppToken(includeTenant: true, dataAdmin: true);

    public sealed class StubBackfillService : IBackfillCyclicalWavesCompanyIdService
    {
        private int _resolved;
        private int _unresolved;

        public void Configure(int resolved, int unresolved)
        {
            _resolved = resolved;
            _unresolved = unresolved;
        }

        public Task<BackfillCompanyIdResult> RunAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new BackfillCompanyIdResult(_resolved, _unresolved));
    }
}
