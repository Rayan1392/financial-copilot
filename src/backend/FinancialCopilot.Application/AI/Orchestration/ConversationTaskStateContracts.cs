namespace FinancialCopilot.Application.AI.Orchestration;

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
    ICapabilitySlotValidator slotValidator) : IConversationDialogueGate
{
    public async Task<ConversationDialogueGateResult> PrepareAsync(AiQueryRequest request, Guid conversationId, CancellationToken cancellationToken)
    {
        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var interpretation = interpreter.Interpret(request.Message);
        var capability = interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode;
        var entity = await entityResolver.ResolveFromInterpretationAsync(interpretation, cancellationToken);
        var slots = capability is null ? [] : slotValidator.Validate(capability, interpretation, entity).Slots
            .Where(slot => slot.ValidationState == QuerySlotValidationState.Valid && !string.IsNullOrWhiteSpace(slot.Value))
            .Select(slot => new ConversationTaskSlot(slot.Type, slot.Value!, entity is EntityResolutionResult.Resolved resolved && slot.Type == QuerySlotType.CompanyOrSymbol ? resolved.Entity.CanonicalId : null, slot.Provenance, slot.Confidence, null, 0)).ToArray();
        var transition = await stateService.ResolveFollowUpAsync(scope, capability, slots, Guid.Empty, request.CorrelationId, cancellationToken);
        var state = transition.Current;
        var effectiveSlots = state?.Slots ?? slots;
        var context = string.Join("; ", effectiveSlots.Select(slot => $"{slot.Type}={slot.Value}"));
        var effective = string.IsNullOrWhiteSpace(context) ? request.Message : $"{request.Message}\n[validated conversation context: {context}]";
        return new(request with { Message = effective, OriginalUserMessage = request.OriginalUserMessage ?? request.Message }, transition, effectiveSlots);
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
            var expected = interpretation.MissingSlots.Contains("symbol", StringComparer.Ordinal) ? QuerySlotType.CompanyOrSymbol : QuerySlotType.Metric;
            await stateService.RecordPendingAsync(scope, capability ?? active?.ActiveCapability, retainedSlots, new(PendingDialogueActionKind.Clarification, expected, [], clarificationReason ?? "clarification_required", Guid.Empty, 0), request.CorrelationId + ":outcome", cancellationToken);
            return;
        }
        await stateService.RecordAnsweredAsync(scope, capability ?? active?.ActiveCapability ?? "unknown", retainedSlots, Guid.Empty, request.CorrelationId + ":outcome", cancellationToken);
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
        var capability = requestedCapability ?? previous?.ActiveCapability;
        var slots = explicitSlots.ToDictionary(slot => slot.Type);
        if (!isSwitch && previous is not null)
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
