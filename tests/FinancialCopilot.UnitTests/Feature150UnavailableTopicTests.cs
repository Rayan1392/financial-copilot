using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 150 change «unavailable-capability-refusals-and-answer-correctness», R0–R3 (BUG-004, BUG-005).
/// Strings are literals on purpose: the phrase list and the copy are checked against the implementation.
/// </summary>
public sealed class Feature150UnavailableTopicTests
{
    private const string TopicCode = "unavailable_topic";
    private const string Technical = "technical_analysis_unavailable";
    private const string Valuation = "valuation_unavailable";
    private const string Advice = "investment_advice_unavailable";

    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());

    // ---- 3.1 catalog ----

    [Theory]
    // The questions reported in the Feature 155 baseline.
    [InlineData("قیمت تعادلی فپنتا", Valuation)]
    [InlineData("فاذر ارزش خرید داره؟", Advice)]
    [InlineData("تکنیکال کگهر", Technical)]
    [InlineData("پیوت های کاما را نشان بده", Technical)]
    [InlineData("ارزش ذاتی رمپنا چقدر است؟", Valuation)]
    // Same pattern, other spellings.
    [InlineData("پیوت‌های کاما", Technical)]
    [InlineData("قيمت تعادلي فولاد", Valuation)]
    [InlineData("فولاد در این قیمت خوبه؟", Advice)]
    [InlineData("این قیمت برای فولاد مناسب است؟", Advice)]
    [InlineData("technical analysis of فولاد", Technical)]
    [InlineData("pivot points فولاد", Technical)]
    [InlineData("equilibrium price فولاد", Valuation)]
    [InlineData("intrinsic value of فولاد", Valuation)]
    public void Catalog_MatchesTheListedPhrases(string message, string reasonCode) =>
        Assert.Equal(reasonCode, UnavailableTopicCatalog.Match(message)?.ReasonCode);

    [Theory]
    [InlineData("قیمت فولاد")]
    [InlineData("آخرین قیمت فولاد")]
    [InlineData("نسبت قیمت به سود فولاد")]
    [InlineData("ارزش بازار فولاد")]
    [InlineData("تحلیل بنیادی فولاد")]
    [InlineData("تحلیل فولاد")]
    [InlineData("تحلیل جامع فولاد")]
    [InlineData("چارت فروش فولاد")]
    [InlineData("فولاد")]
    [InlineData("این قیمت فولاد است")]
    [InlineData("قیمت خوبه")]
    [InlineData("")]
    public void Catalog_DoesNotMatchOtherQuestions(string message) =>
        Assert.Null(UnavailableTopicCatalog.Match(message));

    // ---- 4.3 / 4.5 copy, registry, executor ----

    [Fact]
    public void ReasonCodes_HaveTheApprovedPersianCopyAndAnEnglishText()
    {
        Assert.Equal(
            "تحلیل تکنیکال هنوز در دستیار فعال نشده است. می‌توانم روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم.",
            ChatCapabilityAvailability.MessageForReasonCode(Technical, persian: true));
        Assert.Equal(
            "قیمت تعادلی و ارزش ذاتی هنوز در دستیار محاسبه نمی‌شوند. می‌توانم آخرین قیمت، روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم.",
            ChatCapabilityAvailability.MessageForReasonCode(Valuation, persian: true));
        Assert.Equal(
            "توصیهٔ خرید یا فروش ارائه نمی‌کنم. می‌توانم آخرین قیمت، روند فروش ماهانه یا گزارش فصلی همین نماد را نشان بدهم تا خودتان تصمیم بگیرید.",
            ChatCapabilityAvailability.MessageForReasonCode(Advice, persian: true));
        Assert.All(UnavailableTopicCatalog.Topics, topic =>
            Assert.False(string.IsNullOrWhiteSpace(ChatCapabilityAvailability.MessageForReasonCode(topic.ReasonCode, persian: false))));
    }

    [Fact]
    public void Refusal_IsTheUnsupportedTextOfTheOutcomePolicy()
    {
        var text = AiDialogueOutcomePolicy.ComposeSystemMessage(new DialogueOutcomeResult(DialogueOutcome.Unsupported, Technical, "fa", null, false));

        Assert.Equal(ChatCapabilityAvailability.MessageForReasonCode(Technical, persian: true), text);
    }

    [Fact]
    public void Registry_TopicIsGateRoutedOnly_WithAnOptionalSymbol()
    {
        var definition = Registry.Find(TopicCode);

        Assert.NotNull(definition);
        Assert.True(definition!.GateRoutedOnly);
        Assert.Empty(definition.RequiredSlots);
        Assert.Equal(["symbol"], definition.OptionalSlots.Select(slot => slot.Name));
        Assert.DoesNotContain(Registry.GetEnabled(), item => item.Code == TopicCode);
        Assert.True(new ChatCapabilityAvailability(FinancialDataChatProvider.NoavaranAmin).Evaluate(definition.ExecutionRoute).IsAvailable);
    }

    [Theory]
    [InlineData("تکنیکال کگهر", Technical)]
    [InlineData("ارزش ذاتی رمپنا چقدر است؟", Valuation)]
    [InlineData("فاذر ارزش خرید داره؟", Advice)]
    public async Task Executor_ReturnsUnsupportedWithTheTopicReasonCode_AndIsFree(string message, string reasonCode)
    {
        var topic = UnavailableTopicCatalog.Match(message)!;
        var frame = new ValidatedQueryFrame(TopicCode, Registry.Version, [], UnavailableTopicCatalog.BuildInterpretation(message, topic, Registry.Version));

        var result = await new UnavailableTopicCapabilityExecutor().ExecuteAsync(
            frame, new QueryExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test", "fa"), CancellationToken.None);

        Assert.Equal(CapabilityExecutionStatus.Unsupported, result.Status);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Null(result.Payload);
        Assert.Equal("ValidationFailed", SemanticBillingCompletionStatus.For(result.Status));
    }

    // ---- 5.1 gate ----

    [Theory]
    [InlineData("تکنیکال کگهر", Technical)]
    [InlineData("پیوت های کاما را نشان بده", Technical)]
    [InlineData("قیمت تعادلی فپنتا", Valuation)]
    [InlineData("ارزش ذاتی رمپنا چقدر است؟", Valuation)]
    [InlineData("فاذر ارزش خرید داره؟", Advice)]
    public async Task Gate_ListedTopic_RoutesToTheRefusalWithoutCallingTheModel(string message, string reasonCode)
    {
        var run = await PrepareAsync(message);

        var frame = run.Result.Request.SemanticFrame;
        Assert.Equal(TopicCode, frame?.CapabilityCode);
        Assert.Equal("SemanticPrimary", run.Result.Request.SemanticRoutingDisposition);
        Assert.Equal(reasonCode, UnavailableTopicCatalog.Match(frame!.Interpretation.OriginalText)?.ReasonCode);
        Assert.Equal(0, run.Provider.Calls);
        var slot = Assert.Single(frame.Slots);
        Assert.Equal(QuerySlotType.CompanyOrSymbol, slot.Type);
        Assert.Equal("کگل", slot.Value);
        Assert.Equal(QuerySlotValidationState.Valid, slot.ValidationState);
    }

    [Theory]
    [InlineData(ResolverKind.Missing)]
    [InlineData(ResolverKind.Ambiguous)]
    [InlineData(ResolverKind.Throwing)]
    public async Task Gate_NoSingleCompany_StillRefuses_WithAnEmptySlot(ResolverKind resolver)
    {
        var run = await PrepareAsync("تکنیکال نمونه", resolver: resolver);

        Assert.Equal(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Empty(run.Result.Request.SemanticFrame!.Slots);
        Assert.Equal(0, run.Provider.Calls);
    }

    [Theory]
    [InlineData("قیمت فولاد")]
    [InlineData("آخرین قیمت فولاد")]
    [InlineData("نسبت قیمت به سود فولاد")]
    [InlineData("ارزش بازار فولاد")]
    [InlineData("تحلیل بنیادی فولاد")]
    [InlineData("چارت فروش فولاد")]
    public async Task Gate_OtherQuestions_KeepTodaysRoute(string message) =>
        AssertTodayRoute(await PrepareAsync(message));

    [Fact]
    public async Task Gate_CyclicalWavesBaseProvider_KeepsTodaysRoute() =>
        AssertTodayRoute(await PrepareAsync("قیمت تعادلی فولاد", availability: Availability(FinancialDataChatProvider.CyclicalWaves)));

    [Fact]
    public async Task Gate_NoAvailabilityService_KeepsTodaysRoute() =>
        AssertTodayRoute(await PrepareAsync("تکنیکال کگل", availability: null, withAvailability: false));

    [Fact]
    public async Task Gate_FrozenV1_KeepsTodaysRoute()
    {
        AssertTodayRoute(await PrepareAsync("تکنیکال کگل", scope: Feature154RoutingScope.V1Frozen));

        using (Feature154RoutingScope.EnterFrozenV1())
            AssertTodayRoute(await PrepareAsync("تکنیکال کگل", scope: Feature154RoutingScope.V2));
    }

    [Theory]
    [InlineData(SemanticRoutingMode.Rollback)]
    [InlineData(SemanticRoutingMode.Legacy)]
    public async Task Gate_RollbackMode_KeepsTodaysRoute(SemanticRoutingMode mode)
    {
        var run = await PrepareAsync("تکنیکال کگل", mode: mode);

        Assert.NotEqual(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.NotEqual(TopicCode, run.Result.Request.SemanticShadowFrame?.CapabilityCode);
        Assert.Equal(1, run.Provider.Calls);
    }

    [Fact]
    public async Task Gate_ShadowMode_StillRefuses()
    {
        var run = await PrepareAsync("تکنیکال کگل", mode: SemanticRoutingMode.Shadow);

        Assert.Equal(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
    }

    [Fact]
    public async Task Gate_PendingQuestion_KeepsTheExistingFlow() =>
        AssertTodayRoute(await PrepareAsync("تکنیکال کگل", pending: true));

    // ---- 5.1a Gate B conditions C1–C3 ----

    // «تحلیل تکنیکال X» now goes to comprehensive analysis directly (BUG-020, test above).
    [Theory]
    [InlineData("تحلیل ارزش ذاتی فولاد")]
    public async Task Gate_AnalysisQuestionWithATopicWord_KeepsTodaysRoute_WhileComprehensiveAnalysisIsAvailable(string message) =>
        AssertTodayRoute(await PrepareAsync(message, availability: MappedAnalysis()));

    [Fact]
    public async Task Gate_AnalysisQuestionWithATopicWord_IsRefused_WhenComprehensiveAnalysisIsUnavailable()
    {
        var run = await PrepareAsync("تحلیل تکنیکال فولاد", availability: Availability(FinancialDataChatProvider.NoavaranAmin));

        Assert.Equal(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
    }

    // BUG-022 (owner, 2026-11-11): these go to comprehensive analysis without the model, whose proposal here is a
    // different capability (product_revenue_mix), so the model can no longer change their route.
    [Theory]
    [InlineData("تحلیل بنیادی فولاد")]
    [InlineData("تحلیل فولاد")]
    [InlineData("تحلیل جامع فولاد")]
    [InlineData("تحلیل اخابر")]
    public async Task Gate_AnalysisQuestions_GoToComprehensiveAnalysisWithoutTheModel(string message)
    {
        var run = await PrepareAsync(message, availability: MappedAnalysis());

        var frame = run.Result.Request.SemanticFrame;
        Assert.Equal("comprehensive_analysis", frame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
        Assert.Equal("کگل", frame!.Slots.Single(slot => slot.Type == QuerySlotType.CompanyOrSymbol).Value);
        Assert.DoesNotContain(frame.Slots, slot => slot.Type == QuerySlotType.AnalysisTopic && slot.Value is not null);
    }

    [Fact]
    public async Task Gate_AnalysisQuestion_WhenTheRestIsNotACompany_KeepsTodaysRoute() =>
        AssertTodayRoute(await PrepareAsync("تحلیل فروش فولاد", availability: MappedAnalysis(), resolver: ResolverKind.Missing));

    [Fact]
    public async Task Gate_AnalysisQuestion_WithoutComprehensiveAnalysis_KeepsTodaysRoute() =>
        AssertTodayRoute(await PrepareAsync("تحلیل فولاد", availability: Availability(FinancialDataChatProvider.NoavaranAmin)));

    [Theory]
    [InlineData("تحلیل اخابر", "اخابر")]
    [InlineData("تحلیل جامع فولاد", "فولاد")]
    [InlineData("تحلیل بنیادی مخابرات ایران", "مخابرات ایران")]
    [InlineData("تحلیل فروش فولاد", "فروش فولاد")]
    [InlineData("فولاد", null)]
    [InlineData("تحلیل", null)]
    [InlineData("تحلیل جامع", null)]
    public void AnalysisRequestCompanyText_IsTheRestAfterTheAnalysisWords(string message, string? expected) =>
        Assert.Equal(expected, UnavailableTopicCatalog.AnalysisRequestCompanyText(message));

    // ---- BUG-020: technical questions search TahlilApp while comprehensive analysis is available ----

    [Theory]
    [InlineData("تکنیکال کگل")]
    [InlineData("پیوت های کگل را نشان بده")]
    [InlineData("تحلیل تکنیکال کگل")]
    public async Task Gate_TechnicalQuestion_WithAvailableAnalysis_GoesToComprehensiveAnalysisWithoutTheModel(string message)
    {
        var run = await PrepareAsync(message, availability: MappedAnalysis());

        var frame = run.Result.Request.SemanticFrame;
        Assert.Equal("comprehensive_analysis", frame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
        Assert.Equal("کگل", frame!.Slots.Single(slot => slot.Type == QuerySlotType.CompanyOrSymbol).Value);
        Assert.Equal(ComprehensiveAnalysisTopics.TechnicalAnalysis, frame.Slots.Single(slot => slot.Type == QuerySlotType.AnalysisTopic).Value);
    }

    [Fact]
    public async Task Gate_TechnicalQuestion_WithoutACompany_IsStillRefused()
    {
        var run = await PrepareAsync("تکنیکال نمونه", availability: MappedAnalysis(), resolver: ResolverKind.Missing);

        Assert.Equal(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
    }

    [Fact]
    public async Task Gate_ValuationQuestion_WithAvailableAnalysis_IsStillRefused()
    {
        var run = await PrepareAsync("ارزش ذاتی کگل چقدر است؟", availability: MappedAnalysis());

        Assert.Equal(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
    }

    [Theory]
    [InlineData("پیوت های کاما را نشان بده", "کاما را نشان بده")]
    [InlineData("فاذر ارزش خرید داره؟", "فاذر داره")]
    [InlineData("تحلیل تکنیکال فولاد", "فولاد")]
    public void WithoutTopicWords_LeavesTheCompanyPart(string message, string expected) =>
        Assert.Equal(expected, UnavailableTopicCatalog.WithoutTopicWords(message));

    [Fact]
    public void Card_NoTechnicalAnalysis_SaysSoBeforeTheLatestAnalysis()
    {
        var item = new ComprehensiveAnalysisSummaryItem(1, "کگل- گزارش عملکرد مهر ۱۴۰۵", "1405/08/19", "", "", [], DateTimeOffset.UtcNow);

        var markdown = ComprehensiveAnalysisCardComposer.ComposeMarkdown([item], [], "https://tahlilapp.com/app/analysis", ["کگل"]);
        var plain = ComprehensiveAnalysisCardComposer.ComposePlainText([item], [], "https://tahlilapp.com/app/analysis", ["کگل"]);

        Assert.StartsWith("برای نماد کگل تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.\n\n### کگل- گزارش عملکرد مهر ۱۴۰۵", markdown);
        Assert.StartsWith("برای نماد کگل تحلیل تکنیکالی در تحلیل‌اپ پیدا نشد.\nکگل- گزارش عملکرد مهر ۱۴۰۵", plain);
        Assert.StartsWith("### کگل- گزارش عملکرد مهر ۱۴۰۵", ComprehensiveAnalysisCardComposer.ComposeMarkdown([item], [], "https://tahlilapp.com/app/analysis"));
    }

    [Fact]
    public async Task Gate_EligibleTickerEqualToATopicWord_SnapshotWins()
    {
        var run = await PrepareAsync("پیوت", eligibleTicker: "پیوت");

        Assert.Equal(SymbolSnapshotCapability.Code, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
    }

    // ---- Harness (same shape as AssistantHelpReply121Tests) ----

    public enum ResolverKind { Resolved, Missing, Ambiguous, Throwing }

    private static ChatCapabilityAvailability Availability(FinancialDataChatProvider provider) => new(provider);

    private static ChatCapabilityAvailability MappedAnalysis() =>
        new(FinancialDataChatProvider.NoavaranAmin, ChatCapabilitySourceMap.Create(new FinancialDataOptions
        {
            ChatProvider = FinancialDataChatProvider.NoavaranAmin,
            CapabilitySources = { ["comprehensive_analysis"] = "CyclicalWaves" }
        }));

    private static void AssertTodayRoute(GateRun run)
    {
        Assert.NotEqual(TopicCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(1, run.Provider.Calls);
    }

    private sealed record GateRun(ConversationDialogueGateResult Result, WrongCapabilityProposalProvider Provider);

    private static async Task<GateRun> PrepareAsync(
        string message,
        Feature154RoutingScope? scope = null,
        SemanticRoutingMode mode = SemanticRoutingMode.SemanticPrimary,
        string? eligibleTicker = null,
        bool pending = false,
        ResolverKind resolver = ResolverKind.Resolved,
        IChatCapabilityAvailability? availability = null,
        bool withAvailability = true)
    {
        var states = new ConversationTaskStateService(new InMemoryTaskStates(), TimeProvider.System, new ConversationTaskStateOptions());
        var stateScope = new ConversationTaskStateScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        if (pending)
            await states.RecordPendingAsync(
                stateScope,
                "monthly_activity_trend",
                [],
                new PendingDialogueAction(PendingDialogueActionKind.Clarification, QuerySlotType.CompanyOrSymbol, [], "required_input_missing", Guid.NewGuid(), 0),
                "seed",
                CancellationToken.None);

        var provider = new WrongCapabilityProposalProvider();
        var interpreter = new DeterministicCapabilityInterpreter(Registry);
        var gate = new ConversationDialogueGate(
            states,
            interpreter,
            new FakeEntityResolver(resolver),
            new CapabilitySlotValidator(Registry),
            new NoDirectMetrics(),
            new SemanticRoutingRolloutCoordinator(new SemanticRoutingOptions(DefaultMode: mode), new NullSemanticRoutingTelemetrySink()),
            new NoMessages(),
            TimeProvider.System,
            frameEnricher: new SemanticQueryFrameEnricher(scope),
            hybridInterpreter: new HybridCapabilityInterpreter(interpreter, Registry, new QueryInterpretationValidator(Registry), provider),
            feature154: scope,
            eligibleSymbolResolver: new FakeEligible(eligibleTicker),
            symbolSnapshotOptions: new SymbolSnapshotOptions { Enabled = eligibleTicker is not null },
            capabilityRegistry: Registry,
            capabilityAvailability: withAvailability ? availability ?? Availability(FinancialDataChatProvider.NoavaranAmin) : null);
        var request = new AiQueryRequest(message, stateScope.TenantId, stateScope.ActorId, Guid.NewGuid().ToString("N"));
        return new GateRun(await gate.PrepareAsync(request, stateScope.ConversationId, CancellationToken.None), provider);
    }

    private sealed class WrongCapabilityProposalProvider : IQueryInterpretationProposalProvider
    {
        public int Calls { get; private set; }

        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<QueryInterpretationProposal?>(
                new QueryInterpretationProposal(["product_revenue_mix"], [], null, 0.9m, []));
        }
    }

    private sealed class FakeEligible(string? ticker) : IEligibleSymbolResolver
    {
        public Task<EligibleSymbol?> ResolveAsync(string compactKey, CancellationToken cancellationToken) =>
            Task.FromResult(compactKey == ticker ? new EligibleSymbol(ticker!, "شرکت نمونه", "بورس", "گروه نمونه") : null);
    }

    private sealed class FakeEntityResolver(ResolverKind kind) : ICanonicalQueryEntityResolver
    {
        private static CanonicalQueryEntity Entity(string symbol) => new(Guid.NewGuid(), symbol, "شرکت نمونه", "company", "exact_ticker", "4");

        private EntityResolutionResult Result() => kind switch
        {
            ResolverKind.Resolved => new EntityResolutionResult.Resolved(Entity("کگل"), new EntityResolutionEvidence("exact_ticker", 1m)),
            ResolverKind.Ambiguous => new EntityResolutionResult.Ambiguous(
                [new EntityResolutionCandidate(Entity("کگل"), 0.9m, "exact_ticker"), new EntityResolutionCandidate(Entity("کگل"), 0.9m, "exact_ticker")]),
            ResolverKind.Throwing => throw new InvalidOperationException("simulated resolver failure"),
            _ => new EntityResolutionResult.Missing("CompanyOrSymbol")
        };

        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult(Result());
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>(Result() is EntityResolutionResult.Resolved resolved ? [resolved] : []);
    }

    private sealed class InMemoryTaskStates : IConversationTaskStateRepository
    {
        private readonly Dictionary<ConversationTaskStateScope, ConversationTaskState> states = [];

        public Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken) =>
            Task.FromResult(states.GetValueOrDefault(scope));

        public Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken)
        {
            states[new(state.ConversationId, state.TenantId, state.ActorId)] = state;
            return Task.FromResult(new ConversationTaskStateWriteResult(true, state));
        }

        public Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
        {
            states.Remove(scope);
            return Task.CompletedTask;
        }
    }

    private sealed class NoMessages : IMessageRepository
    {
        public Task<Guid> AppendAsync(Guid conversationId, MessageRole role, string content, string? scannerQueryPlanJson, DateTimeOffset createdAt, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MessageRecord>>([]);
    }

    private sealed class NoDirectMetrics : IDirectMetricRoutingRegistry
    {
        public DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf) => null;
        public IReadOnlyList<DirectMetricRoutingMatch> ResolveAll(string userMessage, DateOnly asOf) => [];
        public bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf) => false;
        public SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode) => null;
        public string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector) => metricCode.Value;
        public string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match) => userMessage;
    }
}
