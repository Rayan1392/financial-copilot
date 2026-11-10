using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 158 (OQ-061): two exactly named companies on a one-company capability ask for one company.</summary>
public sealed class Feature158MultipleCompanyClarificationTests
{
    private const string First = "کگل";
    private const string Second = "شپنا";
    private const string TwoTickers = "روند فروش ماهانه کگل/شپنا";
    private const string Monthly = "monthly_activity_trend";
    private const string Reason = DialogueOutcomeReasonCodes.MultipleCompaniesNotSupported;

    private static readonly ConversationalCapabilityRegistry Registry =
        new(InitialConversationalCapabilityCatalog.Create());

    [Fact]
    public async Task TwoExactCompanies_OnOneCompanyCapability_AskForOneCompany()
    {
        await using var db = CreateDb();
        var first = AddEligible(db, First, "1");
        var second = AddEligible(db, Second, "2");
        await db.SaveChangesAsync();
        var repository = new Feature158TaskStateRepository();
        var (gate, conversation, tenant, actor) = Gate(db, repository, Proposal(Monthly, First, Second));

        var prepared = await gate.PrepareAsync(new AiQueryRequest(TwoTickers, tenant, actor, "c1"), conversation, default);

        var slot = Assert.Single(prepared.Request.SemanticFrame!.Slots, item => item.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Invalid, slot.ValidationState);
        Assert.Equal(Reason, slot.Detail);
        Assert.Null(slot.Value);
        var pending = (await repository.FindAsync(new(conversation, tenant, actor), default))!.PendingAction!;
        Assert.Equal(PendingDialogueActionKind.Disambiguation, pending.Kind);
        Assert.Equal(QuerySlotType.CompanyOrSymbol, pending.ExpectedSlot);
        Assert.Equal(Reason, pending.ReasonCode);
        Assert.Equal(
            new[] { first.Id, second.Id }.OrderBy(id => id),
            pending.Candidates.Select(item => item.CanonicalEntityId!.Value).OrderBy(id => id));
    }

