namespace FinancialCopilot.Application.AI.Orchestration;

using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.Authentication;
using System.Security.Cryptography;
using System.Text;

public enum SemanticRoutingMode { Legacy, Shadow, Canary, SemanticPrimary, Rollback }
public enum CapabilityExecutionStatus { Executed, Partial, ClarificationRequired, DisambiguationRequired, Unsupported, NoData, TemporarilyUnavailable, Failed }

public sealed record ValidatedQueryFrame(
    string CapabilityCode,
    int RegistryVersion,
    IReadOnlyCollection<ResolvedQuerySlot> Slots,
    QueryInterpretation Interpretation);

public sealed record QueryExecutionContext(
    Guid TenantId,
    Guid ActorId,
    Guid ConversationId,
    string CorrelationId,
    string ReplyLanguage,
    DateTimeOffset Now = default,
    int Page = 1,
    int PageSize = 20,
    string Channel = "web-ai",
    ActorType ActorType = ActorType.User,
    AuthenticationMode AuthenticationMode = AuthenticationMode.WebAppUser,
    Guid? UserId = null,
    Guid? ApiClientId = null);

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
    CapabilityExecutionResult? Validate(ValidatedQueryFrame frame);
    Task<CapabilityExecutionResult> DispatchAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken);
}

public sealed class SemanticCapabilityDispatcher(
    IConversationalCapabilityRegistry registry,
    IEnumerable<IConversationalCapabilityExecutor> executors) : ISemanticCapabilityDispatcher
{
    private readonly IReadOnlyDictionary<string, IConversationalCapabilityExecutor> executors = executors.ToDictionary(item => item.CapabilityCode, StringComparer.Ordinal);

    public CapabilityExecutionResult? Validate(ValidatedQueryFrame frame)
    {
        if (registry.Find(frame.CapabilityCode) is not { Enabled: true } definition || definition.Version != frame.RegistryVersion)
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, DialogueOutcomeReasonCodes.CapabilityNotRecognized);
        if (!executors.ContainsKey(frame.CapabilityCode))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, "executor_not_registered");

        var allowedSlots = definition.RequiredSlots.Concat(definition.OptionalSlots)
            .Select(slot => QuerySlotSchema.TryGetType(slot.Name, out var type) ? type : (QuerySlotType?)null)
            .Where(type => type.HasValue)
            .Select(type => type!.Value)
            .ToHashSet();
        if (frame.Slots.GroupBy(slot => slot.Type).Any(group => group.Count() > 1) ||
            frame.Slots.Any(slot => !allowedSlots.Contains(slot.Type) || slot.Value?.Length > 500))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, "unsupported_slot");

        var requiredSlots = definition.RequiredSlots
            .Select(required => frame.Slots.FirstOrDefault(slot => QuerySlotSchema.Name(slot.Type) == required.Name))
            .ToArray();
        if (requiredSlots.Any(slot => slot?.ValidationState == QuerySlotValidationState.Ambiguous))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.DisambiguationRequired, DialogueOutcomeReasonCodes.EntityAmbiguous);
        if (requiredSlots.Any(slot => slot?.ValidationState == QuerySlotValidationState.Invalid && slot.Detail == DialogueOutcomeReasonCodes.EntityNotFound))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.DisambiguationRequired, DialogueOutcomeReasonCodes.EntityNotFound);
        if (requiredSlots.Any(slot => slot?.ValidationState == QuerySlotValidationState.Unsupported))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Unsupported, "unsupported_slot");

        var missingRequired = definition.RequiredSlots.Any(required =>
            !frame.Slots.Any(slot => QuerySlotSchema.Name(slot.Type) == required.Name && slot.ValidationState == QuerySlotValidationState.Valid && !string.IsNullOrWhiteSpace(slot.Value)));
        return missingRequired
            ? new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, DialogueOutcomeReasonCodes.RequiredInputMissing)
            : null;
    }

    public async Task<CapabilityExecutionResult> DispatchAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
    {
        if (Validate(frame) is { } invalid) return invalid;
        var executor = executors[frame.CapabilityCode];
        try
        {
            return await executor.ExecuteAsync(frame, context, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout); }
        catch { return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure); }
    }

}

