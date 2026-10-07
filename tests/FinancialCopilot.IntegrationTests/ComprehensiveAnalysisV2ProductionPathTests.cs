using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Production-active V2 path for «تحلیل بنیادی فولاد»: POST /api/ai/v1/query → hybrid interpreter
/// (deterministic + LLM proposal) → canonical entity resolution → slot validation → dispatcher →
/// real ComprehensiveAnalysisCapabilityExecutor → real use case/repository → billing.
/// Only the LLM client and the live-metric lookup are replaced.
/// </summary>
public sealed class V2ComprehensiveAnalysisProductionPathTests : IClassFixture<V2ComprehensiveAnalysisApiFactory>
{
    private static readonly Guid CanonicalFouladId = Guid.Parse("66000000-0000-0000-0000-000000000007");
    private readonly V2ComprehensiveAnalysisApiFactory _factory;

    public V2ComprehensiveAnalysisProductionPathTests(V2ComprehensiveAnalysisApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Reset();
    }

    [Theory]
    [InlineData("تحلیل بنیادی فولاد؟")]
    [InlineData("تحلیل بنیادی فولاد")]
    [InlineData("تحلیل فولاد")]
    [InlineData("آخرین تحلیل فولاد")]
    [InlineData("فولاد را بررسی کن")]
    [InlineData("فولاد ارزنده است؟")]
    public async Task ComprehensiveAnalysisQuery_ReachesRealExecutorAndReturnsStoredAnalysis(string message)
    {
        var root = await PostAsync(message);

        Assert.True("ComprehensiveAnalysis" == root.GetProperty("intent").GetString(), root.ToString());
        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        Assert.True(root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal() > 0m);

        // The real executor was invoked with the canonical Noavaran company, not the CyclicalWaves duplicate.
        var invocation = Assert.IsType<Feature128ExecutorInvocation>(_factory.ExecutorProbe.LastInvocation);
        Assert.Equal("comprehensive_analysis", invocation.CapabilityCode);
        var company = Assert.Single(invocation.Frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, company.ValidationState);
        Assert.Equal("فولاد", company.Value);
        Assert.Equal(CanonicalFouladId, company.CanonicalEntity!.CanonicalId);

        // Live-metric lookup received the resolved symbol, and the stored analysis came from the repository.
        Assert.All(_factory.Lookup.Requests.SelectMany(request => request.Pairs), pair => Assert.Equal("فولاد", pair.SymbolName));
        var analysis = Assert.Single(root.GetProperty("comprehensiveAnalysisResult").GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("تحلیل بنیادی فولاد مبارکه", analysis.GetProperty("title").GetString());
        Assert.Equal("ارزش ذاتی فولاد بالاتر از قیمت بازار است", analysis.GetProperty("plainTextSummary").GetString());
        Assert.Contains("ارزش ذاتی فولاد بالاتر از قیمت بازار است", root.GetProperty("textAnswer").GetString());
    }

    [Theory]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":[],"presentation":null,"confidence":0.95,"evidence":["analysis"],"intent":"analysis","entities":[{"text":"فولاد","entityType":"company","scope":null,"confidence":0.97}],"metricHints":[]}""")]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":[],"presentation":null,"confidence":0.9,"evidence":[],"intent":null,"entities":[{"text":"تحلیل بنیادی فولاد","entityType":"company","scope":null,"confidence":0.6}],"metricHints":[]}""")]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":[],"presentation":null,"confidence":0.9,"evidence":[],"intent":null,"entities":[{"text":"بنیادی فولاد","entityType":"company","scope":null,"confidence":0.6}],"metricHints":[]}""")]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":[],"presentation":null,"confidence":0.9,"evidence":[],"intent":null,"entities":[{"text":"فولاد؟","entityType":"company","scope":null,"confidence":0.6}],"metricHints":[]}""")]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":[],"presentation":null,"confidence":0.9,"evidence":[],"intent":null,"entities":[{"text":"فولاد","entityType":"company","scope":null,"confidence":0.9},{"text":"فولاد","entityType":"company","scope":null,"confidence":0.9},{"text":"بنیادی","entityType":"company","scope":null,"confidence":0.3}],"metricHints":[]}""")]
    [InlineData("""{"capabilityCodes":["comprehensive_analysis"],"missingSlots":["symbol"],"presentation":null,"confidence":0.5,"evidence":[],"intent":null,"entities":[],"metricHints":[]}""")]
    public async Task LlmProposalVariants_DoNotContaminateCompanyResolution(string proposalJson)
    {
        _factory.Fake.ProposalJson = proposalJson;

        var root = await PostAsync("تحلیل بنیادی فولاد؟");

        Assert.Equal("comprehensive_analysis", root.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        var invocation = Assert.IsType<Feature128ExecutorInvocation>(_factory.ExecutorProbe.LastInvocation);
        var company = Assert.Single(invocation.Frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(CanonicalFouladId, company.CanonicalEntity!.CanonicalId);
        Assert.True(_factory.Fake.ProposalCalls > 0, "the LLM proposal path must actually be exercised");
    }

    [Fact]
    public async Task SemanticPath_DoesNotUseLegacyParser_AndExecutorInputIsLatestUserMessageOnly()
    {
        var first = await PostAsync("سلام، کگل چطور است؟ پاسخ قبلی: جدول [Recent conversation] User Assistant");
        var conversationId = first.GetProperty("conversationId").GetGuid();
        _factory.Reset();

        using var client = NewClient();
        using var response = await client.PostAsJsonAsync(
            "/api/ai/v1/query", new { message = "تحلیل بنیادی فولاد؟", conversationId }, CancellationToken.None);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal("comprehensive_analysis", document.RootElement.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
        // The semantic route builds executor input from validated slots; the legacy parser is never consulted.
        Assert.Equal(0, _factory.LegacyParser.Calls);
        var invocation = Assert.IsType<Feature128ExecutorInvocation>(_factory.ExecutorProbe.LastInvocation);
        Assert.Equal("تحلیل بنیادی فولاد؟", invocation.Frame.Interpretation.OriginalText);
        var queryTexts = _factory.Lookup.Requests.Select(request => request.QueryText).ToArray();
        Assert.NotEmpty(queryTexts);
        Assert.All(queryTexts, text =>
        {
            Assert.Equal("تحلیل بنیادی فولاد؟", text);
            Assert.DoesNotContain("[Recent conversation]", text);
            Assert.DoesNotContain("Assistant", text);
        });
    }

    private HttpClient NewClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        return client;
    }

    private async Task<JsonElement> PostAsync(string message)
    {
        using var client = NewClient();
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}

public class V2ComprehensiveAnalysisApiFactory : AiFacadeApiFactory
{
    private readonly string _dbName = $"v2-comprehensive-analysis-{Guid.NewGuid():N}";
    private bool _seeded;
    private readonly object _seedLock = new();

    public ProposalAiModelClient Fake { get; } = new();
    public Feature128ExecutorProbe ExecutorProbe { get; } = new();
    public RecordingSymbolLookupService Lookup { get; } = new();
    public RecordingLegacyAnalysisParser LegacyParser { get; } = new();

    protected override bool ForceV1Orchestration => false;
    protected virtual bool SemanticPrimary => true;
    protected virtual TimeSpan AnalysisAge => TimeSpan.Zero;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            ["SemanticRouting:Capabilities:comprehensive_analysis"] = SemanticPrimary ? "SemanticPrimary" : "Shadow"
        }));
        builder.ConfigureTestServices(services =>
        {
            ReplaceIngestionDbContext(services, _dbName);
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
            services.RemoveAll<ISymbolMetricLookupService>();
            services.AddSingleton<ISymbolMetricLookupService>(Lookup);
            services.RemoveAll<IComprehensiveAnalysisQueryParser>();
            services.AddSingleton<IComprehensiveAnalysisQueryParser>(LegacyParser);
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IConversationalCapabilityExecutor) && d.ImplementationType == typeof(ComprehensiveAnalysisCapabilityExecutor)).ToList())
                services.Remove(descriptor);
            services.AddScoped<IConversationalCapabilityExecutor>(provider => new RecordingExecutor(
                ActivatorUtilities.CreateInstance<ComprehensiveAnalysisCapabilityExecutor>(provider), ExecutorProbe));
            services.RemoveAll<ISemanticCapabilityExecutionObserver>();
            services.AddSingleton<ISemanticCapabilityExecutionObserver>(ExecutorProbe);
        });
    }

    public void Reset()
    {
        ExecutorProbe.Reset();
        Lookup.Requests.Clear();
        Fake.ProposalJson = "{}";
        Fake.ProposalCalls = 0;
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
            var now = DateTimeOffset.UtcNow;

            // Realistic provider shape: the canonical Noavaran row and the CyclicalWaves row both carry
            // the symbol in TseSymbol and CompanySymbol (Ticker is empty); other فولاد companies exist.
            db.Companies.AddRange(
                Company("66000000-0000-0000-0000-000000000007", "NoavaranCurrentApi", "7", "فولاد", "فولاد مبارکه اصفهان", now),
                Company("66000000-0000-0000-0000-0000000000a7", "CyclicalWaves", "7", "فولاد", "فولاد مبارکه اصفهان", now),
                Company("66000000-0000-0000-0000-000000000008", "NoavaranCurrentApi", "8", "فخوز", "فولاد خوزستان", now),
                Company("66000000-0000-0000-0000-000000000009", "NoavaranCurrentApi", "9", "فولاژ", "فولاد آلیاژی ایران", now),
                Company("66000000-0000-0000-0000-000000000005", "NoavaranCurrentApi", "5", "کگل", "معدنی و صنعتی گل گهر", now));
            db.ComprehensiveAnalyses.Add(new ComprehensiveAnalysisRow
            {
                Id = 66001,
                Title = "تحلیل بنیادی فولاد مبارکه",
                Summary = "<p>ارزش ذاتی فولاد بالاتر از قیمت بازار است</p>",
                PlainTextSummary = "ارزش ذاتی فولاد بالاتر از قیمت بازار است",
                CreatedAt = now - AnalysisAge,
                PersianCreatedAt = "1405/07/15",
                AuthorId = 66,
                AuthorName = "تحلیلگر",
                SyncedAt = now
            });
            db.ComprehensiveAnalysisTags.Add(new ComprehensiveAnalysisTagRow
            {
                AnalysisId = 66001, TagId = 66001, TagName = "فولاد", TagSlug = "foolad", TagTypeId = 1, IsAnalytic = false
            });
            db.SaveChanges();
            _seeded = true;
        }
    }

    private static NormalizedCompanyRow Company(string id, string provider, string externalId, string symbol, string name, DateTimeOffset now) =>
        new()
        {
            Id = Guid.Parse(id),
            ProviderName = provider,
            ExternalCompanyId = externalId,
            CompanySymbol = symbol,
            TseSymbol = symbol,
            Name = name,
            LastSynchronizedAt = now
        };
}

