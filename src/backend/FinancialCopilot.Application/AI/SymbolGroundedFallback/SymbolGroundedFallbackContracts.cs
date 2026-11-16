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
    OfferIncomeStatement
}

/// <summary>The non-answer a fallback answer replaced. Persisted with the answer.</summary>
public sealed record FallbackOrigin(DialogueOutcome Outcome, string ReasonCode);

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
    SuggestedAction? Suggestion = null)
{
    public static SymbolGroundedFallbackResult UnchangedBecause(string reason, SymbolGroundedFallbackDiagnostics? diagnostics = null) =>
        new(SymbolGroundedFallbackKind.Unchanged,
            Diagnostics: (diagnostics ?? new SymbolGroundedFallbackDiagnostics(null, 0, 0, 0, 0, null)) with { Reason = reason });
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
    IReadOnlyList<string> UsedEvidenceIds);

public static class SymbolGroundedFallbackExecutionRoute
{
    public const string Code = "symbol_grounded_fallback";
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

/// <summary>Runs the single bounded fallback model call. Throws <see cref="ReportDiscussionModelUnavailableException"/> on provider failure or timeout.</summary>
public interface ISymbolGroundedFallbackModel
{
    Task<SymbolGroundedFallbackModelReply> AnswerAsync(
        SymbolGroundedFallbackPrompt prompt,
        Guid tenantId,
        string correlationId,
        CancellationToken cancellationToken);
}

/// <summary>Whether the cash-flow report can be offered in the closing sentence (Feature 161 flag).</summary>
public sealed record SymbolGroundedFallbackOffers(bool CashFlowEnabled);
