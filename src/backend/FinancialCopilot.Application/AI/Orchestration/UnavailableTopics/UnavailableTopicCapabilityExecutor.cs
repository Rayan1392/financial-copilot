namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Refuses a question about a topic no capability answers (Feature 150 change, R0). Reads no data and calls no model;
/// the reply text comes from <see cref="ChatCapabilityAvailability.MessageForReasonCode"/> and the follow-ups from the
/// existing guidance service.
/// </summary>
public sealed class UnavailableTopicCapabilityExecutor : IConversationalCapabilityExecutor
{
    public string CapabilityCode => UnavailableTopicCapability.Code;

    public Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        // The catalog is the single owner of topic → reason code; the gate selected this capability from the same match.
        var topic = UnavailableTopicCatalog.Match(frame.Interpretation.OriginalText);
        return Task.FromResult(new CapabilityExecutionResult(
            frame.CapabilityCode,
            frame.RegistryVersion,
            CapabilityExecutionStatus.Unsupported,
            topic?.ReasonCode ?? DialogueOutcomeReasonCodes.CapabilityNotRecognized));
    }
}
