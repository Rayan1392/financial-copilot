using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.Application.AI.ReportDiscussion;

// Feature 162 — Report Discussion. Once a supported financial report is shown in a conversation, later
// messages in the same conversation are answered from that report's persisted data by one bounded model
// call (design-proposal.md, Gate A decisions D1–D9). "Follow-up" already names the suggestion chips
// (FollowUps/FollowUpSuggestionPipeline), so this capability is called Report Discussion (D7).

/// <summary>The report families a discussion can be about (D1). Persisted as a string in the payload marker.</summary>
public enum ReportFamily
{
    QuarterlyIncomeStatement,
    BalanceSheet,
    MonthlySales
}

/// <summary>What the single discussion model call decided for the turn (D3).</summary>
public enum ReportDiscussionDisposition
{
    /// <summary>Answered from the active report's facts and calculations.</summary>
    Answered,
    /// <summary>The question is about the active report but the needed item or period is not in it.</summary>
    MissingInReport,
    /// <summary>Another company, report type or period is needed: the normal route answers the turn.</summary>
    NeedsDifferentData,
    /// <summary>The question is not about the active report: the normal route answers and the report becomes inactive.</summary>
    OutsideReport,
    /// <summary>The model's answer cited numbers that are not in the report (numeric grounding check failed).</summary>
    Unverified,
    /// <summary>The report's relevant data does not fit the input budget.</summary>
    NarrowRequired,
    /// <summary>The model provider failed or timed out.</summary>
    ProviderUnavailable
}

/// <summary>
/// Stored in <see cref="AssistantMessagePayload.ReportDiscussion"/> on every turn the discussion handled or handed
/// back, so the active report can be derived from the conversation history without another table (D6).
/// </summary>
public sealed record ReportDiscussionMarker(
    Guid SourceAssistantMessageId,
    string ReportFamily,
    string Disposition)
{
    public bool DeactivatesReport =>
        string.Equals(Disposition, nameof(ReportDiscussionDisposition.OutsideReport), StringComparison.Ordinal);
}

public sealed record ActiveReport(
    Guid SourceAssistantMessageId,
    ReportFamily Family,
    string CompanySymbol,
    string? CompanyName,
    string ProviderName,
    AssistantMessagePayload Payload);

/// <summary>One source value of the active report, addressed by a short evidence id in the prompt.</summary>
public sealed record ReportFact(
    string EvidenceId,
    string Label,
    string Period,
    decimal Value,
    string Unit,
    int PeriodRank);

/// <summary>A value computed deterministically from source facts (D5). The model never computes numbers.</summary>
public sealed record ReportCalculation(
    string EvidenceId,
    string Label,
    string Period,
    decimal Value,
    string Unit,
    string Formula,
    IReadOnlyList<string> OperandEvidenceIds,
    int PeriodRank);

/// <summary>The data the discussion prompt may use, in report order. Period rank 0 is the newest period.</summary>
public sealed record ReportDataSet(
    ReportFamily Family,
    string CompanySymbol,
    string? CompanyName,
    string Scope,
    string ProviderName,
    IReadOnlyList<string> PeriodsNewestFirst,
    IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<ReportCalculation> Calculations)
{
    public ReportDataSet KeepNewestPeriods(int count) => this with
    {
        PeriodsNewestFirst = PeriodsNewestFirst.Take(count).ToArray(),
        Facts = Facts.Where(fact => fact.PeriodRank < count).ToArray(),
        Calculations = Calculations.Where(calculation => calculation.PeriodRank < count).ToArray()
    };
}

public sealed record ReportDiscussionHistoryMessage(MessageRole Role, string Content);

public sealed record ReportDiscussionPrompt(
    string SystemPrompt,
    string UserContent,
    ReportDataSet Data,
    int EstimatedInputTokens);

public sealed record ReportDiscussionModelReply(
    ReportDiscussionDisposition Disposition,
    string Answer,
    IReadOnlyList<string> UsedEvidenceIds,
    int? ReportScore = null);

public enum ReportDiscussionTurnKind
{
    /// <summary>Report Discussion does not apply: the turn runs exactly as before.</summary>
    NotApplicable,
    /// <summary>The discussion call ran and decided the normal route must answer; carries the marker to persist.</summary>
    HandBack,
    /// <summary>The discussion answered the turn (an answer or a zero-credit non-answer).</summary>
    Reply
}

