using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 121 change «follow-up-suggestion-pipeline» (owner, 2026-11-11): everything a follow-up provider may look at
/// for one answer. Built by both V2 paths (main workflow and fallback runner) from their typed results.
/// </summary>
public sealed record FollowUpSuggestionContext(
    AiQueryRequest Request,
    DetectedIntent Intent,
    DialogueOutcome Outcome,
    string ReplyLanguage,
    SymbolSnapshotResult? SymbolSnapshot = null,
    ProductRevenueMixResponse? ProductRevenueMix = null,
    MonthlyActivityTrendResponse? MonthlyActivityTrend = null,
    MonthlyProductTrendResult? MonthlyProductTrend = null,
    MonthlySalesQualityRankingResponse? MonthlySalesQualityRanking = null,
    FinancialStatementTableResult? FinancialStatementTable = null,
    FinancialStatementAnalysisResponse? FinancialStatementAnalysis = null,
    IndustryRelativeValuationReadModel? IndustryComparison = null)
{
    public bool IsAnsweredOrPartial => Outcome is DialogueOutcome.Answered or DialogueOutcome.PartialAnswer;
}

/// <param name="Applied">
/// True when a provider owned the answer (its actions are authoritative, even when empty); false means the guidance
/// service decides, exactly as before this pipeline.
/// </param>
public sealed record FollowUpSuggestionResult(IReadOnlyCollection<SuggestedAction>? Actions, bool Applied)
{
    public static readonly FollowUpSuggestionResult NotApplied = new(null, false);
}

/// <summary>One link of the chain: owns the follow-ups of the answers it can handle.</summary>
public interface IFollowUpSuggestionProvider
{
    /// <summary>Lower runs first. The existing providers keep their historical order (10–80); new ones come after.</summary>
    int Order { get; }

    bool CanHandle(FollowUpSuggestionContext context);

    Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken);
}

public interface IFollowUpSuggestionPipeline
{
    Task<FollowUpSuggestionResult> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Chain of Responsibility: the first provider (by <see cref="IFollowUpSuggestionProvider.Order"/>) that can handle the
/// answer returns its follow-ups; when none can, the caller falls back to <see cref="ICapabilityGuidanceService"/>.
/// </summary>
/// <remarks>
/// Feature 162 change «report-interpretation-suggestion» (owner, 2026-11-16): when the owning provider's answer holds a
/// report Report Discussion can discuss, the interpretation suggestion is put first and the list is capped at
/// <see cref="MaximumPresentedActions"/>. Answers no provider owns are unchanged.
/// </remarks>
public sealed class FollowUpSuggestionPipeline(
    IEnumerable<IFollowUpSuggestionProvider> providers,
    ReportInterpretationFollowUp? reportInterpretation = null) : IFollowUpSuggestionPipeline
{
    /// <summary>The most follow-ups one answer presents, on the web and in Telegram (owner, 2026-11-16).</summary>
    public const int MaximumPresentedActions = 5;

    private readonly IReadOnlyList<IFollowUpSuggestionProvider> _providers = providers.OrderBy(provider => provider.Order).ToArray();

    public IReadOnlyList<IFollowUpSuggestionProvider> Providers => _providers;

    public async Task<FollowUpSuggestionResult> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var provider in _providers)
        {
            if (provider.CanHandle(context))
                return new FollowUpSuggestionResult(WithInterpretationFirst(context, await provider.BuildAsync(context, cancellationToken)), true);
        }

        return FollowUpSuggestionResult.NotApplied;
    }

    private IReadOnlyCollection<SuggestedAction> WithInterpretationFirst(
        FollowUpSuggestionContext context, IReadOnlyCollection<SuggestedAction> actions)
    {
        if (reportInterpretation?.TryCreate(context) is not { } interpretation) return actions;
        return actions.Where(action => !string.Equals(action.Id, interpretation.Id, StringComparison.Ordinal))
            .Prepend(interpretation)
            .Take(MaximumPresentedActions)
            .ToArray();
    }
}
