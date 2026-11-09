using static FinancialCopilot.Application.AI.Orchestration.SemanticExecutionResultFactory;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class SymbolSnapshotCapabilityExecutor(ISymbolSnapshotUseCase useCase) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => SymbolSnapshotCapability.Code;

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var symbol = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbol is null)
            return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);

        var result = await useCase.ExecuteAsync(symbol, context.CorrelationId, cancellationToken);
        if (result is null)
            return NoData(frame);
        if (!result.HasData())
            return NoData(frame, result);

        var answered = Success(frame, result);
        return result.IsComplete() ? answered : answered with { Status = CapabilityExecutionStatus.Partial };
    }
}
