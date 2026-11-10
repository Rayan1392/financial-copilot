using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 121 change: the assistant greeting and help reply. All questions are invented, same-pattern text.
/// Strings are literals on purpose: the phrase list here is checked against the implementation, not derived from it.
/// </summary>
public sealed class AssistantHelpReply121Tests
{
    private const string HelpCode = "assistant_help";
    private const string Symbol = "کگل";

    private static readonly ConversationalCapabilityRegistry Registry = new(InitialConversationalCapabilityCatalog.Create());

    private static readonly string[] Greetings =
    [
        "سلام", "سلام علیکم", "درود", "درود بر شما", "وقت بخیر", "وقت به خیر", "روز بخیر", "صبح بخیر", "ظهر بخیر", "عصر بخیر",
        "سلام وقت بخیر", "سلام صبح بخیر", "سلام روز بخیر", "سلام خسته نباشید", "خسته نباشید", "سلام خوبی", "سلام چطوری",
        "سلام ساپیو", "سلام دستیار", "hi", "hello", "hey"
    ];

    private static readonly string[] Introductions =
    [
        "خودت را معرفی کن", "خودت رو معرفی کن", "خودتو معرفی کن",
        "خودت را معرفی کن و بگو چه کارهایی بلدی", "خودت رو معرفی کن و بگو چه کارهایی بلدی", "خودتو معرفی کن و بگو چه کارهایی بلدی",
        "خودت را معرفی کن و بگو چه کارهایی میتوانی انجام بدهی",
        "خودت را معرفی کن و بگو چیکار میکنی", "خودت رو معرفی کن و بگو چیکار میکنی", "خودتو معرفی کن و بگو چیکار میکنی",
        "خودت را معرفی کن و بگو چیکار میتونی بکنی", "خودت رو معرفی کن و بگو چیکار میتونی بکنی", "خودتو معرفی کن و بگو چیکار میتونی بکنی",
        "کی هستی", "تو کی هستی", "تو چی هستی", "شما کی هستید",
        "چیکار میکنی", "چکار میکنی", "چه کار میکنی", "چیکار میتونی بکنی", "چکار میتونی بکنی", "چه کاری میتونی بکنی",
        "چه کارهایی میتونی بکنی", "چه کارهایی میتوانی انجام بدهی", "چه کارهایی بلدی", "چه کارایی بلدی", "چی بلدی", "چیکار بلدی",
        "who are you", "introduce yourself", "what can you do", "what do you do"
    ];

    private static readonly string[] FollowUps =
    [
        "چی میتونم بپرسم", "چی میتوانم بپرسم", "چی میشه پرسید", "چی میشه بپرسم", "چی بپرسم", "چی رو بپرسم", "چی را بپرسم",
        "چه چیزی میتونم بپرسم", "چه چیزی میتوانم بپرسم", "چه سوالی میتونم بپرسم", "چه سوالی میتوانم بپرسم", "چه سوالی بپرسم",
        "چه سوالایی میتونم بپرسم", "چه سوالهایی میتونم بپرسم", "چه سوالاتی میتونم بپرسم", "چه سوالاتی میشه پرسید",
        "از تو چی میتونم بپرسم", "ازت چی میتونم بپرسم", "ازت چی بپرسم", "از شما چی میتونم بپرسم", "از شما چه سوالی میتونم بپرسم",
        "درباره چی میتونم بپرسم", "راجع به چی میتونم بپرسم", "راهنما", "راهنمایی", "help",
        "what can i ask", "what can i ask you", "what should i ask"
    ];

    public static TheoryData<string> AllPhrases()
    {
        var data = new TheoryData<string>();
        foreach (var phrase in Greetings.Concat(Introductions).Concat(FollowUps))
            data.Add(phrase);
        return data;
    }

    [Fact]
    public void PhraseLists_MatchTheApprovedCounts()
    {
        Assert.Equal(22, Greetings.Length);
        Assert.Equal(33, Introductions.Length);
        Assert.Equal(29, FollowUps.Length);
    }

    // ---- 1.1 to 1.4: must fail before the rule exists ----

