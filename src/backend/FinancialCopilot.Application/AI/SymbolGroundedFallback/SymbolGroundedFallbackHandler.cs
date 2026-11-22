using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.Conversations;
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
/// Feature 163 use case, changed by «sol-deep-analysis-web-search» (design-proposal §6): the fallback never runs on its
/// own any more. <see cref="TryOfferAsync"/> checks eligibility, the single company and its quarterly income statement
/// and returns an analysis offer — no model call and no reservation (PO-10, AC-8). <see cref="RunConfirmedAsync"/> runs
/// the analysis of an offer the user confirmed, at the chosen depth and web switch. Neither throws for an expected
/// failure (FR-11); a short balance throws from the caller's capacity callback before the model call.
/// </summary>
public sealed class SymbolGroundedFallbackHandler(
    ICanonicalQueryEntityResolver entityResolver,
    IFinancialStatementTableQueryUseCase statementTables,
    ISymbolGroundedFallbackModel model,
    SymbolGroundedFallbackOptions options,
    SymbolGroundedFallbackOffers offers,
    RelatedFollowUpActionFactory? suggestionFactory = null,
    GroundedAnalysisOptions? analysisOptions = null,
    GroundedAnalysisPricing? analysisPricing = null,
    IReportDiscussionMessageReader? messageReader = null,
    TimeProvider? timeProvider = null,
    // Change «local-company-data-context»: the company's other statements and monthly activity for a confirmed run
    // (FR-3). Null or DataPack:Enabled false = the income statement only, as before the change (FR-13).
    CompanyAnalysisDataPackLoader? dataPack = null)
{
    /// <summary>How far back an offer is looked up; an older offer reads as expired.</summary>
    internal const int OfferLookbackMessages = 20;

    private readonly SymbolGroundedFallbackPromptBuilder _promptBuilder = new(options, offers);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// True when 163 offers an analysis: both flags on (rollback, design-proposal §15). With either off the turn stays
    /// unchanged; no path runs 163 without confirmation.
    /// </summary>
    public bool OfferEnabled =>
        options.Enabled && analysisOptions is { Enabled: true } && analysisPricing is not null;

    /// <summary>The configured default depth and web switch of an offer.</summary>
    public AnalysisChoice DefaultChoice => analysisOptions?.DefaultChoice ?? new AnalysisChoice(AnalysisDepth.Deep, true);

    /// <summary>
    /// FR-7: an offer for an eligible turn about exactly one company with a usable quarterly income statement. No model
    /// call, no reservation. The caller keeps the original outcome (Feature 155) and appends <see cref="SymbolGroundedFallbackResult.Text"/>.
    /// </summary>
    public async Task<SymbolGroundedFallbackResult> TryOfferAsync(
        SymbolGroundedFallbackRequest turn,
        CancellationToken cancellationToken)
    {
        if (SymbolGroundedFallbackEligibility.IneligibleReason(turn, options) is { } ineligible)
            return SymbolGroundedFallbackResult.UnchangedBecause(ineligible);
        if (!OfferEnabled) return SymbolGroundedFallbackResult.UnchangedBecause("analysis_disabled");

        var question = turn.Request.OriginalUserMessage ?? turn.Request.Message;
        if (await ResolveSingleCompanyAsync(turn.Request, question, cancellationToken) is not { } company)
            return SymbolGroundedFallbackResult.UnchangedBecause("no_single_company");

        var symbol = company.DisplaySymbol.Trim();
        if (await ReadStatementAsync(symbol, cancellationToken) is not { } statement)
            return SymbolGroundedFallbackResult.UnchangedBecause("no_income_statement");

        var offer = NewOffer(company, symbol, statement.Data);
        return new SymbolGroundedFallbackResult(
            SymbolGroundedFallbackKind.OfferAnalysis,
            SymbolGroundedFallbackMessages.AnalysisOffer(offer, analysisPricing!, DefaultChoice),
            CompanySymbol: symbol,
            Diagnostics: new SymbolGroundedFallbackDiagnostics(null, 0, statement.Data.Facts.Count, 0, 0, "offered"),
            Offer: offer);
    }

    /// <summary>
    /// FR-8: the pending offer with this id on an assistant message of this conversation, read in the request's
    /// tenant and actor scope, with the question of the same exchange and the non-answer it was offered on. Null when
    /// there is none (another conversation, too old, or a deleted conversation).
    /// </summary>
    public async Task<PendingAnalysisOffer?> FindOfferAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        Guid offerId,
        CancellationToken cancellationToken)
    {
        if (messageReader is null) return null;
        var messages = await messageReader.ListRecentAsync(conversationId, tenantId, actorId, OfferLookbackMessages, cancellationToken);
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role != MessageRole.Assistant ||
                message.AssistantPayload?.GroundedAnalysisOffer is not { } offer || offer.OfferId != offerId)
                continue;
            var question = index > 0 && messages[index - 1].Role == MessageRole.User ? messages[index - 1].Content : null;
            if (string.IsNullOrWhiteSpace(question)) return null;
            var payload = message.AssistantPayload;
            return new PendingAnalysisOffer(offer, question,
                new FallbackOrigin(payload.Outcome, payload.OutcomeReasonCode, offer.OfferId));
        }

        return null;
    }

    /// <summary>The offer sentence with the current prices and defaults (FR-7).</summary>
    public string OfferText(GroundedAnalysisOffer offer) =>
        SymbolGroundedFallbackMessages.AnalysisOffer(offer, analysisPricing!, DefaultChoice);

    /// <summary>True when the offer may no longer be confirmed.</summary>
    public bool IsExpired(GroundedAnalysisOffer offer) => _time.GetUtcNow() >= offer.ExpiresAt;

    /// <summary>
    /// FR-8, FR-9: the analysis of a confirmed offer. Re-checks the price list, the company and the newest period (a
    /// change returns a fresh offer, nothing runs); then calls <paramref name="ensureBillingCapacity"/> with the tier's
    /// fixed credits, runs the analysis model once and validates the answer. Every non-answer carries a fresh offer when
    /// the statement is still available, so the user can retry (each offer is single-use).
    /// </summary>
    public async Task<SymbolGroundedFallbackResult> RunConfirmedAsync(
        AiQueryRequest request,
        PendingAnalysisOffer pending,
        AnalysisChoice choice,
        CancellationToken cancellationToken,
        Func<decimal, CancellationToken, Task>? ensureBillingCapacity = null)
    {
        if (!OfferEnabled) return SymbolGroundedFallbackResult.NotAnsweredBecause("analysis_disabled");
        var offer = pending.Offer;
        var pricing = analysisPricing!;

        var company = await ResolveExactAsync(offer.Symbol, cancellationToken);
        var statement = company is null ? null : await ReadStatementAsync(company.DisplaySymbol.Trim(), cancellationToken);
        if (company is null || statement is null)
            return SymbolGroundedFallbackResult.NotAnsweredBecause("no_income_statement");
        var fresh = NewOffer(company, company.DisplaySymbol.Trim(), statement.Data);

        // PO-9 and design-proposal §6: what the user agreed to must still hold; otherwise they consent again at 0 credits.
        var stale = !string.Equals(offer.PricingVersion, pricing.PricingVersion, StringComparison.Ordinal) ? "pricing_changed"
            : !string.Equals(offer.CompanyCanonicalId, fresh.CompanyCanonicalId, StringComparison.Ordinal) ? "company_changed"
            : !string.Equals(offer.NewestPeriod, fresh.NewestPeriod, StringComparison.Ordinal) ? "period_changed"
            : null;
        if (stale is not null)
            return SymbolGroundedFallbackResult.StaleBecause(stale, fresh);

        var profile = new GroundedAnalysisProfiles(analysisOptions!).For(choice.Depth);
        var data = statement.Data;
        var symbol = fresh.Symbol;
        // FR-3, FR-4: the pack is read only for a confirmed, current offer; the statement just read is not read again.
        var pack = dataPack is { Enabled: true }
            ? await dataPack.LoadAsync(data, cancellationToken)
            : CompanyDataPack.PrimaryOnly(data);
        // FR-8, FR-9 (BUG-042): over the target the data is trimmed; only the hard ceiling refuses.
        if (_promptBuilder.BuildAnalysis(pack, pending.Question, choice, profile.InputTokenTarget,
                analysisOptions!.MaxInputTokens) is not { } prompt)
            return SymbolGroundedFallbackResult.NotAnsweredBecause("over_input_budget",
                new SymbolGroundedFallbackDiagnostics(null, 0, data.Facts.Count, 0, 0, "over_max_input_tokens"), fresh);

        // FR-11: the hold covers the fixed tier before the model call; a short balance stops here (no model call).
        if (ensureBillingCapacity is not null)
            await ensureBillingCapacity(pricing.CreditsFor(choice), cancellationToken);

        var run = new GroundedAnalysisRun(choice.Depth.ToString(), choice.WebSearch, choice.Tier, profile.Model,
            prompt.DataFacts?.SectionsSent);
        SymbolGroundedFallbackModelReply reply;
        try
        {
            reply = await model.AnalyzeAsync(prompt, choice, request.TenantId, request.CorrelationId, cancellationToken);
        }
        catch (ReportDiscussionModelUnavailableException exception)
        {
            return SymbolGroundedFallbackResult.NotAnsweredBecause($"model_unavailable:{exception.Reason}",
                new SymbolGroundedFallbackDiagnostics(null, prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, null,
                    prompt.DataFacts),
                fresh) with { AnalysisRun = run };
        }

        var facts = new GroundedAnalysisCallFacts(choice, reply.Usage, reply.ConsultedSources?.Count ?? 0);
        var diagnostics = new SymbolGroundedFallbackDiagnostics(
            reply.Disposition.ToString(), prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, null, prompt.DataFacts);
        if (reply.Disposition == SymbolGroundedFallbackDisposition.NotAnswerable)
            return new SymbolGroundedFallbackResult(SymbolGroundedFallbackKind.OfferIncomeStatement,
                SymbolGroundedFallbackMessages.IncomeStatementOffer(symbol), CompanySymbol: symbol, Diagnostics: diagnostics,
                // The chip is kept only when its text routes to the quarterly report for this symbol (existing factory rule).
                Suggestion: suggestionFactory?.TryCreate(
                    SymbolGroundedFallbackMessages.QuarterlyReportCapabilityCode,
                    SymbolGroundedFallbackMessages.IncomeStatementRequest(symbol),
                    symbol,
                    SymbolGroundedFallbackMessages.RelevanceReason),
                AnalysisRun: run,
                AnalysisFacts: facts);

        if (reply.Answer.Length == 0)
            return SymbolGroundedFallbackResult.NotAnsweredBecause("empty_answer", diagnostics, fresh) with
            {
                AnalysisRun = run, AnalysisFacts = facts
            };
        var closing = reply.ClosingOffer.Length > 0 ? reply.ClosingOffer : _promptBuilder.FixedClosingOffer(symbol);
        // Change «answer-readability»: the layout safety net runs before grounding, so the stored text is the verified text.
        // Change «analytical-answer-and-report-score» (D3): the server-written score goes before the closing offer; the
        // grounding check accepts the score and 100 it wrote.
        var score = reply.ReportScore is { } value && prompt.Data.PeriodsNewestFirst.Count > 0
            ? "\n\n" + ReportDiscussion.GroundedAnalysis.ScoreBlock(value, prompt.Data.PeriodsNewestFirst[0], reply.ScoreReason)
            : string.Empty;
        var text = GroundedAnswerLayout.Normalize(reply.Answer + score + "\n\n" + closing);
        // FR-10: external markers first (web content is kept apart and traceable), then every other number against the report.
        var evidence = ExternalEvidenceValidator.Validate(text, reply.DeclaredSources ?? [], reply.ConsultedSources ?? []);
        var grounding = NumericGroundingValidator.Validate(evidence.GroundingText, prompt.Data, pending.Question,
            score.Length > 0 ? ReportDiscussion.GroundedAnalysis.ScoreNumbers(reply.ReportScore) : []);
        diagnostics = diagnostics with
        {
            NumbersChecked = grounding.NumbersChecked,
            UngroundedNumbers = grounding.UngroundedCount,
            Reason = evidence.FailureReason
        };
        if (!evidence.Passed || !grounding.Passed)
            return SymbolGroundedFallbackResult.NotAnsweredBecause("unverified_answer", diagnostics, fresh) with
            {
                AnalysisRun = run, AnalysisFacts = facts
            };

        // The rendered table text is not kept: the report was not shown, only its data is the audit trail (FR-10).
        return new SymbolGroundedFallbackResult(SymbolGroundedFallbackKind.Answered,
            ExternalEvidenceValidator.AppendSourcesBlock(evidence.Text, evidence.Sources),
            statement.Table with { RenderedAnswer = null }, symbol, diagnostics,
            AnalysisRun: run,
            Sources: evidence.Sources.Count == 0 ? null : evidence.Sources,
            AnalysisFacts: facts);
    }

    private GroundedAnalysisOffer NewOffer(CanonicalQueryEntity company, string symbol, ReportDataSet data) =>
        new(Guid.NewGuid(),
            company.CanonicalId.ToString("N"),
            symbol,
            data.PeriodsNewestFirst.Count > 0 ? data.PeriodsNewestFirst[0] : string.Empty,
            analysisPricing!.PricingVersion,
            _time.GetUtcNow().AddMinutes(analysisOptions!.OfferLifetimeMinutes));

    private sealed record Statement(FinancialStatementTableResult Table, ReportDataSet Data);

    /// <summary>The symbol's quarterly income statement; null unless it is the symbol's own and has facts.</summary>
    private async Task<Statement?> ReadStatementAsync(string symbol, CancellationToken cancellationToken)
    {
        var table = await statementTables.ExecuteAsync(
            FinancialStatementTableIntentRules.BuildV2QuarterlyQuery(SymbolGroundedFallbackMessages.IncomeStatementRequest(symbol)),
            cancellationToken);
        // The statement must be the resolved company's own: a different match would ground the answer on the wrong data.
        return table is not null &&
               ReportDataSetBuilder.BuildQuarterly(table) is { Facts.Count: > 0 } data &&
               RelatedFollowUpActionFactory.Canonical(data.CompanySymbol) == RelatedFollowUpActionFactory.Canonical(symbol)
            ? new Statement(table, data)
            : null;
    }

    /// <summary>Upper bound of words checked one by one, so a long message cannot cause many catalogue reads.</summary>
    private const int MaximumWordsResolved = 12;

    private async Task<CanonicalQueryEntity?> ResolveExactAsync(string mention, CancellationToken cancellationToken) =>
        await entityResolver.ResolveMentionAsync(mention, cancellationToken) is EntityResolutionResult.Resolved resolved &&
        EntityResolutionMatchKinds.IsExact(resolved.Evidence.MatchKind)
            ? resolved.Entity
            : null;

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
