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
    QueryInterpretation Interpretation,
    string? LegacyCapability = null,
    decimal? LegacyConfidence = null,
    string SemanticStatus = "Completed",
    string? SemanticProvider = null,
    string? SemanticModel = null);

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
    Guid? ApiClientId = null,
    int? MonthlyActivityTrendReportYear = null,
    int? MonthlyActivityTrendReportMonth = null,
    AiQueryRequest? Request = null);

public sealed record CapabilityExecutionResult(
    string CapabilityCode,
    int RegistryVersion,
    CapabilityExecutionStatus Status,
    string ReasonCode,
    object? Payload = null,
    IReadOnlyCollection<string>? Warnings = null);

/// <summary>
/// Observes the exact governed frame immediately before a capability executor is invoked.
/// This is diagnostic-only and never participates in routing or execution decisions.
/// </summary>
public interface ISemanticCapabilityExecutionObserver
{
    void BeforeExecute(string capabilityCode, ValidatedQueryFrame frame, QueryExecutionContext context);
}

public sealed class NullSemanticCapabilityExecutionObserver : ISemanticCapabilityExecutionObserver
{
    public void BeforeExecute(string capabilityCode, ValidatedQueryFrame frame, QueryExecutionContext context) { }
}

/// <summary>
/// Receives executor faults that the dispatcher converts into typed failure outcomes, so their cause
/// stays diagnosable. This is diagnostic-only and never changes the returned outcome.
/// </summary>
public interface ISemanticCapabilityFailureObserver
{
    void ExecutionFaulted(string capabilityCode, QueryExecutionContext context, Exception exception);
}

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
    IEnumerable<IConversationalCapabilityExecutor> executors,
    ISemanticCapabilityFailureObserver? failureObserver = null) : ISemanticCapabilityDispatcher
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
        if (frame.Slots.Any(slot => slot.ValidationState == QuerySlotValidationState.Ambiguous))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.DisambiguationRequired, DialogueOutcomeReasonCodes.EntityAmbiguous);
        if (frame.Slots.Any(slot => slot.ValidationState == QuerySlotValidationState.Invalid && slot.Detail == "different_industries"))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, "different_industries");
        if (frame.Slots.Any(slot => slot.ValidationState == QuerySlotValidationState.Invalid && slot.Detail == "invalid_industry_membership"))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, "invalid_industry_membership");
        if (requiredSlots.Any(slot => slot?.ValidationState == QuerySlotValidationState.Invalid && slot.Detail == DialogueOutcomeReasonCodes.EntityNotFound))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, DialogueOutcomeReasonCodes.EntityNotFound);
        if (requiredSlots.Any(slot => slot?.ValidationState == QuerySlotValidationState.Invalid && slot.Detail == "product_not_found"))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows);
        if (frame.Slots.Any(slot => slot.ValidationState == QuerySlotValidationState.Invalid))
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.ClarificationRequired, DialogueOutcomeReasonCodes.EntityNotFound);
        if (frame.Slots.Any(slot => slot.ValidationState == QuerySlotValidationState.Unsupported))
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
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            failureObserver?.ExecutionFaulted(frame.CapabilityCode, context, exception);
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.TemporarilyUnavailable, DialogueOutcomeReasonCodes.ProviderOrToolTimeout);
        }
        catch (Exception exception)
        {
            failureObserver?.ExecutionFaulted(frame.CapabilityCode, context, exception);
            return new(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Failed, DialogueOutcomeReasonCodes.ProviderOrToolFailure);
        }
    }

}

public sealed record SemanticOperationResult(
    CapabilityExecutionResult Execution,
    UsageAccountingResult? Usage,
    BillingReservationHandle? DeferredReservation = null);
public interface ISemanticExecutionCoordinator
{
    Task<SemanticOperationResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, AiQueryRequest request, CancellationToken cancellationToken);

    Task<SemanticOperationResult> ExecuteWithReservationAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        AiQueryRequest request,
        BillingReservationHandle? reservation,
        bool deferSettlementForFallback,
        CancellationToken cancellationToken) =>
        ExecuteAsync(frame, context, request, cancellationToken);
}

