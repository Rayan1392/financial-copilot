using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.SymbolGroundedFallback;

/// <summary>
/// The one rule for when the fallback may run (FR-1). Pure: no I/O. The excluded reason codes are owned by
/// <see cref="ChatCapabilityAvailability"/> and <see cref="UnavailableTopicCatalog"/>; no keyword list lives here.
/// </summary>
public static class SymbolGroundedFallbackEligibility
{
    /// <summary>
    /// The outcomes the fallback may replace: every <see cref="DialogueOutcome.Unsupported"/>, and the one no-data answer
    /// the owner sent to it — no recent TahlilApp analysis (change «analysis-recency-window», 2026-11-16). Every other
    /// no-data answer keeps its message (OQ-092).
    /// </summary>
    public static bool MayReplace(DialogueOutcome outcome, string reasonCode) =>
        outcome == DialogueOutcome.Unsupported ||
        outcome == DialogueOutcome.NoData && reasonCode == DialogueOutcomeReasonCodes.ComprehensiveAnalysisNotRecent;

    /// <summary>Null when the turn is eligible; otherwise the reason it is not.</summary>
    public static string? IneligibleReason(SymbolGroundedFallbackRequest turn, SymbolGroundedFallbackOptions options)
    {
        if (!options.Enabled) return "disabled";
        if (!MayReplace(turn.Outcome, turn.ReasonCode)) return "outcome_not_replaceable";
        if (turn.ReportDiscussionApplied) return "report_discussion_turn";
        if (!string.Equals(turn.ReplyLanguage, "fa", StringComparison.OrdinalIgnoreCase)) return "reply_language_not_persian";
        var request = turn.Request;
        if (!string.IsNullOrWhiteSpace(request.SuggestedActionId) ||
            request.Context?.InsightEventId is not null || request.Context?.AlertId is not null)
            return "structured_action";
        // Valuation and investment-advice questions use the fallback (OQ-091); every other fixed availability
        // reply (provider policy, technical analysis) stays as it is.
        if (turn.ReasonCode is not (UnavailableTopicCatalog.ValuationReasonCode or UnavailableTopicCatalog.InvestmentAdviceReasonCode) &&
            ChatCapabilityAvailability.MessageForReasonCode(turn.ReasonCode, persian: true) is not null)
            return "fixed_availability_reply";
        return null;
    }
}

