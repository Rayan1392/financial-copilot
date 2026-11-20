using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.SymbolGroundedFallback;

// Feature 163 — Symbol-Grounded Fallback Answer. A question that names one company and that no capability
// answers (outcome Unsupported) is answered from that company's quarterly income statement by one bounded
// model call. The statement is not shown; it is stored hidden with the answer and becomes the conversation's
// active report (specs/163-symbol-grounded-fallback-answer, decisions D1–D9, OQ-090 … OQ-096).

/// <summary>What the single fallback model call decided.</summary>
public enum SymbolGroundedFallbackDisposition
{
    /// <summary>The income statement helps answer the question.</summary>
    Answered,
    /// <summary>The question cannot be answered from an income statement.</summary>
    NotAnswerable
}

public enum SymbolGroundedFallbackKind
{
    /// <summary>The turn stays exactly as the normal route left it.</summary>
    Unchanged,
    /// <summary>The fallback answered; the turn becomes an answer written from the hidden report.</summary>
    Answered,
    /// <summary>The original non-answer stays, with an offer to show the income statement (OQ-093).</summary>
    OfferIncomeStatement,
    /// <summary>
    /// Change «sol-deep-analysis-web-search» (FR-7): the original non-answer stays, with an offer to analyse the
    /// income statement after confirmation. No model call and no reservation happened.
    /// </summary>
    OfferAnalysis,
    /// <summary>A confirmed analysis did not answer (FR-9): zero credits, a fresh offer when the statement is available.</summary>
    NotAnswered,
    /// <summary>The price list, company or newest period changed since the offer: nothing ran, a fresh offer is returned.</summary>
    Stale
}

/// <summary>
/// A 163 offer found on an assistant message of the conversation (FR-8): the question of the same exchange and the
/// non-answer it was offered on (with the offer id, persisted as <c>FallbackOf</c> when the analysis answers).
/// </summary>
public sealed record PendingAnalysisOffer(
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer Offer,
    string Question,
    FallbackOrigin Origin);

/// <summary>
/// The non-answer a fallback answer replaced. Persisted with the answer. <see cref="ConfirmedOfferId"/> is the
/// analysis offer the user confirmed (change «sol-deep-analysis-web-search»); null on answers written before it.
/// </summary>
public sealed record FallbackOrigin(DialogueOutcome Outcome, string ReasonCode, Guid? ConfirmedOfferId = null);

public sealed record SymbolGroundedFallbackRequest(
    AiQueryRequest Request,
    DialogueOutcome Outcome,
    string ReasonCode,
    string ReplyLanguage,
    bool ReportDiscussionApplied);

public sealed record SymbolGroundedFallbackResult(
    SymbolGroundedFallbackKind Kind,
    string? Text = null,
    FinancialStatementTableResult? GroundingReport = null,
    string? CompanySymbol = null,
    SymbolGroundedFallbackDiagnostics? Diagnostics = null,
    SuggestedAction? Suggestion = null,
    // Change «sol-deep-analysis-web-search»: the offer made (OfferAnalysis) or the fresh offer of a failed or stale
    // confirmation; what the confirmed analysis ran, the sources it cited and its cost facts (monitoring only).
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer? Offer = null,
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisRun? AnalysisRun = null,
    IReadOnlyList<FinancialCopilot.Application.AI.GroundedAnalysis.ExternalSource>? Sources = null,
    FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisCallFacts? AnalysisFacts = null)
{
    public static SymbolGroundedFallbackResult UnchangedBecause(string reason, SymbolGroundedFallbackDiagnostics? diagnostics = null) =>
        new(SymbolGroundedFallbackKind.Unchanged,
            Diagnostics: (diagnostics ?? new SymbolGroundedFallbackDiagnostics(null, 0, 0, 0, 0, null)) with { Reason = reason });

    public static SymbolGroundedFallbackResult NotAnsweredBecause(
        string reason,
        SymbolGroundedFallbackDiagnostics? diagnostics = null,
        FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer? freshOffer = null) =>
        new(SymbolGroundedFallbackKind.NotAnswered,
            CompanySymbol: freshOffer?.Symbol,
            Diagnostics: (diagnostics ?? new SymbolGroundedFallbackDiagnostics(null, 0, 0, 0, 0, null)) with { Reason = reason },
            Offer: freshOffer);

    public static SymbolGroundedFallbackResult StaleBecause(
        string reason,
        FinancialCopilot.Application.AI.GroundedAnalysis.GroundedAnalysisOffer freshOffer) =>
        new(SymbolGroundedFallbackKind.Stale,
            CompanySymbol: freshOffer.Symbol,
            Diagnostics: new SymbolGroundedFallbackDiagnostics(null, 0, 0, 0, 0, reason),
            Offer: freshOffer);
}

