using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class CashFlowReport161EndpointTests : IClassFixture<CashFlowReport161EndpointApiFactory>
{
    private readonly CashFlowReport161EndpointApiFactory _factory;

    public CashFlowReport161EndpointTests(CashFlowReport161EndpointApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2Report_PersistsAndReplaysChargesOnce_IsolatesCompaniesAndKeepsNoDataFree()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        var firstId = Guid.NewGuid();
        var usageBefore = _factory.ReadUsageEntries().Count;
        using var firstResponse = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message = "جریان وجه نقد فلوله", idempotencyKey = firstId }, CancellationToken.None);
        using var firstDocument = await ReadJsonAsync(firstResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstRoot = firstDocument.RootElement;
        Assert.Equal("financial_statement_table", firstRoot.GetProperty("semanticCapabilityCode").GetString());
        Assert.True(firstRoot.GetProperty("financialStatementTableResult").ValueKind == JsonValueKind.Object, firstRoot.GetRawText());
        var firstReport = firstRoot.GetProperty("financialStatementTableResult").GetProperty("cashFlowReport");
        Assert.Equal("available", firstReport.GetProperty("outcome").GetString());
        Assert.Equal("فلوله", firstReport.GetProperty("company").GetProperty("symbol").GetString());

        var conversationId = firstRoot.GetProperty("conversationId").GetGuid();
        using var historyResponse = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        using var historyDocument = await ReadJsonAsync(historyResponse);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var replayedReport = historyDocument.RootElement.GetProperty("messages").EnumerateArray().Last()
            .GetProperty("assistantContent").GetProperty("financialStatementTableResult").GetProperty("cashFlowReport");
        Assert.Equal(firstReport.GetRawText(), replayedReport.GetRawText());

        var firstCharge = Assert.Single(_factory.ReadUsageEntries(),
            entry => entry.IdempotencyKey == $"{firstId:N}:charge");
        Assert.Equal("AiQuery.FinancialStatement", firstCharge.OperationCode);
        Assert.Equal(2m, firstCharge.CreditsCharged);
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);

        var secondId = Guid.NewGuid();
        using var secondResponse = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message = "جریان وجه نقد فولاد", idempotencyKey = secondId }, CancellationToken.None);
        using var secondDocument = await ReadJsonAsync(secondResponse);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondReport = secondDocument.RootElement.GetProperty("financialStatementTableResult").GetProperty("cashFlowReport");
        Assert.Equal("فولاد", secondReport.GetProperty("company").GetProperty("symbol").GetString());

        var captured = _factory.CashFlow.Requests.TakeLast(2).ToArray();
        Assert.Equal(new[] { "فلوله", "فولاد" }, captured.Select(request => request.CompanyQuery));
        Assert.All(captured, request => Assert.Equal("NoavaranCurrentApi", request.ProviderName));

        _factory.CashFlow.ReturnNoData = true;
        var noDataId = Guid.NewGuid();
        using var noDataResponse = await client.PostAsJsonAsync("/api/ai/v1/query",
            new { message = "جریان وجه نقد کگل", idempotencyKey = noDataId }, CancellationToken.None);
        using var noDataDocument = await ReadJsonAsync(noDataResponse);
        Assert.Equal(HttpStatusCode.OK, noDataResponse.StatusCode);
        Assert.Equal("company_unresolved", noDataDocument.RootElement.GetProperty("financialStatementTableResult")
            .GetProperty("cashFlowReport").GetProperty("outcome").GetString());
        var noDataCharge = Assert.Single(_factory.ReadUsageEntries(),
            entry => entry.IdempotencyKey == $"{noDataId:N}:charge");
        Assert.Equal(0m, noDataCharge.CreditsCharged);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class CashFlowReport161EndpointApiFactory : AiFacadeApiFactory
{
    public CashFlowReport161EndpointQueryUseCase CashFlow { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["CashFlowReport:Enabled"] = "true",
            ["NoavaranCurrentApi:ProviderName"] = "NoavaranCurrentApi",
            ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient, V2MonthlySalesRoutingFakeAiModelClient>();
            services.RemoveAll<ICashFlowReportQueryUseCase>();
            services.AddSingleton<ICashFlowReportQueryUseCase>(CashFlow);
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        if (db.Companies.Any()) return;

        var companies = new[] { ("فلوله", "Fixture Steel"), ("فولاد", "Fixture Metals"), ("کگل", "Fixture Mining") };
        for (var index = 0; index < companies.Length; index++)
        {
            var (symbol, name) = companies[index];
            db.Companies.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "NoavaranCurrentApi",
                ExternalCompanyId = $"cash-flow-161-{index}",
                Name = name,
                Ticker = symbol,
                CompanySymbol = symbol,
                TseSymbol = symbol,
                LastSynchronizedAt = DateTimeOffset.UtcNow
            });
        }
        db.SaveChanges();
    }
}

public sealed class CashFlowReport161EndpointQueryUseCase : ICashFlowReportQueryUseCase
{
    public List<CashFlowReportRequest> Requests { get; } = [];
    public bool ReturnNoData { get; set; }

    public Task<CashFlowReportResult> ExecuteAsync(CashFlowReportRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        var outcome = ReturnNoData ? "company_unresolved" : "available";
        var report = new CashFlowReportResult(
            CashFlowReportContract.Version,
            DateTimeOffset.UtcNow,
            outcome,
            ReturnNoData ? "entity_not_found" : null,
            ReturnNoData ? "برای نماد درخواستی داده‌ای پیدا نشد." : null,
            Array.Empty<DateOnly>(),
            false,
            "GeneralCashFlow",
            new CashFlowReportCompany(request.CompanyQuery, request.CompanyQuery, request.ProviderName, "fixture-company"),
            request,
            new Dictionary<Guid, CashFlowReportStatement>(),
            null,
            Array.Empty<CashFlowReportColumn>(),
            Array.Empty<CashFlowReportSection>(),
            null,
            Array.Empty<CashFlowReportKpi>(),
            null,
            new CashFlowReportVerification("unverified", Array.Empty<CashFlowReportCheck>()),
            Array.Empty<CashFlowReportWarning>(),
            Array.Empty<string>(),
            null);

        return Task.FromResult(report);
    }
}
