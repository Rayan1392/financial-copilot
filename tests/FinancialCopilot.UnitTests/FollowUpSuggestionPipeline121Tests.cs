using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 121 change «follow-up-suggestion-pipeline» (owner, 2026-11-11): one Chain of Responsibility for every
/// answer's follow-ups. The first eight providers keep the former if/else order; the new ones only add follow-ups
/// to Answered results, so suggestions that existed before do not change.
/// </summary>
public sealed class FollowUpSuggestionPipeline121Tests
{
    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());
    private static readonly RelatedFollowUpActionFactory Factory = new(Registry, new DeterministicCapabilityInterpreter(Registry));

    // ---- the chain ----

    [Fact]
    public async Task Pipeline_RunsProvidersByOrder_AndTheFirstThatCanHandleWins()
    {
        var pipeline = new FollowUpSuggestionPipeline([new Fixed(30, true, "c"), new Fixed(10, false, "a"), new Fixed(20, true, "b")]);

        var result = await pipeline.BuildAsync(Context(DialogueOutcome.Answered), default);

        Assert.True(result.Applied);
        Assert.Equal("b", Assert.Single(result.Actions!).Id);
    }

    [Fact]
    public async Task Pipeline_NoProviderCanHandle_IsNotApplied_SoTheGuidanceServiceDecidesAsBefore()
    {
        var result = await new FollowUpSuggestionPipeline([new Fixed(10, false, "a")]).BuildAsync(Context(DialogueOutcome.PartialAnswer), default);

        Assert.False(result.Applied);
        Assert.Null(result.Actions);
    }

    [Fact]
    public async Task Pipeline_AProviderThatOwnsTheAnswerWithNoActions_IsStillApplied()
    {
        var result = await new FollowUpSuggestionPipeline([new Fixed(10, true, null)]).BuildAsync(Context(DialogueOutcome.Answered), default);

        Assert.True(result.Applied);
        Assert.Empty(result.Actions!);
    }

    [Fact]
    public void Providers_KeepTheFormerChainOrder_AndTheNewOnesComeLast()
    {
        IFollowUpSuggestionProvider[] providers =
        [
            new SingleSymbolFollowUpProvider(Factory),
            new BalanceSheetReportFollowUpProvider(Factory),
            new FinancialStatementAnalysisFollowUpProvider(null!),
            new QuarterlyFinancialReportFollowUpProvider(null!),
            new MonthlySalesQualityRankingFollowUpProvider(null!),
            new MonthlyProductTrendFollowUpProvider(null!),
            new MonthlySalesProductFollowUpProvider(null!),
            new ProductRevenueMixFollowUpProvider(null!),
            new AssistantHelpFollowUpProvider(null!),
            new SymbolSnapshotFollowUpProvider(null!)
        ];

        var ordered = new FollowUpSuggestionPipeline(providers).Providers.Select(provider => provider.GetType().Name);

        Assert.Equal(
        [
            nameof(SymbolSnapshotFollowUpProvider), nameof(AssistantHelpFollowUpProvider), nameof(ProductRevenueMixFollowUpProvider),
            nameof(MonthlySalesProductFollowUpProvider), nameof(MonthlyProductTrendFollowUpProvider),
            nameof(MonthlySalesQualityRankingFollowUpProvider), nameof(QuarterlyFinancialReportFollowUpProvider),
            nameof(FinancialStatementAnalysisFollowUpProvider), nameof(BalanceSheetReportFollowUpProvider), nameof(SingleSymbolFollowUpProvider)
        ], ordered);
    }

    // ---- the new providers never touch outcomes that had suggestions before ----

    [Theory]
    [InlineData(DialogueOutcome.PartialAnswer)]
    [InlineData(DialogueOutcome.NoData)]
    [InlineData(DialogueOutcome.Unsupported)]
    [InlineData(DialogueOutcome.ClarificationNeeded)]
    public void NewProviders_OnlyHandleAnsweredResults(DialogueOutcome outcome)
    {
        var context = Context(outcome, table: Table(BalanceSheetOutcomes.Available, false), frame: Frame("financial_statement_table", "فولاد"));

        Assert.False(new BalanceSheetReportFollowUpProvider(Factory).CanHandle(context));
        Assert.False(new SingleSymbolFollowUpProvider(Factory).CanHandle(context));
    }

    // ---- balance sheet («ترازنامه X», «صورت وضعیت X») ----

    [Fact]
    public async Task BalanceSheet_SuggestsTheQuarterlyReportAndTheMonthlySalesTrend()
    {
        var provider = new BalanceSheetReportFollowUpProvider(Factory);
        var context = Context(DialogueOutcome.Answered, table: Table(BalanceSheetOutcomes.Available, false));

        Assert.True(provider.CanHandle(context));
        var actions = await provider.BuildAsync(context, default);

        Assert.Equal(["گزارش فصلی فولاد", "روند فروش ماهانه فولاد را نشان بده"], actions.Select(action => action.Message));
        Assert.All(actions, action => Assert.Equal(SuggestedActionKind.RunRelatedCapability, action.Kind));
        Assert.Equal(["financial_statement_table", "monthly_activity_trend"], actions.Select(action => action.CapabilityCode));
    }

    [Fact]
    public async Task BalanceSheet_WithAConsolidatedAlternative_AlsoSuggestsIt()
    {
        var actions = await new BalanceSheetReportFollowUpProvider(Factory)
            .BuildAsync(Context(DialogueOutcome.Answered, table: Table(BalanceSheetOutcomes.Available, true)), default);

        Assert.Contains("ترازنامه تلفیقی فولاد", actions.Select(action => action.Message));
    }

    [Fact]
    public void BalanceSheet_ThatIsNotAvailable_IsNotHandled() =>
        Assert.False(new BalanceSheetReportFollowUpProvider(Factory)
            .CanHandle(Context(DialogueOutcome.Answered, table: Table(BalanceSheetOutcomes.NoSnapshotAtDate, false))));

    // ---- any other answered single-symbol question ----

    [Fact]
    public async Task SingleSymbol_MetricLookup_GetsTheRelatedCapabilities()
    {
        var provider = new SingleSymbolFollowUpProvider(Factory, new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves));
        var context = Context(DialogueOutcome.Answered, frame: Frame("symbol_metric_lookup", "فولاد"), intent: DetectedIntent.SymbolLookup);

        Assert.True(provider.CanHandle(context));
        var actions = await provider.BuildAsync(context, default);

        Assert.Equal(["گزارش فصلی فولاد", "روند فروش ماهانه فولاد را نشان بده", "ترازنامه فولاد", "تحلیل جامع فولاد"],
            actions.Select(action => action.Message));
    }

    [Fact]
    public async Task SingleSymbol_DoesNotSuggestTheCapabilityItJustRan()
    {
        var provider = new SingleSymbolFollowUpProvider(Factory, new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves));

        var afterAnalysis = await provider.BuildAsync(
            Context(DialogueOutcome.Answered, frame: Frame("comprehensive_analysis", "فولاد"), intent: DetectedIntent.ComprehensiveAnalysis), default);
        var afterQuarterly = await provider.BuildAsync(
            Context(DialogueOutcome.Answered, frame: Frame("financial_statement_table", "فولاد"), intent: DetectedIntent.FinancialStatementTableLookup), default);

        Assert.DoesNotContain("تحلیل جامع فولاد", afterAnalysis.Select(action => action.Message));
        Assert.DoesNotContain("گزارش فصلی فولاد", afterQuarterly.Select(action => action.Message));
        Assert.Contains("ترازنامه فولاد", afterQuarterly.Select(action => action.Message));
    }

    [Fact]
    public async Task SingleSymbol_WithoutComprehensiveAnalysis_DoesNotOfferIt()
    {
        var actions = await new SingleSymbolFollowUpProvider(Factory, new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin))
            .BuildAsync(Context(DialogueOutcome.Answered, frame: Frame("symbol_metric_lookup", "فولاد"), intent: DetectedIntent.SymbolLookup), default);

        Assert.DoesNotContain("تحلیل جامع فولاد", actions.Select(action => action.Message));
    }

    [Fact]
    public void SingleSymbol_SeveralCompanies_IsNotHandled()
    {
        var single = Frame("symbol_metric_lookup", "فولاد");
        var frame = single with
        {
            Slots = [.. single.Slots,
                new ResolvedQuerySlot(QuerySlotType.CompaniesOrSymbols, "فولاد,فملی", QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid)]
        };

        Assert.False(new SingleSymbolFollowUpProvider(Factory).CanHandle(Context(DialogueOutcome.Answered, frame: frame)));
    }

    [Fact]
    public void SingleSymbol_NoCompany_IsNotHandled() =>
        Assert.False(new SingleSymbolFollowUpProvider(Factory).CanHandle(Context(DialogueOutcome.Answered)));

    // ---- helpers ----

    private static FollowUpSuggestionContext Context(
        DialogueOutcome outcome,
        FinancialStatementTableResult? table = null,
        ValidatedQueryFrame? frame = null,
        DetectedIntent intent = DetectedIntent.FinancialStatementTableLookup) =>
        new(new AiQueryRequest("پرسش", Guid.NewGuid(), Guid.NewGuid(), "c") { SemanticFrame = frame },
            intent, outcome, "fa", FinancialStatementTable: table);

    private static ValidatedQueryFrame Frame(string capability, string symbol) => new(
        capability, 1,
        [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, symbol, QueryValueProvenance.UserExplicit, 1m, QuerySlotValidationState.Valid,
            capability, CanonicalEntity: new CanonicalQueryEntity(Guid.NewGuid(), symbol, "شرکت", "company", "exact_ticker"))],
        new(symbol, symbol, "fa", [], [], [], null, null, null, [], [], 1m, [], 1));

    private static FinancialStatementTableResult Table(string outcome, bool consolidatedAlternative) => new(
        null, [], [], [], null, DateTimeOffset.UtcNow,
        BalanceSheetReport: new BalanceSheetReportResult(
            BalanceSheetReportContract.Version, outcome, null, null, null, null, null, null, consolidatedAlternative, null,
            BalanceSheetReportContract.Title, new BalanceSheetCompany("فولاد", "فولاد مبارکه", "NoavaranCurrentApi", "1"),
            null, new Dictionary<string, BalanceSheetSnapshot>(), null, BalanceSheetViews.Default, null,
            new BalanceSheetChart(5, []), DateTimeOffset.UtcNow));

    private sealed class Fixed(int order, bool canHandle, string? id) : IFollowUpSuggestionProvider
    {
        public int Order => order;
        public bool CanHandle(FollowUpSuggestionContext context) => canHandle;
        public Task<IReadOnlyCollection<SuggestedAction>> BuildAsync(FollowUpSuggestionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SuggestedAction>>(id is null
                ? []
                : [new SuggestedAction(id, SuggestedActionKind.RunRelatedCapability, id, id, "x", new Dictionary<string, string>(), "test", 1)]);
    }
}
