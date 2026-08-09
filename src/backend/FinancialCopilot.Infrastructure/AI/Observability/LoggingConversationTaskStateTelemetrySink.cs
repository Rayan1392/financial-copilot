using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.Observability;

public sealed class LoggingConversationTaskStateTelemetrySink(
    ILogger<LoggingConversationTaskStateTelemetrySink> logger,
    ISemanticDialogueEventSink eventSink,
    IConversationalCapabilityRegistry registry,
    TimeProvider timeProvider) : IConversationTaskStateTelemetrySink
{
    public void Record(ConversationTaskStateTransition transition, ConversationTaskStateScope scope)
    {
        var lifecycle = transition.Kind switch
        {
            ConversationTaskStateTransitionKind.ClarificationRequested => "clarification_requested",
            ConversationTaskStateTransitionKind.ClarificationResolved => "clarification_resolved",
            ConversationTaskStateTransitionKind.ClarificationAbandoned or ConversationTaskStateTransitionKind.TaskSwitched => "clarification_abandoned",
            _ => "task_state_updated"
        };
        logger.LogInformation("Conversation dialogue lifecycle {Lifecycle}: conversation {ConversationId}, version {Version}, reason {ReasonCode}.", lifecycle, scope.ConversationId, transition.Current?.Version, transition.ReasonCode);

        var state = transition.Current ?? transition.Previous;
        if (state is null || string.IsNullOrWhiteSpace(state.LastCorrelationId)) return;
        var eventName = transition.Kind switch
        {
            ConversationTaskStateTransitionKind.ClarificationResolved => SemanticEventName.ClarificationResolved,
            ConversationTaskStateTransitionKind.ClarificationAbandoned or ConversationTaskStateTransitionKind.TaskSwitched => SemanticEventName.ClarificationAbandoned,
            _ => (SemanticEventName?)null
        };
        if (eventName is { } name)
            eventSink.Record(new SemanticDialogueEvent(
                name, state.LastCorrelationId, state.ActiveCapability, registry.Version,
                transition.ReasonCode, "conversation", timeProvider.GetUtcNow(), transition.Kind.ToString()));

        var reused = state.Slots
            .Where(slot => slot.Provenance == QueryValueProvenance.ConversationInferred)
            .Select(slot => slot.Type.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (reused.Length > 0)
            eventSink.Record(new SemanticDialogueEvent(
                SemanticEventName.ConversationSlotReused, state.LastCorrelationId,
                state.ActiveCapability, registry.Version, "conversation_slot_reused",
                "conversation", timeProvider.GetUtcNow(), string.Join(',', reused)));
    }
}