public sealed class ProposalAiModelClient : IAiModelClient
{
    public string ProposalJson { get; set; } = "{}";
    public int ProposalCalls;
    public string? LegacyAgentSymbol { get; set; }

    public AiModelProviderDescriptor Descriptor { get; } = new(
        "ProposalFake", "fake-v2", AiProviderHostingMode.Fake,
        AiModelCapability.ChatCompletion | AiModelCapability.ToolCalling | AiModelCapability.StructuredOutput |
        AiModelCapability.UsageReporting | AiModelCapability.HealthCheck,
        Enabled: true, Priority: 1);

    public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken)
    {
        var usage = new AiExecutionUsageFacts(request.CorrelationId, Descriptor.ProviderKey, Descriptor.ModelKey,
            AiExecutionStatus.Completed, TimeSpan.Zero, AttemptNumber: 0, InputTokens: 10, OutputTokens: 4, UsedTools: false);
        if (request.Tools is { Count: > 0 })
        {
            // Legacy V2 outer agent: an LLM that, like production, selects query_comprehensive_analysis
            // with the symbol it read from the user's message.
            if (LegacyAgentSymbol is not null && request.PreviousResponseId is null)
            {
                return Task.FromResult(new AiModelResult(null, null,
                    [new AiToolCall("legacy-call-1", "query_comprehensive_analysis",
                        JsonSerializer.Serialize(new { symbolNames = new[] { LegacyAgentSymbol }, topicTags = Array.Empty<string>(), fromDateIso = (string?)null, limit = 3 }))],
                    usage, ResponseId: $"legacy-resp-{request.CorrelationId}"));
            }
            return Task.FromResult(new AiModelResult(LegacyAgentSymbol is null ? "outer agent should not be needed" : "legacy agent answer", null, [], usage));
        }

        var json = request.StructuredOutput?.SchemaName == "QueryInterpretationProposal_v2"
            ? CountAndGet()
            : "{}";
        return Task.FromResult(new AiModelResult(null, json, [], usage));
    }

    private string CountAndGet()
    {
        Interlocked.Increment(ref ProposalCalls);
        return ProposalJson;
    }

    public IAsyncEnumerable<AiStreamingChunk> StreamAsync(AiModelRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiEmbeddingResult> CreateEmbeddingsAsync(AiEmbeddingRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<AiProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiProviderHealthResult(Descriptor.ProviderKey, Descriptor.ModelKey, true, DateTimeOffset.UtcNow, "OK"));
}