/// <summary>Safe telemetry: identifiers and counts only, never report values, the question or the answer.</summary>
public sealed record SymbolGroundedFallbackDiagnostics(
    string? Disposition,
    int EstimatedInputTokens,
    int FactCount,
    int NumbersChecked,
    int UngroundedNumbers,
    string? Reason);

public sealed record SymbolGroundedFallbackPrompt(
    string SystemPrompt,
    string UserContent,
    ReportDataSet Data,
    int EstimatedInputTokens);

public sealed record SymbolGroundedFallbackModelReply(
    SymbolGroundedFallbackDisposition Disposition,
    string Answer,
    string ClosingOffer,
    IReadOnlyList<string> UsedEvidenceIds,
    int? ReportScore = null,
    // Change «sol-deep-analysis-web-search» (FR-10): the sources the model declared (unverified) and the URLs the
    // hosted search consulted; the usage of the call for cost telemetry (FR-13). Empty or null with web off.
    IReadOnlyList<FinancialCopilot.Application.AI.ReportDiscussion.DeclaredExternalSource>? DeclaredSources = null,
    IReadOnlyCollection<string>? ConsultedSources = null,
    FinancialCopilot.Application.AI.ModelProviders.AiExecutionUsageFacts? Usage = null,
    // Amendment A2: the model's one-sentence reason for the score (shown only through GroundedAnalysis.ScoreBlock).
    string? ScoreReason = null);

public static class SymbolGroundedFallbackExecutionRoute
{
    public const string Code = "symbol_grounded_fallback";
}

/// <summary>
/// The persisted reason codes of the replies to a 163 offer action (change «sol-deep-analysis-web-search», FR-8, FR-9).
/// Each reply is zero-credit except an answered analysis.
/// </summary>
public static class GroundedAnalysisOfferReasonCodes
{
    public const string Cancelled = "analysis_offer_cancelled";
    public const string AlreadyResolved = "analysis_offer_resolved";
    public const string InProgress = "analysis_offer_in_progress";
    public const string Expired = "analysis_offer_expired";
    public const string Changed = "analysis_offer_changed";
    public const string NotAnswered = "analysis_not_answered";
    public const string NotAnswerable = "analysis_not_answerable";
}

/// <summary>
/// An answered fallback costs the same as an answered Report Discussion (OQ-096), so it settles with the same
/// completion status; the execution route tells the two apart in usage records.
/// </summary>
public static class SymbolGroundedFallbackBillingPolicy
{
    public const string AnsweredCompletionStatus = ReportDiscussionBillingPolicy.AnsweredCompletionStatus;
    public const decimal AnsweredCredits = ReportDiscussionBillingPolicy.AnsweredCredits;
    /// <summary>A zero-charge settlement status for a fallback that did not answer.</summary>
    public const string NotAnsweredCompletionStatus = SemanticBillingCompletionStatus.NoData;
}

public sealed class SymbolGroundedFallbackOptions
{
    public const string SectionName = "SymbolGroundedFallback";

    public bool Enabled { get; set; }
    public int InputTokenTarget { get; set; } = 5000;
    public int MaxOutputTokens { get; set; } = 1600;
    public int ModelTimeoutSeconds { get; set; } = 30;
    /// <summary>Same conservative estimate as Report Discussion.</summary>
    public decimal CharsPerToken { get; set; } = 2.5m;

    public void Validate()
    {
        if (InputTokenTarget is < 4000 or > 6000) throw new InvalidOperationException("SymbolGroundedFallback:InputTokenTarget must be between 4000 and 6000.");
        if (MaxOutputTokens is < 100 or > 2000) throw new InvalidOperationException("SymbolGroundedFallback:MaxOutputTokens must be between 100 and 2000.");
        if (ModelTimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("SymbolGroundedFallback:ModelTimeoutSeconds must be between 1 and 120.");
        if (CharsPerToken is < 1m or > 6m) throw new InvalidOperationException("SymbolGroundedFallback:CharsPerToken must be between 1 and 6.");
    }
}

/// <summary>
/// Runs the analysis call of a confirmed offer with the depth's profile and the web switch (change
/// «sol-deep-analysis-web-search»). Throws <see cref="ReportDiscussionModelUnavailableException"/> on provider failure,
/// timeout or invalid output.
/// </summary>
public interface ISymbolGroundedFallbackModel
{
    Task<SymbolGroundedFallbackModelReply> AnalyzeAsync(
        SymbolGroundedFallbackPrompt prompt,
        FinancialCopilot.Application.AI.GroundedAnalysis.AnalysisChoice choice,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>Whether the cash-flow report can be offered in the closing sentence (Feature 161 flag).</summary>
public sealed record SymbolGroundedFallbackOffers(bool CashFlowEnabled);
