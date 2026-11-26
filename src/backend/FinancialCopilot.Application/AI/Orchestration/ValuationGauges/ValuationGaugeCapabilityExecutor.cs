using FinancialCopilot.Application.FinancialData.ValuationGauges;
using static FinancialCopilot.Application.AI.Orchestration.SemanticExecutionResultFactory;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Standalone valuation gauge cards for one resolved symbol. Executed when every requested kind is complete, Partial
/// when at least one card is usable, otherwise TemporarilyUnavailable (a read timed out) or NoData; the typed result is
/// always the payload so the unavailable disposition is reported from the same facts.
/// </summary>
public sealed class ValuationGaugeCapabilityExecutor(
    IValuationGaugeCardsUseCase useCase,
    ICanonicalQueryEntityResolver entityResolver) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => ValuationGaugeCapability.Code;

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var symbol = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbol is null)
            return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);

        var entity = frame.Slots.FirstOrDefault(slot => slot.Type == QuerySlotType.CompanyOrSymbol)?.CanonicalEntity;
        if (entity is null && await entityResolver.ResolveMentionAsync(symbol, cancellationToken) is EntityResolutionResult.Resolved resolved)
            entity = resolved.Entity;
        var kinds = ValuationGaugeIntentRules.ParseKinds(frame.Value(QuerySlotType.MetricSet));
        var security = new ValuationGaugeSecurity(
            entity?.DisplaySymbol ?? symbol,
            entity?.CanonicalId is { } id && id != Guid.Empty ? id : null,
            entity?.ExternalCompanyId,
            entity?.CompanyName);

        var result = await useCase.ExecuteAsync(new ValuationGaugeCardsQuery(security, kinds), cancellationToken);
        if (result.HasDisplayableData())
        {
            var answered = Success(frame, result);
            return result.IsComplete() ? answered : answered with { Status = CapabilityExecutionStatus.Partial };
        }

        if (result.Cards.Any(card => card.State == ValuationGaugeCardState.TemporarilyUnavailable))
            return new CapabilityExecutionResult(frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.TemporarilyUnavailable,
                DialogueOutcomeReasonCodes.ProviderOrToolTimeout, result);
        return result.Cards.Any(card => card.State == ValuationGaugeCardState.Stale)
            ? NoData(frame, result) with { ReasonCode = DialogueOutcomeReasonCodes.DataStaleOrIneligible }
            : NoData(frame, result);
    }
}
