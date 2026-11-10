namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Answers with the fixed help text. Reads no data and calls no model.</summary>
public sealed class AssistantHelpCapabilityExecutor(IAssistantHelpReplyComposer composer) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => AssistantHelpCapability.Code;

    public Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CapabilityExecutionResult(
            frame.CapabilityCode,
            frame.RegistryVersion,
            CapabilityExecutionStatus.Executed,
            DialogueOutcomeReasonCodes.AssistantHelp,
            composer.Compose(context.ReplyLanguage).Text));
}
