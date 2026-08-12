namespace FinancialCopilot.Application.AI.Orchestration;

using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.Conversations;

public enum PendingDialogueActionKind { Clarification, Disambiguation }

public enum ConversationTaskStateTransitionKind { Answered, ClarificationRequested, ClarificationResolved, ClarificationAbandoned, TaskSwitched, Expired, Replay }

public sealed record ConversationTaskSlot(
    QuerySlotType Type,
    string Value,
    Guid? CanonicalEntityId,
    QueryValueProvenance Provenance,
    decimal Confidence,
    Guid? OriginatingMessageId,
    long OriginatingStateVersion);

public sealed record PendingDialogueAction(
    PendingDialogueActionKind Kind,
    QuerySlotType ExpectedSlot,
    IReadOnlyList<ConversationTaskSlot> Candidates,
    string ReasonCode,
    Guid OriginatingMessageId,
    long OriginatingStateVersion);

public sealed record ConversationTaskState(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    long Version,
    string? ActiveCapability,
    IReadOnlyList<ConversationTaskSlot> Slots,
    PendingDialogueAction? PendingAction,
    int TurnCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    string? LastCorrelationId = null)
{
    public ConversationTaskSlot? FindSlot(QuerySlotType type) =>
        Slots.FirstOrDefault(slot => slot.Type == type);
}

public sealed record ConversationTaskStateScope(Guid ConversationId, Guid TenantId, Guid ActorId);

public sealed record ConversationTaskStateWriteResult(bool Succeeded, ConversationTaskState? State, bool IsReplay = false);

public interface IConversationTaskStateRepository
{
    Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
    Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken);
    Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
}

public sealed record ConversationTaskStateOptions(
    int ExpiryMinutes = 20,
    int MaximumTurns = 6,
    decimal MinimumCarryOverConfidence = 0.85m)
{
    public ConversationTaskStateOptions() : this(20, 6, 0.85m) { }

    public const string SectionName = "ConversationTaskState";

    public void Validate()
    {
        if (ExpiryMinutes is < 1 or > 240) throw new InvalidOperationException("Conversation task-state expiry must be between 1 and 240 minutes.");
        if (MaximumTurns is < 1 or > 30) throw new InvalidOperationException("Conversation task-state maximum turns must be between 1 and 30.");
        if (MinimumCarryOverConfidence is < 0m or > 1m) throw new InvalidOperationException("Conversation task-state confidence must be between zero and one.");
    }
}

public sealed record ConversationTaskStateTransition(
    ConversationTaskStateTransitionKind Kind,
    ConversationTaskState? Previous,
    ConversationTaskState? Current,
    string ReasonCode);

public interface IConversationTaskStateService
{
    Task<ConversationTaskState?> GetActiveAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> RecordAnsweredAsync(ConversationTaskStateScope scope, string capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, Guid messageId, string correlationId, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> RecordPendingAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, PendingDialogueAction pendingAction, string correlationId, CancellationToken cancellationToken);
    Task<ConversationTaskStateTransition> ResolveFollowUpAsync(ConversationTaskStateScope scope, string? requestedCapability, IReadOnlyCollection<ConversationTaskSlot> explicitSlots, Guid messageId, string correlationId, CancellationToken cancellationToken);
}

public interface IConversationTaskStateTelemetrySink
{
    void Record(ConversationTaskStateTransition transition, ConversationTaskStateScope scope);
}

public sealed record ConversationDialogueGateResult(AiQueryRequest Request, ConversationTaskStateTransition? Transition, IReadOnlyCollection<ConversationTaskSlot> Slots);
public interface IConversationDialogueGate
{
    Task<ConversationDialogueGateResult> PrepareAsync(AiQueryRequest request, Guid conversationId, CancellationToken cancellationToken);
    Task RecordOutcomeAsync(AiQueryRequest request, Guid conversationId, bool clarificationRequired, string? clarificationReason, CancellationToken cancellationToken);
}

