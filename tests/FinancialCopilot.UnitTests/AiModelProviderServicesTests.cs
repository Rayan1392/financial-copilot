using FinancialCopilot.Application.AI.ModelProviders;

namespace FinancialCopilot.UnitTests;

public sealed class AiModelProviderServicesTests
{
    private static readonly Guid TenantId = Guid.Parse("8c9be50e-01e9-428c-8510-fb88cd739003");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-27T08:00:00Z");

    [Fact]
    public void Resolver_SelectsCapabilitiesWithinTenantAndLocalRuntimePolicy()
    {
        var permitted = Client(
            "structured-hosted",
            AiProviderHostingMode.Hosted,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            allowedTenants: new HashSet<Guid> { TenantId });
        var foreign = Client(
            "foreign-provider",
            AiProviderHostingMode.Hosted,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            allowedTenants: new HashSet<Guid> { Guid.NewGuid() });
        var local = Client(
            "local-provider",
            AiProviderHostingMode.Local,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput);
        var resolver = new CapabilityBasedAiModelProviderResolver([local, foreign, permitted]);

        var candidates = resolver.ResolveCandidates(new AiModelSelectionRequest(
            TenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            "resolve-1",
            AllowLocalRuntime: false));

        Assert.Single(candidates);
        Assert.Equal("structured-hosted", candidates.Single().Descriptor.ProviderKey);
    }

    [Fact]
    public async Task ExecutionService_FallsBackAfterInvalidStructuredOutputAndRecordsAttempts()
    {
        var first = Client(
            "first",
            AiProviderHostingMode.Hosted,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            result: new AiModelResult("bad", """{"intent":"scanner"}""", [], EmptyUsage("first")));
        var second = Client(
            "second",
            AiProviderHostingMode.Hosted,
            AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput,
            result: new AiModelResult("good", """{"intent":"scanner","conditions":[]}""", [], EmptyUsage("second")));
        var telemetry = new CapturingTelemetrySink();
        var service = new AiModelExecutionService(
            new CapabilityBasedAiModelProviderResolver([first, second]),
            new JsonStructuredOutputValidator(),
            telemetry,
            new FixedTimeProvider(Now));
        var selection = new AiModelSelectionRequest(
            TenantId,
            AiWorkloadKind.ScannerParsing,
            AiModelCapability.None,
            "scanner-parse-1");
        var request = new AiModelRequest(
            "scanner-parse-1",
            TenantId,
            AiWorkloadKind.ScannerParsing,
            [new AiConversationMessage(AiMessageRole.User, "find low P/E symbols")],
            new AiStructuredOutputContract("ScannerQueryPlan", ["intent", "conditions"]));

        var result = await service.ExecuteAsync(selection, request, CancellationToken.None);

        Assert.Equal("second", result.Usage.ProviderKey);
        Assert.Equal(2, result.Usage.AttemptNumber);
        Assert.Equal(AiExecutionStatus.InvalidStructuredOutput, telemetry.Facts[0].Status);
        Assert.Equal(AiExecutionStatus.Completed, telemetry.Facts[1].Status);
        Assert.All(telemetry.Facts, facts => Assert.Equal("scanner-parse-1", facts.CorrelationId));
    }

    [Fact]
    public async Task ExecutionService_RejectsScannerProviderWithoutStructuredOutputCapability()
    {
        var service = new AiModelExecutionService(
            new CapabilityBasedAiModelProviderResolver(
                [Client("chat-only", AiProviderHostingMode.Hosted, AiModelCapability.ChatCompletion)]),
            new JsonStructuredOutputValidator(),
            new CapturingTelemetrySink(),
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<AiModelProviderException>(() =>
            service.ExecuteAsync(
                new AiModelSelectionRequest(
                    TenantId,
                    AiWorkloadKind.ScannerParsing,
                    AiModelCapability.None,
                    "scanner-parse-2"),
                new AiModelRequest(
                    "scanner-parse-2",
                    TenantId,
                    AiWorkloadKind.ScannerParsing,
                    [new AiConversationMessage(AiMessageRole.User, "screen stocks")],
                    new AiStructuredOutputContract("ScannerQueryPlan", ["intent"])),
                CancellationToken.None));

        Assert.Equal(AiExecutionStatus.CapabilityUnavailable, exception.Status);
    }

    [Fact]
    public async Task ExecutionService_RejectsMismatchedTenantOrCorrelationEvidence()
    {
        var service = new AiModelExecutionService(
            new CapabilityBasedAiModelProviderResolver(
                [Client("chat", AiProviderHostingMode.Hosted, AiModelCapability.ChatCompletion)]),
            new JsonStructuredOutputValidator(),
            new CapturingTelemetrySink(),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ExecuteAsync(
                new AiModelSelectionRequest(
                    TenantId,
                    AiWorkloadKind.Summarization,
                    AiModelCapability.None,
                    "correlation-a"),
                new AiModelRequest(
                    "correlation-b",
                    Guid.NewGuid(),
                    AiWorkloadKind.Summarization,
                    [new AiConversationMessage(AiMessageRole.User, "summarize")]),
                CancellationToken.None));
    }

    [Fact]
    public void CapabilityRegistry_ExcludesDisabledAndForeignTenantProviders()
    {
        var resolver = new CapabilityBasedAiModelProviderResolver(
        [
            Client("enabled", AiProviderHostingMode.Hosted, AiModelCapability.ChatCompletion),
            Client("disabled", AiProviderHostingMode.Hosted, AiModelCapability.ChatCompletion, enabled: false),
            Client(
                "foreign",
                AiProviderHostingMode.Hosted,
                AiModelCapability.ChatCompletion,
                allowedTenants: new HashSet<Guid> { Guid.NewGuid() })
        ]);

        var visible = resolver.GetAvailableProviders(TenantId);

        Assert.Equal("enabled", visible.Single().ProviderKey);
    }

    private static StubAiModelClient Client(
        string providerKey,
        AiProviderHostingMode mode,
        AiModelCapability capabilities,
        bool enabled = true,
        IReadOnlySet<Guid>? allowedTenants = null,
        AiModelResult? result = null) =>
        new(
            new AiModelProviderDescriptor(
                providerKey,
                $"{providerKey}-model",
                mode,
                capabilities,
                enabled,
                Priority: providerKey == "first" ? 1 : 2,
                allowedTenants),
            result ?? new AiModelResult("ok", null, [], EmptyUsage(providerKey)));

    private static AiExecutionUsageFacts EmptyUsage(string providerKey) =>
        new("unused", providerKey, $"{providerKey}-model", AiExecutionStatus.Completed, TimeSpan.Zero, 0);

    private sealed class StubAiModelClient(
        AiModelProviderDescriptor descriptor,
        AiModelResult result) : IAiModelClient
    {
        public AiModelProviderDescriptor Descriptor { get; } = descriptor;

        public Task<AiModelResult> CompleteAsync(AiModelRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);

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
                true,
                Now));
    }

    private sealed class CapturingTelemetrySink : IAiExecutionTelemetrySink
    {
        public List<AiExecutionUsageFacts> Facts { get; } = [];

        public Task RecordAttemptAsync(AiExecutionUsageFacts facts, CancellationToken cancellationToken)
        {
            Facts.Add(facts);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
