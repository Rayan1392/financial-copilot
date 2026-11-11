using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

// Feature 121 change «follow-up-suggestion-pipeline» (owner, 2026-11-11): follow-ups for answered questions that had
// none. Both providers run after the eight existing ones and only for an Answered outcome, where the guidance service
// returns nothing; a partial or unanswered outcome keeps today's guidance suggestions.

/// <summary>
/// Builds one «run a related capability» action and keeps it only when its text routes back to that capability with the
/// same single company, the check the existing follow-up services use.
/// </summary>
public sealed class RelatedFollowUpActionFactory(IConversationalCapabilityRegistry registry, ICapabilityInterpreter interpreter)
{
    public const int MaximumActions = 4;

    public SuggestedAction? TryCreate(string capabilityCode, string message, string symbol, string relevanceReason)
    {
        if (registry.Find(capabilityCode) is not { Enabled: true })
            return null;

        try
        {
            var interpretation = interpreter.Interpret(message);
            // The texts are fixed templates naming only this symbol, so the symbol must be among the mentions; the
            // deterministic interpreter also lists untyped words («ترازنامه», «تلفیقی») as mentions, so their count is not
            // checked here (the existing services keep their stricter rule).
            if (interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode != capabilityCode ||
                interpretation.MissingSlots.Count != 0 ||
                !interpretation.EntityMentions.Any(entity => Canonical(entity.Text) == Canonical(symbol)))
                return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var idInput = string.Join('\n', relevanceReason, capabilityCode, registry.Version, Canonical(symbol), message);
        var id = "related:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant()[..32];
        return new SuggestedAction(
            id,
            SuggestedActionKind.RunRelatedCapability,
            message,
            message,
            capabilityCode,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
            relevanceReason,
            registry.Version);
    }

    public static string Canonical(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : QueryNormalization.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("‌", string.Empty, StringComparison.Ordinal);
}

/// <summary>Related follow-up texts shared by the new providers (Persian, like the existing follow-up services).</summary>
public static class RelatedFollowUpTexts
{
    public static string QuarterlyReport(string symbol) => $"گزارش فصلی {symbol}";
    public static string MonthlySalesTrend(string symbol) => $"روند فروش ماهانه {symbol} را نشان بده";
    public static string BalanceSheet(string symbol) => $"ترازنامه {symbol}";
    public static string ConsolidatedBalanceSheet(string symbol) => $"ترازنامه تلفیقی {symbol}";
    public static string ComprehensiveAnalysis(string symbol) => $"تحلیل جامع {symbol}";
}

/// <summary>A successful Feature 154 balance-sheet report («ترازنامه X», «صورت وضعیت X»).</summary>
public sealed class BalanceSheetReportFollowUpProvider(RelatedFollowUpActionFactory factory) : IFollowUpSuggestionProvider
{
    public const string RelevanceReason = "balance_sheet_report_follow_up";

    public int Order => 90;

    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Outcome == DialogueOutcome.Answered &&
        context.FinancialStatementTable?.BalanceSheetReport is { Outcome: BalanceSheetOutcomes.Available } report &&
        !string.IsNullOrWhiteSpace(report.Company.Symbol);

    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken)
    {
        var report = context.FinancialStatementTable!.BalanceSheetReport!;
        var symbol = report.Company.Symbol.Trim();
        var candidates = new List<(string Code, string Message)>
        {
            ("financial_statement_table", RelatedFollowUpTexts.QuarterlyReport(symbol)),
            ("monthly_activity_trend", RelatedFollowUpTexts.MonthlySalesTrend(symbol))
        };
        if (report.ConsolidatedAlternativeAvailable)
            candidates.Add(("financial_statement_table", RelatedFollowUpTexts.ConsolidatedBalanceSheet(symbol)));

        IReadOnlyCollection<SuggestedAction> actions = candidates
            .Select(candidate => factory.TryCreate(candidate.Code, candidate.Message, symbol, RelevanceReason))
            .OfType<SuggestedAction>()
            .Take(RelatedFollowUpActionFactory.MaximumActions)
            .ToArray();
        return Task.FromResult(actions);
    }
}

/// <summary>
/// Any other answered question about one company: the related capabilities it did not just run — quarterly report,
/// monthly sales trend, balance sheet, and comprehensive analysis while that is available.
/// </summary>
public sealed class SingleSymbolFollowUpProvider(
    RelatedFollowUpActionFactory factory,
    IChatCapabilityAvailability? capabilityAvailability = null) : IFollowUpSuggestionProvider
{
    public const string RelevanceReason = "single_symbol_follow_up";

    public int Order => 100;

    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Outcome == DialogueOutcome.Answered && Symbol(context) is not null;

    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken)
    {
        var symbol = Symbol(context)!;
        var justRun = JustRun(context);
        var candidates = new List<(RelatedKind Kind, string Code, string Message)>
        {
            (RelatedKind.QuarterlyReport, "financial_statement_table", RelatedFollowUpTexts.QuarterlyReport(symbol)),
            (RelatedKind.MonthlySalesTrend, "monthly_activity_trend", RelatedFollowUpTexts.MonthlySalesTrend(symbol)),
            (RelatedKind.BalanceSheet, "financial_statement_table", RelatedFollowUpTexts.BalanceSheet(symbol))
        };
        if (capabilityAvailability?.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable == true)
            candidates.Add((RelatedKind.ComprehensiveAnalysis, CapabilityExecutionRoutes.ComprehensiveAnalysis, RelatedFollowUpTexts.ComprehensiveAnalysis(symbol)));

        IReadOnlyCollection<SuggestedAction> actions = candidates
            .Where(candidate => candidate.Kind != justRun)
            .Select(candidate => factory.TryCreate(candidate.Code, candidate.Message, symbol, RelevanceReason))
            .OfType<SuggestedAction>()
            .Take(RelatedFollowUpActionFactory.MaximumActions)
            .ToArray();
        return Task.FromResult(actions);
    }

