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
    /// <summary>Null when the turn is eligible; otherwise the reason it is not.</summary>
    public static string? IneligibleReason(SymbolGroundedFallbackRequest turn, SymbolGroundedFallbackOptions options)
    {
        if (!options.Enabled) return "disabled";
        if (turn.Outcome != DialogueOutcome.Unsupported) return "outcome_not_unsupported";
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
        if (await ResolveSingleCompanyAsync(question, cancellationToken) is not { } company)
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
        var text = reply.Answer + "\n\n" + closing;
        var grounding = NumericGroundingValidator.Validate(text, prompt.Data, question);
        diagnostics = diagnostics with { NumbersChecked = grounding.NumbersChecked, UngroundedNumbers = grounding.UngroundedCount };
        if (!grounding.Passed)
            return SymbolGroundedFallbackResult.UnchangedBecause("unverified_answer", diagnostics);

        // The rendered table text is not kept: the report was not shown, only its data is the audit trail (FR-10).
        return new SymbolGroundedFallbackResult(SymbolGroundedFallbackKind.Answered, text,
            table! with { RenderedAnswer = null }, symbol, diagnostics);
    }

    /// <summary>
    /// Exactly one company named by its ticker (FR-2). The resolver reports the first ticker in the text, so the
    /// text is resolved again without that ticker: a second, different company means the question is about several.
    /// </summary>
    private async Task<CanonicalQueryEntity?> ResolveSingleCompanyAsync(string question, CancellationToken cancellationToken)
    {
        if (await entityResolver.ResolveExactTickerFromTextAsync(question, cancellationToken)
            is not EntityResolutionResult.Resolved first)
            return null;

        var ticker = QueryNormalization.Normalize(first.Entity.DisplaySymbol);
        var rest = string.Join(' ', QueryNormalization.Normalize(question)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !string.Equals(token, ticker, StringComparison.Ordinal)));
        return await entityResolver.ResolveExactTickerFromTextAsync(rest, cancellationToken) switch
        {
            EntityResolutionResult.Resolved other when other.Entity.CanonicalId != first.Entity.CanonicalId => null,
            EntityResolutionResult.Ambiguous => null,
            _ => first.Entity
        };
    }
}
