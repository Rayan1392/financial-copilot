using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 148 T148-08.3: CyclicalWaves v2 Admin operations, authorization and queueing.</summary>
public sealed class CyclicalWavesV2AdminEndpointTests : IClassFixture<CyclicalWavesV2AdminApiFactory>
{
    private readonly CyclicalWavesV2AdminApiFactory _factory;

    public CyclicalWavesV2AdminEndpointTests(CyclicalWavesV2AdminApiFactory factory)
    {
        _factory = factory;
        factory.Runs.Reset();
        factory.Coordinator.Reset();
    }

    [Theory]
    [InlineData("POST", "/api/v1/admin/cyclicalwaves-v2/catalog/sync")]
    [InlineData("POST", "/api/v1/admin/cyclicalwaves-v2/sync")]
    [InlineData("GET", "/api/v1/admin/cyclicalwaves-v2/status")]
    public async Task NonDataAdminIsForbidden(string method, string path)
    {
        using var client = Client(dataAdmin: false);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.Coordinator.Requests);
    }

    [Fact]
    public async Task CatalogSyncRunsInlineAndReturnsCounts()
    {
        using var response = await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/catalog/sync", null);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(683, json.RootElement.GetProperty("counts").GetProperty("total").GetInt32());
        var request = Assert.Single(_factory.Coordinator.Requests);
        Assert.Equal(CyclicalWavesV2SyncScope.Catalog, request.Scope);
        Assert.Contains(":", request.RequestedBy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TickerSyncPassesThePersianTicker()
    {
        using var response = await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/tickers/" + Uri.EscapeDataString("فولاد") + "/sync", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("فولاد", Assert.Single(_factory.Coordinator.Requests).Ticker);
    }

    [Theory]
    [InlineData("/api/v1/admin/cyclicalwaves-v2/screener/sync", CyclicalWavesV2SyncScope.Screener, null)]
    [InlineData("/api/v1/admin/cyclicalwaves-v2/income-statements/sync", CyclicalWavesV2SyncScope.IncomeStatements, null)]
    [InlineData("/api/v1/admin/cyclicalwaves-v2/analysis/sync?mode=full", CyclicalWavesV2SyncScope.Analysis, CyclicalWavesV2AnalysisMode.Full)]
    [InlineData("/api/v1/admin/cyclicalwaves-v2/sync", CyclicalWavesV2SyncScope.Full, null)]
    public async Task LongOperationsAreQueuedWith202(string path, CyclicalWavesV2SyncScope scope, CyclicalWavesV2AnalysisMode? mode)
    {
        using var response = await Client().PostAsync(path, null);
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Requested", json.RootElement.GetProperty("status").GetString());
        var queued = Assert.Single(_factory.Runs.Requested);
        Assert.Equal((scope, mode), (queued.Scope, queued.AnalysisMode));
        Assert.Empty(_factory.Coordinator.Requests);
    }

    [Fact]
    public async Task QueueingWhileARunIsActiveReturns409WithTheActiveRunId()
    {
        using var first = await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/sync", null);
        using var second = await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/screener/sync", null);
        using var json = await ReadJsonAsync(second);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(_factory.Runs.Ids[0], json.RootElement.GetProperty("existingRunId").GetGuid());
    }

    [Fact]
    public async Task InvalidAnalysisModeIsRejected()
    {
        using var response = await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/analysis/sync?mode=everything", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StatusReportsFlagsScheduleAndRecentRuns()
    {
        await Client().PostAsync("/api/v1/admin/cyclicalwaves-v2/sync", null);

        using var response = await Client().GetAsync("/api/v1/admin/cyclicalwaves-v2/status?recent=5");
        using var json = await ReadJsonAsync(response);
        var root = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("ingestionEnabled").GetBoolean());
        Assert.Equal("30 23 * * *", root.GetProperty("dailySyncCronUtc").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("nextScheduledRunUtc").ValueKind);
        Assert.True(root.GetProperty("credentialsConfigured").GetBoolean());
        Assert.Equal("Requested", root.GetProperty("activeRun").GetProperty("status").GetString());
        Assert.Single(root.GetProperty("recentRuns").EnumerateArray());
        Assert.DoesNotContain("integration-secret", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    private HttpClient Client(bool dataAdmin = true)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateWebAppToken(includeTenant: true, dataAdmin: dataAdmin));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}

public sealed class CyclicalWavesV2AdminApiFactory : AuthenticationApiFactory
{
    public InMemoryRunStore Runs { get; } = new();
    public RecordingCoordinator Coordinator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CyclicalWavesV2:IngestionEnabled"] = "true",
            ["CyclicalWavesV2:DailySyncEnabled"] = "true",
            ["CyclicalWavesV2:Identifier"] = "integration-user",
            ["CyclicalWavesV2:Password"] = "integration-secret"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICyclicalWavesV2SyncRunStore>();
            services.RemoveAll<ICyclicalWavesV2SyncCoordinator>();
            services.AddSingleton<ICyclicalWavesV2SyncRunStore>(Runs);
            services.AddSingleton<ICyclicalWavesV2SyncCoordinator>(Coordinator);
        });
    }

    public sealed class RecordingCoordinator : ICyclicalWavesV2SyncCoordinator
    {
        private readonly List<CyclicalWavesV2SyncRequest> _requests = [];
        public IReadOnlyList<CyclicalWavesV2SyncRequest> Requests => _requests;

        public void Reset() => _requests.Clear();

        public Task<CyclicalWavesV2SyncOutcome> RunAsync(CyclicalWavesV2SyncRequest request, string lockOwner, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.FromResult(new CyclicalWavesV2SyncOutcome(Guid.NewGuid(), CyclicalWavesV2SyncRunStatus.Succeeded,
                new CyclicalWavesV2RunCounts(683, 683, 683, 0, 0), "Catalog tickers=683 added=683 deactivated=0 reactivated=0"));
        }

        public Task<CyclicalWavesV2SyncOutcome> RunClaimedAsync(CyclicalWavesV2ClaimedRun run, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The API never runs queued work.");
    }

    public sealed class InMemoryRunStore : ICyclicalWavesV2SyncRunStore
    {
        private readonly List<CyclicalWavesV2SyncRunSummary> _runs = [];
        public List<Guid> Ids { get; } = [];
        public IReadOnlyList<CyclicalWavesV2SyncRequest> Requested => _runs
            .Select(run => new CyclicalWavesV2SyncRequest(run.Scope, run.Ticker, run.AnalysisMode, run.Trigger, run.RequestedBy))
            .ToList();

        public void Reset()
        {
            _runs.Clear();
            Ids.Clear();
        }

        public Task<CyclicalWavesV2RequestResult> RequestAsync(CyclicalWavesV2SyncRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var active = _runs.FirstOrDefault(run => run.Status is CyclicalWavesV2SyncRunStatus.Requested or CyclicalWavesV2SyncRunStatus.Running);
            if (active is not null)
                return Task.FromResult(new CyclicalWavesV2RequestResult(active.RunId, false));
            var id = Guid.NewGuid();
            Ids.Add(id);
            _runs.Add(new CyclicalWavesV2SyncRunSummary(id, request.Scope, request.Ticker, request.AnalysisMode, request.Trigger,
                CyclicalWavesV2SyncRunStatus.Requested, now, null, null, new(0, 0, 0, 0, 0), null, request.RequestedBy));
            return Task.FromResult(new CyclicalWavesV2RequestResult(id, true));
        }

        public Task<IReadOnlyList<CyclicalWavesV2SyncRunSummary>> GetRecentRunsAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CyclicalWavesV2SyncRunSummary>>(_runs.TakeLast(count).Reverse().ToList());

        public Task<Guid?> TryStartAsync(CyclicalWavesV2SyncRequest request, string lockOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<CyclicalWavesV2ClaimedRun?> ClaimNextRequestedAsync(string lockOwner, DateTimeOffset now, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) =>
            Task.FromResult<CyclicalWavesV2ClaimedRun?>(null);

        public Task HeartbeatAsync(Guid runId, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteAsync(Guid runId, CyclicalWavesV2SyncRunStatus status, CyclicalWavesV2RunCounts counts, string? errorSummary, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> ExpireStaleLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<DateTimeOffset?> GetLastCompletedFullRunStartAsync(CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(null);
    }
}
