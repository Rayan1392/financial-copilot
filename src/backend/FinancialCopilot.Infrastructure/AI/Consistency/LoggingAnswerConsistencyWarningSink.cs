using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.Consistency;

// Emits a structured warning whenever the consistency validator replaces an inconsistent numeric
// prose answer. The warning carries correlation/conversation ids, the symbol/metric, both the
// conflicting prose value and the authoritative table value, and the orchestration mode/version.
public sealed class LoggingAnswerConsistencyWarningSink(
    ILogger<LoggingAnswerConsistencyWarningSink> logger) : IAnswerConsistencyWarningSink
{
    public void RecordCorrectedInconsistency(
        AnswerConsistencyContext context,
        AnswerConsistencyConflict conflict) =>
        logger.LogWarning(
            "AI answer numeric inconsistency corrected. " +
            "CorrelationId={CorrelationId} ConversationId={ConversationId} " +
            "Symbol={Symbol} Metric={Metric} ProseValue={ProseValue} TableValue={TableValue} " +
            "OrchestrationMode={OrchestrationMode} WorkflowVersion={WorkflowVersion}",
            context.CorrelationId,
            context.ConversationId,
            conflict.SymbolCode,
            conflict.MetricCode,
            conflict.ProseValue,
            conflict.TableValue,
            context.OrchestrationMode,
            context.WorkflowVersion);
}