public sealed class RecordingSymbolLookupService : ISymbolMetricLookupService
{
    public List<SymbolLookupRequest> Requests { get; } = [];

    public Task<SymbolLookupTableResult> LookupAsync(SymbolLookupRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var symbols = request.Pairs.Select(pair => pair.SymbolName).Distinct().ToArray();
        return Task.FromResult(new SymbolLookupTableResult(
            Guid.NewGuid(), [], [], new(DateTimeOffset.UtcNow, TimeSpan.Zero, symbols.Length, 0, false), [], symbols));
    }
}

public sealed class RecordingLegacyAnalysisParser : IComprehensiveAnalysisQueryParser
{
    public int Calls;

    public Task<ComprehensiveAnalysisParseResult> ParseAsync(string userMessage, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        throw new InvalidOperationException("The semantic route must not call the legacy comprehensive-analysis parser.");
    }
}

/// <summary>
/// Production configuration as committed in appsettings.json / docker-compose.yml:
/// SemanticRouting:DefaultMode = Shadow and no comprehensive_analysis override, so the semantic frame
/// is computed in shadow only and the LEGACY V2 route (LLM tool selection) executes.
/// </summary>
public sealed class V2ComprehensiveAnalysisProductionConfigApiFactory : V2ComprehensiveAnalysisApiFactory
{
    protected override bool SemanticPrimary => false;
    protected override TimeSpan AnalysisAge => TimeSpan.FromDays(45);
}

