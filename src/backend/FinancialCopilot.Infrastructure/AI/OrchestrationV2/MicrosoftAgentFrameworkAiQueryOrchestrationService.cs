using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Config;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

// Thin IAiQueryOrchestrationService implementation that delegates to either the native
// MAF Workflow graph (MicrosoftAgentFrameworkV2 mode) or the legacy imperative runner (V1).
// Keeps the MAF dependency boundary explicit: only Infrastructure references MAF packages.
internal sealed class MicrosoftAgentFrameworkAiQueryOrchestrationService(
    FinancialCopilotAgentWorkflowRunner runner,
    FinancialCopilotWorkflowDefinition workflowDefinition,
    IOptions<AiOrchestrationOptions> options) : IAiQueryOrchestrationService
{
    public Task<AiQueryResponse> ExecuteAsync(
        AiQueryRequest request,
        CancellationToken cancellationToken) =>
        options.Value.Mode == AiOrchestrationMode.MicrosoftAgentFrameworkV2
            ? workflowDefinition.RunAsync(request, cancellationToken)
            : runner.RunAsync(request, cancellationToken);
}
