using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class SymbolLookupEndpointTests : IClassFixture<SymbolLookupApiFactory>
{
    private readonly SymbolLookupApiFactory _factory;

    public SymbolLookupEndpointTests(SymbolLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task AiQuery_SymbolLookupIntent_KnownSymbolAndMetric_ReturnsLookupTable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SymbolLookup", document.RootElement.GetProperty("intent").GetString());
        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("HAF_TSE", rows[0].GetProperty("symbolCode").GetString());
        Assert.NotEqual("HAFARI", rows[0].GetProperty("symbolCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(rows[0].GetProperty("companyName").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(
            rows[0].GetProperty("cells").GetProperty("COMPANY_NAME").GetProperty("formattedValue").GetString()));
        Assert.Equal(5.2m, rows[0].GetProperty("cells").GetProperty("PE_TTM").GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task AiQuery_UnknownSymbol_ReturnsEmptyLookupTableRows()
    {
        using var client = _factory.CreateUnknownSymbolClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE شرکتی_که_وجود_ندارد" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("SymbolLookup", document.RootElement.GetProperty("intent").GetString());
        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.NotEqual(JsonValueKind.Null, table.ValueKind);
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Empty(rows);
        // executionFacts should reflect 1 attempted symbol
        var facts = table.GetProperty("executionFacts");
        Assert.Equal(1, facts.GetProperty("totalSymbolsEvaluated").GetInt32());
        Assert.Equal(0, facts.GetProperty("matchingSymbolCount").GetInt32());
    }

    [Fact]
    public async Task AiQuery_UnknownMetric_ReturnsClarificationRequired()
    {
        using var client = _factory.CreateClarificationClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "metric_xyz_unknown حفاری" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
    }

    [Fact]
    public async Task AiQuery_MultipleSymbols_MultipleMetrics_ReturnsAllRows()
    {
        using var client = _factory.CreateMultiSymbolClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE و ROE فملی و حفاری" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var table = document.RootElement.GetProperty("symbolLookupTable");
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var symbols = rows.Select(r => r.GetProperty("symbolCode").GetString()!).ToHashSet();
        Assert.Contains("HAF_TSE", symbols);
        Assert.Contains("FML_TSE", symbols);
    }

    [Fact]
    public async Task AiQuery_SymbolLookup_CreatesUsageLedgerEntry()
    {
        var countBefore = _factory.ReadUsageEntries().Count;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entries = _factory.ReadUsageEntries();
        Assert.Equal(countBefore + 1, entries.Count);
        var entry = entries.OrderBy(e => e.OccurredAt).Last();
        Assert.Equal("AiQuery.Scanner", entry.OperationCode);
    }

    [Fact]
    public async Task Messages_SymbolLookupPayload_PersistedAndReloadable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var queryResponse = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        var queryDoc = await ReadJsonAsync(queryResponse);
        var conversationId = queryDoc.RootElement.GetProperty("conversationId").GetGuid();

        using var messagesResponse = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages",
            CancellationToken.None);
        var messagesDoc = await ReadJsonAsync(messagesResponse);
        var assistant = messagesDoc.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .Last();

        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        Assert.Equal("SymbolLookup",
            assistant.GetProperty("assistantContent").GetProperty("intent").GetString());
        Assert.True(
            assistant.GetProperty("assistantContent").TryGetProperty("symbolLookupTable", out var lt) &&
            lt.ValueKind != JsonValueKind.Null);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class SymbolLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"symbol-lookup-ingestion-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ =>
                new SymbolLookupFakeAiModelClient(
                    symbolLookupSymbol: "حفاری",
                    metricTerm: "نسبت پی به ای",
                    clarificationMetricTerm: null,
                    multiSymbol: false));
        });
    }

    public void EnsureSeeded()
    {
        EnsureBillingSeeded();
        if (_seeded) return;
        lock (_seedLock)
        {
            if (_seeded) return;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            SeedTestData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    public HttpClient CreateUnknownSymbolClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ReplaceIngestionDbContext(services, _dbName);
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "شرکتی_که_وجود_ندارد",
                        metricTerm: "نسبت پی به ای",
                        clarificationMetricTerm: null,
                        multiSymbol: false));
            });
        }).CreateClient();
    }

    public HttpClient CreateClarificationClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "حفاری",
                        metricTerm: "metric_xyz_unknown",
                        clarificationMetricTerm: "metric_xyz_unknown",
                        multiSymbol: false));
            });
        }).CreateClient();
    }

    public HttpClient CreateMultiSymbolClient()
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                ReplaceIngestionDbContext(services, _dbName);
                services.RemoveAll<IAiModelClient>();
                services.AddSingleton<IAiModelClient>(_ =>
                    new SymbolLookupFakeAiModelClient(
                        symbolLookupSymbol: "حفاری",
                        metricTerm: "نسبت پی به ای",
                        clarificationMetricTerm: null,
                        multiSymbol: true));
            });
        }).CreateClient();
    }

    private static void SeedTestData(FinancialIngestionDbContext db)
    {
        var companyHafariId = Guid.Parse("50000000-0000-0000-0000-000000000001");
        var companyFmlcoId = Guid.Parse("50000000-0000-0000-0000-000000000002");
        var symbolHafariId = Guid.Parse("60000000-0000-0000-0000-000000000001");
        var symbolFmlcoId = Guid.Parse("60000000-0000-0000-0000-000000000002");
        var symbolHafariMetricsId = Guid.Parse("60000000-0000-0000-0000-000000000101");
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        db.Companies.AddRange(
            new NormalizedCompanyRow
            {
                Id = companyHafariId,
                Name = "حفاری شمال",
                ProviderName = "test",
                ExternalCompanyId = "hafari-001",
                TseSymbol = "HAF_TSE",
                CompanySymbol = "HAFARI",
                LastSynchronizedAt = now
            },
            new NormalizedCompanyRow
            {
                Id = companyFmlcoId,
                Name = "فولاد مبارکه اصفهان",
                ProviderName = "test",
                ExternalCompanyId = "fmlco-001",
                TseSymbol = "FML_TSE",
                CompanySymbol = "FMLCO",
                LastSynchronizedAt = now
            });

        db.Symbols.AddRange(
            new NormalizedSymbolRow
            {
                Id = symbolHafariId,
                CompanyId = companyHafariId,
                ProviderName = "test",
                ExternalSymbolId = "hafari-001",
                SymbolCode = "HAFARI",
                LastSynchronizedAt = now
            },
            new NormalizedSymbolRow
            {
                Id = symbolHafariMetricsId,
                CompanyId = companyHafariId,
                ProviderName = "metrics-provider",
                ExternalSymbolId = "hafari-metrics-001",
                SymbolCode = "HAFARI_CW",
                LastSynchronizedAt = now
            },
            new NormalizedSymbolRow
            {
                Id = symbolFmlcoId,
                CompanyId = companyFmlcoId,
                ProviderName = "test",
                ExternalSymbolId = "fmlco-001",
                SymbolCode = "FMLCO",
                LastSynchronizedAt = now
            });

        db.DerivedMetrics.AddRange(
            MakeDerivedMetric(symbolHafariMetricsId, "PE_TTM", 5.2m, periodStart, periodEnd, now),
            MakeDerivedMetric(symbolHafariMetricsId, "RETURN_ON_EQUITY", 12.0m, periodStart, periodEnd, now),
            MakeDerivedMetric(symbolFmlcoId, "PE_TTM", 8.4m, periodStart, periodEnd, now),
            MakeDerivedMetric(symbolFmlcoId, "RETURN_ON_EQUITY", 18.5m, periodStart, periodEnd, now));
    }

    private static DerivedMetricRow MakeDerivedMetric(
        Guid symbolId,
        string metricCode,
        decimal value,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SymbolId = symbolId,
            MetricCode = metricCode,
            MetricVersion = "v1",
            CalculationPolicyVersion = $"{metricCode}_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        };
}