public sealed class V2ComprehensiveAnalysisSemanticPrimaryStaleApiFactory : V2ComprehensiveAnalysisApiFactory
{
    protected override TimeSpan AnalysisAge => TimeSpan.FromDays(45);
}

/// <summary>
/// Characterization of the production incident «تحلیل بنیادی فولاد؟» →
/// «نام نماد یا شرکت به‌طور قطعی مشخص نشد» with 0 credits. These tests pin CURRENT behavior with a
/// valid, uniquely resolvable symbol and a stored (but older than 30 days) analysis. They document a
/// defect; flip the assertions when the fix lands.
/// </summary>
public sealed class V2ComprehensiveAnalysisProductionIncidentEvidenceTests
    : IClassFixture<V2ComprehensiveAnalysisProductionConfigApiFactory>
{
    private readonly V2ComprehensiveAnalysisProductionConfigApiFactory _factory;

    public V2ComprehensiveAnalysisProductionIncidentEvidenceTests(V2ComprehensiveAnalysisProductionConfigApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Reset();
        factory.Fake.LegacyAgentSymbol = "فولاد";
    }

    [Fact]
    public async Task ShadowDefaultMode_RunsLegacyRoute_AndReportsValidSymbolAsUnresolved()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "تحلیل بنیادی فولاد؟" });
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        // The semantic route did NOT execute: the executor probe was never invoked.
        Assert.Null(_factory.ExecutorProbe.LastInvocation);
        // The legacy agent called the comprehensive-analysis tool with the correct symbol...
        // ...and the canonical resolver was not involved at all, yet the user sees the symbol-disambiguation text.
        Assert.Contains("نام نماد یا شرکت به‌طور قطعی مشخص نشد", root.GetProperty("textAnswer").GetString());
        Assert.True(root.GetProperty("clarificationRequired").GetBoolean(), root.ToString());
        // Legacy route finalizes the reservation as Completed, so unlike the semantic disambiguation path it charges 1 credit.
        Assert.Equal(1m, root.GetProperty("usage").GetProperty("creditsCharged").GetDecimal());
    }
}