public sealed record SemanticOperationResult(CapabilityExecutionResult Execution, UsageAccountingResult? Usage);
public interface ISemanticExecutionCoordinator
{
    Task<SemanticOperationResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, AiQueryRequest request, CancellationToken cancellationToken);
}

public sealed class SemanticExecutionCoordinator(
    ISemanticCapabilityDispatcher dispatcher,
    IBillingFacadeHook billingHook,
    ISemanticOutcomeFeedbackCollector feedbackCollector,
    ISemanticDialogueEventSink eventSink) : ISemanticExecutionCoordinator
{
    public async Task<SemanticOperationResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, AiQueryRequest request, CancellationToken cancellationToken)
    {
        if (dispatcher.Validate(frame) is { } invalid)
        {
            RecordOutcomeEvent(frame, context, invalid);
            await feedbackCollector.TryCollectAsync(request, frame, invalid, context.Now, CancellationToken.None);
            return new(invalid, null);
        }
        var reservation = await billingHook.TryReserveAsync(new BillingReservationRequest(
            request.CorrelationId, request.TenantId, request.ActorId, "AiQuery.Scanner",
            request.UserId, request.ApiClientId, request.ExternalUserId), cancellationToken);
        try
        {
            var result = await dispatcher.DispatchAsync(frame, context, cancellationToken);
            RecordOutcomeEvent(frame, context, result);
            await feedbackCollector.TryCollectAsync(request, frame, result, context.Now, CancellationToken.None);
            if (reservation is null) return new(result, null);
            var completion = SemanticBillingCompletionStatus.For(result.Status);
            var cached = result.Payload is SemanticScannerPayload scanner && scanner.Table.ExecutionFacts.FromCache;
            var usage = await billingHook.FinalizeAsync(reservation, new BillingFinalizationRequest(completion, cached), cancellationToken);
            return new(result, usage);
        }
        catch (OperationCanceledException)
        {
            if (reservation is not null)
                await billingHook.FinalizeAsync(reservation, new BillingFinalizationRequest("CancelledBeforeExecution"), CancellationToken.None);
            throw;
        }
    }

    private void RecordOutcomeEvent(ValidatedQueryFrame frame, QueryExecutionContext context, CapabilityExecutionResult result)
    {
        var name = result.Status switch
        {
            CapabilityExecutionStatus.ClarificationRequired => SemanticEventName.ClarificationRequested,
            CapabilityExecutionStatus.DisambiguationRequired when result.ReasonCode == DialogueOutcomeReasonCodes.EntityAmbiguous => SemanticEventName.EntityAmbiguous,
            CapabilityExecutionStatus.DisambiguationRequired => SemanticEventName.EntityNotFound,
            CapabilityExecutionStatus.Unsupported => SemanticEventName.CapabilityNotRecognized,
            CapabilityExecutionStatus.NoData => SemanticEventName.SupportedButNoRows,
            CapabilityExecutionStatus.Partial => SemanticEventName.PartialAnswer,
            CapabilityExecutionStatus.TemporarilyUnavailable or CapabilityExecutionStatus.Failed => SemanticEventName.ProviderOrToolFailure,
            _ => SemanticEventName.ExecutionCompleted
        };
        eventSink.Record(new SemanticDialogueEvent(
            name, context.CorrelationId, frame.CapabilityCode, frame.RegistryVersion,
            result.ReasonCode, context.Channel, context.Now, result.Status.ToString()));
    }
}

