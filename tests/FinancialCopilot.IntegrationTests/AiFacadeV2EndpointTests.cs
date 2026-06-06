using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

// ─── V2 Scanner tests ─────────────────────────────────────────────────────────

public sealed class V2ScannerEndpointTests : IClassFixture<V2ScannerApiFactory>
{
    private readonly V2ScannerApiFactory _factory;

    public V2ScannerEndpointTests(V2ScannerApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2AiQuery_ScannerTool_ReturnsScannerTableWithMatchingSymbols()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Scanner", document.RootElement.GetProperty("intent").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());

        var table = document.RootElement.GetProperty("scannerTable");
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var symbols = rows.Select(r => r.GetProperty("symbolCode").GetString()!).ToHashSet();
        Assert.Contains("LIVE", symbols);
        Assert.Contains("FALLBACK", symbols);
    }

    [Fact]
    public async Task V2AiQuery_ScannerTool_ReturnsConversationId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "P/E below 6" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("conversationId").GetGuid());
        Assert.True(document.RootElement.GetProperty("usage").GetProperty("creditsCharged").GetDecimal() >= 0);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

// ─── V2 Symbol Lookup tests ───────────────────────────────────────────────────

public sealed class V2SymbolLookupEndpointTests : IClassFixture<V2SymbolLookupApiFactory>
{
    private readonly V2SymbolLookupApiFactory _factory;

    public V2SymbolLookupEndpointTests(V2SymbolLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2AiQuery_LookupTool_ReturnsSymbolLookupTable()
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
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());

        var table = document.RootElement.GetProperty("symbolLookupTable");
        Assert.NotEqual(JsonValueKind.Null, table.ValueKind);
        Assert.NotEqual(Guid.Empty, table.GetProperty("planId").GetGuid());

        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("HAF_TSE", rows[0].GetProperty("symbolCode").GetString());

        var confidence = document.RootElement.GetProperty("confidenceScore");
        Assert.True(confidence.GetProperty("score").GetDouble() >= 0.95);
        Assert.Equal("v1", confidence.GetProperty("policyVersion").GetString());
    }

    [Fact]
    public async Task V2AiQuery_LookupTool_ReturnsConversationId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "PE حفاری چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("conversationId").GetGuid());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

// ─── V2 numeric-consistency guardrail tests ───────────────────────────────────
// Reproduces the reported bug: the V2 agent authors prose with a hallucinated/stale P/E ("7.88")
// while the deterministic table reports PE_TTM = 5.06. The persisted assistant prose must be
// corrected to the table value, and the table must keep the authoritative value.
public sealed class V2AnswerConsistencyEndpointTests : IClassFixture<V2InconsistentLookupApiFactory>
{
    private readonly V2InconsistentLookupApiFactory _factory;

    public V2AnswerConsistencyEndpointTests(V2InconsistentLookupApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
    }

    [Fact]
    public async Task V2AiQuery_LookupProseConflictsWithTable_PersistedProseCorrected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query",
            new { message = "pe شبندر چقدر است؟" },
            CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = document.RootElement;
        Assert.Equal("SymbolLookup", root.GetProperty("intent").GetString());

        // Table keeps the authoritative deterministic value.
        var table = root.GetProperty("symbolLookupTable");
        var cell = table.GetProperty("rows")[0].GetProperty("cells").GetProperty("PE_TTM");
        Assert.Equal("5.06", cell.GetProperty("formattedValue").GetString());

        var conversationId = root.GetProperty("conversationId").GetGuid();

        // Reload the conversation: the persisted assistant prose must show the corrected value.
        using var reload = await client.GetAsync(
            $"/api/ai/v1/conversations/{conversationId}/messages", CancellationToken.None);
        using var reloadDoc = await ReadJsonAsync(reload);

        var assistant = reloadDoc.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "Assistant");
        var content = assistant.GetProperty("content").GetString()!;

        Assert.DoesNotContain("7.88", content);
        Assert.Contains("5.06", content);
        Assert.True(root.GetProperty("confidenceScore").GetProperty("score").GetDouble() >= 0.95);

        // Persisted structured table also keeps the authoritative value (consistency preserved).
        var assistantContent = assistant.GetProperty("assistantContent");
        Assert.True(assistantContent.GetProperty("confidenceScore").GetProperty("score").GetDouble() >= 0.95);
        var persistedCell = assistantContent.GetProperty("symbolLookupTable").GetProperty("rows")[0]
            .GetProperty("cells").GetProperty("PE_TTM");
        Assert.Equal("5.06", persistedCell.GetProperty("formattedValue").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

// ─── V2 Scanner factory ───────────────────────────────────────────────────────

public sealed class V2ScannerApiFactory : ScannerExecutionApiFactory
{
    // Let V2 orchestration stand — do not replace with V1.
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Override V1 mode set by AiFacadeApiFactory with V2. Later ConfigureAppConfiguration
        // registrations have higher priority in Microsoft.Extensions.Configuration.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2ScannerFakeAiModelClient());
        });
    }
}

