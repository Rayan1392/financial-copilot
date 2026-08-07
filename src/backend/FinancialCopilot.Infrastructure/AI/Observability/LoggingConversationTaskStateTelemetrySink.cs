using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.Observability;

public sealed class LoggingConversationTaskStateTelemetrySink(ILogger<LoggingConversationTaskStateTelemetrySink> logger) : IConversationTaskStateTelemetrySink
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
    }
}
