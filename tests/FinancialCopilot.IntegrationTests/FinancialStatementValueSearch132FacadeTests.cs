using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class FinancialStatementValueSearch132FacadeTests : IClassFixture<FinancialStatementValueSearch132ApiFactory>
{
    private readonly FinancialStatementValueSearch132ApiFactory _factory;

    public FinancialStatementValueSearch132FacadeTests(FinancialStatementValueSearch132ApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_PersianExactValue_UsesValueSearchAndPreservesEvidence()
    {
        var usageBefore = _factory.ReadUsageEntries().Count;
        var reservationsBefore = _factory.ReadUsageReservations().Count;
        using var client = AuthenticatedClient();
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new
        {
            message = "نمادی را پیدا کن با درآمد 3300508"
        });
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = json.RootElement;
        Assert.Equal(1, _factory.StatementCount());
        Assert.Equal("FinancialStatementValueSearch", root.GetProperty("intent").GetString());
        Assert.Equal("financial_statement_value_search", root.GetProperty("semanticCapabilityCode").GetString());
        var result = root.GetProperty("financialStatementValueSearchResult");
        var match = Assert.Single(result.GetProperty("matches").EnumerateArray());
        Assert.Equal("TEST", match.GetProperty("symbol").GetString());
        var item = Assert.Single(match.GetProperty("items").EnumerateArray());
        Assert.Equal(3300508m, item.GetProperty("value").GetDecimal());
        Assert.Equal("REVENUE", item.GetProperty("metricCode").GetString());
        Assert.Equal(usageBefore + 1, _factory.ReadUsageEntries().Count);
        var reservations = _factory.ReadUsageReservations();
        Assert.Equal(reservationsBefore + 1, reservations.Count);
        var reservation = reservations.OrderBy(item => item.CreatedAt).Last();
        Assert.Equal("Committed", reservation.Status);
        Assert.Equal(reservation.ReservedCredits, reservation.CommittedCredits);
        Assert.Equal("Completed", root.GetProperty("usage").GetProperty("completionStatus").GetString());
    }

    [Fact]
    public async Task AiQuery_ExactValueNoMatch_ReturnsDeterministicNoDataWithoutIdentity()
    {
        using var client = AuthenticatedClient();
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new
        {
            message = "نمادی را پیدا کن با درآمد 9999999"
        });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("financialStatementValueSearchResult").ValueKind == JsonValueKind.Null,
            json.RootElement.ToString());
        Assert.Equal("FinancialStatementValueSearch", json.RootElement.GetProperty("intent").GetString());
        Assert.Equal("NoData", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("financialStatementValueSearchResult").GetProperty("outcome").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("financialStatementValueSearchResult").GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task AiQuery_MultipleExactValues_UsesOneStatementAndReturnsBothEvidenceItems()
    {
        using var client = AuthenticatedClient();
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new
        {
            message = "Which company has gross profit 2580407 and revenue 3300508?"
        });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("financialStatementValueSearchResult").ValueKind == JsonValueKind.Null,
            json.RootElement.ToString());
        var match = Assert.Single(json.RootElement.GetProperty("financialStatementValueSearchResult")
            .GetProperty("matches").EnumerateArray());
        Assert.Equal(new[] { 2580407m, 3300508m }, match.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("value").GetDecimal()).OrderBy(value => value).ToArray());
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }
}

public sealed class FinancialStatementValueSearch132ApiFactory : AiFacadeApiFactory
{
    private const string Provider = "NoavaranArchiveSql";
    private readonly string _databaseName = $"feature132-facade-{Guid.NewGuid():N}";
    private bool _seeded;

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            }));
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _databaseName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2FinancialStatementValueSearchFakeAiModelClient());
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
        db.Database.EnsureCreated();
        var marketId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var statementId = Guid.NewGuid();
        db.Markets.Add(new NormalizedMarketRow
        {
            Id = marketId, ProviderName = Provider, ExternalId = "test-market",
            Name = "Test", LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId, ProviderName = Provider, ExternalCompanyId = "test-company",
            Name = "Test Company", CompanySymbol = "TEST", MarketId = marketId,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });
        db.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = statementId, ProviderName = Provider, ExternalCompanyId = "test-company",
            ExternalStatementId = "test-statement", StatementType = "IncomeStatement", PeriodType = "ThreeMonths",
            PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 3, 31),
            PublishedAt = DateOnly.FromDateTime(DateTime.UtcNow), LastSynchronizedAt = DateTimeOffset.UtcNow, CompanyId = companyId
        });
        db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
        {
            Id = Guid.NewGuid(), FinancialStatementId = statementId, Value = 3300508m, MetricCode = "REVENUE"
        });
        db.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
        {
            Id = Guid.NewGuid(), FinancialStatementId = statementId, Value = 2580407m, MetricCode = "GROSS_PROFIT"
        });
        db.SaveChanges();
        _seeded = true;
    }

    public int StatementCount()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>().FinancialStatements.Count();
    }

    public IReadOnlyCollection<UsageReservationRow> ReadUsageReservations()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BillingDbContext>()
            .UsageReservations.AsNoTracking().ToList();
    }
}

internal sealed class V2FinancialStatementValueSearchFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "Feature132FacadeFake", "feature132-v2", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck, true, 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AiModelResult(null, "{}", [], new AiExecutionUsageFacts(
            request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, 0, 1, 1)));

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(AiEmbeddingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(Descriptor.ProviderKey, Descriptor.ModelKey, true, DateTimeOffset.UtcNow, "OK"));
}
