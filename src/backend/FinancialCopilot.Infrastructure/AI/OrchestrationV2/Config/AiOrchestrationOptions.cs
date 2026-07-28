namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Config;

public enum AiOrchestrationMode
{
    V1,
    MicrosoftAgentFrameworkV2
}

public sealed class AiOrchestrationOptions
{
    public const string SectionName = "AiOrchestration";

    public AiOrchestrationMode Mode { get; init; } = AiOrchestrationMode.V1;

    public string WorkflowVersion { get; init; } = "maf-orchestration-v2.0";

    public int AgentTimeoutSeconds { get; init; } = 60;
}
