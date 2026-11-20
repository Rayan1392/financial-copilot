using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure.AI.ModelProviders;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» T02: the OpenAI Responses payload for reasoning effort, the hosted web search
/// tool and consulted sources, and the model override. A request without the new fields serialises exactly as before.
/// </summary>
public sealed class GroundedAnalysisTransportTests
{
    private static readonly Guid TenantId = Guid.Parse("7a3c4b9e-5d1f-4c2a-9e0b-1f2d3c4b5a69");

    private const string SearchResponse =
        """
        {"id":"resp-1","output":[
          {"id":"ws-1","type":"web_search_call","status":"completed","action":{"type":"search","query":"q","sources":[{"type":"url","url":"https://codal.ir/a"},{"type":"url","url":"https://example.ir/b"}]}},
          {"id":"ws-2","type":"web_search_call","status":"completed","action":{"type":"open_page","url":"https://example.ir/c"}},
          {"id":"rs-1","type":"reasoning","summary":[]},
          {"id":"msg-1","type":"message","content":[{"type":"output_text","text":"{\"answer\":\"x\"}","annotations":[{"type":"url_citation","url":"https://example.ir/d","title":"d"}]}]}],
         "usage":{"input_tokens":9000,"output_tokens":2500,"output_tokens_details":{"reasoning_tokens":1800},"input_tokens_details":{"cached_tokens":0}}}
        """;

    private const string PlainResponse =
        """{"id":"resp-2","output":[{"id":"msg-1","type":"message","content":[{"type":"output_text","text":"{\"answer\":\"x\"}"}]}],"usage":{"input_tokens":10,"output_tokens":5}}""";

    [Fact]
    public async Task RequestWithoutNewFields_SerialisesAsBefore()
    {
        string? body = null;
        using var client = Client(request => { body = Read(request); return Json(PlainResponse); });
        var transport = new OpenAiHostedAiModelTransport(client);

        var response = await transport.CompleteAsync("gpt-6-luna", BaseRequest(), CancellationToken.None);

        var payload = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal(["model", "input", "text", "max_output_tokens"], payload.Select(property => property.Key).ToArray());
        Assert.Null(response.ReasoningTokens);
        Assert.Null(response.WebSearchCalls);
        Assert.Null(response.ConsultedSources);
    }