    [Fact]
    public async Task TwoExactCompanies_WithLegacyDuplicates_AskForOneCompany()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, Second, "2");
        AddOther(db, "Tsetmc", First, "a");
        AddOther(db, "CodalDb", First, "b");
        AddOther(db, "Tsetmc", Second, "c");
        AddOther(db, "CodalDb", Second, "d");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, First, Second));

        var prepared = await gate.PrepareAsync(new AiQueryRequest(TwoTickers, tenant, actor, "c1"), conversation, default);

        var slot = Assert.Single(prepared.Request.SemanticFrame!.Slots, item => item.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Invalid, slot.ValidationState);
        Assert.Equal(Reason, slot.Detail);
    }

    [Fact]
    public void Dispatcher_ReturnsClarificationWithNewReason()
    {
        var dispatcher = new SemanticCapabilityDispatcher(Registry, [new StubExecutor()]);
        var frame = new ValidatedQueryFrame(
            Monthly, Registry.Version,
            [new ResolvedQuerySlot(QuerySlotType.CompanyOrSymbol, null, QueryValueProvenance.UserExplicit, 0m, QuerySlotValidationState.Invalid, Monthly, Reason)],
            new("q", "q", "fa", [], [], [], null, null, null, [], [], 1m, [], Registry.Version));

        var result = dispatcher.Validate(frame)!;

        Assert.Equal(CapabilityExecutionStatus.ClarificationRequired, result.Status);
        Assert.Equal(Reason, result.ReasonCode);
    }

    [Theory]
    [InlineData("fa", "این پرسش فقط برای یک نماد قابل پاسخ است. لطفاً نماد موردنظرتان را بنویسید.")]
    [InlineData("en", "This question can be answered for one symbol only. Please send the symbol you want.")]
    public void ClarificationReply_UsesProposedText(string language, string expected)
    {
        var outcome = new DialogueOutcomeResult(DialogueOutcome.ClarificationNeeded, Reason, language, null, false);

        Assert.Equal(expected, AiDialogueOutcomePolicy.ComposeSystemMessage(outcome));
    }

    [Fact]
    public async Task PendingClarification_AnsweredWithOneTicker_RunsCapabilityForIt()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, Second, "2");
        await db.SaveChangesAsync();
        var repository = new Feature158TaskStateRepository();
        var (gate, conversation, tenant, actor) = Gate(db, repository, Proposal(Monthly, First, Second), null);

        var firstTurn = await gate.PrepareAsync(new AiQueryRequest(TwoTickers, tenant, actor, "c1"), conversation, default);
        var asked = firstTurn.Request.SemanticFrame!.Slots.Any(slot => slot.Detail == Reason);
        await gate.RecordOutcomeAsync(firstTurn.Request, conversation, asked, asked ? Reason : null, default);
        var reply = await gate.PrepareAsync(new AiQueryRequest(First, tenant, actor, "c2"), conversation, default);

        var slot = Assert.Single(reply.Request.SemanticFrame!.Slots, item => item.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(Monthly, reply.Request.SemanticFrame.CapabilityCode);
        Assert.Equal(First, slot.Value);
        Assert.Equal(QuerySlotValidationState.Valid, slot.ValidationState);
    }

    [Fact]
    public async Task SymbolMetricLookup_KeepsSeveralCompanies()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, Second, "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal("symbol_metric_lookup", First, Second));

        var prepared = await gate.PrepareAsync(new AiQueryRequest("P/E کگل و شپنا", tenant, actor, "c1"), conversation, default);

        var frame = prepared.Request.SemanticFrame!;
        Assert.DoesNotContain(frame.Slots, slot => slot.Detail == Reason);
        Assert.Contains(frame.Slots, slot => slot.Type == QuerySlotType.CompaniesOrSymbols && slot.Value!.Contains(First) && slot.Value.Contains(Second));
    }

    [Fact]
    public async Task OneExactCompany_DoesNotClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, First));

        await AssertAnswersFor(gate, "روند فروش ماهانه کگل", conversation, tenant, actor, First);
    }

    [Fact]
    public async Task VariantMention_DoesNotClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = "3", Name = "صنایع مبارکه شرق",
            CompanySymbol = "وبشرق", TseSymbol = "وبشرق", PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId
        });
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, First, "مبارکه"));

        var prepared = await gate.PrepareAsync(new AiQueryRequest("روند فروش ماهانه کگل مبارکه", tenant, actor, "c1"), conversation, default);

        Assert.DoesNotContain(prepared.Request.SemanticFrame!.Slots, slot => slot.Detail == Reason);
    }

    [Fact]
    public async Task DeterministicPath_DoesNotClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, "دی", "1");
        AddEligible(db, "فولاد", "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), null);

        await AssertAnswersFor(gate, "روند فروش دی ماه فولاد", conversation, tenant, actor, "فولاد");
    }

    [Fact]
    public async Task MonthNameMention_DoesNotClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, "دی", "1");
        AddEligible(db, "فولاد", "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, "دی", "فولاد"));

        await AssertAnswersFor(gate, "روند فروش دی ماه فولاد", conversation, tenant, actor, "فولاد");
    }

    [Fact]
    public async Task FrozenV1_DoesNotClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, Second, "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, First, Second));

        using (Feature154RoutingScope.EnterFrozenV1())
        {
            await AssertAnswersFor(gate, TwoTickers, conversation, tenant, actor, Second);
        }
    }

    [Fact]
    public async Task NonCompanyEntityType_DoesNotCountAsACompany()
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, "فولاد", "2");
        await db.SaveChangesAsync();
        var repository = new Feature158TaskStateRepository();
        var (gate, conversation, tenant, actor) = Gate(
            db, repository, TypedProposal(Monthly, (First, "company"), ("فولاد", "industry")));

        var prepared = await gate.PrepareAsync(new AiQueryRequest("روند فروش ماهانه کگل فولاد", tenant, actor, "c1"), conversation, default);

        Assert.DoesNotContain(prepared.Request.SemanticFrame!.Slots, slot => slot.Detail == Reason);
        Assert.Null((await repository.FindAsync(new(conversation, tenant, actor), default))!.PendingAction);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("symbol")]
    [InlineData("company")]
    public async Task BlankSymbolAndCompanyEntityTypes_StillClarify(string? entityType)
    {
        await using var db = CreateDb();
        AddEligible(db, First, "1");
        AddEligible(db, Second, "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(
            db, new Feature158TaskStateRepository(), TypedProposal(Monthly, (First, entityType), (Second, entityType)));

        var prepared = await gate.PrepareAsync(new AiQueryRequest(TwoTickers, tenant, actor, "c1"), conversation, default);

        Assert.Contains(prepared.Request.SemanticFrame!.Slots, slot => slot.Detail == Reason);
    }

    [Fact]
    public async Task AdjacentMentionsFormingOneCompanyName_AnswerForThatCompany()
    {
        await using var db = CreateDb();
        AddEligible(db, "پارس", "1");
        AddEligibleNamed(db, "پخودرو", "پارس خودرو", "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, "پارس", "خودرو"));

        await AssertAnswersFor(gate, "روند فروش ماهانه پارس خودرو", conversation, tenant, actor, "پخودرو");
    }

    [Fact]
    public async Task OverlappingSpansOfTwoCompanies_AnswerForTheLongerSpan()
    {
        await using var db = CreateDb();
        AddEligible(db, "فولاد", "1");
        AddEligibleNamed(db, "فمبارکه", "فولاد مبارکه", "2");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, "فولاد", "فولاد مبارکه"));

        await AssertAnswersFor(gate, "روند فروش ماهانه فولاد مبارکه", conversation, tenant, actor, "فمبارکه");
    }

    [Fact]
    public async Task CompanyNamePhraseAndSeparateTicker_StillClarify()
    {
        await using var db = CreateDb();
        AddEligible(db, "پارس", "1");
        AddEligibleNamed(db, "پخودرو", "پارس خودرو", "2");
        AddEligible(db, First, "3");
        await db.SaveChangesAsync();
        var (gate, conversation, tenant, actor) = Gate(db, new Feature158TaskStateRepository(), Proposal(Monthly, "پارس", "خودرو", First));

        var prepared = await gate.PrepareAsync(new AiQueryRequest("روند فروش ماهانه پارس خودرو و کگل", tenant, actor, "c1"), conversation, default);

        Assert.Contains(prepared.Request.SemanticFrame!.Slots, slot => slot.Detail == Reason);
    }

    private static async Task AssertAnswersFor(
        ConversationDialogueGate gate, string message, Guid conversation, Guid tenant, Guid actor, string symbol)
    {
        var prepared = await gate.PrepareAsync(new AiQueryRequest(message, tenant, actor, "c1"), conversation, default);

        var frame = prepared.Request.SemanticFrame!;
        Assert.DoesNotContain(frame.Slots, slot => slot.Detail == Reason);
        var slot = Assert.Single(frame.Slots, item => item.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, slot.ValidationState);
        Assert.Equal(symbol, slot.Value);
    }

    private static QueryInterpretationProposal Proposal(string capability, params string[] entities) =>
        new([capability], [], null, 0.95m, ["test"], null, entities.Select(item => new SemanticEntityProposal(item, "company")).ToArray(), []);

    private static QueryInterpretationProposal TypedProposal(string capability, params (string Text, string? Type)[] entities) =>
        new([capability], [], null, 0.95m, ["test"], null, entities.Select(item => new SemanticEntityProposal(item.Text, item.Type)).ToArray(), []);

    private static (ConversationDialogueGate Gate, Guid Conversation, Guid Tenant, Guid Actor) Gate(
        FinancialIngestionDbContext db, IConversationTaskStateRepository repository, params QueryInterpretationProposal?[]? proposals)
    {
        var deterministic = new DeterministicCapabilityInterpreter(Registry);
        var hybrid = proposals is null
            ? null
            : new HybridCapabilityInterpreter(deterministic, Registry, new QueryInterpretationValidator(Registry), new SequenceProvider(proposals));
        var gate = new ConversationDialogueGate(
            new ConversationTaskStateService(repository, TimeProvider.System, new ConversationTaskStateOptions()),
            deterministic,
            new CanonicalQueryEntityResolver(db, Options.Create(new CanonicalEntityResolutionOptions())),
            new CapabilitySlotValidator(Registry),
            new Feature158NoDirectMetrics(),
            new SemanticRoutingRolloutCoordinator(
                new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.SemanticPrimary),
                new NullSemanticRoutingTelemetrySink()),
            new Feature158NoMessages(),
            TimeProvider.System,
            hybridInterpreter: hybrid,
            capabilityRegistry: Registry);
        return (gate, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static NormalizedCompanyRow AddEligible(FinancialIngestionDbContext db, string symbol, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = externalId, Name = "شرکت " + externalId,
            TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId
        }).Entity;

    private static void AddEligibleNamed(FinancialIngestionDbContext db, string symbol, string name, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalCompanyId = externalId, Name = name,
            TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId
        });

    private static void AddOther(FinancialIngestionDbContext db, string provider, string symbol, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = provider, ExternalCompanyId = externalId, Name = "نام " + externalId, Ticker = symbol
        });

    private sealed class SequenceProvider(IReadOnlyList<QueryInterpretationProposal?> proposals) : IQueryInterpretationProposalProvider
    {
        private int next;

        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId, CancellationToken cancellationToken) =>
            Task.FromResult(proposals[Math.Min(next++, proposals.Count - 1)]);
    }

    private sealed class StubExecutor : IConversationalCapabilityExecutor
    {
        public string CapabilityCode => Monthly;

        public Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityExecutionResult(CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Executed, DialogueOutcomeReasonCodes.None));
    }
}
