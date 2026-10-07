using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Production regression: «تحلیل بنیادی فولاد» answered with a symbol-disambiguation
/// clarification instead of reaching the comprehensive_analysis capability.
/// </summary>
public sealed class ComprehensiveAnalysisSemanticRoutingRegressionTests
{
    private static readonly Guid FouladCanonicalId = Guid.Parse("66000000-0000-0000-0000-000000000007");

    // Production-shaped duplicate provider rows: the canonical Noavaran row (id FouladCanonicalId) and a
    // CyclicalWaves row both carry the symbol in TseSymbol and CompanySymbol; Ticker is empty.
    private static readonly NormalizedCompanyRow[] CyclicalWavesDuplicate =
    [
        new() { Id = Guid.NewGuid(), ProviderName = "CyclicalWaves", ExternalCompanyId = "7", TseSymbol = "فولاد", CompanySymbol = "فولاد", Name = "فولاد مبارکه اصفهان" }
    ];

    private static QueryInterpretationProposal Proposal(string capability, params SemanticEntityProposal[] entities) =>
        new([capability], [], null, 0.95m, ["test"], null, entities, []);

    [Theory]
    [InlineData("comprehensive_analysis", "فولاد")]
    [InlineData("comprehensive_analysis", "تحلیل بنیادی فولاد")]
    [InlineData("comprehensive_analysis", "بنیادی فولاد")]
    [InlineData("comprehensive_analysis", "فولاد؟")]
    public async Task HybridInterpreter_ModelEntityVariants_KeepTheCanonicalCompany(string capability, string modelEntity)
    {
        var prepared = await PrepareAsync("تحلیل بنیادی فولاد؟", useTickerForCanonical: false, extraRows: CyclicalWavesDuplicate,
            proposal: Proposal(capability, new SemanticEntityProposal(modelEntity, "company", null, 0.9m)));
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("comprehensive_analysis", frame.CapabilityCode);
        var company = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, company.ValidationState);
        Assert.Equal(FouladCanonicalId, company.CanonicalEntity!.CanonicalId);
    }

    [Fact]
    public async Task HybridInterpreter_DuplicatedAndOverlappingModelEntities_DoNotCreateAmbiguity()
    {
        var prepared = await PrepareAsync("تحلیل بنیادی فولاد؟", useTickerForCanonical: false, extraRows: CyclicalWavesDuplicate,
            proposal: Proposal("comprehensive_analysis",
                new SemanticEntityProposal("فولاد", "company", null, 0.9m),
                new SemanticEntityProposal("فولاد", "company", null, 0.9m),
                new SemanticEntityProposal("بنیادی", "company", null, 0.3m)));
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        var company = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, company.ValidationState);
        Assert.Equal(FouladCanonicalId, company.CanonicalEntity!.CanonicalId);
    }

    [Theory]
    [InlineData("تحلیل بنیادی فولاد", "comprehensive_analysis")]
    [InlineData("تحلیل فولاد", "comprehensive_analysis")]
    [InlineData("فولاد ارزنده است؟", "comprehensive_analysis")]
    [InlineData("آخرین تحلیل فولاد", "comprehensive_analysis")]
    [InlineData("P/E فولاد", "symbol_metric_lookup")]
    [InlineData("P/S فولاد", "symbol_metric_lookup")]
    [InlineData("EPS فولاد", "symbol_metric_lookup")]
    [InlineData("فروش فولاد", "symbol_metric_lookup")]
    [InlineData("سهام با P/E زیر ۵", "stock_screening")]
    [InlineData("چارت روند فروش ماهانه فولاد", "monthly_activity_trend")]
    public async Task HybridInterpreter_RoutingPrecedenceIsPreserved(string query, string expectedCapability)
    {
        // The model agrees with the deterministic interpretation here; precedence is decided by the real arbiter.
        var prepared = await PrepareAsync(query, useTickerForCanonical: false, extraRows: CyclicalWavesDuplicate,
            proposal: Proposal(expectedCapability, new SemanticEntityProposal("فولاد", "company", null, 0.95m)));
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal(expectedCapability, frame.CapabilityCode);
    }

    [Theory]
    [InlineData("P/E فولاد")]
    [InlineData("P/S فولاد")]
    [InlineData("EPS فولاد")]
    [InlineData("فروش فولاد")]
    public async Task HybridInterpreter_ModelCannotHijackMetricQueriesIntoComprehensiveAnalysis(string query)
    {
        var prepared = await PrepareAsync(query, useTickerForCanonical: false, extraRows: CyclicalWavesDuplicate,
            proposal: Proposal("comprehensive_analysis", new SemanticEntityProposal("فولاد", "company", null, 0.95m)));
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.NotEqual("comprehensive_analysis", frame.CapabilityCode);
    }

    private sealed class StubProposalProvider(QueryInterpretationProposal proposal) : IQueryInterpretationProposalProvider
    {
        public Task<QueryInterpretationProposal?> ProposeAsync(string originalText, Guid tenantId, string correlationId, CancellationToken cancellationToken) =>
            Task.FromResult<QueryInterpretationProposal?>(proposal);
    }

    [Theory]
    [InlineData("تحلیل بنیادی فولاد", "فولاد")]
    [InlineData("تحلیل فولاد", "فولاد")]
    [InlineData("آخرین تحلیل فولاد", "فولاد")]
    [InlineData("فولاد را بررسی کن", "فولاد")]
    [InlineData("فولاد ارزنده است؟", "فولاد")]
    [InlineData("تحلیل تکنیکال فولاد", "فولاد")]
    [InlineData("تحلیل بنیادی فولاد مبارکه", "فولاد")]
    [InlineData("فولاد مبارکه را بررسی کن", "فولاد")]
    [InlineData("تحلیل بنیادی کگل", "کگل")]
    [InlineData("تحلیل شغدیر", "شغدیر")]
    [InlineData("آخرین تحلیل فملی", "فملی")]
    [InlineData("کرازی ارزنده است؟", "کرازی")]
    public async Task AnalysisLanguageWithSymbol_RoutesToComprehensiveAnalysisWithoutClarification(string query, string expectedSymbol)
    {
        var prepared = await PrepareAsync(query);
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("comprehensive_analysis", frame.CapabilityCode);
        var company = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, company.ValidationState);
        Assert.Equal(expectedSymbol, company.Value);
        var dispatcher = new SemanticCapabilityDispatcher(prepared.Registry, [new StubExecutor(frame.CapabilityCode)]);
        Assert.Null(dispatcher.Validate(frame));
    }

    [Theory]
    [InlineData("P/E فولاد")]
    [InlineData("P/S فولاد")]
    [InlineData("EPS فولاد")]
    [InlineData("فروش فولاد")]
    public async Task MetricQueries_DoNotBecomeComprehensiveAnalysis(string query)
    {
        var prepared = await PrepareAsync(query);
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.NotEqual("comprehensive_analysis", frame.CapabilityCode);
    }

    [Fact]
    public async Task CanonicalAliasMatch_WinsOverNonCanonicalProviderAliasRows()
    {
        // Canonical (Noavaran) rows carry the symbol in CompanySymbol only; other providers
        // carry it in TseSymbol. Both are alias-tier matches and must not make the symbol ambiguous.
        var prepared = await PrepareAsync("تحلیل بنیادی فولاد", useTickerForCanonical: false, extraRows:
        [
            new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "Tsetmc", TseSymbol = "فولاد", Name = "فولاد مبارکه اصفهان" },
            new NormalizedCompanyRow { Id = Guid.NewGuid(), ProviderName = "Codal", TseSymbol = "فولاد", Name = "فولاد مبارکه" }
        ]);
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("comprehensive_analysis", frame.CapabilityCode);
        var company = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, company.ValidationState);
        Assert.Equal(FouladCanonicalId, company.CanonicalEntity!.CanonicalId);
    }

    private static async Task<(ConversationDialogueGateResult Result, IConversationalCapabilityRegistry Registry)> PrepareAsync(
        string query,
        bool useTickerForCanonical = true,
        IReadOnlyCollection<NormalizedCompanyRow>? extraRows = null,
        QueryInterpretationProposal? proposal = null)
    {
        await using var db = new FinancialIngestionDbContext(
            new DbContextOptionsBuilder<FinancialIngestionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var industryId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        (string Symbol, string Name)[] canonical =
        [
            ("فولاد", "فولاد مبارکه اصفهان"),
            ("فخوز", "فولاد خوزستان"),
            ("فملی", "ملی صنایع مس ایران"),
            ("کگل", "معدنی و صنعتی گل گهر"),
            ("شغدیر", "غدیر"),
            ("کرازی", "شیمیایی رازی")
        ];
        var rows = canonical.Select(item => new NormalizedCompanyRow
        {
            Id = item.Symbol == "فولاد" ? FouladCanonicalId : Guid.NewGuid(),
            ProviderName = "NoavaranCurrentApi",
            IndustryId = industryId,
            GroupId = groupId,
            Ticker = useTickerForCanonical ? item.Symbol : null,
            Name = item.Name,
            CompanySymbol = item.Symbol
        }).ToList();
        if (extraRows is null)
        {
            // A non-canonical provider row for the same symbol must not make the symbol ambiguous.
            rows.Add(new NormalizedCompanyRow
            {
                Id = Guid.NewGuid(),
                ProviderName = "Tsetmc",
                Ticker = "فولاد",
                Name = "فولاد مبارکه اصفهان",
                CompanySymbol = "فولاد"
            });
        }
        else rows.AddRange(extraRows);
        db.Companies.AddRange(rows);
        await db.SaveChangesAsync();

        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var entityResolver = new CanonicalQueryEntityResolver(db, Options.Create(new CanonicalEntityResolutionOptions()));
        var gate = new ConversationDialogueGate(
            new ConversationTaskStateService(new InMemoryTaskStateRepository(), TimeProvider.System, new ConversationTaskStateOptions()),
            new DeterministicCapabilityInterpreter(registry),
            entityResolver,
            new CapabilitySlotValidator(registry),
            new EmptyDirectMetricRoutingRegistry(),
            new SemanticRoutingRolloutCoordinator(
                new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.SemanticPrimary),
                new NullSemanticRoutingTelemetrySink()),
            new EmptyMessageRepository(),
            TimeProvider.System,
            hybridInterpreter: proposal is null ? null : new HybridCapabilityInterpreter(
                new DeterministicCapabilityInterpreter(registry), registry, new QueryInterpretationValidator(registry), new StubProposalProvider(proposal)));
        var request = new AiQueryRequest(query, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"));
        return (await gate.PrepareAsync(request, Guid.NewGuid(), CancellationToken.None), registry);
    }

    private sealed class StubExecutor(string capabilityCode) : IConversationalCapabilityExecutor
    {
        public string CapabilityCode { get; } = capabilityCode;

        public Task<CapabilityExecutionResult> ExecuteAsync(ValidatedQueryFrame frame, QueryExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityExecutionResult(
                frame.CapabilityCode, frame.RegistryVersion, CapabilityExecutionStatus.Executed, DialogueOutcomeReasonCodes.None));
    }

    private sealed class InMemoryTaskStateRepository : IConversationTaskStateRepository
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

    private sealed class EmptyMessageRepository : IMessageRepository
    {
        public Task<Guid> AppendAsync(Guid conversationId, MessageRole role, string content, string? scannerQueryPlanJson, DateTimeOffset createdAt, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<MessageRecord>>([]);
    }

    private sealed class EmptyDirectMetricRoutingRegistry : IDirectMetricRoutingRegistry
    {
        public DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf) => null;
        public IReadOnlyList<DirectMetricRoutingMatch> ResolveAll(string userMessage, DateOnly asOf) => [];
        public bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf) => false;
        public SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode) => null;
        public string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector) => metricCode.Value;
        public string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match) => userMessage;
    }
}
