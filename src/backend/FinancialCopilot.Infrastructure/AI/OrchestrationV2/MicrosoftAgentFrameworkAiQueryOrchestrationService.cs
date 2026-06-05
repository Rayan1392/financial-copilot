using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

// Thin IAiQueryOrchestrationService implementation that delegates to the runner.
// Keeps the MAF dependency boundary explicit: only Infrastructure references MAF packages.
internal sealed class MicrosoftAgentFrameworkAiQueryOrchestrationService(
    FinancialCopilotAgentWorkflowRunner runner) : IAiQueryOrchestrationService
{
    public Task<AiQueryResponse> ExecuteAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken) =>
        runner.RunAsync(request, cancellationToken);
}