internal sealed class SymbolLookupFakeAiModelClient(
    string symbolLookupSymbol,
    string metricTerm,
    string? clarificationMetricTerm,
    bool multiSymbol) : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "SymbolLookupFake",
        "fake-v1",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.StructuredOutput?.SchemaName switch
        {
            "IntentDetectionOutput" =>
                "{\"intent\":\"SymbolLookup\",\"confidence\":0.96}",

            "SymbolLookupParseOutput" when clarificationMetricTerm is not null =>
                BuildClarificationJson(clarificationMetricTerm),

            "SymbolLookupParseOutput" when multiSymbol =>
                BuildMultiSymbolJson(),

            "SymbolLookupParseOutput" =>
                BuildSinglePairJson(symbolLookupSymbol, metricTerm),

            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: new AiExecutionUsageFacts(
                request.CorrelationId,
                Descriptor.ProviderKey,
                Descriptor.ModelKey,
                AiExecutionStatus.Completed,
                TimeSpan.Zero,
                AttemptNumber: 0,
                InputTokens: 12,
                OutputTokens: 6)));
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(
        AiModelRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(
        AiEmbeddingRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(
            Descriptor.ProviderKey,
            Descriptor.ModelKey,
            Available: true,
            DateTimeOffset.UtcNow,
            "OK"));

    private static string BuildSinglePairJson(string symbol, string metric) =>
        $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"{{symbol}}","metricTerm":"{{metric}}"}],"clarificationRequired":false,"clarificationMessage":null}""";

    private static string BuildMultiSymbolJson() =>
        """{"detectedLanguage":"fa","pairs":[{"symbolName":"حفاری","metricTerm":"نسبت پی به ای"},{"symbolName":"FMLCO","metricTerm":"نسبت پی به ای"}],"clarificationRequired":false,"clarificationMessage":null}""";

    private static string BuildClarificationJson(string metric) =>
        $$"""{"detectedLanguage":"fa","pairs":[{"symbolName":"حفاری","metricTerm":"{{metric}}"}],"clarificationRequired":false,"clarificationMessage":null}""";
}
