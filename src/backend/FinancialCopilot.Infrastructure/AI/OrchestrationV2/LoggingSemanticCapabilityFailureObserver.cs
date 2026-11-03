using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

/// <summary>
/// Logs capability executor faults that the semantic dispatcher converts into typed failure outcomes.
/// Query text is intentionally omitted; CorrelationId links the fault to its SemanticRoutingDecision.
/// </summary>
internal sealed class LoggingSemanticCapabilityFailureObserver(
    ILogger<LoggingSemanticCapabilityFailureObserver> logger) : ISemanticCapabilityFailureObserver
{
    private static readonly EventId EventId = new(12802, "SemanticCapabilityExecutionFaulted");

    public void ExecutionFaulted(string capabilityCode, QueryExecutionContext context, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            logger.LogWarning(EventId, exception,
                "[SemanticRouting] Capability execution timed out. Capability={Capability} CorrelationId={CorrelationId}",
                capabilityCode, context.CorrelationId);
            return;
        }

        logger.LogError(EventId, exception,
            "[SemanticRouting] Capability execution faulted. Capability={Capability} CorrelationId={CorrelationId}",
            capabilityCode, context.CorrelationId);
    }
}
