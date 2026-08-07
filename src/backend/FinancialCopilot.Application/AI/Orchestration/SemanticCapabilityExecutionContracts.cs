namespace FinancialCopilot.Application.AI.Orchestration;

public enum SemanticRoutingMode { Legacy, Shadow, Canary, SemanticPrimary }
public enum CapabilityExecutionStatus { Executed, ClarificationRequired, DisambiguationRequired, Unsupported, NoData, TemporarilyUnavailable, Failed }

public sealed record ValidatedQueryFrame(
    string CapabilityCode,
    int RegistryVersion,
    IReadOnlyCollection<ResolvedQuerySlot> Slots,
    QueryInterpretation Interpretation);

public sealed record QueryExecutionContext(
    Guid TenantId, Guid ActorId, Guid ConversationId, string CorrelationId, string ReplyLanguage);

public sealed record CapabilityExecutionResult(
    string CapabilityCode,
    int RegistryVersion,
    CapabilityExecutionStatus Status,
    string ReasonCode,
    object? Payload = null,
    IReadOnlyCollection<string>? Warnings = null);

public interface IConversationalCapabilityExecutor
{
    string CapabilityCode { get; }
    Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken);
}

public interface ISemanticCapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken);
}

public sealed class SemanticCapabilityDispatcher(
    IConversationalCapabilityRegistry registry,
    IEnumerable<IConversationalCapabilityExecutor> executors) : ISemanticCapabilityDispatcher
{
    private readonly IReadOnlyDictionary<string, IConversationalCapabilityExecutor> executors = executors.ToDictionary(item => item.CapabilityCode, StringComparer.Ordinal);

    public async Task<CapabilityExecutionResult> DispatchAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        if (registry.Find(frame.CapabilityCode) is not { Enabled: true } definition || definition.Version != frame.RegistryVersion)
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, DialogueOutcomeReasonCodes.CapabilityNotRecognized);
        if (!executors.TryGetValue(frame.CapabilityCode, out var executor))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, "executor_not_registered");
        try { return await executor.ExecuteAsync(frame, context, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout); }
        catch { return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure); }
    }
}

public sealed record SemanticRoutingOptions(IReadOnlyDictionary<string, SemanticRoutingMode>? Capabilities = null)
{
    public const string SectionName = "SemanticRouting";
    public SemanticRoutingMode ModeFor(string capabilityCode) => Capabilities?.TryGetValue(capabilityCode, out var mode) == true ? mode : SemanticRoutingMode.Legacy;
}

public sealed record SemanticRoutingComparison(string CapabilityCode, SemanticRoutingMode Mode, string LegacyRoute, string? SemanticRoute, bool Agreement, string CorrelationId);
public interface ISemanticRoutingTelemetrySink { void Record(SemanticRoutingComparison comparison); }
public sealed class NullSemanticRoutingTelemetrySink : ISemanticRoutingTelemetrySink { public void Record(SemanticRoutingComparison comparison) { } }

public sealed record SemanticRoutingDecision(string CapabilityCode, SemanticRoutingMode Mode, bool ExecuteSemanticRoute, bool RunShadowComparison);
public interface ISemanticRoutingRolloutCoordinator
{
    SemanticRoutingDecision Decide(string capabilityCode);
    void RecordShadowComparison(string capabilityCode, string legacyRoute, string? semanticRoute, string correlationId);
}

public sealed class SemanticRoutingRolloutCoordinator(
    SemanticRoutingOptions options,
    ISemanticRoutingTelemetrySink telemetrySink) : ISemanticRoutingRolloutCoordinator
{
    public SemanticRoutingDecision Decide(string capabilityCode)
    {
        var mode = options.ModeFor(capabilityCode);
        return new(capabilityCode, mode,
            ExecuteSemanticRoute: mode is SemanticRoutingMode.Canary or SemanticRoutingMode.SemanticPrimary,
            RunShadowComparison: mode == SemanticRoutingMode.Shadow);
    }

    public void RecordShadowComparison(string capabilityCode, string legacyRoute, string? semanticRoute, string correlationId)
    {
        var decision = Decide(capabilityCode);
        if (!decision.RunShadowComparison) return;
        telemetrySink.Record(new SemanticRoutingComparison(
            capabilityCode, decision.Mode, legacyRoute, semanticRoute,
            string.Equals(legacyRoute, semanticRoute, StringComparison.Ordinal), correlationId));
    }
}
