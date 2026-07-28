using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

// Wraps the ChatClientAgent constructor to isolate the MAF dependency to one callsite
// and allow the factory to be substituted in tests.
internal sealed class FinancialCopilotAgentFactory(ILoggerFactory? loggerFactory = null)
{
    internal ChatClientAgent Create(
        IChatClient chatClient,
        string instructions,
        IList<AITool> tools) =>
        new(chatClient, instructions, "FinancialCopilot",
            "Financial data assistant for the Iranian stock market",
            tools, loggerFactory, services: null);
}
