using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class AdminDataOperationsEndpointTests : IClassFixture<AdminDataOperationsApiFactory>
{
    private readonly AdminDataOperationsApiFactory _factory;

    public AdminDataOperationsEndpointTests(AdminDataOperationsApiFactory factory)
    {
        _factory = factory;
        factory.Reset();
    }

    [Fact]
    public async Task DataSync_WithNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { idempotencyKey = "forbidden-symbols" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.PublishedRequests);
    }

    [Fact]
    public async Task DataSync_WithBillingAdmin_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _factory.CreateWebAppToken(includeTenant: true, billingAdmin: true));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { idempotencyKey = "billing-admin-symbols" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DataSync_WithApiClient_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { idempotencyKey = "api-client-symbols" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DataAdmin_CanPublishAllDataSyncDatasets()
    {
        using var client = CreateDataAdminClient();

        using var symbolsResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { idempotencyKey = "sync-symbols" },
            CancellationToken.None);
        using var statementsResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/financial-statements",
            new { externalReference = "company-live", idempotencyKey = "sync-statements" },
            CancellationToken.None);
        using var monthlyResponse = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/monthly-reports",
            new { externalReference = "company-live", idempotencyKey = "sync-monthly" },
            CancellationToken.None);
        using var statements = await ReadJsonAsync(statementsResponse);

        Assert.Equal(HttpStatusCode.Accepted, symbolsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, statementsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, monthlyResponse.StatusCode);
        Assert.Equal("Queued", statements.RootElement.GetProperty("status").GetString());
        Assert.Equal("FinancialStatements", statements.RootElement.GetProperty("dataset").GetString());
        Assert.Equal("company-live", statements.RootElement.GetProperty("externalReference").GetString());
        Assert.Equal(
            [ProviderDataset.Symbols, ProviderDataset.FinancialStatements, ProviderDataset.MonthlyProductionSales],
            _factory.PublishedRequests.Select(request => request.Dataset).ToArray());
    }

    [Fact]
    public async Task DataAdmin_CompanyScopedSyncRequiresExternalReference()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/financial-statements",
            new { idempotencyKey = "missing-company" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.PublishedRequests);
    }

    [Fact]
    public async Task DataAdmin_CanViewSyncRunOperationalFields()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/data-sync/runs?limit=1", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var run = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", run.GetProperty("status").GetString());
        Assert.Equal(7, run.GetProperty("processedRecords").GetInt32());
        Assert.Equal(0, run.GetProperty("errorCount").GetInt32());
        Assert.True(run.TryGetProperty("startedAt", out _));
        Assert.True(run.TryGetProperty("completedAt", out _));
    }

    [Fact]
    public async Task DataAdmin_CanViewProviderHealth()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/provider-health", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TestFinancialProvider", document.RootElement.GetProperty("providerName").GetString());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
    }

    private HttpClient CreateDataAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _factory.CreateWebAppToken(includeTenant: true, dataAdmin: true));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class AdminDataOperationsApiFactory : AuthenticationApiFactory
{
    private readonly CapturingDataSyncPublisher _publisher = new();
    private readonly StubDataSyncRunReader _runReader = new();
    private readonly StubProviderHealthService _providerHealth = new();

    public IReadOnlyCollection<DataSyncRequest> PublishedRequests => _publisher.Requests;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataSyncRequestPublisher>();
            services.RemoveAll<IDataSyncRunReader>();
            services.RemoveAll<IFinancialDataProviderHealthService>();
            services.AddSingleton<IDataSyncRequestPublisher>(_publisher);
            services.AddSingleton<IDataSyncRunReader>(_runReader);
            services.AddSingleton<IFinancialDataProviderHealthService>(_providerHealth);
        });
    }

    public void Reset() => _publisher.Requests.Clear();

    private sealed class CapturingDataSyncPublisher : IDataSyncRequestPublisher
    {
        public List<DataSyncRequest> Requests { get; } = [];

        public Task PublishAsync(DataSyncRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class StubDataSyncRunReader : IDataSyncRunReader
    {
        private static readonly DateTimeOffset RequestedAt = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

        public Task<IReadOnlyCollection<DataSyncRun>> QueryRecentAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<DataSyncRun>>(
                [new DataSyncRun(
                    Guid.NewGuid(),
                    "completed-sync",
                    ProviderDataset.Symbols,
                    null,
                    DataSyncRunStatus.Completed,
                    RequestedAt,
                    RequestedAt.AddSeconds(1),
                    RequestedAt.AddSeconds(2),
                    ProcessedRecords: 7,
                    ErrorCount: 0,
                    ErrorMessage: null,
                    SourcePayloadChecksum: "CHECKSUM")]);
    }

    private sealed class StubProviderHealthService : IFinancialDataProviderHealthService
    {
        public Task<ProviderHealthResult> CheckAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthResult(
                "TestFinancialProvider",
                ProviderHealthStatus.Healthy,
                DateTimeOffset.Parse("2026-05-27T08:00:00Z"),
                "Available."));
    }
}