public sealed class SemanticExecutionCoordinator(
    ISemanticCapabilityDispatcher dispatcher,
    IBillingFacadeHook billingHook,
    ISemanticOutcomeFeedbackCollector feedbackCollector,
    ISemanticDialogueEventSink eventSink) : ISemanticExecutionCoordinator
{
    public async Task<SemanticOperationResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, AiQueryRequest request, CancellationToken cancellationToken)
        => await ExecuteCoreAsync(frame, context, request, reservation: null, reservationAlreadyMade: false,
            deferSettlementForFallback: false, cancellationToken);

    public async Task<SemanticOperationResult> ExecuteWithReservationAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        AiQueryRequest request,
        BillingReservationHandle? reservation,
        bool deferSettlementForFallback,
        CancellationToken cancellationToken) =>
        await ExecuteCoreAsync(frame, context, request, reservation, reservationAlreadyMade: true,
            deferSettlementForFallback, cancellationToken);

    private async Task<SemanticOperationResult> ExecuteCoreAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        AiQueryRequest request,
        BillingReservationHandle? reservation,
        bool reservationAlreadyMade,
        bool deferSettlementForFallback,
        CancellationToken cancellationToken)
    {
        if (dispatcher.Validate(frame) is { } invalid)
        {
            RecordOutcomeEvent(frame, context, invalid);
            await feedbackCollector.TryCollectAsync(request, frame, invalid, context.Now, CancellationToken.None);
            var invalidUsage = reservation is null
                ? null
                : await billingHook.FinalizeAsync(reservation,
                    new BillingFinalizationRequest("ValidationFailed", ExecutionRoute: $"semantic:{frame.CapabilityCode}"),
                    CancellationToken.None);
            return new(invalid, invalidUsage);
        }
        if (!reservationAlreadyMade)
        {
            reservation = await billingHook.TryReserveAsync(new BillingReservationRequest(
                request.CorrelationId, request.TenantId, request.ActorId, "AiQuery.Scanner",
                request.UserId, request.ApiClientId, request.ExternalUserId,
                BillingOperationId: request.BillingOperationId), cancellationToken);
        }
        try
        {
            var result = await dispatcher.DispatchAsync(frame, context, cancellationToken);
            RecordOutcomeEvent(frame, context, result);
            await feedbackCollector.TryCollectAsync(request, frame, result, context.Now, CancellationToken.None);
            if (reservation is null) return new(result, null);
            if (deferSettlementForFallback && CanFallback(result.Status))
                return new(result, null, reservation);
            var completion = SemanticBillingCompletionStatus.For(result.Status);
            var cached = result.Payload is SemanticScannerPayload scanner && scanner.Table.ExecutionFacts.FromCache;
            var usage = await billingHook.FinalizeAsync(reservation,
                new BillingFinalizationRequest(completion, cached, ExecutionRoute: $"semantic:{frame.CapabilityCode}"),
                cancellationToken);
            return new(result, usage);
        }
        catch (OperationCanceledException)
        {
            if (reservation is not null)
                await billingHook.FinalizeAsync(reservation,
                    new BillingFinalizationRequest("CancelledBeforeExecution", ExecutionRoute: $"semantic:{frame.CapabilityCode}"),
                    CancellationToken.None);
            throw;
        }
    }

    private static bool CanFallback(CapabilityExecutionStatus status) =>
        status is CapabilityExecutionStatus.ClarificationRequired
            or CapabilityExecutionStatus.DisambiguationRequired
            or CapabilityExecutionStatus.Unsupported
            or CapabilityExecutionStatus.NoData;

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
    int CanaryPercentage = 10,
    int SemanticInterpretationTimeoutMilliseconds = 5000,
    int SemanticInterpretationMaxOutputTokens = 384)
{
    public SemanticRoutingOptions() : this((IReadOnlyDictionary<string, SemanticRoutingMode>?)null, SemanticRoutingMode.SemanticPrimary, 10, 5000, 384) { }

    public const string SectionName = "SemanticRouting";
    public SemanticRoutingMode ModeFor(string capabilityCode) => Capabilities?.TryGetValue(capabilityCode, out var mode) == true ? mode : DefaultMode;
}

public sealed record SemanticRoutingComparison(
    string CapabilityCode,
    SemanticRoutingMode Mode,
    string LegacyRoute,
    string? SemanticRoute,
    bool Agreement,
    string CorrelationId,
    SemanticRoutingDisagreementCategory Category = SemanticRoutingDisagreementCategory.AMBIGUOUS,
    SemanticRoutingTelemetryContext? Context = null);
