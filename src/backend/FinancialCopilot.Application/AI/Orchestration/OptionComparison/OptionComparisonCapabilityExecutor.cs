using FinancialCopilot.Application.FinancialData.Options;
using static FinancialCopilot.Application.AI.Orchestration.SemanticExecutionResultFactory;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Option comparison for one resolved underlying (Feature 168). Executed when the comparison has
/// rows; otherwise NoData with a typed reason the text composer turns into one sentence. The
/// answer path only reads stored data; it never calls the market data provider.
/// </summary>
public sealed class OptionComparisonCapabilityExecutor(
    IOptionComparisonUseCase useCase,
    ICanonicalQueryEntityResolver entityResolver) : IConversationalCapabilityExecutor
{
    public string CapabilityCode => OptionComparisonCapability.Code;

    public async Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken)
    {
        var symbolText = frame.Value(QuerySlotType.CompanyOrSymbol);
        if (symbolText is null)
            return Missing(frame, DialogueOutcomeReasonCodes.RequiredInputMissing);

        var request = OptionComparisonRequest.ParseSlot(frame.Value(QuerySlotType.MetricSet), symbolText);
        var entity = frame.Slots.FirstOrDefault(slot => slot.Type == QuerySlotType.CompanyOrSymbol)?.CanonicalEntity;
        if (entity is null && await entityResolver.ResolveMentionAsync(symbolText, cancellationToken) is EntityResolutionResult.Resolved resolved)
            entity = resolved.Entity;

        var underlying = entity?.DisplaySymbol ?? symbolText;
        var view = request.View;
        var expiryDate = request.ExpiryDate;
        // A question that names one contract («ضمخا9021») was turned into its underlying, side and
        // expiry by the dialogue gate; its row is marked in the answer.
        var contractSymbol = request.ContractSymbol;

        var query = new OptionComparisonQuery(underlying, view, request.Quantity, expiryDate, contractSymbol);
        var outcome = await useCase.ExecuteAsync(query, cancellationToken);

        // «سررسید دی» or «نزدیک‌ترین سررسید»: pick the listed expiry, then ask again for that one.
        if (outcome.Failure == OptionComparisonFailure.None && expiryDate is null &&
            (request.ExpiryMonth is not null || request.NearestExpiryOnly))
        {
            var listed = outcome.ListedExpiryDates;
            var chosen = request.ExpiryMonth is { } month
                ? listed.Where(date => OptionComparisonIntentRules.JalaliMonthOf(date) == month).Cast<DateOnly?>().FirstOrDefault()
                : listed.Cast<DateOnly?>().FirstOrDefault();
            if (chosen is null)
                return NoData(frame, new OptionComparisonUnavailable(underlying, OptionComparisonFailure.ExpiryNotListed, listed));
            outcome = await useCase.ExecuteAsync(query with { ExpiryDate = chosen }, cancellationToken);
        }

        return outcome.Comparison is { } comparison && comparison.HasRows()
            ? Success(frame, comparison)
            : NoData(frame, new OptionComparisonUnavailable(
                underlying,
                outcome.Failure == OptionComparisonFailure.None ? OptionComparisonFailure.NoOptionContracts : outcome.Failure,
                outcome.ListedExpiryDates));
    }
}

/// <summary>
/// OQ-173: an option comparison answer that shows rows costs one credit, in every usage-based
/// mode. Clarifications and answers without rows are free.
/// </summary>
public static class OptionComparisonBillingPolicy
{
    public const decimal CreditsPerAnswer = 1m;
    public const string AnsweredStatus = "OptionComparisonAnswered";

    public static decimal? FixedCredits(string? completionStatus) =>
        string.Equals(completionStatus, AnsweredStatus, StringComparison.OrdinalIgnoreCase) ? CreditsPerAnswer : null;
}