    [Theory]
    [MemberData(nameof(AllPhrases))]
    public async Task Gate_WholeMessageHelpPhrase_RoutesToAssistantHelpWithoutCallingTheModel(string message)
    {
        var run = await PrepareAsync(message);

        Assert.Equal(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
    }

    [Theory]
    [InlineData("سلام!")]
    [InlineData("چی می‌تونم بپرسم؟")]
    [InlineData("چي ميتونم بپرسم")]
    [InlineData("چه سؤال‌هایی می‌تونم بپرسم؟")]
    [InlineData("چی کار می‌کنی؟")]
    [InlineData("خب چی می‌تونم بپرسم؟")]
    [InlineData("پس چی بپرسم؟")]
    [InlineData("سلام، خودت را معرفی کن و بگو چه کارهایی بلدی؟")]
    [InlineData("خودت را کامل معرفی کن و بگو چه کارهایی بلدی؟")]
    [InlineData("خودت رو کاملا معرفی کن و بگو چیکار میتونی بکنی؟")]
    [InlineData("خودت را کاملاً معرفی کن")]
    [InlineData("سلام، خودتو کامل معرفی کن")]
    public async Task Gate_HelpPhraseVariants_Match(string message)
    {
        var run = await PrepareAsync(message);

        Assert.Equal(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
    }

    [Fact]
    public async Task Gate_FollowUpAfterGenericRefusal_RoutesToAssistantHelp()
    {
        var run = await PrepareAsync("خب چی می‌تونم بپرسم؟", seed: Seed.GenericRefusal);

        Assert.Equal(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
    }

    [Fact]
    public async Task Gate_FollowUpAfterNormalAnswer_RoutesToAssistantHelpAndLeavesTaskStateUnchanged()
    {
        var run = await PrepareAsync("چی می‌تونم بپرسم؟", seed: Seed.NormalAnswer);

        Assert.Equal(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        await run.Gate.RecordOutcomeAsync(run.Result.Request, run.Scope.ConversationId, false, HelpCode, CancellationToken.None);

        var after = await run.States.GetActiveAsync(run.Scope, CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal(run.Before!.Version, after!.Version);
        Assert.Equal("symbol_metric_lookup", after.ActiveCapability);
        Assert.Equal(run.Before.Slots.Select(slot => (slot.Type, slot.Value)), after.Slots.Select(slot => (slot.Type, slot.Value)));
        Assert.Null(after.PendingAction);
    }

    // ---- 1.5 and 1.6 ----

    [Fact]
    public void Catalog_AssistantHelp_IsGateRoutedOnlyWithNoSlots()
    {
        var definition = Registry.Find(HelpCode);

        Assert.NotNull(definition);
        Assert.True(definition!.GateRoutedOnly);
        Assert.True(definition.Enabled);
        Assert.Empty(definition.RequiredSlots);
        Assert.Empty(definition.OptionalSlots);
        Assert.Equal(HelpCode, definition.ExecutionRoute);
    }

    [Fact]
    public void Availability_AssistantHelpRoute_IsAvailableInCyclicalWavesMode() =>
        Assert.True(new ChatCapabilityAvailability(FinancialDataChatProvider.CyclicalWaves).Evaluate(HelpCode).IsAvailable);

    // ---- 1.10 to 1.13: guards ----

    [Theory]
    [InlineData("سلام، کگل چطور است؟")]
    [InlineData("سلام فولاد چطوره")]
    public async Task Gate_GreetingWithQuestion_KeepsCurrentRoute(string message) =>
        AssertTodayRoute(await PrepareAsync(message));

    public static TheoryData<string> ShortRealQuestions() => new()
    {
        "کگل",
        "قیمت کگل",
        "خب",
        "سلام" + new string('!', 80)
    };

    [Theory]
    [MemberData(nameof(ShortRealQuestions))]
    public async Task Gate_ShortRealQuestion_KeepsCurrentRoute(string message) =>
        AssertTodayRoute(await PrepareAsync(message));

    [Theory]
    [InlineData("کامل")]
    [InlineData("گزارش کامل کگل")]
    public async Task Gate_AdverbWithoutListedPhrase_KeepsCurrentRoute(string message) =>
        AssertTodayRoute(await PrepareAsync(message));

    [Theory]
    [InlineData("سلام")]
    [InlineData("خودت را معرفی کن")]
    [InlineData("چی می‌تونم بپرسم؟")]
    public async Task Gate_HelpPhraseDuringPendingClarification_KeepsExistingFlow(string message) =>
        AssertTodayRoute(await PrepareAsync(message, seed: Seed.PendingClarification));

    [Fact]
    public async Task Gate_HelpPhraseUnderFrozenV1_KeepsCurrentRoute()
    {
        AssertTodayRoute(await PrepareAsync("سلام", scope: Feature154RoutingScope.V1Frozen));

        using (Feature154RoutingScope.EnterFrozenV1())
            AssertTodayRoute(await PrepareAsync("سلام", scope: Feature154RoutingScope.V2));
    }

    [Theory]
    [InlineData(SemanticRoutingMode.Rollback)]
    [InlineData(SemanticRoutingMode.Legacy)]
    public async Task Gate_HelpPhraseInRollbackMode_KeepsCurrentRoute(SemanticRoutingMode mode)
    {
        var run = await PrepareAsync("سلام", mode: mode);

        Assert.NotEqual(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.NotEqual(HelpCode, run.Result.Request.SemanticShadowFrame?.CapabilityCode);
        Assert.Equal(1, run.Provider.Calls);
    }

    [Fact]
    public async Task Gate_EligibleTickerEqualToListedPhrase_SnapshotWins()
    {
        var run = await PrepareAsync("سلام", eligibleTicker: "سلام");

        Assert.Equal(SymbolSnapshotCapability.Code, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(0, run.Provider.Calls);
    }

    [Fact]
    public async Task Catalog_AssistantHelp_IsNotOfferedToTheModelOrStarterPrompts()
    {
        var execution = new CapturingModelExecution();
        var provider = new LlmQueryInterpretationProposalProvider(execution, Registry);

        await Assert.ThrowsAsync<PromptCaptured>(() =>
            provider.ProposeAsync("سلام", Guid.NewGuid(), "prompt-check", CancellationToken.None));

        Assert.DoesNotContain(HelpCode, execution.Prompt);
        Assert.DoesNotContain(Registry.GetEnabled(), definition => definition.Code == HelpCode);
        foreach (var language in new[] { "fa", "en" })
            Assert.DoesNotContain(new CapabilityGuidanceService(Registry).StarterPrompts(language), prompt => prompt.CapabilityCode == HelpCode);
    }

    [Fact]
    public void TelegramRenderer_AssistantHelpResponse_ShowsTextAndSuggestions()
    {
        const string text = "سلام! برای نمونه بپرسید:\n• گزارش فصلی کگل را نشان بده";
        var action = new SuggestedAction(
            "assistant_help:quarterly_report", SuggestedActionKind.ShowCapabilityHelp, "گزارش فصلی", "گزارش فصلی کگل را نشان بده",
            "financial_statement_table", new Dictionary<string, string>(), HelpCode, 1);
        var response = new AiQueryResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.Unknown,
            null, null, null, null, null, text, false, null, null,
            Outcome: DialogueOutcome.Answered, OutcomeReasonCode: HelpCode, ReplyLanguage: "fa", SuggestedActions: [action]);

        var messages = new TelegramAssistantResponseRenderer(new UnusedChartRenderer(), NullLogger<TelegramAssistantResponseRenderer>.Instance)
            .Render(response, "fa-IR");
        var rendered = string.Join("\n", messages.Select(message => message.Text));

        Assert.Contains("برای نمونه بپرسید", rendered);
        Assert.Contains("پیشنهادها", rendered);
        Assert.Contains("گزارش فصلی", rendered);
    }

    // ---- Harness ----

    private static void AssertTodayRoute(GateRun run)
    {
        Assert.NotEqual(HelpCode, run.Result.Request.SemanticFrame?.CapabilityCode);
        Assert.Equal(1, run.Provider.Calls);
    }

    private enum Seed { None, GenericRefusal, NormalAnswer, PendingClarification }

    private sealed record GateRun(
        ConversationDialogueGateResult Result,
        ConversationDialogueGate Gate,
        WrongCapabilityProposalProvider Provider,
        ConversationTaskStateService States,
        ConversationTaskStateScope Scope,
        ConversationTaskState? Before);

    private static async Task<GateRun> PrepareAsync(
        string message,
        Seed seed = Seed.None,
        Feature154RoutingScope? scope = null,
        SemanticRoutingMode mode = SemanticRoutingMode.SemanticPrimary,
        string? eligibleTicker = null)
    {
        var states = new ConversationTaskStateService(new InMemoryTaskStates(), TimeProvider.System, new ConversationTaskStateOptions());
        var stateScope = new ConversationTaskStateScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var slot = new ConversationTaskSlot(QuerySlotType.CompanyOrSymbol, Symbol, null, QueryValueProvenance.UserExplicit, 1m, null, 0);
        switch (seed)
        {
            case Seed.GenericRefusal:
                await states.RecordAnsweredAsync(stateScope, "unknown", [], Guid.Empty, "seed", CancellationToken.None);
                break;
            case Seed.NormalAnswer:
                await states.RecordAnsweredAsync(stateScope, "symbol_metric_lookup", [slot], Guid.Empty, "seed", CancellationToken.None);
                break;
            case Seed.PendingClarification:
                await states.RecordPendingAsync(
                    stateScope,
                    "monthly_activity_trend",
                    [],
                    new PendingDialogueAction(PendingDialogueActionKind.Clarification, QuerySlotType.CompanyOrSymbol, [], "required_input_missing", Guid.NewGuid(), 0),
                    "seed",
                    CancellationToken.None);
                break;
        }

        var before = await states.GetActiveAsync(stateScope, CancellationToken.None);
        var provider = new WrongCapabilityProposalProvider();
        var interpreter = new DeterministicCapabilityInterpreter(Registry);
        var gate = new ConversationDialogueGate(
            states,
            interpreter,
            new FixedEntityResolver(),
            new CapabilitySlotValidator(Registry),
            new NoDirectMetrics(),
            new SemanticRoutingRolloutCoordinator(new SemanticRoutingOptions(DefaultMode: mode), new NullSemanticRoutingTelemetrySink()),
            new NoMessages(),
            TimeProvider.System,
            hybridInterpreter: new HybridCapabilityInterpreter(interpreter, Registry, new QueryInterpretationValidator(Registry), provider),
            feature154: scope,
            eligibleSymbolResolver: new FakeEligible(eligibleTicker),
            symbolSnapshotOptions: new SymbolSnapshotOptions { Enabled = eligibleTicker is not null },
            capabilityRegistry: Registry);
        var request = new AiQueryRequest(message, stateScope.TenantId, stateScope.ActorId, Guid.NewGuid().ToString("N"));
        var result = await gate.PrepareAsync(request, stateScope.ConversationId, CancellationToken.None);
        return new GateRun(result, gate, provider, states, stateScope, before);
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

    private sealed class PromptCaptured : Exception;

    private sealed class CapturingModelExecution : IAiModelExecutionService
    {
        public string Prompt { get; private set; } = string.Empty;

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request, CancellationToken cancellationToken)
        {
            Prompt = string.Join("\n", request.Messages.Select(message => message.Content));
            throw new PromptCaptured();
        }
    }

    private sealed class FakeEligible(string? ticker) : IEligibleSymbolResolver
    {
        public Task<EligibleSymbol?> ResolveAsync(string compactKey, CancellationToken cancellationToken) =>
            Task.FromResult(compactKey == ticker ? new EligibleSymbol(ticker!, "شرکت نمونه", "بورس", "گروه نمونه") : null);
    }

    private sealed class FixedEntityResolver : ICanonicalQueryEntityResolver
    {
        private static EntityResolutionResult Resolved() => new EntityResolutionResult.Resolved(
            new CanonicalQueryEntity(Guid.NewGuid(), Symbol, "شرکت نمونه", "company", "exact_ticker", "4"),
            new EntityResolutionEvidence("exact_ticker", 1m));

        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => Task.FromResult(Resolved());
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([(EntityResolutionResult.Resolved)Resolved()]);
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

    private sealed class UnusedChartRenderer : ITelegramMonthlyTrendChartRenderer
    {
        public TelegramAssistantMediaAttachment Render(MonthlyActivityTrendResponse trend) =>
            throw new InvalidOperationException("must not be called");
    }
}
