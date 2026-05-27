using System.Net;
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

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

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
