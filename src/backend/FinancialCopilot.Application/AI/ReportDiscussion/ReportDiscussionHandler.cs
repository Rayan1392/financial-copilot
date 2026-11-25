using FinancialCopilot.Application.AI.GroundedAnalysis;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Feature 162 use case. Explicit sales report requests go to their existing routes. For remaining turns with
/// an active report, the grounded model decides whether to reply or hand the turn back to normal routing.
/// </summary>
public sealed class ReportDiscussionHandler(
    IReportDiscussionMessageReader messageReader,
    IConversationTaskStateService taskStateService,
    IReportDiscussionModel model,
    ReportDiscussionOptions options,
    TimeProvider? timeProvider = null,
    // Change «sol-deep-analysis-web-search»: the analysis path. With GroundedAnalysis:Enabled false (or these not
    // registered) the existing single-call path runs unchanged (rollback, design-proposal §15).
    IReportDiscussionAnalysisModel? analysisModel = null,
    GroundedAnalysisOptions? analysisOptions = null,
    GroundedAnalysisPricing? analysisPricing = null,
    // Change «local-company-data-context»: the company's other statements and monthly activity (FR-1 … FR-4). Null or
    // DataPack:Enabled false = only the active report, as before the change (FR-13).
    CompanyAnalysisDataPackLoader? dataPack = null)
{
    /// <summary>Same lifetime as every other suggestion selection (ConversationTaskStateService).</summary>
    private static readonly TimeSpan SuggestionLifetime = TimeSpan.FromMinutes(30);

    private readonly ReportDiscussionContextBuilder _contextBuilder = new(options);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public bool IsEnabled => options.Enabled;

    /// <summary>True when 162 answers with the analysis model at a fixed tier (change «sol-deep-analysis-web-search»).</summary>
    public bool AnalysisEnabled =>
        analysisOptions is { Enabled: true } && analysisModel is not null && analysisPricing is not null;

    /// <param name="ensureBillingCapacityBeforeModelCall">
    /// Called with the credits the answer will cost, before the (analysis) model call; throws when the balance is short.
    /// </param>
    public async Task<ReportDiscussionTurn> TryHandleAsync(
        AiQueryRequest request,
        Guid conversationId,
        bool createConversation,
        CancellationToken cancellationToken,
        Func<decimal, CancellationToken, Task>? ensureBillingCapacityBeforeModelCall = null)
    {
        if (!options.Enabled) return ReportDiscussionTurn.NotApplicableBecause("disabled");
        if (createConversation) return ReportDiscussionTurn.NotApplicableBecause("new_conversation");
        // A suggestion chip or an insight/alert action is a structured request for specific data, not a question
        // about the shown report; those keep their existing deterministic routes. The one exception is the
        // interpretation suggestion (DiscussReport), validated against the active report below.
        var selectedSuggestion = request.SuggestedActionId;
        if (request.Context?.InsightEventId is not null || request.Context?.AlertId is not null ||
            !string.IsNullOrWhiteSpace(selectedSuggestion) &&
            !selectedSuggestion.StartsWith(ReportInterpretationFollowUp.IdPrefix, StringComparison.Ordinal))
            return ReportDiscussionTurn.NotApplicableBecause("structured_action");

        var scope = new ConversationTaskStateScope(conversationId, request.TenantId, request.ActorId);
        var taskState = await taskStateService.GetActiveAsync(scope, cancellationToken);
        if (taskState?.PendingAction is not null) return ReportDiscussionTurn.NotApplicableBecause("pending_clarification");
        if (string.IsNullOrWhiteSpace(selectedSuggestion) && SalesReportIntentRules.IsExplicitDataRequest(request))
            return ReportDiscussionTurn.NotApplicableBecause("explicit_sales_report");

        var messages = await messageReader.ListRecentAsync(
            conversationId, request.TenantId, request.ActorId, options.LookbackMessages, cancellationToken);
        if (ActiveReportResolver.Resolve(messages) is not { } report)
            return ReportDiscussionTurn.NotApplicableBecause("no_active_report");
        if (!string.IsNullOrWhiteSpace(selectedSuggestion) && !IsInterpretationSuggestionOf(report, messages, request))
            return ReportDiscussionTurn.NotApplicableBecause("structured_action");
        if (ReportDataSetBuilder.Build(report) is not { Facts.Count: > 0 } data)
            return ReportDiscussionTurn.NotApplicableBecause("report_without_facts");

        var family = report.Family.ToString();
        var question = request.OriginalUserMessage ?? request.Message;
        if (AnalysisEnabled)
            return await AnalyzeAsync(request, report, data, messages, question, cancellationToken,
                ensureBillingCapacityBeforeModelCall);

        var prompt = _contextBuilder.Build(data, messages, question);
        if (prompt is null)
        {
            return Reply(report, ReportDiscussionDisposition.NarrowRequired,
                ReportDiscussionMessages.NarrowRequired(report), DialogueOutcome.NoData,
                ReportDiscussionReasonCodes.NarrowRequired, SemanticBillingCompletionStatus.NoData,
                new ReportDiscussionDiagnostics(family, nameof(ReportDiscussionDisposition.NarrowRequired), 0, data.Facts.Count, 0, 0, null));
        }

        if (ensureBillingCapacityBeforeModelCall is not null)
            await ensureBillingCapacityBeforeModelCall(ReportDiscussionBillingPolicy.AnsweredCredits, cancellationToken);

        ReportDiscussionModelReply reply;
        try
        {
            reply = await model.AnswerAsync(prompt, request.TenantId, request.CorrelationId, cancellationToken);
        }
        catch (ReportDiscussionModelUnavailableException exception)
        {
            return Reply(report, ReportDiscussionDisposition.ProviderUnavailable,
                ReportDiscussionMessages.ProviderUnavailable, DialogueOutcome.TemporarilyUnavailable,
                ReportDiscussionReasonCodes.ProviderUnavailable, "ProviderFailed",
                new ReportDiscussionDiagnostics(family, nameof(ReportDiscussionDisposition.ProviderUnavailable),
                    prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, exception.Reason));
        }

        var diagnostics = new ReportDiscussionDiagnostics(family, reply.Disposition.ToString(),
            prompt.EstimatedInputTokens, prompt.Data.Facts.Count, 0, 0, null);
        switch (reply.Disposition)
        {
            case ReportDiscussionDisposition.NeedsDifferentData:
            case ReportDiscussionDisposition.OutsideReport:
                return new ReportDiscussionTurn(ReportDiscussionTurnKind.HandBack,
                    Marker(report, reply.Disposition), Diagnostics: diagnostics);
            case ReportDiscussionDisposition.MissingInReport:
                return Reply(report, ReportDiscussionDisposition.MissingInReport,
                    ReportDiscussionMessages.MissingInReport(report), DialogueOutcome.NoData,
                    ReportDiscussionReasonCodes.MissingInReport, SemanticBillingCompletionStatus.NoData, diagnostics);
            case ReportDiscussionDisposition.Answered:
                // Change «answer-readability» (OQ-101): the layout safety net runs before grounding, so the stored text is
                // the verified text.
                // Change «analytical-answer-and-report-score» (D3): the server writes the score line, only for a quarterly
                // income statement (OQ-104), and the grounding check accepts the score and 100 it wrote.
                var score = GroundedAnalysis.ShownScore(prompt.Data.Family, reply.ReportScore);
                var answer = reply.Answer.Length == 0
                    ? string.Empty
                    : GroundedAnswerLayout.Normalize(score is { } value && prompt.Data.PeriodsNewestFirst.Count > 0
                        ? reply.Answer + "\n\n" + GroundedAnalysis.ScoreBlock(value, prompt.Data.PeriodsNewestFirst[0])
                        : reply.Answer);
                var grounding = NumericGroundingValidator.Validate(answer, prompt.Data, question, GroundedAnalysis.ScoreNumbers(score));
                diagnostics = diagnostics with { NumbersChecked = grounding.NumbersChecked, UngroundedNumbers = grounding.UngroundedCount };
                if (answer.Length == 0 || !grounding.Passed)
                {
                    return Reply(report, ReportDiscussionDisposition.Unverified,
                        ReportDiscussionMessages.Unverified(report), DialogueOutcome.NoData,
                        ReportDiscussionReasonCodes.Unverified, SemanticBillingCompletionStatus.NoData,
                        diagnostics with { Disposition = nameof(ReportDiscussionDisposition.Unverified) });
                }
                return Reply(report, ReportDiscussionDisposition.Answered, answer, DialogueOutcome.Answered,
                    DialogueOutcomeReasonCodes.None, ReportDiscussionBillingPolicy.AnsweredCompletionStatus, diagnostics);
            default:
                return ReportDiscussionTurn.NotApplicableBecause("unexpected_disposition");
        }
    }

    /// <summary>
    /// Change «sol-deep-analysis-web-search» (design-proposal §5): price check → routing step (no tools) → fixed tier
    /// reservation → analysis call → layout → external and internal grounding. Every non-answer is zero-credit.
    /// </summary>
    private async Task<ReportDiscussionTurn> AnalyzeAsync(
        AiQueryRequest request,
        ActiveReport report,
        ReportDataSet data,
        IReadOnlyList<MessageRecord> messages,
        string question,
        CancellationToken cancellationToken,
        Func<decimal, CancellationToken, Task>? ensureBillingCapacity)
    {
        var family = report.Family.ToString();
        var pricing = analysisPricing!;
        // PO-9: a page that showed other prices is never charged at the new ones; the reply carries the current list.
        if (!pricing.IsCurrent(request.AnalysisOptions?.PricingVersion))
        {
            return Reply(report, ReportDiscussionDisposition.PricingChanged, ReportDiscussionMessages.PricingChanged(pricing),
                DialogueOutcome.ClarificationNeeded, ReportDiscussionReasonCodes.PricingChanged, "ClarificationRequired",
                new ReportDiscussionDiagnostics(family, nameof(ReportDiscussionDisposition.PricingChanged), 0, data.Facts.Count, 0, 0, null));
        }

        // PO-14: the routing decision comes first, with no tools, so a hand-back never reaches the analysis model.
        var routing = _contextBuilder.BuildDisposition(data, messages, question);
        ReportDiscussionDisposition decision;
        try
        {
            decision = await analysisModel!.DecideAsync(routing, request.TenantId, request.CorrelationId, cancellationToken);
        }
        catch (ReportDiscussionModelUnavailableException exception)
        {
            return ProviderUnavailable(report, family, routing.EstimatedInputTokens, data.Facts.Count, $"disposition:{exception.Reason}");
        }

        var routed = new ReportDiscussionDiagnostics(family, decision.ToString(), routing.EstimatedInputTokens, data.Facts.Count,
            0, 0, null, DispositionStep: decision.ToString());
        switch (decision)
        {
            case ReportDiscussionDisposition.NeedsDifferentData:
            case ReportDiscussionDisposition.OutsideReport:
                return new ReportDiscussionTurn(ReportDiscussionTurnKind.HandBack, Marker(report, decision), Diagnostics: routed);
            case ReportDiscussionDisposition.MissingInReport:
                return Reply(report, ReportDiscussionDisposition.MissingInReport, ReportDiscussionMessages.MissingInReport(report),
                    DialogueOutcome.NoData, ReportDiscussionReasonCodes.MissingInReport, SemanticBillingCompletionStatus.NoData, routed);
        }

        var defaults = report.Family == ReportFamily.MonthlySales
            ? GroundedAnalysisHintFactory.MonthlySalesDefaults
            : analysisOptions!.DefaultChoice;
        var choice = request.AnalysisOptions?.Resolve(defaults) ?? defaults;
        var profile = new GroundedAnalysisProfiles(analysisOptions!).For(choice.Depth);
        // FR-3: the pack is read only for a turn the routing step answers; a hand-back has returned above.
        var pack = dataPack is { Enabled: true }
            ? await dataPack.LoadAsync(data, cancellationToken)
            : CompanyDataPack.PrimaryOnly(data);
        // FR-8, FR-9 (BUG-042): over the target the data is trimmed; only the hard ceiling refuses.
        if (_contextBuilder.BuildAnalysis(pack, messages, question, choice, profile.InputTokenTarget,
                analysisOptions!.MaxInputTokens) is not { } prompt)
        {
            return Reply(report, ReportDiscussionDisposition.NarrowRequired, ReportDiscussionMessages.NarrowRequired(report),
                DialogueOutcome.NoData, ReportDiscussionReasonCodes.NarrowRequired, SemanticBillingCompletionStatus.NoData,
                routed with { Disposition = nameof(ReportDiscussionDisposition.NarrowRequired), Reason = "over_max_input_tokens" });
        }

        // FR-11: the hold covers the fixed tier before the model call; a short balance stops here (no model call).
        if (ensureBillingCapacity is not null)
            await ensureBillingCapacity(pricing.CreditsFor(choice), cancellationToken);

        var run = new GroundedAnalysisRun(choice.Depth.ToString(), choice.WebSearch, choice.Tier, profile.Model,
            prompt.DataFacts?.SectionsSent);
        ReportDiscussionAnalysisReply reply;
        try
        {
            reply = await analysisModel!.AnalyzeAsync(prompt, choice, request.TenantId, request.CorrelationId, cancellationToken);
        }
        catch (ReportDiscussionModelUnavailableException exception)
        {
            var unavailable = ProviderUnavailable(report, family, prompt.EstimatedInputTokens, prompt.Data.Facts.Count, exception.Reason);
            return unavailable with
            {
                AnalysisRun = run,
                Diagnostics = unavailable.Diagnostics! with { DataFacts = prompt.DataFacts }
            };
        }

        var diagnostics = routed with
        {
            Disposition = reply.Disposition.ToString(),
            EstimatedInputTokens = prompt.EstimatedInputTokens,
            FactCount = prompt.Data.Facts.Count,
            DataFacts = prompt.DataFacts
        };
        var facts = new GroundedAnalysisCallFacts(choice, reply.Usage, reply.ConsultedSources.Count);
        if (reply.Disposition == ReportDiscussionDisposition.MissingInReport)
        {
            return Reply(report, ReportDiscussionDisposition.MissingInReport, ReportDiscussionMessages.MissingInReport(report),
                DialogueOutcome.NoData, ReportDiscussionReasonCodes.MissingInReport, SemanticBillingCompletionStatus.NoData,
                diagnostics) with { AnalysisRun = run, AnalysisFacts = facts };
        }

        var score = GroundedAnalysis.ShownScore(prompt.Data.Family, reply.ReportScore);
        var answer = reply.Answer.Length == 0
            ? string.Empty
            : GroundedAnswerLayout.Normalize(score is { } value && prompt.Data.PeriodsNewestFirst.Count > 0
                ? reply.Answer + "\n\n" + GroundedAnalysis.ScoreBlock(value, prompt.Data.PeriodsNewestFirst[0], reply.ScoreReason)
                : reply.Answer);
        // FR-10: external markers first (web content is kept apart and traceable), then every other number against the report.
        var evidence = ExternalEvidenceValidator.Validate(answer, reply.DeclaredSources, reply.ConsultedSources);
        var grounding = NumericGroundingValidator.Validate(evidence.GroundingText, prompt.Data, question, GroundedAnalysis.ScoreNumbers(score));
        diagnostics = diagnostics with
        {
            NumbersChecked = grounding.NumbersChecked,
            UngroundedNumbers = grounding.UngroundedCount,
            ExternalMarkers = evidence.MarkersChecked,
            ExternalItemsRemoved = evidence.ItemsRemoved,
            Reason = evidence.FailureReason
        };
        if (answer.Length == 0 || !evidence.Passed || !grounding.Passed)
        {
            return Reply(report, ReportDiscussionDisposition.Unverified, ReportDiscussionMessages.Unverified(report),
                DialogueOutcome.NoData, ReportDiscussionReasonCodes.Unverified, SemanticBillingCompletionStatus.NoData,
                diagnostics with { Disposition = nameof(ReportDiscussionDisposition.Unverified) }) with
            {
                AnalysisRun = run, AnalysisFacts = facts
            };
        }

        var text = ExternalEvidenceValidator.AppendSourcesBlock(evidence.Text, evidence.Sources);
        return Reply(report, ReportDiscussionDisposition.Answered, text, DialogueOutcome.Answered,
            DialogueOutcomeReasonCodes.None, GroundedAnalysisBillingPolicy.AnsweredStatus(choice), diagnostics) with
        {
            AnalysisRun = run,
            Sources = evidence.Sources.Count == 0 ? null : evidence.Sources,
            AnalysisFacts = facts
        };
    }

    private static ReportDiscussionTurn ProviderUnavailable(ActiveReport report, string family, int inputTokens, int facts, string reason) =>
        Reply(report, ReportDiscussionDisposition.ProviderUnavailable, ReportDiscussionMessages.ProviderUnavailable,
            DialogueOutcome.TemporarilyUnavailable, ReportDiscussionReasonCodes.ProviderUnavailable, "ProviderFailed",
            new ReportDiscussionDiagnostics(family, nameof(ReportDiscussionDisposition.ProviderUnavailable), inputTokens, facts, 0, 0, reason));

    /// <summary>
    /// The selected suggestion is a <see cref="SuggestedActionKind.DiscussReport"/> stored on the active report's own
    /// message, its text was sent unchanged, and it is not older than the suggestion lifetime.
    /// </summary>
    private bool IsInterpretationSuggestionOf(ActiveReport report, IReadOnlyList<MessageRecord> messages, AiQueryRequest request)
    {
        var action = report.Payload.SuggestedActions?.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, request.SuggestedActionId, StringComparison.Ordinal));
        var source = messages.FirstOrDefault(message => message.MessageId == report.SourceAssistantMessageId);
        return action is { Kind: SuggestedActionKind.DiscussReport } && source is not null &&
               string.Equals(action.Message.Trim(), request.Message.Trim(), StringComparison.Ordinal) &&
               _time.GetUtcNow() - source.CreatedAt <= SuggestionLifetime;
    }

    private static ReportDiscussionTurn Reply(ActiveReport report, ReportDiscussionDisposition disposition, string text,
        DialogueOutcome outcome, string reasonCode, string billingStatus, ReportDiscussionDiagnostics diagnostics) =>
        new(ReportDiscussionTurnKind.Reply, Marker(report, disposition), text, outcome, reasonCode, billingStatus, diagnostics);

    private static ReportDiscussionMarker Marker(ActiveReport report, ReportDiscussionDisposition disposition) =>
        new(report.SourceAssistantMessageId, report.Family.ToString(), disposition.ToString());
}