public interface ISemanticRoutingTelemetrySink { void Record(SemanticRoutingComparison comparison); }
public sealed class NullSemanticRoutingTelemetrySink : ISemanticRoutingTelemetrySink { public void Record(SemanticRoutingComparison comparison) { } }

public sealed record SemanticRoutingDecision(
    string CapabilityCode,
    SemanticRoutingMode Mode,
    bool ExecuteSemanticRoute,
    bool RunShadowComparison,
    bool? InCanaryCohort = null,
    int? CohortBucket = null);
public interface ISemanticRoutingRolloutCoordinator
{
    SemanticRoutingDecision Decide(string capabilityCode, string? cohortKey = null);
    void RecordShadowComparison(
        string capabilityCode,
        string legacyRoute,
        string? semanticRoute,
        string correlationId,
        SemanticRoutingTelemetryContext? context = null,
        string? cohortKey = null);
}

public sealed class SemanticRoutingRolloutCoordinator(
    SemanticRoutingOptions options,
    ISemanticRoutingTelemetrySink telemetrySink) : ISemanticRoutingRolloutCoordinator
{
    public SemanticRoutingDecision Decide(string capabilityCode, string? cohortKey = null)
    {
        var mode = options.ModeFor(capabilityCode);
        var cohortBucket = mode == SemanticRoutingMode.Canary && !string.IsNullOrWhiteSpace(cohortKey)
            ? CohortBucket(cohortKey)
            : (int?)null;
        var inCanaryCohort = mode == SemanticRoutingMode.Canary &&
            (string.IsNullOrWhiteSpace(cohortKey) || cohortBucket < Math.Clamp(options.CanaryPercentage, 0, 100));
        var rollback = mode is SemanticRoutingMode.Legacy or SemanticRoutingMode.Rollback;
        return new(capabilityCode, mode,
            ExecuteSemanticRoute: !rollback,
            RunShadowComparison: mode == SemanticRoutingMode.Shadow ||
                (mode == SemanticRoutingMode.Canary && !inCanaryCohort),
            InCanaryCohort: mode == SemanticRoutingMode.Canary ? inCanaryCohort : null,
            CohortBucket: cohortBucket);
    }

    public void RecordShadowComparison(
        string capabilityCode,
        string legacyRoute,
        string? semanticRoute,
        string correlationId,
        SemanticRoutingTelemetryContext? context = null,
        string? cohortKey = null)
    {
        var decision = Decide(capabilityCode, cohortKey);
        if (!decision.RunShadowComparison) return;
        var category = context?.Category ?? Categorize(legacyRoute, semanticRoute);
        context ??= new SemanticRoutingTelemetryContext(
            SemanticRoutingTelemetryFactory.QueryHash(correlationId),
            legacyRoute,
            null,
            semanticRoute,
            semanticRoute,
            null,
            [],
            semanticRoute,
            null,
            [],
            legacyRoute,
            decision.Mode,
            "unknown",
            1,
            SemanticArbitrationPolicy.Version,
            category);
        telemetrySink.Record(new SemanticRoutingComparison(
            capabilityCode, decision.Mode, legacyRoute, semanticRoute,
            string.Equals(legacyRoute, semanticRoute, StringComparison.Ordinal), correlationId, category,
            context with { RolloutMode = decision.Mode, Category = category }));
    }

    private static SemanticRoutingDisagreementCategory Categorize(string legacyRoute, string? semanticRoute) =>
        string.Equals(legacyRoute, semanticRoute, StringComparison.Ordinal)
            ? SemanticRoutingDisagreementCategory.AGREE
            : string.IsNullOrWhiteSpace(semanticRoute)
                ? SemanticRoutingDisagreementCategory.SEMANTIC_UNAVAILABLE
                : semanticRoute is "unknown" or "clarification"
                    ? SemanticRoutingDisagreementCategory.UNSUPPORTED
                    : SemanticRoutingDisagreementCategory.AMBIGUOUS;

    private static int CohortBucket(string cohortKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(cohortKey));
        return (int)(BitConverter.ToUInt32(hash, 0) % 100);
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
        DetectedIntent.FinancialStatementValueSearch => "financial_statement_value_search",
        DetectedIntent.Clarification => "clarification",
        _ => "unknown"
    };
}