// ─── V2 Symbol Lookup factory ─────────────────────────────────────────────────

public sealed class V2SymbolLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-lookup-ingestion-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    // Let V2 orchestration stand — do not replace with V1.
    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Override V1 mode set by AiFacadeApiFactory with V2. Later ConfigureAppConfiguration
        // registrations have higher priority in Microsoft.Extensions.Configuration.
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2SymbolLookupFakeAiModelClient());
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var companyHafariId = Guid.Parse("50000000-0000-0000-0000-100000000001");
        var staleHafariCompanyId = Guid.Parse("50000000-0000-0000-0000-100000000101");
        var symbolHafariMetricsId = Guid.Parse("60000000-0000-0000-0000-100000000101");
        var now = DateTimeOffset.UtcNow;
        var periodStart = new DateOnly(2025, 1, 1);
        var periodEnd = new DateOnly(2025, 12, 31);

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyHafariId,
            Name = "حفاری شمال",
            ProviderName = "test",
            ExternalCompanyId = "hafari-v2-001",
            TseSymbol = "HAF_TSE",
            CompanySymbol = "HAFARI",
            LastSynchronizedAt = now
        });

        db.Symbols.AddRange(
            new NormalizedSymbolRow
            {
                Id = Guid.Parse("60000000-0000-0000-0000-100000000001"),
                CompanyId = staleHafariCompanyId,
                ProviderName = "test",
                ExternalSymbolId = "hafari-v2-001",
                SymbolCode = "HAFARI",
                LastSynchronizedAt = now
            },
            new NormalizedSymbolRow
            {
                Id = symbolHafariMetricsId,
                CompanyId = staleHafariCompanyId,
                ProviderName = "metrics-provider",
                ExternalSymbolId = "hafari-metrics-v2-001",
                SymbolCode = "HAFARI_CW",
                LastSynchronizedAt = now
            });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbolHafariMetricsId,
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "TrailingTwelveMonths",
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = 5.2m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });
    }
}

// ─── V2 inconsistent-lookup factory (consistency guardrail) ───────────────────

public sealed class V2InconsistentLookupApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-inconsistent-lookup-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(_ => new V2InconsistentLookupFakeAiModelClient());
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
            SeedLookupData(db);
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static void SeedLookupData(FinancialIngestionDbContext db)
    {
        var companyId = Guid.Parse("50000000-0000-0000-0000-200000000001");
        var symbolMetricsId = Guid.Parse("60000000-0000-0000-0000-200000000101");
        var now = DateTimeOffset.UtcNow;

        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = companyId,
            Name = "پتروشیمی بندرامام",
            ProviderName = "test",
            ExternalCompanyId = "shabandar-v2-001",
            TseSymbol = "شبندر",
            CompanySymbol = "شبندر",
            LastSynchronizedAt = now
        });

        db.Symbols.Add(new NormalizedSymbolRow
        {
            Id = symbolMetricsId,
            CompanyId = companyId,
            ProviderName = "metrics-provider",
            ExternalSymbolId = "shabandar-metrics-v2-001",
            SymbolCode = "شبندر",
            LastSynchronizedAt = now
        });

        db.DerivedMetrics.Add(new DerivedMetricRow
        {
            Id = Guid.NewGuid(),
            SymbolId = symbolMetricsId,
            MetricCode = "PE_TTM",
            MetricVersion = "v1",
            CalculationPolicyVersion = "PE_TTM_v1",
            PeriodType = "ThreeMonths",
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            Value = 5.06m,
            Unit = "Ratio",
            ObservedAt = now,
            LastSynchronizedAt = now,
            WarningsJson = "[]",
            SourceEvidenceJson = "[]",
            DependencyEvidenceJson = "[]"
        });
    }
}