/// <summary>The fixed Persian texts of the zero-credit discussion replies (FR-8).</summary>
public static class ReportDiscussionMessages
{
    public const string ProviderUnavailable =
        "پاسخ‌گویی درباره‌ی این گزارش موقتاً در دسترس نیست. لطفاً چند لحظه دیگر دوباره بپرسید. اعتباری از شما کسر نشد.";

    public static string MissingInReport(ActiveReport report) =>
        $"این مورد در {Subject(report)} که در همین گفتگو نمایش داده شد وجود ندارد. اگر به قلم یا دوره‌ی دیگری نیاز دارید، گزارش مربوط به آن را درخواست کنید. اعتباری از شما کسر نشد.";

    public static string Unverified(ActiveReport report) =>
        $"نتوانستم پاسخی بسازم که همه‌ی اعدادش با داده‌های {Subject(report)} قابل تطبیق باشد. لطفاً قلم و دوره‌ی موردنظر را دقیق‌تر بپرسید. اعتباری از شما کسر نشد.";

    /// <summary>PO-9: the prices changed after the page was loaded; nothing ran and the current list is shown.</summary>
    public static string PricingChanged(GroundedAnalysisPricing pricing) =>
        "تعرفهٔ تحلیل به‌روز شده است و پرسش شما اجرا نشد. تعرفهٔ فعلی: " +
        string.Join("، ", pricing.Prices.Select(price => $"{AnalysisLabels.Tier(price.Depth, price.WebSearch)} {AnalysisLabels.Credits(price.Credits)}")) +
        ". لطفاً تنظیمات را بررسی کنید و دوباره بفرستید. اعتباری از شما کسر نشد.";

    public static string NarrowRequired(ActiveReport report) =>
        $"داده‌های {Subject(report)} برای پاسخ یکجا بیش از حد بزرگ است. لطفاً قلم یا دوره‌ی موردنظر را محدودتر بپرسید. اعتباری از شما کسر نشد.";

    private static string Subject(ActiveReport report) =>
        $"{ReportDataSetBuilder.FamilyLabel(report.Family)} {report.CompanySymbol}";
}