public sealed record ReportDiscussionTurn(
    ReportDiscussionTurnKind Kind,
    ReportDiscussionMarker? Marker = null,
    string? Text = null,
    DialogueOutcome Outcome = DialogueOutcome.Answered,
    string ReasonCode = DialogueOutcomeReasonCodes.None,
    string BillingCompletionStatus = "Completed",
    ReportDiscussionDiagnostics? Diagnostics = null)
{
    public static ReportDiscussionTurn NotApplicableBecause(string reason) =>
        new(ReportDiscussionTurnKind.NotApplicable, Diagnostics: new ReportDiscussionDiagnostics(null, null, 0, 0, 0, 0, reason));
}

/// <summary>Safe telemetry for one discussion decision: identifiers and counts only, never report values or answer text.</summary>
public sealed record ReportDiscussionDiagnostics(
    string? ReportFamily,
    string? Disposition,
    int EstimatedInputTokens,
    int FactCount,
    int NumbersChecked,
    int UngroundedNumbers,
    string? Reason);

public static class ReportDiscussionReasonCodes
{
    public const string MissingInReport = "report_discussion_missing_in_report";
    public const string Unverified = "report_discussion_unverified_answer";
    public const string NarrowRequired = "report_discussion_narrow_required";
    public const string ProviderUnavailable = "report_discussion_provider_unavailable";
}

public static class ReportDiscussionExecutionRoute
{
    public const string Code = "report_discussion";
}

public static class ReportDiscussionBillingPolicy
{
    public const string AnsweredCompletionStatus = "ReportDiscussionAnswered";
    public const decimal AnsweredCredits = 2m;

    public static decimal ApplyFixedAnsweredPrice(string completionStatus, decimal operationBasedCredits) =>
        string.Equals(completionStatus, AnsweredCompletionStatus, StringComparison.OrdinalIgnoreCase)
            ? AnsweredCredits
            : operationBasedCredits;
}

public sealed class ReportDiscussionOptions
{
    public const string SectionName = "ReportDiscussion";

    public bool Enabled { get; set; }
    public int LookbackMessages { get; set; } = 20;
    public int RecentMessages { get; set; } = 6;
    public int InputTokenTarget { get; set; } = 5000;
    public int RecentMessagesTokenCap { get; set; } = 1200;
    /// <summary>650 truncated Persian analysis answers inside the JSON envelope (BUG-031).</summary>
    public int MaxOutputTokens { get; set; } = 1600;
    public int ModelTimeoutSeconds { get; set; } = 30;
    /// <summary>Conservative characters-per-token estimate for mixed Persian text and digits; calibrate from usage logs.</summary>
    public decimal CharsPerToken { get; set; } = 2.5m;

    public void Validate()
    {
        if (LookbackMessages is < 2 or > 100) throw new InvalidOperationException("ReportDiscussion:LookbackMessages must be between 2 and 100.");
        if (RecentMessages is < 0 or > 20) throw new InvalidOperationException("ReportDiscussion:RecentMessages must be between 0 and 20.");
        if (InputTokenTarget is < 4000 or > 6000) throw new InvalidOperationException("ReportDiscussion:InputTokenTarget must be between 4000 and 6000.");
        if (RecentMessagesTokenCap is < 0 or > 3000) throw new InvalidOperationException("ReportDiscussion:RecentMessagesTokenCap must be between 0 and 3000.");
        if (MaxOutputTokens is < 100 or > 2000) throw new InvalidOperationException("ReportDiscussion:MaxOutputTokens must be between 100 and 2000.");
        if (ModelTimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("ReportDiscussion:ModelTimeoutSeconds must be between 1 and 120.");
        if (CharsPerToken is < 1m or > 6m) throw new InvalidOperationException("ReportDiscussion:CharsPerToken must be between 1 and 6.");
    }
}

/// <summary>Reads the newest messages of one conversation, scoped by tenant and actor, oldest first.</summary>
public interface IReportDiscussionMessageReader
{
    Task<IReadOnlyList<MessageRecord>> ListRecentAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        int take,
        CancellationToken cancellationToken);
}

/// <summary>Runs the single bounded discussion model call. Throws <see cref="ReportDiscussionModelUnavailableException"/> on provider failure or timeout.</summary>
public interface IReportDiscussionModel
{
    Task<ReportDiscussionModelReply> AnswerAsync(
        ReportDiscussionPrompt prompt,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class ReportDiscussionModelUnavailableException(string reason, Exception? innerException = null)
    : Exception($"Report discussion model unavailable: {reason}", innerException)
{
    public string Reason { get; } = reason;
}