// Fake whose turn-2 prose contradicts the deterministic table (states "7.88" instead of 5.06).
internal sealed class V2InconsistentLookupFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2InconsistentLookupFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution — author CONFLICTING prose.
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "نسبت P/E نماد شبندر برابر است با 7.88",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: fire the lookup tool.
        if (request.Tools is { Count: > 0 })
        {
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-inconsistent-call-1",
                    "lookup_symbol_metrics",
                    "{\"query\":\"pe شبندر چقدر است؟\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        var json = request.StructuredOutput?.SchemaName switch
        {
            "SymbolLookupParseOutput" =>
                """{"detectedLanguage":"fa","pairs":[{"symbolName":"شبندر","metricTerm":"pe"}],"clarificationRequired":false,"clarificationMessage":null}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
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
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);
}

// ─── V2 composite fake for scanner ────────────────────────────────────────────
// Handles the three call types that occur in a V2 scanner flow:
//   1. V2 outer agent, turn 1 — tools present, no PreviousResponseId → return screen_stocks tool call
//   2. V2 outer agent, turn 2 — PreviousResponseId set → return final text
//   3. Internal ScannerParsing / ExplanationGeneration structured output calls
internal sealed class V2ScannerFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2ScannerFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution via previous_response_id
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "I found 2 stocks matching your P/E criteria.",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: V2 outer agent turn — fire the screen_stocks tool
        if (request.Tools is { Count: > 0 })
        {
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-scanner-call-1",
                    "screen_stocks",
                    "{\"query\":\"P/E below 6\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        // Internal structured output calls (ScannerParsing, ExplanationGeneration)
        var json = request.StructuredOutput?.SchemaName switch
        {
            "ScannerParseOutput" =>
                """{"detectedLanguage":"en","conditions":[{"userTerminology":"P/E","language":"en","operator":"LessThan","threshold":6.0,"periodHint":null,"growthComparison":null,"inferredDefault":false,"inferredReason":null}],"requestedColumns":[],"clarificationRequired":false,"clarificationMessage":null}""",
            "ScannerExplanationOutput" =>
                """{"explanationText":"Found 2 stocks with P/E below 6.","suggestedFollowUpQuestions":["Show ROE above 15","Filter market cap above 1B"]}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
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
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);
}

// ─── V2 composite fake for symbol lookup ──────────────────────────────────────
// Handles the three call types that occur in a V2 symbol lookup flow:
//   1. V2 outer agent, turn 1 — tools present, no PreviousResponseId → return lookup_symbol_metrics call
//   2. V2 outer agent, turn 2 — PreviousResponseId set → return final text
//   3. Internal SymbolLookupParsing structured output call
internal sealed class V2SymbolLookupFakeAiModelClient : IAiModelClient
{
    public AiModelProviderDescriptor Descriptor { get; } = new(
        "V2LookupFake",
        "fake-v2",
        AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling |
        AiModelCapability.StructuredOutput | AiModelCapability.UsageReporting |
        AiModelCapability.HealthCheck,
        Enabled: true,
        Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        // Turn 2: continuation after tool execution via previous_response_id
        if (request.PreviousResponseId is not null)
        {
            return Task.FromResult(new AiModelResult(
                Text: "Here are the P/E metrics for حفاری.",
                StructuredJson: null,
                ToolCalls: [],
                Usage: MakeUsage(request)));
        }

        // Turn 1: V2 outer agent turn — fire the lookup_symbol_metrics tool
        if (request.Tools is { Count: > 0 })
        {
            return Task.FromResult(new AiModelResult(
                Text: null,
                StructuredJson: null,
                ToolCalls: [new AiToolCall(
                    "v2-lookup-call-1",
                    "lookup_symbol_metrics",
                    "{\"query\":\"PE حفاری چقدر است؟\"}")],
                Usage: MakeUsage(request, usedTools: true),
                ResponseId: $"fake-v2-resp-{request.CorrelationId}"));
        }

        // Internal structured output calls (SymbolLookupParsing)
        var json = request.StructuredOutput?.SchemaName switch
        {
            "SymbolLookupParseOutput" =>
                """{"detectedLanguage":"fa","pairs":[{"symbolName":"حفاری","metricTerm":"نسبت پی به ای"}],"clarificationRequired":false,"clarificationMessage":null}""",
            _ => "{}"
        };

        return Task.FromResult(new AiModelResult(
            Text: null,
            StructuredJson: json,
            ToolCalls: [],
            Usage: MakeUsage(request)));
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
            Descriptor.ProviderKey, Descriptor.ModelKey,
            Available: true, DateTimeOffset.UtcNow, "OK"));

    private AiExecutionUsageFacts MakeUsage(AiModelRequest request, bool usedTools = false) =>
        new(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0,
            InputTokens: 10, OutputTokens: 4, UsedTools: usedTools);
}
