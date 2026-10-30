using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

internal sealed class RegisteredFinancialSourceCapabilityExecutor(
    Feature141ChatQueryService feature141ChatQueryService) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => "registered_financial_source";

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (request is null)
            return new(frame.CapabilityCode, frame.RegistryVersion,
                CapabilityExecutionStatus.TemporarilyUnavailable, "chat_request_unavailable");

        var companySymbol = frame.Slots.FirstOrDefault(slot =>
            slot.Type == QuerySlotType.CompanyOrSymbol &&
            slot.ValidationState == QuerySlotValidationState.Valid)?.Value;
        if (string.IsNullOrWhiteSpace(companySymbol))
            return new(frame.CapabilityCode, frame.RegistryVersion,
                CapabilityExecutionStatus.ClarificationRequired, DialogueOutcomeReasonCodes.RequiredInputMissing);

        var answer = await feature141ChatQueryService.AnswerAsync(
            request, companySymbol, cancellationToken).ConfigureAwait(false);
        // An answer settles as an executed capability. Any other result defers its settlement to the chat workflow,
        // which presents the same text and settles it with the answer's own (free) completion status.
        return answer.Answered
            ? new(frame.CapabilityCode, frame.RegistryVersion,
                CapabilityExecutionStatus.Executed, DialogueOutcomeReasonCodes.None, answer)
            : new(frame.CapabilityCode, frame.RegistryVersion,
                CapabilityExecutionStatus.NoData, DialogueOutcomeReasonCodes.SupportedButNoRows, answer);
    }
}
