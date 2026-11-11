using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

// Feature 121 change «follow-up-suggestion-pipeline»: the eight result-specific follow-up services that existed before
// the pipeline, as providers. Conditions and order are copied unchanged from the former if/else chain in
// FinancialCopilotWorkflowDefinition, and each adapter only delegates, so their suggestions do not change.

/// <summary>Feature 156: a Symbol Snapshot card always owns its follow-ups.</summary>
public sealed class SymbolSnapshotFollowUpProvider(ISymbolSnapshotFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 10;
    public bool CanHandle(FollowUpSuggestionContext context) => context.SymbolSnapshot is not null;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.SymbolSnapshot!));
}

/// <summary>Feature 121 change «assistant-greeting-and-help-reply».</summary>
public sealed class AssistantHelpFollowUpProvider(IAssistantHelpReplyComposer composer) : IFollowUpSuggestionProvider
{
    public int Order => 20;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Request.SemanticFrame?.CapabilityCode == AssistantHelpCapability.Code &&
        context.Outcome == DialogueOutcome.Answered;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<SuggestedAction>>(composer.Compose(context.ReplyLanguage).Actions);
}

/// <summary>Feature 075 extension: the typed product revenue mix owns its follow-ups, including empty.</summary>
public sealed class ProductRevenueMixFollowUpProvider(IProductRevenueMixFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 30;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Intent == DetectedIntent.ProductRevenueMix &&
        context.ProductRevenueMix is not null &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.ProductRevenueMix!));
}

/// <summary>Feature 137: a monthly sales trend with current-year sales.</summary>
public sealed class MonthlySalesProductFollowUpProvider(IMonthlySalesProductFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 40;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Intent == DetectedIntent.MonthlyActivityTrend &&
        context.MonthlyActivityTrend is not null &&
        context.MonthlyActivityTrend.ChartPoints.Any(point => point.CurrentFiscalYearSalesAmount.HasValue) &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        service.BuildAsync(context.MonthlyActivityTrend!, cancellationToken);
}

/// <summary>Feature 138: a resolved, usable typed product trend (result-driven, never phrasing).</summary>
public sealed class MonthlyProductTrendFollowUpProvider(IMonthlyProductTrendFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 50;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Intent == DetectedIntent.MonthlyProductTrend &&
        context.MonthlyProductTrend is { IsResolved: true } productTrend &&
        productTrend.Points.Any(point => !point.IsGap && point.SalesValueMillionRial.HasValue) &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.MonthlyProductTrend!));
}

public sealed class MonthlySalesQualityRankingFollowUpProvider(IMonthlySalesQualityRankingFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 60;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.Intent == DetectedIntent.MonthlySalesQualityRanking &&
        context.MonthlySalesQualityRanking is { Items.Count: > 0 } &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.MonthlySalesQualityRanking!));
}

/// <summary>Feature 144: a successful or partial quarterly report table.</summary>
public sealed class QuarterlyFinancialReportFollowUpProvider(IQuarterlyFinancialReportFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 70;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.FinancialStatementTable?.QuarterlyResult is
            { Outcome: QuarterlyFinancialReportOutcome.SuccessfulTable or QuarterlyFinancialReportOutcome.PartialTable } &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.FinancialStatementTable!.QuarterlyResult!));
}

public sealed class FinancialStatementAnalysisFollowUpProvider(IFinancialStatementAnalysisFollowUpSuggestionService service) : IFollowUpSuggestionProvider
{
    public int Order => 80;
    public bool CanHandle(FollowUpSuggestionContext context) =>
        context.FinancialStatementAnalysis is { SourceReferences.Count: > 0 } &&
        context.IsAnsweredOrPartial;
    public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
        Task.FromResult(service.Build(context.FinancialStatementAnalysis!));
}