public sealed class V2ComprehensiveAnalysisSemanticPrimaryStaleEvidenceTests
    : IClassFixture<V2ComprehensiveAnalysisSemanticPrimaryStaleApiFactory>
{
    private readonly V2ComprehensiveAnalysisSemanticPrimaryStaleApiFactory _factory;

    public V2ComprehensiveAnalysisSemanticPrimaryStaleEvidenceTests(V2ComprehensiveAnalysisSemanticPrimaryStaleApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Reset();
    }

    [Fact]
    public async Task SemanticPrimary_ResolvesCompanyAndReachesExecutor_ButStaleStoredAnalysisIsNotReturned()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "تحلیل بنیادی فولاد؟" });
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        var invocation = Assert.IsType<Feature128ExecutorInvocation>(_factory.ExecutorProbe.LastInvocation);
        Assert.Equal("comprehensive_analysis", invocation.CapabilityCode);
        Assert.Equal("66000000-0000-0000-0000-000000000007", invocation.Frame.Slots
            .Single(slot => slot.Type == QuerySlotType.CompanyOrSymbol).CanonicalEntity!.CanonicalId.ToString());
        Assert.False(root.GetProperty("clarificationRequired").GetBoolean());
        // The 45-day-old stored analysis for the resolved symbol is silently excluded by the 30-day default window.
        Assert.True(root.GetProperty("comprehensiveAnalysisResult").ValueKind == JsonValueKind.Null
            || !root.GetProperty("comprehensiveAnalysisResult").GetProperty("items").EnumerateArray().Any(), root.ToString());
    }
}

/// <summary>Decorates the REAL executor: records the validated frame, then delegates unchanged.</summary>
public sealed class RecordingExecutor(IConversationalCapabilityExecutor inner, Feature128ExecutorProbe probe) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => inner.CapabilityCode;

    public Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        probe.BeforeExecute(CapabilityCode, frame, context);
        return inner.ExecuteAsync(frame, context, cancellationToken);
    }
}

public sealed class V2ComprehensiveAnalysisTechnicalTopicTests : IClassFixture<V2ComprehensiveAnalysisApiFactory>
{
    private readonly V2ComprehensiveAnalysisApiFactory _factory;

    public V2ComprehensiveAnalysisTechnicalTopicTests(V2ComprehensiveAnalysisApiFactory factory)
    {
        _factory = factory;
        factory.EnsureSeeded();
        factory.Reset();
    }

    [Fact]
    public async Task TechnicalAnalysisQuery_ResolvesCompanyAndReachesExecutorWithTopicFilter()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message = "تحلیل تکنیکال فولاد" });
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.Equal("comprehensive_analysis", document.RootElement.GetProperty("semanticCapabilityCode").GetString());
        Assert.False(document.RootElement.GetProperty("clarificationRequired").GetBoolean());
        var frame = Assert.IsType<Feature128ExecutorInvocation>(_factory.ExecutorProbe.LastInvocation).Frame;
        Assert.Equal("66000000-0000-0000-0000-000000000007", frame.Slots.Single(slot => slot.Type == QuerySlotType.CompanyOrSymbol).CanonicalEntity!.CanonicalId.ToString());
        Assert.Equal("تحلیل_تکنیکال", frame.Slots.Single(slot => slot.Type == QuerySlotType.AnalysisTopic).Value);
    }
}
