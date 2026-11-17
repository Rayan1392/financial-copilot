using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

/// <summary>
/// Feature 162 use case. Decides, with state checks only (D3: no keyword rules), whether the turn belongs to the
/// conversation's active report; if so, runs the single grounded model call and returns either a reply for the
/// workflow to persist and bill, or a hand-back so the normal route answers the turn unchanged.
/// </summary>
public sealed class ReportDiscussionHandler(
    IReportDiscussionMessageReader messageReader,
    IConversationTaskStateService taskStateService,
    IReportDiscussionModel model,
    ReportDiscussionOptions options,
    TimeProvider? timeProvider = null)
{
    /// <summary>Same lifetime as every other suggestion selection (ConversationTaskStateService).</summary>
    private static readonly TimeSpan SuggestionLifetime = TimeSpan.FromMinutes(30);

    private readonly ReportDiscussionContextBuilder _contextBuilder = new(options);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ReportDiscussionTurn> TryHandleAsync(
        AiQueryRequest request,
        Guid conversationId,
        bool createConversation,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? ensureBillingCapacityBeforeModelCall = null)
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
        var prompt = _contextBuilder.Build(data, messages, question);
        if (prompt is null)
        {
            return Reply(report, ReportDiscussionDisposition.NarrowRequired,
                ReportDiscussionMessages.NarrowRequired(report), DialogueOutcome.NoData,
                ReportDiscussionReasonCodes.NarrowRequired, SemanticBillingCompletionStatus.NoData,
                new ReportDiscussionDiagnostics(family, nameof(ReportDiscussionDisposition.NarrowRequired), 0, data.Facts.Count, 0, 0, null));
        }

        if (ensureBillingCapacityBeforeModelCall is not null)
            await ensureBillingCapacityBeforeModelCall(cancellationToken);

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
                var answer = GroundedAnswerLayout.Normalize(reply.Answer);
                var grounding = NumericGroundingValidator.Validate(answer, prompt.Data, question);
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

    public static string NarrowRequired(ActiveReport report) =>
        $"داده‌های {Subject(report)} برای پاسخ یکجا بیش از حد بزرگ است. لطفاً قلم یا دوره‌ی موردنظر را محدودتر بپرسید. اعتباری از شما کسر نشد.";

    private static string Subject(ActiveReport report) =>
        $"{ReportDataSetBuilder.FamilyLabel(report.Family)} {report.CompanySymbol}";
}