public sealed class ConversationDialogueGate(
    IConversationTaskStateService stateService,
    ICapabilityInterpreter interpreter,
    ICanonicalQueryEntityResolver entityResolver,
    ICapabilitySlotValidator slotValidator,
    IDirectMetricRoutingRegistry directMetricRegistry,
    ISemanticRoutingRolloutCoordinator rolloutCoordinator,
    IMessageRepository messageRepository,
    TimeProvider timeProvider,
    ISemanticDialogueEventSink? eventSink = null,
    ISemanticQueryFrameEnricher? frameEnricher = null,
    IIndustryRelativeValuationSemanticResolver? industryRelativeValuationResolver = null) : IConversationDialogueGate
{
    public async Task<ConversationDialogueGateResult> PrepareAsync(AiQueryRequest request, Guid conversationId, CancellationToken cancellationToken)
    {
        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var interpretation = interpreter.Interpret(request.Message);
        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        var detectedCapability = request.Context?.InsightEventId is not null
            ? "personalized_insight_explanation"
            : interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        var capability = detectedCapability ?? (active?.PendingAction is not null ? active.ActiveCapability : null);
        if (!string.IsNullOrWhiteSpace(request.SuggestedActionId) && request.SuggestedActionId.Length <= 160)
        {
            var selected = await FindSelectedActionAsync(conversationId, request.SuggestedActionId, cancellationToken);
            var selection = selected.GetValueOrDefault();
            var expired = selected is null ||
                !string.Equals(selection.Action.Message.Trim(), request.Message.Trim(), StringComparison.Ordinal) ||
                timeProvider.GetUtcNow() - selection.CreatedAt > TimeSpan.FromMinutes(30);
            eventSink?.Record(new SemanticDialogueEvent(
                expired ? SemanticEventName.SuggestionExpired : SemanticEventName.SuggestionSelected,
                request.CorrelationId,
                selected?.Action.CapabilityCode ?? capability,
                selected?.Action.RegistryVersion ?? interpretation.RegistryVersion,
                expired ? "suggested_action_expired_or_invalid" : "suggested_action_selected",
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                timeProvider.GetUtcNow(),
                request.SuggestedActionId));
            if (expired) request = request with { SuggestedActionId = null };
        }
        eventSink?.Record(new SemanticDialogueEvent(
            SemanticEventName.InterpretationCompleted,
            request.CorrelationId,
            capability,
            interpretation.RegistryVersion,
            capability is null ? DialogueOutcomeReasonCodes.CapabilityNotRecognized : DialogueOutcomeReasonCodes.None,
            request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
            timeProvider.GetUtcNow()));
        var entity = await entityResolver.ResolveFromInterpretationAsync(interpretation, cancellationToken);
        var entityEvent = entity switch
        {
            EntityResolutionResult.Resolved => SemanticEventName.EntityResolved,
            EntityResolutionResult.Ambiguous => SemanticEventName.EntityAmbiguous,
            EntityResolutionResult.NotFound => SemanticEventName.EntityNotFound,
            _ => (SemanticEventName?)null
        };
        if (entityEvent is { } eventName)
            eventSink?.Record(new SemanticDialogueEvent(eventName, request.CorrelationId, capability, interpretation.RegistryVersion,
                entity is EntityResolutionResult.Ambiguous ? DialogueOutcomeReasonCodes.EntityAmbiguous : entity is EntityResolutionResult.NotFound ? DialogueOutcomeReasonCodes.EntityNotFound : DialogueOutcomeReasonCodes.None,
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai", timeProvider.GetUtcNow()));
        var validatedFrameSlots = capability is null
            ? Array.Empty<ResolvedQuerySlot>()
            : slotValidator.Validate(capability, interpretation, entity).Slots.ToArray();
        if (request.Context?.InsightEventId is Guid insightEventId && capability == "personalized_insight_explanation")
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type != QuerySlotType.Insight)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Insight,
                    insightEventId.ToString("D"),
                    QueryValueProvenance.UserExplicit,
                    1m,
                    QuerySlotValidationState.Valid,
                    capability))
                .ToArray();
        if (capability is not null && frameEnricher is not null)
            validatedFrameSlots = frameEnricher.Enrich(
                capability,
                interpretation,
                validatedFrameSlots,
                timeProvider.GetUtcNow()).ToArray();
        IndustryRelativeValuationResolution? relativeResolution = null;
        PendingDialogueAction? relativePendingAction = null;
        if (industryRelativeValuationResolver is not null && capability is not null && capability.Contains("relative_valuation", StringComparison.Ordinal))
        {
            relativeResolution = await industryRelativeValuationResolver.ResolveAsync(capability, interpretation, cancellationToken);
            if (relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Resolved)
            {
                validatedFrameSlots = validatedFrameSlots
                    .Where(slot => slot.Type is not QuerySlotType.Industry and not QuerySlotType.CompanyOrSymbol and not QuerySlotType.CompaniesOrSymbols)
                    .Append(new ResolvedQuerySlot(QuerySlotType.Industry, relativeResolution.IndustryId!.Value.ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, relativeResolution.IndustryName))
                    .Concat(relativeResolution.CompanyIds is { Count: 1 } ? [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, relativeResolution.CompanyIds[0].ToString("D"), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, relativeResolution.Symbols?.FirstOrDefault())] : [])
                    .Concat(relativeResolution.CompanyIds is { Count: > 1 } ? [new ResolvedQuerySlot(QuerySlotType.CompaniesOrSymbols, string.Join(',', relativeResolution.CompanyIds), QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid, capability, string.Join(',', relativeResolution.Symbols ?? []))] : [])
                    .ToArray();
            }
            else
            {
                var companyIssue = relativeResolution.Status is IndustryRelativeValuationResolutionStatus.DifferentIndustries or IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership;
                var expected = companyIssue
                    ? QuerySlotType.CompaniesOrSymbols
                    : relativeResolution.Detail == "Industry"
                        ? QuerySlotType.Industry
                        : QuerySlotType.CompanyOrSymbol;
                var validation = relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Ambiguous
                    ? QuerySlotValidationState.Ambiguous
                    : relativeResolution.Status == IndustryRelativeValuationResolutionStatus.Missing
                        ? QuerySlotValidationState.Missing
                        : QuerySlotValidationState.Invalid;
                var reason = relativeResolution.Status switch
                {
                    IndustryRelativeValuationResolutionStatus.Ambiguous => DialogueOutcomeReasonCodes.EntityAmbiguous,
                    IndustryRelativeValuationResolutionStatus.NotFound => DialogueOutcomeReasonCodes.EntityNotFound,
                    IndustryRelativeValuationResolutionStatus.DifferentIndustries => DialogueOutcomeReasonCodes.DifferentIndustries,
                    IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership => DialogueOutcomeReasonCodes.InvalidIndustryMembership,
                    _ => DialogueOutcomeReasonCodes.RequiredInputMissing
                };
                validatedFrameSlots = validatedFrameSlots
                    .Where(slot => slot.Type != expected)
                    .Append(new ResolvedQuerySlot(expected, null, QueryValueProvenance.UserExplicit, 0m, validation, capability, reason))
                    .ToArray();
                var candidateSlots = (relativeResolution.CandidateIds ?? [])
                    .Select((id, index) => new ConversationTaskSlot(
                        expected,
                        id.ToString("D"),
                        id,
                        QueryValueProvenance.UserExplicit,
                        1m,
                        Guid.Empty,
                        0))
                    .ToArray();
                relativePendingAction = new(
                    validation == QuerySlotValidationState.Ambiguous ? PendingDialogueActionKind.Disambiguation : PendingDialogueActionKind.Clarification,
                    expected,
                    candidateSlots,
                    reason,
                    Guid.Empty,
                    0);
            }
        }
        var resolvedEntities = capability == "symbol_metric_lookup"
            ? await entityResolver.ResolveAllFromInterpretationAsync(interpretation, cancellationToken)
            : [];
        if (resolvedEntities.Count > 1)
        {
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type != QuerySlotType.CompaniesOrSymbols)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.CompaniesOrSymbols,
                    string.Join(',', resolvedEntities.Select(item => item.Entity.DisplaySymbol)),
                    QueryValueProvenance.UserExplicit,
                    resolvedEntities.Min(item => item.Evidence.Confidence),
                    QuerySlotValidationState.Valid,
                    capability))
                .ToArray();
        }
        var slots = validatedFrameSlots
            .Where(slot => slot.ValidationState == QuerySlotValidationState.Valid && !string.IsNullOrWhiteSpace(slot.Value))
            .Select(slot => new ConversationTaskSlot(slot.Type, slot.Value!, entity is EntityResolutionResult.Resolved resolved && slot.Type == QuerySlotType.CompanyOrSymbol ? resolved.Entity.CanonicalId : null, slot.Provenance, slot.Confidence, null, 0)).ToArray();
        var governedMetrics = capability == "symbol_metric_lookup"
            ? directMetricRegistry.ResolveAll(
                request.Message,
                DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime))
            : [];
        if (capability == "symbol_metric_lookup" && governedMetrics.Count > 0)
        {
            var governedMetric = governedMetrics.FirstOrDefault();
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Metric,
                    governedMetric?.MetricCode.Value,
                    QueryValueProvenance.UserExplicit,
                    governedMetric is null ? 0m : 1m,
                    governedMetric is null ? QuerySlotValidationState.Invalid : QuerySlotValidationState.Valid,
                    capability,
                    governedMetric is null ? "metric_not_resolved" : null))
                .Concat(governedMetrics.Count > 1
                    ? new[]
                    {
                        new ResolvedQuerySlot(
                            QuerySlotType.Metrics,
                            string.Join(',', governedMetrics.Select(item => item.MetricCode.Value)),
                            QueryValueProvenance.UserExplicit,
                            1m,
                            QuerySlotValidationState.Valid,
                            capability)
                    }
                    : [])
                .Concat(governedMetric?.PeriodSelector is null
                    ? []
                    : new[]
                    {
                        new ResolvedQuerySlot(
                            QuerySlotType.Period,
                            governedMetric.PeriodSelector.Value.ToString(),
                            QueryValueProvenance.UserExplicit,
                            1m,
                            QuerySlotValidationState.Valid,
                            capability)
                    })
                .ToArray();
            slots = slots.Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Concat(governedMetric is null
                    ? []
                    : new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Metric,
                            governedMetric.MetricCode.Value,
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    })
                .Concat(governedMetrics.Count > 1
                    ? new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Metrics,
                            string.Join(',', governedMetrics.Select(item => item.MetricCode.Value)),
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    }
                    : [])
                .Concat(governedMetric?.PeriodSelector is null
                    ? []
                    : new[]
                    {
                        new ConversationTaskSlot(
                            QuerySlotType.Period,
                            governedMetric.PeriodSelector.Value.ToString(),
                            null,
                            QueryValueProvenance.UserExplicit,
                            1m,
                            null,
                            0)
                    })
                .ToArray();
        }
        else if (detectedCapability == "symbol_metric_lookup" && interpretation.Metrics.Count > 0)
        {
            validatedFrameSlots = validatedFrameSlots
                .Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period)
                .Append(new ResolvedQuerySlot(
                    QuerySlotType.Metric,
                    null,
                    QueryValueProvenance.UserExplicit,
                    0m,
                    QuerySlotValidationState.Invalid,
                    capability,
                    "metric_not_resolved"))
                .ToArray();
            slots = slots.Where(slot => slot.Type is not QuerySlotType.Metric and not QuerySlotType.Metrics and not QuerySlotType.Period).ToArray();
        }
        var hasExplicitUnresolvedEntity = relativePendingAction is not null || validatedFrameSlots.Any(slot =>
            (slot.Type is QuerySlotType.CompanyOrSymbol or QuerySlotType.CompaniesOrSymbols or QuerySlotType.Industry) &&
            slot.ValidationState is QuerySlotValidationState.Ambiguous or QuerySlotValidationState.Invalid or QuerySlotValidationState.Missing);
        ConversationTaskStateTransition? transition;
        if (relativePendingAction is not null)
        {
            // First let Feature 120 apply task-switch semantics, then persist the complete
            // Feature-125 pending action and its canonical candidates.
            var switched = await stateService.ResolveFollowUpAsync(scope, capability, slots, Guid.Empty, request.CorrelationId + ":switch", cancellationToken);
            transition = await stateService.RecordPendingAsync(
                scope,
                capability,
                switched.Current?.Slots ?? slots,
                relativePendingAction,
                request.CorrelationId + ":pending",
                cancellationToken);
        }
        else
        {
            transition = hasExplicitUnresolvedEntity
                ? null
                : await stateService.ResolveFollowUpAsync(scope, capability, slots, Guid.Empty, request.CorrelationId, cancellationToken);
        }
        var state = transition?.Current ?? active;
        var effectiveSlots = state?.Slots ?? slots;
        var effectiveCapability = state?.ActiveCapability ?? capability;
        var frameSlotsByType = validatedFrameSlots.ToDictionary(slot => slot.Type);
        foreach (var slot in effectiveSlots)
        {
            if (frameSlotsByType.TryGetValue(slot.Type, out var current) &&
                current.ValidationState is QuerySlotValidationState.Ambiguous or QuerySlotValidationState.Invalid or QuerySlotValidationState.Unsupported)
                continue;
            frameSlotsByType[slot.Type] = new ResolvedQuerySlot(
                slot.Type,
                slot.Value,
                slot.Provenance,
                slot.Confidence,
                QuerySlotValidationState.Valid,
                effectiveCapability);
        }
        var frame = effectiveCapability is null
            ? null
            : new ValidatedQueryFrame(effectiveCapability, interpretation.RegistryVersion, frameSlotsByType.Values.ToArray(), interpretation);
        var routing = frame is null ? null : rolloutCoordinator.Decide(frame.CapabilityCode, request.ActorId.ToString("N"));
        return new(
            request with
            {
                OriginalUserMessage = request.OriginalUserMessage ?? request.Message,
                SemanticFrame = routing?.ExecuteSemanticRoute == true ? frame : null,
                SemanticShadowFrame = routing?.ExecuteSemanticRoute == false ? frame : null
            },
            transition,
            effectiveSlots);
    }

    private async Task<(SuggestedAction Action, DateTimeOffset CreatedAt)?> FindSelectedActionAsync(
        Guid conversationId,
        string actionId,
        CancellationToken cancellationToken)
    {
        var messages = await messageRepository.ListByConversationAsync(conversationId, cancellationToken);
        return messages
            .Where(message => message.Role == MessageRole.Assistant && message.AssistantPayload?.SuggestedActions is { Count: > 0 })
            .OrderByDescending(message => message.CreatedAt)
            .SelectMany(message => message.AssistantPayload!.SuggestedActions!
                .Where(action => string.Equals(action.Id, actionId, StringComparison.Ordinal))
                .Select(action => ((SuggestedAction Action, DateTimeOffset CreatedAt)?)(action, message.CreatedAt)))
            .FirstOrDefault();
    }

    public async Task RecordOutcomeAsync(AiQueryRequest request, Guid conversationId, bool clarificationRequired, string? clarificationReason, CancellationToken cancellationToken)
    {
        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var interpretation = interpreter.Interpret(request.OriginalUserMessage ?? request.Message);
        var capability = interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        var active = await stateService.GetActiveAsync(scope, cancellationToken);
        var retainedSlots = active?.Slots ?? [];
        if (clarificationRequired)
        {
            // Feature-specific adapters may already have persisted a candidate-bearing
            // Feature 120 pending action. Do not overwrite it with the legacy empty action.
            if (active?.PendingAction is not null &&
                string.Equals(active.PendingAction.ReasonCode, clarificationReason, StringComparison.Ordinal))
                return;
            var entityReason = clarificationReason is DialogueOutcomeReasonCodes.EntityAmbiguous or DialogueOutcomeReasonCodes.EntityNotFound;
            var expected = entityReason || interpretation.MissingSlots.Contains("symbol", StringComparer.Ordinal)
                ? QuerySlotType.CompanyOrSymbol
                : QuerySlotType.Metric;
            var kind = entityReason ? PendingDialogueActionKind.Disambiguation : PendingDialogueActionKind.Clarification;
            await stateService.RecordPendingAsync(scope, capability ?? active?.ActiveCapability, retainedSlots, new(kind, expected, [], clarificationReason ?? "clarification_required", Guid.Empty, 0), request.CorrelationId + ":outcome", cancellationToken);
            return;
        }
        await stateService.RecordAnsweredAsync(scope, capability ?? active?.ActiveCapability ?? "unknown", retainedSlots, Guid.Empty, request.CorrelationId + ":outcome", cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.SuggestedActionId) && request.SuggestedActionId.Length <= 160)
            eventSink?.Record(new SemanticDialogueEvent(
                SemanticEventName.SuggestionResolved,
                request.CorrelationId,
                capability ?? active?.ActiveCapability,
                interpretation.RegistryVersion,
                "suggested_action_resolved",
                request.ExternalUserId?.StartsWith("telegram:", StringComparison.Ordinal) == true ? "telegram" : "web-ai",
                timeProvider.GetUtcNow(),
                request.SuggestedActionId));
    }
}

