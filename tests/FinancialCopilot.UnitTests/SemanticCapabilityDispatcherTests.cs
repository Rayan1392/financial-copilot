using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.UnitTests;

public sealed class SemanticCapabilityDispatcherTests
{
    [Fact]
    public async Task DisabledOrUnregisteredCapability_CannotExecute()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var dispatcher = new SemanticCapabilityDispatcher(registry, []);
        var result = await dispatcher.DispatchAsync(Frame("monthly_activity_trend"), Context(), default);
        Assert.Equal(CapabilityExecutionStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task RegisteredEnabledCapability_UsesOnlyItsExecutor()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var dispatcher = new SemanticCapabilityDispatcher(registry, [new Executor("monthly_activity_trend")]);
        var result = await dispatcher.DispatchAsync(Frame("monthly_activity_trend"), Context(), default);
        Assert.Equal(CapabilityExecutionStatus.Executed, result.Status);
    }

    [Fact]
    public void ShadowMode_IsObservationalAndRecordsOneBoundedComparison()
    {
        var telemetry = new RecordingTelemetry();
        var coordinator = new SemanticRoutingRolloutCoordinator(
            new(new Dictionary<string, SemanticRoutingMode> { ["monthly_activity_trend"] = SemanticRoutingMode.Shadow }), telemetry);

        var decision = coordinator.Decide("monthly_activity_trend");
        coordinator.RecordShadowComparison("monthly_activity_trend", "legacy_trend", "semantic_trend", "correlation");

        Assert.False(decision.ExecuteSemanticRoute);
        Assert.True(decision.RunShadowComparison);
        var comparison = Assert.Single(telemetry.Items);
        Assert.False(comparison.Agreement);
        Assert.Equal("correlation", comparison.CorrelationId);
    }

    [Fact]
    public void CanaryAndPrimary_EnableOnlyTheSemanticExecutionPath()
    {
        foreach (var mode in new[] { SemanticRoutingMode.Canary, SemanticRoutingMode.SemanticPrimary })
        {
            var coordinator = new SemanticRoutingRolloutCoordinator(new(new Dictionary<string, SemanticRoutingMode> { ["symbol_metric_lookup"] = mode }), new RecordingTelemetry());
            var decision = coordinator.Decide("symbol_metric_lookup");
            Assert.True(decision.ExecuteSemanticRoute);
            Assert.False(decision.RunShadowComparison);
        }
    }

    private static ValidatedQueryFrame Frame(string capability) => new(capability, 1, [], new("", "", "en", [], [], [], null, null, null, [], [], 0, [], 1));
    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "en");
    private sealed class Executor(string code) : IConversationalCapabilityExecutor
    {
        public string CapabilityCode => code;
        public Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken) => Task.FromResult(new CapabilityExecutionResult(code, 1, CapabilityExecutionStatus.Executed, "none"));
    }
    private sealed class RecordingTelemetry : ISemanticRoutingTelemetrySink
    {
        public List<SemanticRoutingComparison> Items { get; } = [];
        public void Record(SemanticRoutingComparison comparison) => Items.Add(comparison);
    }
}