    private enum RelatedKind { None, QuarterlyReport, MonthlySalesTrend, BalanceSheet, ComprehensiveAnalysis }

    /// <summary>One company only: a question about several companies gets no single-symbol follow-ups.</summary>
    private static string? Symbol(FollowUpSuggestionContext context)
    {
        var frame = context.Request.SemanticFrame;
        if (frame?.Slots.Any(slot => slot.Type == QuerySlotType.CompaniesOrSymbols &&
                                     slot.ValidationState == QuerySlotValidationState.Valid &&
                                     slot.Value?.Contains(',', StringComparison.Ordinal) == true) == true)
            return null;

        var symbol = CapabilityGuidanceSubject.From(frame, context.Intent, context.Request.Message).Symbol?.Trim();
        return string.IsNullOrWhiteSpace(symbol) || symbol.Length > 120 ? null : symbol;
    }

    private static RelatedKind JustRun(FollowUpSuggestionContext context)
    {
        var capability = context.Request.SemanticFrame?.CapabilityCode;
        if (capability == CapabilityExecutionRoutes.ComprehensiveAnalysis || context.Intent == DetectedIntent.ComprehensiveAnalysis)
            return RelatedKind.ComprehensiveAnalysis;
        if (capability == "monthly_activity_trend" || context.Intent == DetectedIntent.MonthlyActivityTrend)
            return RelatedKind.MonthlySalesTrend;
        if (capability == "financial_statement_table" || context.Intent == DetectedIntent.FinancialStatementTableLookup)
            return context.FinancialStatementTable is { } table && (table.BalanceSheetReport is not null || table.BalanceSheetRows.Count > 0)
                ? RelatedKind.BalanceSheet
                : RelatedKind.QuarterlyReport;
        return RelatedKind.None;
    }
}
