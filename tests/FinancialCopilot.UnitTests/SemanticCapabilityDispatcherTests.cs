using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.Evaluation;

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
    public void UndeclaredOrDuplicateSlots_CannotReachExecutor()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var executor = new Executor("monthly_activity_trend");
        var dispatcher = new SemanticCapabilityDispatcher(registry, [executor]);
        var frame = Frame("monthly_activity_trend");

        var undeclared = dispatcher.Validate(frame with
        {
            Slots = frame.Slots.Append(new ResolvedQuerySlot(
                QuerySlotType.AuditStatus, "true", QueryValueProvenance.UserExplicit,
                1m, QuerySlotValidationState.Valid)).ToArray()
        });
        var duplicate = dispatcher.Validate(frame with { Slots = frame.Slots.Concat(frame.Slots).ToArray() });

        Assert.Equal(CapabilityExecutionStatus.Unsupported, undeclared?.Status);
        Assert.Equal("unsupported_slot", undeclared?.ReasonCode);
        Assert.Equal(CapabilityExecutionStatus.Unsupported, duplicate?.Status);
        Assert.Equal(0, executor.Calls);
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

    [Fact]
    public void CanaryCohortsAreStableAndRollbackImmediatelyUsesLegacy()
    {
        var canary = new SemanticRoutingRolloutCoordinator(
            new(new Dictionary<string, SemanticRoutingMode> { ["symbol_metric_lookup"] = SemanticRoutingMode.Canary }, CanaryPercentage: 50),
            new RecordingTelemetry());
        Assert.Equal(
            canary.Decide("symbol_metric_lookup", "actor-1").ExecuteSemanticRoute,
            canary.Decide("symbol_metric_lookup", "actor-1").ExecuteSemanticRoute);

        var rollback = new SemanticRoutingRolloutCoordinator(
            new(new Dictionary<string, SemanticRoutingMode> { ["symbol_metric_lookup"] = SemanticRoutingMode.Rollback }),
            new RecordingTelemetry());
        Assert.False(rollback.Decide("symbol_metric_lookup", "actor-1").ExecuteSemanticRoute);
    }

    [Fact]
    public async Task SemanticCoordinator_ReservesAndFinalizesExactlyOnce()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var billing = new RecordingBilling();
        var coordinator = new SemanticExecutionCoordinator(
            new SemanticCapabilityDispatcher(registry, [new Executor("monthly_activity_trend")]), billing,
            new RecordingFeedback(), new RecordingEventSink());

        var operation = await coordinator.ExecuteAsync(
            Frame("monthly_activity_trend"), Context(),
            new AiQueryRequest("trend", Guid.NewGuid(), Guid.NewGuid(), "billing-once"), default);

        Assert.Equal(CapabilityExecutionStatus.Executed, operation.Execution.Status);
        Assert.Equal(1, billing.Reservations);
        Assert.Equal(1, billing.Finalizations);
        Assert.Equal(0, billing.Releases);
    }

    [Fact]
    public async Task InvalidFrame_DoesNotTouchBillingOrExecutor()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var billing = new RecordingBilling();
        var executor = new Executor("monthly_activity_trend");
        var feedback = new RecordingFeedback();
        var coordinator = new SemanticExecutionCoordinator(new SemanticCapabilityDispatcher(registry, [executor]), billing, feedback, new RecordingEventSink());
        var invalid = Frame("monthly_activity_trend") with { Slots = [] };

        var operation = await coordinator.ExecuteAsync(invalid, Context(), new AiQueryRequest("trend", Guid.NewGuid(), Guid.NewGuid(), "no-billing"), default);

        Assert.Equal(CapabilityExecutionStatus.ClarificationRequired, operation.Execution.Status);
        Assert.Equal(0, billing.Reservations);
        Assert.Equal(0, executor.Calls);
        Assert.Equal(1, feedback.Calls);
    }

    [Theory]
    [InlineData(QuerySlotValidationState.Ambiguous, null, DialogueOutcomeReasonCodes.EntityAmbiguous)]
    [InlineData(QuerySlotValidationState.Invalid, DialogueOutcomeReasonCodes.EntityNotFound, DialogueOutcomeReasonCodes.EntityNotFound)]
    public void EntityFailures_RemainDistinctFromMissingSlots(QuerySlotValidationState state, string? detail, string reason)
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var dispatcher = new SemanticCapabilityDispatcher(registry, [new Executor("monthly_activity_trend")]);
        var frame = Frame("monthly_activity_trend") with
        {
            Slots = [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, "unknown", QueryValueProvenance.UserExplicit, 0m, state, "monthly_activity_trend", detail)]
        };

        var result = dispatcher.Validate(frame);

        Assert.Equal(CapabilityExecutionStatus.DisambiguationRequired, result?.Status);
        Assert.Equal(reason, result?.ReasonCode);
    }

    private static ValidatedQueryFrame Frame(string capability) => new(
        capability,
        1,
        [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, "FOLD", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability)],
        new("", "", "en", [], [], [], null, null, null, [], [], 0, [], 1));
    private static QueryExecutionContext Context() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "en");
    private sealed class Executor(string code) : IConversationalCapabilityExecutor
    {
        public string CapabilityCode => code;
        public int Calls { get; private set; }
        public Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new CapabilityExecutionResult(code, 1, CapabilityExecutionStatus.Executed, "none"));
        }
    }
    private sealed class RecordingTelemetry : ISemanticRoutingTelemetrySink
    {
        public List<SemanticRoutingComparison> Items { get; } = [];
        public void Record(SemanticRoutingComparison comparison) => Items.Add(comparison);
    }
    private sealed class RecordingBilling : IBillingFacadeHook
    {
        public int Reservations { get; private set; }
        public int Finalizations { get; private set; }
        public int Releases { get; private set; }
        public Task<BillingReservationHandle?> TryReserveAsync(BillingReservationRequest request, CancellationToken cancellationToken)
        {
            Reservations++;
            return Task.FromResult<BillingReservationHandle?>(new("reservation", request.CorrelationId, Guid.NewGuid(), request.TenantId, request.ActorId, request.ApiClientId, request.ExternalUserId, request.OperationCode));
        }
        public Task<UsageAccountingResult?> FinalizeAsync(BillingReservationHandle handle, BillingFinalizationRequest request, CancellationToken cancellationToken)
        {
            Finalizations++;
            return Task.FromResult<UsageAccountingResult?>(new(handle.OperationCode, request.CompletionStatus, 1m, 9m, "test", false));
        }
        public Task ReleaseAsync(BillingReservationHandle handle, CancellationToken cancellationToken) { Releases++; return Task.CompletedTask; }
    }
    private sealed class RecordingFeedback : ISemanticOutcomeFeedbackCollector
    {
        public int Calls { get; private set; }
        public Task TryCollectAsync(AiQueryRequest request, ValidatedQueryFrame frame, CapabilityExecutionResult result, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
    private sealed class RecordingEventSink : ISemanticDialogueEventSink
    {
        private readonly List<SemanticDialogueEvent> events = [];
        public void Record(SemanticDialogueEvent semanticEvent) => events.Add(semanticEvent);
        public IReadOnlyCollection<SemanticDialogueEvent> Snapshot() => events;
    }
}