/// <summary>
/// Feature 163 use case. For an eligible turn it resolves the single company named in the question, reads its
/// quarterly income statement, runs the single grounded model call and returns an answer, an income-statement
/// offer, or "unchanged". It never throws for an expected failure: every failure leaves the turn unchanged (FR-11).
/// </summary>
public sealed class SymbolGroundedFallbackHandler(
    ICanonicalQueryEntityResolver entityResolver,
    IFinancialStatementTableQueryUseCase statementTables,
    ISymbolGroundedFallbackModel model,
    SymbolGroundedFallbackOptions options,
    SymbolGroundedFallbackOffers offers,
    RelatedFollowUpActionFactory? suggestionFactory = null)
{
    private readonly SymbolGroundedFallbackPromptBuilder _promptBuilder = new(options, offers);

    public async Task<SymbolGroundedFallbackResult> TryHandleAsync(
        SymbolGroundedFallbackRequest turn,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? ensureBillingCapacityBeforeModelCall = null)
    {
        if (SymbolGroundedFallbackEligibility.IneligibleReason(turn, options) is { } ineligible)
            return SymbolGroundedFallbackResult.UnchangedBecause(ineligible);

        var question = turn.Request.OriginalUserMessage ?? turn.Request.Message;
        if (await ResolveSingleCompanyAsync(turn.Request, question, cancellationToken) is not { } company)
            return SymbolGroundedFallbackResult.UnchangedBecause("no_single_company");

        var symbol = company.DisplaySymbol.Trim();
        var table = await statementTables.ExecuteAsync(
            FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(SymbolGroundedFallbackMessages.IncomeStatementRequest(symbol)),
            cancellationToken);
        // The statement must be the resolved company's own: a different match would ground the answer on the wrong data.
        if (ReportDataSetBuilder.BuildQuarterly(table) is not { Facts.Count: > 0 } data ||
            RelatedFollowUpActionFactory.Canonical(data.CompanySymbol) != RelatedFollowUpActionFactory.Canonical(symbol))
            return SymbolGroundedFallbackResult.UnchangedBecause("no_income_statement");

        if (_promptBuilder.Build(data, question) is not { } prompt)
            return SymbolGroundedFallbackResult.UnchangedBecause("over_input_budget",
                new SymbolGroundedFallbackDiagnostics(null, 0, data.Facts.Count, 0, 0, null));

        if (ensureBillingCapacityBeforeModelCall is not null)
            await ensureBillingCapacityBeforeModelCall(cancellationToken);

        SymbolGroundedFallbackModelReply reply;
        try
        {
            reply = await model.AnswerAsync(prompt, turn.Request.TenantId, turn.Request.CorrelationId, cancellationToken);
        }
        catch (ReportDiscussionModelUnavailableException exception)
        {
            return SymbolGroundedFallbackResult.UnchangedBecause($"model_unavailable:{exception.Reason}",
                new SymbolGroundedFallbackDiagnostics(null, prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, null));
        }

        var diagnostics = new SymbolGroundedFallbackDiagnostics(
            reply.Disposition.ToString(), prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, null);
        if (reply.Disposition == SymbolGroundedFallbackDisposition.NotAnswerable)
            return new SymbolGroundedFallbackResult(SymbolGroundedFallbackKind.OfferIncomeStatement,
                SymbolGroundedFallbackMessages.IncomeStatementOffer(symbol), CompanySymbol: symbol, Diagnostics: diagnostics,
                // The chip is kept only when its text routes to the quarterly report for this symbol (existing factory rule).
                Suggestion: suggestionFactory?.TryCreate(
                    SymbolGroundedFallbackMessages.QuarterlyReportCapabilityCode,
                    SymbolGroundedFallbackMessages.IncomeStatementRequest(symbol),
                    symbol,
                    SymbolGroundedFallbackMessages.RelevanceReason));

        if (reply.Answer.Length == 0)
            return SymbolGroundedFallbackResult.UnchangedBecause("empty_answer", diagnostics);
        var closing = reply.ClosingOffer.Length > 0 ? reply.ClosingOffer : _promptBuilder.FixedClosingOffer(symbol);
        // Change «answer-readability»: the layout safety net runs before grounding, so the stored text is the verified text.
        var text = GroundedAnswerLayout.Normalize(reply.Answer + "\n\n" + closing);
        var grounding = NumericGroundingValidator.Validate(text, prompt.Data, question);
        diagnostics = diagnostics with { NumbersChecked = grounding.NumbersChecked, UngroundedNumbers = grounding.UngroundedCount };
        if (!grounding.Passed)
            return SymbolGroundedFallbackResult.UnchangedBecause("unverified_answer", diagnostics);

        // The rendered table text is not kept: the report was not shown, only its data is the audit trail (FR-10).
        return new SymbolGroundedFallbackResult(SymbolGroundedFallbackKind.Answered, text,
            table! with { RenderedAnswer = null }, symbol, diagnostics);
    }

    /// <summary>Upper bound of words checked one by one, so a long message cannot cause many catalogue reads.</summary>
    private const int MaximumWordsResolved = 12;

    /// <summary>
    /// Exactly one company (FR-2): the company the routed capability already resolved for this turn (the same identity
    /// its own answer used), together with every word of the question resolved by the catalogue's exact identity rules
    /// (symbol, name, approved alias; never a fuzzy or "contains" match). One company means the question is about it;
    /// two mean several companies (a frame holds only the first one); a word that matches several companies exactly is
    /// unsafe. Not by the ticker column: Noavaran rows carry no ticker (owner report «سهم چدن», 2026-11-16).
    /// </summary>
    private async Task<CanonicalQueryEntity?> ResolveSingleCompanyAsync(
        AiQueryRequest request, string question, CancellationToken cancellationToken)
    {
        var found = (request.SemanticFrame?.Slots ?? [])
            .Where(slot => slot.Type == QuerySlotType.CompanyOrSymbol &&
                           slot.ValidationState == QuerySlotValidationState.Valid && slot.CanonicalEntity is not null)
            .Select(slot => slot.CanonicalEntity!)
            .DistinctBy(entity => entity.CanonicalId)
            .ToDictionary(entity => entity.CanonicalId);
        if (found.Count > 1) return null;

        var words = QueryNormalization.Normalize(question)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 2 && !QueryNormalization.IsPresentationWord(word))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumWordsResolved);
        foreach (var word in words)
        {
            switch (await entityResolver.ResolveMentionAsync(word, cancellationToken))
            {
                case EntityResolutionResult.Resolved resolved when EntityResolutionMatchKinds.IsExact(resolved.Evidence.MatchKind):
                    found[resolved.Entity.CanonicalId] = resolved.Entity;
                    break;
                case EntityResolutionResult.Ambiguous ambiguous when ambiguous.Candidates.Any(candidate =>
                    EntityResolutionMatchKinds.IsExact(candidate.MatchKind)):
                    return null;
            }
            if (found.Count > 1) return null;
        }

        return found.Count == 1 ? found.Values.Single() : null;
    }
}