public static class SemanticBillingCompletionStatus
{
    public static string For(CapabilityExecutionStatus status) => status switch
    {
        CapabilityExecutionStatus.Executed or CapabilityExecutionStatus.Partial or CapabilityExecutionStatus.NoData => "Completed",
        CapabilityExecutionStatus.ClarificationRequired or CapabilityExecutionStatus.DisambiguationRequired => "ClarificationRequired",
        CapabilityExecutionStatus.Unsupported => "ValidationFailed",
        CapabilityExecutionStatus.TemporarilyUnavailable or CapabilityExecutionStatus.Failed => "ProviderFailed",
        _ => "ProviderFailed"
    };
}

public sealed record SemanticRoutingOptions(
    IReadOnlyDictionary<string, SemanticRoutingMode>? Capabilities = null,
    SemanticRoutingMode DefaultMode = SemanticRoutingMode.SemanticPrimary,
    int CanaryPercentage = 10)
{
    public SemanticRoutingOptions() : this((IReadOnlyDictionary<string, SemanticRoutingMode>?)null, SemanticRoutingMode.SemanticPrimary, 10) { }

    public const string SectionName = "SemanticRouting";
    public SemanticRoutingMode ModeFor(string capabilityCode) => Capabilities?.TryGetValue(capabilityCode, out var mode) == true ? mode : DefaultMode;
}

public sealed record SemanticRoutingComparison(string CapabilityCode, SemanticRoutingMode Mode, string LegacyRoute, string? SemanticRoute, bool Agreement, string CorrelationId);
public interface ISemanticRoutingTelemetrySink { void Record(SemanticRoutingComparison comparison); }
public sealed class NullSemanticRoutingTelemetrySink : ISemanticRoutingTelemetrySink { public void Record(SemanticRoutingComparison comparison) { } }

public sealed record SemanticRoutingDecision(string CapabilityCode, SemanticRoutingMode Mode, bool ExecuteSemanticRoute, bool RunShadowComparison);
public interface ISemanticRoutingRolloutCoordinator
{
    SemanticRoutingDecision Decide(string capabilityCode, string? cohortKey = null);
    void RecordShadowComparison(string capabilityCode, string legacyRoute, string? semanticRoute, string correlationId);
}

public sealed class SemanticRoutingRolloutCoordinator(
    SemanticRoutingOptions options,
    ISemanticRoutingTelemetrySink telemetrySink) : ISemanticRoutingRolloutCoordinator
{
    public SemanticRoutingDecision Decide(string capabilityCode, string? cohortKey = null)
    {
        var mode = options.ModeFor(capabilityCode);
        var canaryEnabled = mode == SemanticRoutingMode.Canary &&
            (string.IsNullOrWhiteSpace(cohortKey) || InCanaryCohort(cohortKey, options.CanaryPercentage));
        return new(capabilityCode, mode,
            ExecuteSemanticRoute: mode == SemanticRoutingMode.SemanticPrimary || canaryEnabled,
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

    private static bool InCanaryCohort(string cohortKey, int percentage)
    {
        var boundedPercentage = Math.Clamp(percentage, 0, 100);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cohortKey));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100;
        return bucket < boundedPercentage;
    }
}

public static class SemanticRouteMapping
{
    public static string FromIntent(DetectedIntent intent) => intent switch
    {
        DetectedIntent.Scanner => "stock_screening",
        DetectedIntent.SymbolLookup => "symbol_metric_lookup",
        DetectedIntent.ComprehensiveAnalysis => "comprehensive_analysis",
        DetectedIntent.MonthlyActivityTrend => "monthly_activity_trend",
        DetectedIntent.ProductRevenueMix => "product_revenue_mix",
        DetectedIntent.FinancialStatementTableLookup => "financial_statement_table",
        DetectedIntent.FinancialStatementPeriodAnalysis => "financial_statement_period_analysis",
        DetectedIntent.DisclosureListing => "disclosure_listing",
        DetectedIntent.MonthlySalesQualityRanking => "monthly_sales_quality_ranking",
        DetectedIntent.PsGaugeVisualization => "ps_gauge_visualization",
        DetectedIntent.PersonalizedInsightExplanation => "personalized_insight_explanation",
        DetectedIntent.Clarification => "clarification",
        _ => "unknown"
    };
}