public sealed class NullConversationTaskStateTelemetrySink : IConversationTaskStateTelemetrySink
{
    public void Record(ConversationTaskStateTransition transition, ConversationTaskStateScope scope) { }
}

public sealed class ConversationTaskStateService(
    IConversationTaskStateRepository repository,
    TimeProvider timeProvider,
    ConversationTaskStateOptions options,
    IConversationTaskStateTelemetrySink? telemetrySink = null) : IConversationTaskStateService
{
    public async Task<ConversationTaskState?> GetActiveAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
    {
        var state = await repository.FindAsync(scope, cancellationToken);
        if (state is null) return null;
        var now = timeProvider.GetUtcNow();
        if (state.ExpiresAt > now && state.TurnCount < options.MaximumTurns) return state;
        await repository.DeleteAsync(scope, cancellationToken);
        telemetrySink?.Record(new(ConversationTaskStateTransitionKind.Expired, state, null, "state_expired"), scope);
        return null;
    }

    public Task<ConversationTaskStateTransition> RecordAnsweredAsync(ConversationTaskStateScope scope, string capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, Guid messageId, string correlationId, CancellationToken cancellationToken) =>
        WriteAsync(scope, capabilityCode, validatedSlots, null, messageId, correlationId, ConversationTaskStateTransitionKind.Answered, cancellationToken);

    public Task<ConversationTaskStateTransition> RecordPendingAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> validatedSlots, PendingDialogueAction pendingAction, string correlationId, CancellationToken cancellationToken) =>
        WriteAsync(scope, capabilityCode, validatedSlots, pendingAction, pendingAction.OriginatingMessageId, correlationId, ConversationTaskStateTransitionKind.ClarificationRequested, cancellationToken);

    public async Task<ConversationTaskStateTransition> ResolveFollowUpAsync(ConversationTaskStateScope scope, string? requestedCapability, IReadOnlyCollection<ConversationTaskSlot> explicitSlots, Guid messageId, string correlationId, CancellationToken cancellationToken)
    {
        var previous = await GetActiveAsync(scope, cancellationToken);
        if (previous?.LastCorrelationId == correlationId)
            return new(ConversationTaskStateTransitionKind.Replay, previous, previous, "correlation_replay");

        var isSwitch = !string.IsNullOrWhiteSpace(requestedCapability) &&
            !string.Equals(requestedCapability, previous?.ActiveCapability, StringComparison.Ordinal);
        var compatibleRefinement = isSwitch &&
            string.Equals(previous?.ActiveCapability, "symbol_metric_lookup", StringComparison.Ordinal) &&
            string.Equals(requestedCapability, "monthly_activity_trend", StringComparison.Ordinal) &&
            explicitSlots.Any(slot => slot.Type == QuerySlotType.Presentation);
        var capability = requestedCapability ?? previous?.ActiveCapability;
        var slots = explicitSlots.ToDictionary(slot => slot.Type);
        if ((!isSwitch || compatibleRefinement) && previous is not null)
        {
            foreach (var slot in previous.Slots.Where(slot => slot.Confidence >= options.MinimumCarryOverConfidence))
            {
                if (!slots.ContainsKey(slot.Type))
                    slots[slot.Type] = slot with { Provenance = QueryValueProvenance.ConversationInferred, OriginatingStateVersion = previous.Version };
            }
        }

        var pending = previous?.PendingAction;
        if (pending is not null && !isSwitch && slots.ContainsKey(pending.ExpectedSlot))
            pending = null;
        var kind = isSwitch ? ConversationTaskStateTransitionKind.TaskSwitched : pending is null && previous?.PendingAction is not null
            ? ConversationTaskStateTransitionKind.ClarificationResolved : ConversationTaskStateTransitionKind.Answered;
        return await WriteAsync(scope, capability, slots.Values.ToArray(), pending, messageId, correlationId, kind, cancellationToken, previous);
    }

    private async Task<ConversationTaskStateTransition> WriteAsync(ConversationTaskStateScope scope, string? capabilityCode, IReadOnlyCollection<ConversationTaskSlot> slots, PendingDialogueAction? pending, Guid messageId, string correlationId, ConversationTaskStateTransitionKind kind, CancellationToken cancellationToken, ConversationTaskState? knownPrevious = null)
    {
        options.Validate();
        var previous = knownPrevious ?? await GetActiveAsync(scope, cancellationToken);
        if (previous?.LastCorrelationId == correlationId)
            return new(ConversationTaskStateTransitionKind.Replay, previous, previous, "correlation_replay");
        var now = timeProvider.GetUtcNow();
        var current = new ConversationTaskState(scope.ConversationId, scope.TenantId, scope.ActorId, (previous?.Version ?? 0) + 1, capabilityCode, slots.ToArray(), pending, (previous?.TurnCount ?? 0) + 1, now, now.AddMinutes(options.ExpiryMinutes), correlationId);
        var written = await repository.TryWriteAsync(current, previous?.Version, cancellationToken);
        if (!written.Succeeded) throw new InvalidOperationException("Conversation task state changed concurrently; retry with the latest version.");
        var transition = new ConversationTaskStateTransition(kind, previous, written.State!, pending?.ReasonCode ?? "state_updated");
        telemetrySink?.Record(transition, scope);
        return transition;
    }
}
