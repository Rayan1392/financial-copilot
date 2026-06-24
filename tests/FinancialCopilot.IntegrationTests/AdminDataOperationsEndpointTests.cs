using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.MissingAnswer;
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
    public async Task DataAdmin_CanRouteSymbolsSyncToCodalDbWithoutFullSyncFanOut()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/data-sync/symbols",
            new { idempotencyKey = "codaldb-symbols-only", providerName = " CodalDb " },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = Assert.Single(_factory.PublishedRequests);
        Assert.Equal(ProviderDataset.Symbols, request.Dataset);
        Assert.Equal("CodalDb", request.ProviderName);
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

    [Fact]
    public async Task CodalDb_FullSync_AsDataAdmin_ReturnsRunSummary()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync("/api/v1/admin/codaldb/full-sync", content: null, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("fullReload").GetBoolean());
        Assert.Equal(5, document.RootElement.GetProperty("companiesConsidered").GetInt32());
        Assert.Equal(5, document.RootElement.GetProperty("companiesEnqueued").GetInt32());
        Assert.Equal([true], _factory.CodalDbSync.InvocationModes);
    }

    [Fact]
    public async Task CodalDb_IncrementalSync_AsDataAdmin_InvokesIncrementalMode()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync("/api/v1/admin/codaldb/incremental-sync", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([false], _factory.CodalDbSync.InvocationModes);
    }

    [Fact]
    public async Task CodalDb_FullSync_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsync("/api/v1/admin/codaldb/full-sync", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.CodalDbSync.InvocationModes);
    }

    [Fact]
    public async Task CodalDb_FullSync_AsApiClient_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsync("/api/v1/admin/codaldb/full-sync", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.CodalDbSync.InvocationModes);
    }

    [Fact]
    public async Task NoavaranArchive_DryRun_AsDataAdmin_ReturnsRunWithoutEnqueuing()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-archive/dry-run", new { }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DryRun", document.RootElement.GetProperty("action").GetString());
        Assert.Equal("Succeeded", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("requestsEnqueued").GetInt32());
        var request = Assert.Single(_factory.ArchiveImport.Requests);
        Assert.Equal(ArchiveImportAction.DryRun, request.Action);
        Assert.StartsWith("User:", request.RequestedBy);
    }

    [Fact]
    public async Task NoavaranArchive_Freeze_ThenImport_IsRejected()
    {
        using var client = CreateDataAdminClient();

        using var freeze = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-archive/freeze",
            new { reason = "Archive verified complete." },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, freeze.StatusCode);

        using var import = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-archive/import", new { }, CancellationToken.None);
        using var document = await ReadJsonAsync(import);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        Assert.Equal("RejectedFrozen", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NoavaranArchive_Coverage_AsDataAdmin_ReturnsSummary()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/noavaran-archive/coverage", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("companyMappingValid").GetBoolean());
        Assert.Equal("NoavaranArchiveSql", document.RootElement.GetProperty("coverage").GetProperty("sourceName").GetString());
    }

    [Fact]
    public async Task NoavaranArchive_Import_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-archive/import", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.ArchiveImport.Requests);
    }

    [Fact]
    public async Task NoavaranCurrent_Health_AsDataAdmin_ReportsSeparatelyFromArchive()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/noavaran-current/health", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NoavaranCurrentApi", document.RootElement.GetProperty("sourceName").GetString());
        Assert.Equal("Healthy", document.RootElement.GetProperty("providerHealthStatus").GetString());
        Assert.True(document.RootElement.GetProperty("scheduledSyncEnabled").GetBoolean());
    }

    [Fact]
    public async Task NoavaranCurrent_Gaps_AsDataAdmin_ReturnsBoundaryAndGaps()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/noavaran-current/gaps", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1403, document.RootElement.GetProperty("currentApiBoundaryShamsiYear").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("totalGapRows").GetInt32());
    }

    [Fact]
    public async Task NoavaranCurrent_Backfill_AsDataAdmin_PassesShamsiYearOverride()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-current/backfill", new { fromShamsiYear = 1401 }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1401, document.RootElement.GetProperty("appliedFromShamsiYear").GetInt32());
        var request = Assert.Single(_factory.CurrentApi.BackfillRequests);
        Assert.Equal(1401, request.FromShamsiYearOverride);
        Assert.StartsWith("User:", request.RequestedBy);
    }

    [Fact]
    public async Task NoavaranCurrent_Backfill_RejectsImplausibleShamsiYear()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-current/backfill", new { fromShamsiYear = 3000 }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.CurrentApi.BackfillRequests);
    }

    [Fact]
    public async Task NoavaranCurrent_Backfill_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/noavaran-current/backfill", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.CurrentApi.BackfillRequests);
    }

    [Fact]
    public async Task ProductRevenueMixBackfill_AsDataAdmin_ReturnsSummary()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync(
            "/api/v1/admin/noavaran-current/product-revenue-mix-backfill",
            content: null,
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("companiesConsidered").GetInt32());
        Assert.Equal(9, document.RootElement.GetProperty("companyMonthsDiscovered").GetInt32());
        Assert.Single(_factory.ProductRevenueMixBackfill.Requests);
        Assert.StartsWith("User:", _factory.ProductRevenueMixBackfill.Requests[0].RequestedBy);
    }

    [Fact]
    public async Task ProductRevenueMixBackfill_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsync(
            "/api/v1/admin/noavaran-current/product-revenue-mix-backfill",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.ProductRevenueMixBackfill.Requests);
    }

    [Fact]
    public async Task FundamentalIndexCatchUp_AsDataAdmin_DefaultsTo1403Through1405()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/nadpcoapi/fundamental-index-catch-up", new { }, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Succeeded", document.RootElement.GetProperty("status").GetString());
        var request = Assert.Single(_factory.FundamentalIndexCatchUp.Requests);
        Assert.Equal(1403, request.FromShamsiYear);
        Assert.Equal(1405, request.ToShamsiYear);
        Assert.StartsWith("User:", request.RequestedBy);
    }

    [Fact]
    public async Task FundamentalIndexCatchUp_RejectsInvalidYearRange()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/nadpcoapi/fundamental-index-catch-up",
            new { fromShamsiYear = 1405, toShamsiYear = 1403 },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.FundamentalIndexCatchUp.Requests);
    }

    [Fact]
    public async Task FundamentalIndexCatchUp_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/nadpcoapi/fundamental-index-catch-up", new { }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.FundamentalIndexCatchUp.Requests);
    }

    [Fact]
    public async Task NadpcoApi_FullSync_AsDataAdmin_ReturnsRunSummary()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync("/api/v1/admin/nadpcoapi/full-sync", content: null, CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("FullSync", document.RootElement.GetProperty("runMode").GetString());
        Assert.True(document.RootElement.GetProperty("fullReload").GetBoolean());
        Assert.Equal(4, document.RootElement.GetProperty("companiesConsidered").GetInt32());
        Assert.Equal(13, document.RootElement.GetProperty("requestsEnqueued").GetInt32());
        Assert.Equal([true], _factory.NadpcoApiSync.InvocationModes);
    }

    [Fact]
    public async Task NadpcoApi_IncrementalSync_AsDataAdmin_InvokesIncrementalMode()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync("/api/v1/admin/nadpcoapi/incremental-sync", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([false], _factory.NadpcoApiSync.InvocationModes);
    }

    [Fact]
    public async Task NadpcoApi_CompanyCatalogCleanSlate_AsDataAdmin_ReturnsRunModeAndCleanupCounts()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync(
            "/api/v1/admin/nadpcoapi/company-catalog/clean-slate",
            content: null,
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CompanyCatalogCleanSlate", document.RootElement.GetProperty("runMode").GetString());
        Assert.True(document.RootElement.GetProperty("fullReload").GetBoolean());
        Assert.Equal([true], _factory.NadpcoApiSync.CompanyCatalogModes);
        var cleanSlate = document.RootElement.GetProperty("cleanSlate");
        Assert.Equal(2, cleanSlate.GetProperty("companiesDeleted").GetInt32());
        Assert.Equal(5, cleanSlate.GetProperty("symbolsDeleted").GetInt32());
    }

    [Fact]
    public async Task NadpcoApi_CompanyCatalogRefresh_AsDataAdmin_InvokesRefreshMode()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync(
            "/api/v1/admin/nadpcoapi/company-catalog/refresh",
            content: null,
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CompanyCatalogRefresh", document.RootElement.GetProperty("runMode").GetString());
        Assert.False(document.RootElement.GetProperty("fullReload").GetBoolean());
        Assert.Equal([false], _factory.NadpcoApiSync.CompanyCatalogModes);
    }

    [Fact]
    public async Task NadpcoApi_SyncState_AsDataAdmin_ReturnsOperationalState()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/nadpcoapi/sync-state", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("FinancialStatements", state.GetProperty("dataset").GetString());
        Assert.Equal(4, state.GetProperty("lastCompaniesConsidered").GetInt32());
        Assert.Equal("IncrementalSync", state.GetProperty("lastRunMode").GetString());
    }

    [Fact]
    public async Task NadpcoApiScheduledSync_ManualRun_AsDataAdmin_InvokesCoordinator()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/admin/nadpcoapi/scheduled-sync/run",
            new { reason = "operator-triggered" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Manual", document.RootElement.GetProperty("triggerSource").GetString());
        Assert.Equal("Succeeded", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(["operator-triggered"], _factory.NadpcoScheduledSync.ManualReasons);
    }

    [Fact]
    public async Task NadpcoApiScheduledSync_Status_AsDataAdmin_ReturnsHealthView()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync(
            "/api/v1/admin/nadpcoapi/scheduled-sync/status?recentRunLimit=1",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("enabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("ready").GetBoolean());
        Assert.Equal("Succeeded", document.RootElement
            .GetProperty("recentRuns")
            .EnumerateArray()
            .Single()
            .GetProperty("status")
            .GetString());
    }

    [Fact]
    public async Task NadpcoApiScheduledSync_Runs_AsDataAdmin_ReturnsHistory()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync(
            "/api/v1/admin/nadpcoapi/scheduled-sync/runs?limit=1",
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Succeeded", run.GetProperty("status").GetString());
        Assert.Equal(4, run.GetProperty("processedBatches").GetInt32());
    }

    [Fact]
    public async Task NadpcoApi_FullSync_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsync("/api/v1/admin/nadpcoapi/full-sync", content: null, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.NadpcoApiSync.InvocationModes);
    }

    [Fact]
    public async Task NadpcoApi_CompanyCatalogCleanSlate_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsync(
            "/api/v1/admin/nadpcoapi/company-catalog/clean-slate",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_factory.NadpcoApiSync.CompanyCatalogModes);
    }

    [Fact]
    public async Task StockMarketDb_IntradayTradesSync_AsDataAdmin_InvokesDataset()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.PostAsync(
            "/api/v1/admin/stockmarketdb/intradaytrades/sync?fullReload=true",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([(StockMarketDataset.IntradayTrades, true)], _factory.StockMarketDbSync.Invocations);
    }

    [Fact]
    public async Task StockMarketDb_SyncState_AsDataAdmin_ReturnsOperationalState()
    {
        using var client = CreateDataAdminClient();

        using var response = await client.GetAsync("/api/v1/admin/stockmarketdb/sync-state", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MissingAnswerFeedback_AsDataAdmin_ReturnsItems()
    {
        _factory.MissingAnswerFeedback.Items.Add(new MissingAnswerFeedback(
            Guid.NewGuid(), "user-1", "list revenue growth", "HASH",
            MissingAnswerFeedbackClassification.MetricGap,
            "REVENUE_GROWTH_YOY", "revenue growth",
            SymbolCountTotal: 100, SymbolCountMatched: 0,
            SubmittedAt: DateTimeOffset.UtcNow,
            DateBucket: DateOnly.FromDateTime(DateTime.UtcNow),
            Context: null, FrequencyCount: 3, ResolvedAt: null));

        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = document.RootElement.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("MetricGap", items[0].GetProperty("classification").GetString());
        Assert.Equal("REVENUE_GROWTH_YOY", items[0].GetProperty("requestedMetricCode").GetString());
        Assert.Equal(3, items[0].GetProperty("frequencyCount").GetInt32());
    }

    [Fact]
    public async Task MissingAnswerFeedback_FiltersByClassification()
    {
        _factory.MissingAnswerFeedback.Items.Add(SampleFeedback(MissingAnswerFeedbackClassification.MetricGap));
        _factory.MissingAnswerFeedback.Items.Add(SampleFeedback(MissingAnswerFeedbackClassification.CalculationGap));

        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback?classification=CalculationGap", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        var items = document.RootElement.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("CalculationGap", items[0].GetProperty("classification").GetString());
    }

    [Fact]
    public async Task MissingAnswerFeedback_InvalidClassification_ReturnsValidationError()
    {
        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback?classification=NotAClassification", CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingAnswerFeedback_Summary_ReturnsCountsByClassification()
    {
        _factory.MissingAnswerFeedback.Items.Add(SampleFeedback(MissingAnswerFeedbackClassification.MetricGap, frequencyCount: 5));
        _factory.MissingAnswerFeedback.Items.Add(SampleFeedback(MissingAnswerFeedbackClassification.CalculationGap, frequencyCount: 2));

        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback/summary", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = document.RootElement.GetProperty("countsByClassification");
        Assert.Equal(5, counts.GetProperty("MetricGap").GetInt32());
        Assert.Equal(2, counts.GetProperty("CalculationGap").GetInt32());
        Assert.Equal(7, document.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task MissingAnswerFeedback_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingAnswerFeedback_AsApiClient_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            "/api/v1/admin/missing-answer-feedback", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static MissingAnswerFeedback SampleFeedback(
        MissingAnswerFeedbackClassification classification,
        int frequencyCount = 1) =>
        new(
            Guid.NewGuid(), "user-x", "q", "HASH", classification,
            null, null, 100, 0,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null, frequencyCount, null);

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

    // -----------------------------------------------------------------------
    // Spec 058 — GET /api/v1/admin/data-sync/activity
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DataSyncActivity_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/admin/data-sync/activity",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DataSyncActivity_WithNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync(
            "/api/v1/admin/data-sync/activity",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DataSyncActivity_WithDataAdmin_ReturnsActiveRunInSnapshot()
    {
        const string runId = "test-run-001";
        _factory.ActivityMonitor.SetSnapshot(
            AdminDataOperationsApiFactory.StubDataSyncActivityMonitor.WithRunningItem(runId));

        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/data-sync/activity",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        var activeRuns = doc.RootElement.GetProperty("activeRuns");
        Assert.Equal(1, activeRuns.GetArrayLength());
        Assert.Equal(runId, activeRuns[0].GetProperty("runId").GetString());
        Assert.Equal("Running", activeRuns[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task DataSyncActivity_EmptySnapshot_ReturnsEmptyArrays()
    {
        _factory.ActivityMonitor.Reset();

        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/data-sync/activity",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await ReadJsonAsync(response);
        Assert.Equal(0, doc.RootElement.GetProperty("activeRuns").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("recentRuns").GetArrayLength());
    }

    [Fact]
    public async Task DataSyncActivity_RecentPerProviderOutOfRange_ReturnsBadRequest()
    {
        using var client = CreateDataAdminClient();
        using var response = await client.GetAsync(
            "/api/v1/admin/data-sync/activity?recentPerProvider=0",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class AdminDataOperationsApiFactory : AuthenticationApiFactory
{
    private readonly CapturingDataSyncPublisher _publisher = new();
    private readonly StubDataSyncRunReader _runReader = new();
    private readonly StubProviderHealthService _providerHealth = new();
    private readonly StubCodalDbScheduledSyncService _codalDbSync = new();
    private readonly StubNadpcoApiScheduledSyncService _nadpcoApiSync = new();
    private readonly StubNadpcoScheduledSyncCoordinator _nadpcoScheduledSync = new();
    private readonly StubStockMarketDbSyncService _stockMarketDbSync = new();
    private readonly StubArchiveImportCoordinator _archiveImport = new();
    private readonly StubCurrentApiIngestion _currentApi = new();
    private readonly StubProductRevenueMixBackfill _productRevenueMixBackfill = new();
    private readonly StubFundamentalIndexCatchUp _fundamentalIndexCatchUp = new();
    private readonly StubMissingAnswerFeedbackRepository _missingAnswerFeedback = new();
    private readonly StubDataSyncActivityMonitor _activityMonitor = new();

    public StubMissingAnswerFeedbackRepository MissingAnswerFeedback => _missingAnswerFeedback;
    public StubArchiveImportCoordinator ArchiveImport => _archiveImport;
    public StubCurrentApiIngestion CurrentApi => _currentApi;
    public StubProductRevenueMixBackfill ProductRevenueMixBackfill => _productRevenueMixBackfill;
    public StubFundamentalIndexCatchUp FundamentalIndexCatchUp => _fundamentalIndexCatchUp;
    public StubDataSyncActivityMonitor ActivityMonitor => _activityMonitor;

    public IReadOnlyCollection<DataSyncRequest> PublishedRequests => _publisher.Requests;
    public StubCodalDbScheduledSyncService CodalDbSync => _codalDbSync;
    public StubNadpcoApiScheduledSyncService NadpcoApiSync => _nadpcoApiSync;
    public StubNadpcoScheduledSyncCoordinator NadpcoScheduledSync => _nadpcoScheduledSync;
    public StubStockMarketDbSyncService StockMarketDbSync => _stockMarketDbSync;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataSyncActivityMonitor>();
            services.RemoveAll<IDataSyncRequestPublisher>();
            services.RemoveAll<IDataSyncRunReader>();
            services.RemoveAll<IFinancialDataProviderHealthService>();
            services.RemoveAll<ICodalDbScheduledSyncService>();
            services.RemoveAll<INadpcoApiScheduledSyncService>();
            services.RemoveAll<INadpcoApiSyncStateReader>();
            services.RemoveAll<INadpcoScheduledSyncCoordinator>();
            services.RemoveAll<INadpcoScheduledSyncRunReader>();
            services.RemoveAll<IStockMarketDbSyncService>();
            services.RemoveAll<IStockMarketDbSyncStateReader>();
            services.RemoveAll<IArchiveImportCoordinator>();
            services.RemoveAll<IArchiveImportRunReader>();
            services.RemoveAll<ICurrentApiBackfillCoordinator>();
            services.RemoveAll<ICurrentApiGapReader>();
            services.RemoveAll<IProductRevenueMixBackfillService>();
            services.RemoveAll<IFundamentalIndexCatchUpCoordinator>();
            services.RemoveAll<IFundamentalIndexCatchUpRunReader>();
            services.RemoveAll<IMissingAnswerFeedbackRepository>();
            services.AddSingleton<IDataSyncActivityMonitor>(_activityMonitor);
            services.AddSingleton<IDataSyncRequestPublisher>(_publisher);
            services.AddSingleton<IDataSyncRunReader>(_runReader);
            services.AddSingleton<IFinancialDataProviderHealthService>(_providerHealth);
            services.AddSingleton<ICodalDbScheduledSyncService>(_codalDbSync);
            services.AddSingleton<INadpcoApiScheduledSyncService>(_nadpcoApiSync);
            services.AddSingleton<INadpcoApiSyncStateReader>(_nadpcoApiSync);
            services.AddSingleton<INadpcoScheduledSyncCoordinator>(_nadpcoScheduledSync);
            services.AddSingleton<INadpcoScheduledSyncRunReader>(_nadpcoScheduledSync);
            services.AddSingleton<IStockMarketDbSyncService>(_stockMarketDbSync);
            services.AddSingleton<IStockMarketDbSyncStateReader>(_stockMarketDbSync);
            services.AddSingleton<IArchiveImportCoordinator>(_archiveImport);
            services.AddSingleton<IArchiveImportRunReader>(_archiveImport);
            services.AddSingleton<ICurrentApiBackfillCoordinator>(_currentApi);
            services.AddSingleton<ICurrentApiGapReader>(_currentApi);
            services.AddSingleton<IProductRevenueMixBackfillService>(_productRevenueMixBackfill);
            services.AddSingleton<IFundamentalIndexCatchUpCoordinator>(_fundamentalIndexCatchUp);
            services.AddSingleton<IFundamentalIndexCatchUpRunReader>(_fundamentalIndexCatchUp);
            services.AddSingleton<IMissingAnswerFeedbackRepository>(_missingAnswerFeedback);
        });
    }

    public void Reset()
    {
        _publisher.Requests.Clear();
        _codalDbSync.Reset();
        _nadpcoApiSync.Reset();
        _nadpcoScheduledSync.Reset();
        _stockMarketDbSync.Reset();
        _archiveImport.Reset();
        _currentApi.Reset();
        _productRevenueMixBackfill.Reset();
        _fundamentalIndexCatchUp.Reset();
        _missingAnswerFeedback.Reset();
        _activityMonitor.Reset();
    }

    public sealed class StubMissingAnswerFeedbackRepository : IMissingAnswerFeedbackRepository
    {
        public List<MissingAnswerFeedback> Items { get; } = new();

        public Task UpsertAsync(MissingAnswerFeedback feedback, CancellationToken cancellationToken)
        {
            Items.Add(feedback);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<MissingAnswerFeedback>> QueryAsync(
            MissingAnswerFeedbackQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<MissingAnswerFeedback> q = Items;
            if (query.Classification is not null)
                q = q.Where(item => item.Classification == query.Classification);
            if (!string.IsNullOrWhiteSpace(query.ActorId))
                q = q.Where(item => item.ActorId == query.ActorId);
            return Task.FromResult<IReadOnlyCollection<MissingAnswerFeedback>>(q.ToArray());
        }

        public Task<IReadOnlyDictionary<MissingAnswerFeedbackClassification, int>> GetCountByClassificationAsync(
            DateTimeOffset? dateFrom, DateTimeOffset? dateTo, CancellationToken cancellationToken)
        {
            var grouped = Items
                .GroupBy(item => item.Classification)
                .ToDictionary(g => g.Key, g => g.Sum(item => item.FrequencyCount));
            return Task.FromResult<IReadOnlyDictionary<MissingAnswerFeedbackClassification, int>>(grouped);
        }

        public void Reset() => Items.Clear();
    }

    public sealed class StubCodalDbScheduledSyncService : ICodalDbScheduledSyncService
    {
        public List<bool> InvocationModes { get; } = [];
        public List<bool> DryRunModes { get; } = [];

        public Task<CodalDbScheduledSyncResult> ExecuteAsync(
            bool fullReload, CancellationToken cancellationToken, bool dryRun = false)
        {
            InvocationModes.Add(fullReload);
            DryRunModes.Add(dryRun);
            return Task.FromResult(new CodalDbScheduledSyncResult(
                fullReload,
                CompaniesConsidered: 5,
                CompaniesEnqueued: dryRun ? 0 : 5,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                AdvancedWatermark: DateTimeOffset.Parse("2026-05-31T08:00:00Z"),
                Duration: TimeSpan.FromSeconds(1.25)));
        }

        public void Reset()
        {
            InvocationModes.Clear();
            DryRunModes.Clear();
        }
    }

    public sealed class StubArchiveImportCoordinator : IArchiveImportCoordinator, IArchiveImportRunReader
    {
        public List<ArchiveImportRequest> Requests { get; } = [];
        public bool Frozen { get; set; }

        public Task<ArchiveImportRun> RunAsync(ArchiveImportRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var status = request.Action == ArchiveImportAction.Import && Frozen
                ? ArchiveImportRunStatus.RejectedFrozen
                : ArchiveImportRunStatus.Succeeded;
            if (request.Action == ArchiveImportAction.Freeze)
            {
                Frozen = true;
            }

            return Task.FromResult(new ArchiveImportRun(
                Guid.NewGuid(),
                request.Action,
                status,
                request.RequestedBy,
                request.Datasets,
                request.Reason,
                StartedAt: DateTimeOffset.Parse("2026-06-09T09:00:00Z"),
                FinishedAt: DateTimeOffset.Parse("2026-06-09T09:00:01Z"),
                CompaniesConsidered: request.Action == ArchiveImportAction.DryRun ? 7 : 5,
                RequestsEnqueued: request.Action is ArchiveImportAction.DryRun or ArchiveImportAction.Freeze ? 0 : 5,
                SkippedCount: 0,
                ConflictCount: 0,
                FailedCount: 0,
                Frozen: request.Action == ArchiveImportAction.Freeze,
                Diagnostics: null));
        }

        public Task<ArchiveImportValidationResult> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ArchiveImportValidationResult(
                CompanyMappingValid: true,
                CompaniesWithoutCanonicalSymbol: 0,
                UnmappedExternalCompanyIds: [],
                Coverage: new ArchiveCoverageSummary(
                    "NoavaranArchiveSql",
                    CompanyCount: 5,
                    RowCountByDataset: new Dictionary<string, int> { ["FinancialStatements"] = 10 },
                    RowCountByFiscalYear: new Dictionary<int, int> { [2023] = 10 },
                    Rows: [])));

        public Task<ArchiveFreezeState> GetFreezeStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ArchiveFreezeState(Frozen, Frozen ? DateTimeOffset.Parse("2026-06-09T09:00:00Z") : null, null, null));

        public Task<IReadOnlyCollection<ArchiveImportRun>> QueryRecentAsync(int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ArchiveImportRun>>([]);

        public void Reset()
        {
            Requests.Clear();
            Frozen = false;
        }
    }

    public sealed class StubCurrentApiIngestion : ICurrentApiBackfillCoordinator, ICurrentApiGapReader
    {
        public List<CurrentApiBackfillRequest> BackfillRequests { get; } = [];

        public Task<CurrentApiBackfillResult> BackfillAsync(
            CurrentApiBackfillRequest request, CancellationToken cancellationToken)
        {
            BackfillRequests.Add(request);
            return Task.FromResult(new CurrentApiBackfillResult(
                FullReload: true,
                AppliedFromShamsiYear: request.FromShamsiYearOverride,
                CompaniesConsidered: 4,
                RequestsEnqueued: 12,
                FailedCompanies: 0,
                Duration: "0:00:02"));
        }

        public Task<CurrentApiHealthStatus> GetHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentApiHealthStatus(
                "NoavaranCurrentApi",
                "Healthy",
                "Authenticated token request succeeded.",
                ScheduledSyncEnabled: true,
                LastSuccessfulSyncAt: DateTimeOffset.Parse("2026-06-09T03:00:00Z"),
                NextDueAt: DateTimeOffset.Parse("2026-06-10T03:00:00Z"),
                CheckedAt: DateTimeOffset.Parse("2026-06-09T09:00:00Z")));

        public Task<CurrentApiGapReport> ReportAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentApiGapReport(
                CurrentApiBoundaryShamsiYear: 1403,
                TotalGapRows: 3,
                Gaps:
                [
                    new CurrentApiCoverageGap("FinancialStatements", "1001", 2024, CurrentApiRowCount: 3, ArchiveRowCount: 0)
                ]));

        public void Reset() => BackfillRequests.Clear();
    }

    public sealed class StubProductRevenueMixBackfill : IProductRevenueMixBackfillService
    {
        public List<ProductRevenueMixBackfillRequest> Requests { get; } = [];

        public Task<ProductRevenueMixBackfillResult> RunAsync(
            ProductRevenueMixBackfillRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProductRevenueMixBackfillResult(
                "Completed",
                request.RequestedBy,
                CompaniesConsidered: 4,
                CompanyMonthsDiscovered: 9,
                CompanyMonthsProcessed: 8,
                CompanyMonthsSkippedNoSalesLineItems: 1,
                Duration: "0:00:01"));
        }

        public void Reset() => Requests.Clear();
    }

    public sealed class StubFundamentalIndexCatchUp
        : IFundamentalIndexCatchUpCoordinator, IFundamentalIndexCatchUpRunReader
    {
        public List<FundamentalIndexCatchUpRequest> Requests { get; } = [];

        public Task<FundamentalIndexCatchUpRun> RunAsync(
            FundamentalIndexCatchUpRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new FundamentalIndexCatchUpRun(
                Guid.NewGuid(),
                FundamentalIndexCatchUpRunStatus.Succeeded,
                request.RequestedBy,
                request.FromShamsiYear,
                request.ToShamsiYear,
                StartedAt: DateTimeOffset.Parse("2026-06-09T09:00:00Z"),
                FinishedAt: DateTimeOffset.Parse("2026-06-09T09:00:05Z"),
                CompaniesConsidered: 6,
                RequestsEnqueued: 6,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                Diagnostics: null));
        }

        public Task<IReadOnlyCollection<FundamentalIndexCatchUpRun>> QueryRecentAsync(
            int maximumCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FundamentalIndexCatchUpRun>>([]);

        public void Reset() => Requests.Clear();
    }

    public sealed class StubStockMarketDbSyncService : IStockMarketDbSyncService, IStockMarketDbSyncStateReader
    {
        public List<(StockMarketDataset Dataset, bool FullReload)> Invocations { get; } = [];

        public Task<StockMarketSyncResult> SynchronizeAsync(
            StockMarketDataset dataset,
            bool fullReload,
            CancellationToken cancellationToken)
        {
            Invocations.Add((dataset, fullReload));
            return Task.FromResult(new StockMarketSyncResult(
                dataset, RowsRead: 3, RowsPersisted: 3,
                AdvancedWatermark: DateTimeOffset.Parse("2026-06-01T12:35:00Z"),
                Duration: TimeSpan.FromSeconds(1)));
        }

        public void Reset() => Invocations.Clear();

        public Task<IReadOnlyCollection<StockMarketSyncState>> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<StockMarketSyncState>>([]);
    }

    public sealed class StubNadpcoApiScheduledSyncService : INadpcoApiScheduledSyncService, INadpcoApiSyncStateReader
    {
        public List<bool> InvocationModes { get; } = [];
        public List<int?> FromShamsiYearOverrides { get; } = [];
        public List<bool> CompanyCatalogModes { get; } = [];

        public Task<NadpcoApiSyncResult> ExecuteAsync(
            bool fullReload, CancellationToken cancellationToken, int? fromShamsiYearOverride = null)
        {
            InvocationModes.Add(fullReload);
            FromShamsiYearOverrides.Add(fromShamsiYearOverride);
            return Task.FromResult(new NadpcoApiSyncResult(
                fullReload,
                CompaniesConsidered: 4,
                CompaniesEnqueued: 4,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                RequestsEnqueued: 13,
                OverlapFrom: fullReload ? null : DateTimeOffset.Parse("2026-05-27T10:00:00Z"),
                AdvancedWatermark: DateTimeOffset.Parse("2026-06-03T10:00:00Z"),
                Duration: TimeSpan.FromSeconds(2),
                RunMode: fullReload ? NadpcoApiSyncRunMode.FullSync : NadpcoApiSyncRunMode.IncrementalSync));
        }

        public Task<NadpcoApiSyncResult> ExecuteCompanyCatalogAsync(
            bool cleanSlate,
            CancellationToken cancellationToken)
        {
            CompanyCatalogModes.Add(cleanSlate);
            var runMode = cleanSlate
                ? NadpcoApiSyncRunMode.CompanyCatalogCleanSlate
                : NadpcoApiSyncRunMode.CompanyCatalogRefresh;
            return Task.FromResult(new NadpcoApiSyncResult(
                FullReload: cleanSlate,
                CompaniesConsidered: cleanSlate ? 2 : 4,
                CompaniesEnqueued: 0,
                FailedCompanies: 0,
                FailedCompanyIds: [],
                RequestsEnqueued: 1,
                OverlapFrom: null,
                AdvancedWatermark: DateTimeOffset.Parse("2026-06-03T10:00:00Z"),
                Duration: TimeSpan.FromSeconds(1),
                RunMode: runMode,
                CleanSlate: cleanSlate
                    ? new NadpcoCompanyCatalogCleanSlateResult(
                        MetricRecalculationRequestsDeleted: 1,
                        FeatureComputationJobsDeleted: 2,
                        FeatureSnapshotsDeleted: 3,
                        DerivedMetricsDeleted: 4,
                        SymbolsDeleted: 5,
                        TradingInstrumentLinksCleared: 6,
                        CompaniesDeleted: 2)
                    : null));
        }

        public Task<IReadOnlyCollection<NadpcoApiSyncState>> QueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<NadpcoApiSyncState>>(
                [new NadpcoApiSyncState(
                    "FinancialStatements",
                    DateTimeOffset.Parse("2026-06-03T10:00:00Z"),
                    DateTimeOffset.Parse("2026-05-27T10:00:00Z"),
                    DateTimeOffset.Parse("2026-06-03T10:00:00Z"),
                    DateTimeOffset.Parse("2026-06-03T10:00:02Z"),
                    LastCompaniesConsidered: 4,
                    LastCompaniesEnqueued: 4,
                    LastFailedCompanies: 0,
                    LastRunMode: NadpcoApiSyncRunMode.IncrementalSync.ToString(),
                    LastError: null)]);

        public void Reset()
        {
            InvocationModes.Clear();
            CompanyCatalogModes.Clear();
        }
    }

    public sealed class StubNadpcoScheduledSyncCoordinator :
        INadpcoScheduledSyncCoordinator,
        INadpcoScheduledSyncRunReader
    {
        public List<string?> ManualReasons { get; } = [];

        public Task<NadpcoScheduledSyncRun> RunAsync(
            NadpcoScheduledSyncRunRequest request,
            CancellationToken cancellationToken)
        {
            ManualReasons.Add(request.ManualReason);
            return Task.FromResult(SampleRun(request.TriggerSource, request.ManualReason));
        }

        public Task<NadpcoScheduledSyncStatus> GetStatusAsync(
            int recentRunLimit,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NadpcoScheduledSyncStatus(
                Enabled: true,
                Ready: true,
                NextDueAt: DateTimeOffset.Parse("2026-06-04T10:00:00Z"),
                LastSuccessfulExecutionAt: DateTimeOffset.Parse("2026-06-03T10:00:02Z"),
                ActiveRun: null,
                RecentRuns: [SampleRun(NadpcoScheduledSyncTriggerSource.Automatic, null)]));

        public Task<IReadOnlyCollection<NadpcoScheduledSyncRun>> QueryRecentAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<NadpcoScheduledSyncRun>>(
                [SampleRun(NadpcoScheduledSyncTriggerSource.Automatic, null)]);

        public void Reset() => ManualReasons.Clear();

        private static NadpcoScheduledSyncRun SampleRun(
            NadpcoScheduledSyncTriggerSource triggerSource,
            string? manualReason) =>
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                triggerSource,
                NadpcoScheduledSyncRunStatus.Succeeded,
                DateTimeOffset.Parse("2026-06-03T10:00:00Z"),
                DateTimeOffset.Parse("2026-06-03T10:00:02Z"),
                DateTimeOffset.Parse("2026-06-03T10:00:02Z"),
                ProcessedBatches: 4,
                FailedBatches: 0,
                RetryAttempts: 0,
                Diagnostics: null,
                ScheduleSnapshotJson: "{}",
                DatasetSelectionJson: "[\"Symbols\"]",
                LockOwner: null,
                LockLeaseExpiresAt: null,
                AlertEmitted: false,
                ManualReason: manualReason);
    }

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

    // -----------------------------------------------------------------------
    // Spec 058 stub
    // -----------------------------------------------------------------------

    public sealed class StubDataSyncActivityMonitor : IDataSyncActivityMonitor
    {
        private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-06-13T08:00:00Z");

        public DataSyncActivitySnapshot Snapshot { get; private set; } = EmptySnapshot();
        public int ActiveConnections => 0;

        public static DataSyncActivitySnapshot EmptySnapshot() =>
            new([], []);

        public static DataSyncActivitySnapshot WithRunningItem(string runId, string provider = "TestProvider") =>
            new(
                ActiveRuns:
                [
                    new DataSyncActivityItem(
                        RunId: runId,
                        Provider: provider,
                        Dataset: "TestDataset",
                        Status: "Running",
                        StartedAt: T0,
                        CompletedAt: null,
                        DurationMs: null,
                        ProcessedRecords: 0,
                        ErrorCount: 0,
                        ErrorMessage: null,
                        TriggerSource: "Manual",
                        RequestedShamsiMonth: null,
                        LogicalVendor: null,
                        PhysicalSource: null,
                        SourceMode: null)
                ],
                RecentRuns: []);

        public Task<DataSyncActivitySnapshot> GetSnapshotAsync(
            int recentPerProvider, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot);

        public Task SubscribeAsync(
            ChannelWriter<DataSyncActivityEvent> writer, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetResult(), tcs);
            return tcs.Task;
        }

        public void SetSnapshot(DataSyncActivitySnapshot snapshot) => Snapshot = snapshot;

        public void Reset() => Snapshot = EmptySnapshot();
    }
}
