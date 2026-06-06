using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.AI.ModelProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialCopilot.IntegrationTests;

public sealed class AiModelProviderAdapterTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

    [Fact]
    public async Task ConfiguredFakeProvider_ResolvesAndExecutesStructuredRequestWithoutNetwork()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = "Host=localhost;Database=fake",
                ["AiModelProviders:Providers:0:ProviderKey"] = "TestFake",
                ["AiModelProviders:Providers:0:ModelKey"] = "fake-v1",
                ["AiModelProviders:Providers:0:HostingMode"] = "Fake",
                ["AiModelProviders:Providers:0:Adapter"] = "Fake",
                ["AiModelProviders:Providers:0:Enabled"] = "true",
                ["AiModelProviders:Providers:0:Priority"] = "1",
                ["AiModelProviders:Providers:0:Capabilities"] =
                    "ChatCompletion,StructuredOutput,UsageReporting,HealthCheck"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        var execution = provider.GetRequiredService<IAiModelExecutionService>();

        var result = await execution.ExecuteAsync(
            new AiModelSelectionRequest(
                TenantId,
                AiWorkloadKind.ScannerParsing,
                AiModelCapability.None,
                "fake-integration-1"),
            new AiModelRequest(
                "fake-integration-1",
                TenantId,
                AiWorkloadKind.ScannerParsing,
                [new AiConversationMessage(AiMessageRole.User, "find stocks")],
                new AiStructuredOutputContract("ScannerQueryPlan", ["intent", "conditions"])),
            CancellationToken.None);

        Assert.Equal("TestFake", result.Usage.ProviderKey);
        Assert.Equal(AiExecutionStatus.Completed, result.Usage.Status);
        Assert.Contains("\"intent\"", result.StructuredJson);
    }

    [Fact]
    public async Task AiProviderDefaultProvider_SelectsDeepSeekRegistrationFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FinancialCopilot"] = "Host=localhost;Database=fake",
                ["AiProvider:DefaultProvider"] = "DeepSeek",
                ["AiProvider:DeepSeek:Model"] = "deepseek-chat",
                ["AiProvider:DeepSeek:BaseUrl"] = "https://api.deepseek.com"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IAiModelProviderResolver>();
        var diagnostics = provider.GetRequiredService<IAiModelProviderDiagnostics>();

        var candidates = resolver.ResolveCandidates(new AiModelSelectionRequest(
            TenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.StructuredOutput,
            "deepseek-config-1"));
        var active = diagnostics.GetActiveProvider(TenantId);

        Assert.Equal("DeepSeek", Assert.Single(candidates).Descriptor.ProviderKey);
        Assert.Equal("deepseek-chat", candidates.Single().Descriptor.ModelKey);
        Assert.Equal("DeepSeek", active.ConfiguredProviderKey);
        Assert.Equal("DeepSeek", active.ProviderKey);
        Assert.Equal("deepseek-chat", active.ModelKey);
    }

    [Fact]
    public async Task OllamaAdapter_MapsChatEmbeddingUsageAndHealthIntoNormalizedModels()
    {
        using var httpClient = new HttpClient(new RouteHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/api/chat" => Json(
                    """{"message":{"content":"{\"intent\":\"scanner\"}"},"prompt_eval_count":12,"eval_count":5}"""),
                "/api/embed" => Json("""{"embeddings":[[0.1,0.2]],"prompt_eval_count":7}"""),
                "/api/tags" => Json("""{"models":[{"name":"model-v1","model":"model-v1"}]}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            }))
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var client = new OllamaAiModelClient(
            httpClient,
            Descriptor(
                "ollama-local",
                AiProviderHostingMode.Local,
                AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput |
                AiModelCapability.Embeddings | AiModelCapability.HealthCheck),
            new FixedTimeProvider(Now));
        var request = new AiModelRequest(
            "ollama-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            [new AiConversationMessage(AiMessageRole.User, "scan")],
            new AiStructuredOutputContract("ScannerQueryPlan", ["intent"]));

        var completion = await client.CompleteAsync(request, CancellationToken.None);
        var embeddings = await client.CreateEmbeddingsAsync(
            new AiEmbeddingRequest("embed-1", TenantId, ["text"]),
            CancellationToken.None);
        var health = await client.CheckHealthAsync(CancellationToken.None);

        Assert.Equal("""{"intent":"scanner"}""", completion.StructuredJson);
        Assert.Equal(12, completion.Usage.InputTokens);
        Assert.Equal(5, completion.Usage.OutputTokens);
        Assert.Equal(7, embeddings.Usage.InputTokens);
        Assert.True(embeddings.Usage.EmbeddingOperation);
        Assert.True(health.Available);
    }

    [Fact]
    public async Task OllamaAdapter_ReportsLocalRuntimeUnavailabilityExplicitly()
    {
        using var httpClient = new HttpClient(new ThrowingHandler())
        {
            BaseAddress = new Uri("http://localhost:11434/")
        };
        var client = new OllamaAiModelClient(
            httpClient,
            Descriptor("ollama-local", AiProviderHostingMode.Local, AiModelCapability.ChatCompletion),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<AiModelProviderException>(() =>
            client.CompleteAsync(
                new AiModelRequest(
                    "ollama-failure",
                    TenantId,
                    AiWorkloadKind.Summarization,
                    [new AiConversationMessage(AiMessageRole.User, "summary")]),
                CancellationToken.None));

        Assert.Equal(AiExecutionStatus.RuntimeUnavailable, exception.Status);
        Assert.Equal("local_runtime_unavailable", exception.Code);
    }

    [Fact]
    public async Task OpenAiAdapter_MapsStructuredChatToolsUsageAndHealth()
    {
        string? requestBody = null;
        using var httpClient = new HttpClient(new RouteHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-key", request.Headers.Authorization?.Parameter);

            return request.RequestUri!.AbsolutePath switch
            {
                "/v1/responses" => Json(
                    """{"output":[{"id":"message-1","type":"message","content":[{"type":"output_text","text":"{\"intent\":\"Scanner\",\"confidence\":0.9}"}]},{"id":"function-1","type":"function_call","call_id":"tool-1","name":"screen","arguments":"{\"limit\":5}"}],"usage":{"input_tokens":14,"output_tokens":6}}""",
                    requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()),
                "/v1/models/gpt-5.5" => Json("""{"id":"gpt-5.5"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        var transport = new OpenAiHostedAiModelTransport(httpClient);
        var request = new AiModelRequest(
            "openai-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            [new AiConversationMessage(AiMessageRole.User, "scan")],
            new AiStructuredOutputContract("IntentDetectionOutput", ["intent", "confidence"]),
            [new AiToolDefinition("screen", "Screen stocks.", """{"type":"object"}""")]);

        var completion = await transport.CompleteAsync("gpt-5.5", request, CancellationToken.None);
        var available = await transport.CheckAvailabilityAsync("gpt-5.5", CancellationToken.None);

        Assert.Equal("""{"intent":"Scanner","confidence":0.9}""", completion.StructuredJson);
        Assert.Equal(14, completion.InputTokens);
        Assert.Equal(6, completion.OutputTokens);
        Assert.Equal("screen", Assert.Single(completion.ToolCalls).Name);
        Assert.True(available);
        Assert.Contains("\"text\":{\"format\":{\"type\":\"json_object\"}}", requestBody);
        Assert.Contains("\"tools\":[{\"type\":\"function\",\"name\":\"screen\"", requestBody);
    }

    [Fact]
    public async Task OpenAiAdapter_ReportsMissingCredentialExplicitly()
    {
        using var httpClient = new HttpClient(new RouteHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        var transport = new OpenAiHostedAiModelTransport(httpClient);

        var exception = await Assert.ThrowsAsync<AiModelProviderException>(() =>
            transport.CompleteAsync(
                "gpt-5.5",
                new AiModelRequest(
                    "openai-missing-credential",
                    TenantId,
                    AiWorkloadKind.Summarization,
                    [new AiConversationMessage(AiMessageRole.User, "summary")]),
                CancellationToken.None));

        Assert.Equal(AiExecutionStatus.RuntimeUnavailable, exception.Status);
        Assert.Equal("hosted_provider_credentials_missing", exception.Code);
    }

    [Fact]
    public async Task DeepSeekAdapter_MapsStructuredChatToolsUsageAndHealth()
    {
        string? requestBody = null;
        using var httpClient = new HttpClient(new RouteHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("deepseek-key", request.Headers.Authorization?.Parameter);

            return request.RequestUri!.AbsolutePath switch
            {
                "/chat/completions" => Json(
                    """{"id":"ds-1","choices":[{"index":0,"message":{"content":"{\"intent\":\"Scanner\",\"confidence\":0.8}","tool_calls":[{"id":"call-1","type":"function","function":{"name":"screen","arguments":"{\"limit\":5}"}}]}}],"usage":{"prompt_tokens":11,"completion_tokens":7,"total_tokens":18}}""",
                    requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()),
                "/models/deepseek-chat" => Json("""{"id":"deepseek-chat"}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }))
        {
            BaseAddress = new Uri("https://api.deepseek.com/")
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "deepseek-key");
        var transport = new DeepSeekHostedAiModelTransport(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new AiProviderOptions
            {
                DeepSeek = new DeepSeekProviderOptions { Model = "deepseek-chat" }
            }));
        var request = new AiModelRequest(
            "deepseek-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            [new AiConversationMessage(AiMessageRole.User, "scan")],
            new AiStructuredOutputContract("IntentDetectionOutput", ["intent", "confidence"]),
            [new AiToolDefinition("screen", "Screen stocks.", """{"type":"object"}""")]);

        var completion = await transport.CompleteAsync("deepseek-chat", request, CancellationToken.None);
        var available = await transport.CheckAvailabilityAsync("deepseek-chat", CancellationToken.None);

        Assert.Equal("""{"intent":"Scanner","confidence":0.8}""", completion.StructuredJson);
        Assert.Equal(11, completion.InputTokens);
        Assert.Equal(7, completion.OutputTokens);
        Assert.Equal("screen", Assert.Single(completion.ToolCalls).Name);
        Assert.True(available);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", requestBody);
        Assert.Contains("\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"screen\"", requestBody);
    }

    [Fact]
    public async Task DeepSeekAdapter_ReportsMissingCredentialExplicitly()
    {
        using var httpClient = new HttpClient(new RouteHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)))
        {
            BaseAddress = new Uri("https://api.deepseek.com/")
        };
        var transport = new DeepSeekHostedAiModelTransport(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new AiProviderOptions()));

        var exception = await Assert.ThrowsAsync<AiModelProviderException>(() =>
            transport.CompleteAsync(
                "deepseek-chat",
                new AiModelRequest(
                    "deepseek-missing-credential",
                    TenantId,
                    AiWorkloadKind.Summarization,
                    [new AiConversationMessage(AiMessageRole.User, "summary")]),
                CancellationToken.None));

        Assert.Equal(AiExecutionStatus.RuntimeUnavailable, exception.Status);
        Assert.Equal("hosted_provider_credentials_missing", exception.Code);
    }

    [Fact]
    public async Task OpenAiAdapter_ReportsQuotaExceededWithoutRetry()
    {
        var requestCount = 0;
        using var httpClient = AuthenticatedOpenAiClient(new RouteHandler(_ =>
        {
            requestCount++;
            return Error(
                HttpStatusCode.TooManyRequests,
                """{"error":{"message":"You exceeded your current quota.","type":"insufficient_quota","code":"insufficient_quota"}}""");
        }));
        var transport = new OpenAiHostedAiModelTransport(httpClient);

        var exception = await Assert.ThrowsAsync<AiModelProviderException>(() =>
            transport.CompleteAsync(
                "gpt-5.5",
                CompletionRequest("openai-quota"),
                CancellationToken.None));

        Assert.Equal("hosted_provider_quota_exceeded", exception.Code);
        Assert.Contains("You exceeded your current quota.", exception.Message);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task OpenAiAdapter_RetriesTemporaryRateLimit()
    {
        var requestCount = 0;
        using var httpClient = AuthenticatedOpenAiClient(new RouteHandler(_ =>
        {
            requestCount++;
            if (requestCount < 3)
            {
                var response = Error(
                    HttpStatusCode.TooManyRequests,
                    """{"error":{"message":"Rate limit reached.","type":"rate_limit_error","code":"rate_limit_exceeded"}}""");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }

            return Json("""{"output":[{"id":"message-1","type":"message","content":[{"type":"output_text","text":"done"}]}] }""");
        }));
        var transport = new OpenAiHostedAiModelTransport(httpClient);

        var result = await transport.CompleteAsync(
            "gpt-5.5",
            CompletionRequest("openai-rate-limit"),
            CancellationToken.None);

        Assert.Equal("done", result.Text);
        Assert.Equal(3, requestCount);
    }

    [Fact]
    public async Task HostedAdapter_MapsContractedTransportUsageWithoutLeakingTransportResult()
    {
        var client = new ConfiguredHostedAiModelClient(
            Descriptor(
                "hosted-mvp",
                AiProviderHostingMode.Hosted,
                AiModelCapability.ChatCompletion | AiModelCapability.Embeddings | AiModelCapability.HealthCheck),
            new StubHostedTransport(),
            new FixedTimeProvider(Now));

        var completion = await client.CompleteAsync(
            new AiModelRequest(
                "hosted-1",
                TenantId,
                AiWorkloadKind.ExplanationGeneration,
                [new AiConversationMessage(AiMessageRole.User, "explain")]),
            CancellationToken.None);
        var embeddings = await client.CreateEmbeddingsAsync(
            new AiEmbeddingRequest("hosted-embedding", TenantId, ["content"]),
            CancellationToken.None);

        Assert.Equal("hosted response", completion.Text);
        Assert.Equal(20, completion.Usage.InputTokens);
        Assert.Equal(0.02m, completion.Usage.ProviderReportedCost);
        Assert.Equal("USD", completion.Usage.ProviderReportedCurrency);
        Assert.True(embeddings.Usage.EmbeddingOperation);
    }

    private static AiModelProviderDescriptor Descriptor(
        string providerKey,
        AiProviderHostingMode mode,
        AiModelCapability capabilities) =>
        new(providerKey, "model-v1", mode, capabilities, Enabled: true, Priority: 1);

    private static HttpResponseMessage Json(string content, string? _ = null) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode statusCode, string content) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private static HttpClient AuthenticatedOpenAiClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        return client;
    }

    private static AiModelRequest CompletionRequest(string correlationId) =>
        new(
            correlationId,
            TenantId,
            AiWorkloadKind.Summarization,
            [new AiConversationMessage(AiMessageRole.User, "summary")]);

    private sealed class RouteHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Runtime is offline.");
    }

    private sealed class StubHostedTransport : IHostedAiModelTransport
    {
        public Task<HostedAiCompletionResponse> CompleteAsync(
            string modelKey,
            AiModelRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HostedAiCompletionResponse(
                "hosted response",
                null,
                [],
                InputTokens: 20,
                OutputTokens: 8,
                ProviderReportedCost: 0.02m,
                ProviderReportedCurrency: "USD"));

        public async IAsyncEnumerable<AiStreamingChunk> StreamAsync(
            string modelKey,
            AiModelRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new AiStreamingChunk("chunk", null, true);
        }

        public Task<HostedAiEmbeddingResponse> CreateEmbeddingsAsync(
            string modelKey,
            AiEmbeddingRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HostedAiEmbeddingResponse([[0.1f, 0.2f]], InputTokens: 3));

        public Task<bool> CheckAvailabilityAsync(string modelKey, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