    [Fact]
    public async Task WebOff_SendsReasoningButNoTools()
    {
        string? body = null;
        using var client = Client(request => { body = Read(request); return Json(PlainResponse); });
        var transport = new OpenAiHostedAiModelTransport(client);

        await transport.CompleteAsync("gpt-6.2-sol", BaseRequest() with { ReasoningEffort = "medium" }, CancellationToken.None);

        var payload = JsonNode.Parse(body!)!.AsObject();
        Assert.Equal("medium", payload["reasoning"]!["effort"]!.GetValue<string>());
        Assert.False(payload.ContainsKey("tools"));
        Assert.False(payload.ContainsKey("max_tool_calls"));
        Assert.False(payload.ContainsKey("include"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WebOn_SendsSearchToolWithProfileCapAndSources(int maxToolCalls)
    {
        string? body = null;
        using var client = Client(request => { body = Read(request); return Json(SearchResponse); });
        var transport = new OpenAiHostedAiModelTransport(client);
        var request = BaseRequest() with
        {
            ReasoningEffort = "low",
            WebSearch = new AiWebSearchOptions(maxToolCalls, "low", ["codal.ir"], "IR")
        };

        var response = await transport.CompleteAsync("gpt-6.2-sol", request, CancellationToken.None);

        var payload = JsonNode.Parse(body!)!.AsObject();
        var tool = Assert.Single(payload["tools"]!.AsArray())!.AsObject();
        Assert.Equal("web_search", tool["type"]!.GetValue<string>());
        Assert.Equal("low", tool["search_context_size"]!.GetValue<string>());
        Assert.Equal("IR", tool["user_location"]!["country"]!.GetValue<string>());
        Assert.Equal("codal.ir", tool["filters"]!["allowed_domains"]![0]!.GetValue<string>());
        Assert.Equal(maxToolCalls, payload["max_tool_calls"]!.GetValue<int>());
        Assert.False(payload.ContainsKey("tool_choice"));
        Assert.Equal("web_search_call.action.sources", payload["include"]![0]!.GetValue<string>());
        Assert.True(payload.ContainsKey("text"));

        Assert.Equal("""{"answer":"x"}""", response.StructuredJson);
        Assert.Equal(1800, response.ReasoningTokens);
        Assert.Equal(2, response.WebSearchCalls);
        Assert.Equal(
            ["https://codal.ir/a", "https://example.ir/b", "https://example.ir/c", "https://example.ir/d"],
            response.ConsultedSources);
    }

    [Fact]
    public async Task WebOn_WithoutSearch_CountsZeroCalls()
    {
        using var client = Client(_ => Json(PlainResponse));
        var transport = new OpenAiHostedAiModelTransport(client);

        var response = await transport.CompleteAsync(
            "gpt-6.2-sol", BaseRequest() with { WebSearch = new AiWebSearchOptions(2) }, CancellationToken.None);

        Assert.Equal(0, response.WebSearchCalls);
        Assert.Empty(response.ConsultedSources!);
    }

    [Fact]
    public async Task HostedClient_SendsModelOverrideAndReportsIt()
    {
        string? body = null;
        using var http = Client(request => { body = Read(request); return Json(SearchResponse); });
        var client = new ConfiguredHostedAiModelClient(
            Descriptor(AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput | AiModelCapability.WebSearch),
            new OpenAiHostedAiModelTransport(http),
            TimeProvider.System);

        var result = await client.CompleteAsync(
            BaseRequest() with { ModelKeyOverride = "gpt-6.2-sol", WebSearch = new AiWebSearchOptions(1) },
            CancellationToken.None);

        Assert.Equal("gpt-6.2-sol", JsonNode.Parse(body!)!["model"]!.GetValue<string>());
        Assert.Equal("gpt-6.2-sol", result.Usage.ModelKey);
        Assert.Equal(1800, result.Usage.ReasoningTokens);
        Assert.Equal(2, result.Usage.WebSearchCalls);
        Assert.Equal(4, result.ConsultedSources!.Count);
    }

    [Fact]
    public async Task HostedClient_WithoutWebSearchCapability_RejectsWebSearchBeforeAnyCall()
    {
        var calls = 0;
        using var http = Client(_ => { calls++; return Json(PlainResponse); });
        var client = new ConfiguredHostedAiModelClient(
            Descriptor(AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput),
            new OpenAiHostedAiModelTransport(http),
            TimeProvider.System);

        var failure = await Assert.ThrowsAsync<AiModelProviderException>(() => client.CompleteAsync(
            BaseRequest() with { WebSearch = new AiWebSearchOptions(1) }, CancellationToken.None));

        Assert.Equal(AiExecutionStatus.CapabilityUnavailable, failure.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task DeepSeekTransport_RejectsWebSearch()
    {
        var calls = 0;
        using var http = Client(_ => { calls++; return Json(PlainResponse); });
        var transport = new DeepSeekHostedAiModelTransport(
            http, Microsoft.Extensions.Options.Options.Create(new AiProviderOptions()));

        var failure = await Assert.ThrowsAsync<AiModelProviderException>(() => transport.CompleteAsync(
            "deepseek-chat", BaseRequest() with { WebSearch = new AiWebSearchOptions(1) }, CancellationToken.None));

        Assert.Equal(AiExecutionStatus.CapabilityUnavailable, failure.Status);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExecutionService_RecordsTheModelActuallyCalled()
    {
        var client = new DeterministicFakeAiModelClient(
            new AiModelProviderDescriptor("OpenAI", "gpt-6-luna", AiProviderHostingMode.Fake,
                AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput, true, 1),
            TimeProvider.System);
        var accumulator = new InMemoryAiExecutionUsageAccumulator();
        var service = new AiModelExecutionService(
            new CapabilityBasedAiModelProviderResolver([client]),
            new JsonStructuredOutputValidator(),
            new NullTelemetrySink(),
            TimeProvider.System,
            accumulator);
        var request = new AiModelRequest("corr-1", TenantId, AiWorkloadKind.ExplanationGeneration,
            [new AiConversationMessage(AiMessageRole.User, "q")], ModelKeyOverride: "gpt-6.2-sol");

        var result = await service.ExecuteAsync(
            new AiModelSelectionRequest(TenantId, AiWorkloadKind.ExplanationGeneration, AiModelCapability.ChatCompletion,
                "corr-1", RequiredProviderKey: "OpenAI"),
            request,
            CancellationToken.None);

        Assert.Equal("gpt-6.2-sol", result.Usage.ModelKey);
        Assert.Equal("gpt-6.2-sol", accumulator.GetSummary("corr-1")!.ModelKey);
    }

    [Fact]
    public async Task Resolver_RequiredProviderKey_ExcludesOtherProviders()
    {
        var deepSeek = new DeterministicFakeAiModelClient(
            new AiModelProviderDescriptor("DeepSeek", "deepseek-chat", AiProviderHostingMode.Fake,
                AiModelCapability.ChatCompletion, true, 1),
            TimeProvider.System);
        var resolver = new CapabilityBasedAiModelProviderResolver([deepSeek]);

        var candidates = resolver.ResolveCandidates(new AiModelSelectionRequest(
            TenantId, AiWorkloadKind.ExplanationGeneration, AiModelCapability.ChatCompletion, "c", RequiredProviderKey: "OpenAI"));

        Assert.Empty(candidates);
        await Task.CompletedTask;
    }

    private static AiModelRequest BaseRequest() =>
        new(
            "corr-1",
            TenantId,
            AiWorkloadKind.ExplanationGeneration,
            [new AiConversationMessage(AiMessageRole.System, "s"), new AiConversationMessage(AiMessageRole.User, "q")],
            StructuredOutput: new AiStructuredOutputContract("Schema_v1", ["answer"],
                """{"type":"object","additionalProperties":false,"required":["answer"],"properties":{"answer":{"type":"string"}}}"""),
            MaxOutputTokens: 3000);

    private static AiModelProviderDescriptor Descriptor(AiModelCapability capabilities) =>
        new("OpenAI", "gpt-6-luna", AiProviderHostingMode.Hosted, capabilities, true, 10);

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var client = new HttpClient(new RouteHandler(responder)) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        return client;
    }

    private static string Read(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class NullTelemetrySink : IAiExecutionTelemetrySink
    {
        public Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
