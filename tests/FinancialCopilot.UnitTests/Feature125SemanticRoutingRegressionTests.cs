using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class Feature125SemanticRoutingRegressionTests
{
    private static readonly Guid ShgolGroupId =
        Guid.Parse("97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c");

    [Theory]
    [InlineData("خودش")]
    [InlineData("خود")]
    [InlineData("همان")]
    [InlineData("مقایسه")]
    [InlineData("بررسی")]
    [InlineData("تحلیل")]
    public void ReflexiveReferencesAndActionWords_AreEntityDistractors(string token)
    {
        Assert.True(QueryNormalization.IsEntityDistractor(token));
    }

    [Fact]
    public async Task OwnIndustryComparison_RoutesSingleCanonicalCompanyAndDerivesGroup()
    {
        var prepared = await PrepareAsync("نماد کگهر را با صنعت خودش مقایسه کن", "کگهر");
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("symbol_vs_industry_relative_valuation", frame.CapabilityCode);
        Assert.Equal(["کگهر"], frame.Interpretation.EntityMentions.Select(mention => mention.Text));
        var company = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(prepared.CompanyIds[0].ToString("D"), company.Value);
        var group = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.IndustryGroup);
        Assert.Equal(prepared.GroupId.ToString("D"), group.Value);
        AssertNoClarification(frame, prepared.Registry);
    }

    [Fact]
    public async Task TwoSymbolComparison_RoutesOnlyAfterTwoCanonicalCompaniesResolve()
    {
        var prepared = await PrepareAsync("کگهر و کگل را مقایسه کن", "کگهر", "کگل");
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("symbol_pair_within_industry", frame.CapabilityCode);
        Assert.Equal(["کگهر", "کگل"], frame.Interpretation.EntityMentions.Select(mention => mention.Text));
        var companies = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.CompaniesOrSymbols);
        Assert.Equal(prepared.CompanyIds, companies.Value!.Split(',').Select(Guid.Parse));
        AssertNoClarification(frame, prepared.Registry);
    }

    [Theory]
    [InlineData("نماد کگهر را با کگل مقایسه کن", "symbol_pair_within_industry", "کگهر", "کگل")]
    [InlineData("کگهر را با کگل مقایسه کن", "symbol_pair_within_industry", "کگهر", "کگل")]
    [InlineData("کگهر و کگل را مقایسه کن", "symbol_pair_within_industry", "کگهر", "کگل")]
    [InlineData("مقایسه کگهر با کگل", "symbol_pair_within_industry", "کگهر", "کگل")]
    [InlineData("نماد کگهر را با صنعت خودش مقایسه کن", "symbol_vs_industry_relative_valuation", "کگهر", "")]
    [InlineData("کگهر را با صنعت خودش مقایسه کن", "symbol_vs_industry_relative_valuation", "کگهر", "")]
    public async Task ComparisonForms_SelectTheExpectedFeature125Capability(
        string query,
        string expectedCapability,
        string firstSymbol,
        string secondSymbol)
    {
        var symbols = string.IsNullOrWhiteSpace(secondSymbol)
            ? new[] { firstSymbol }
            : new[] { firstSymbol, secondSymbol };
        var prepared = await PrepareAsync(query, symbols);
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal(expectedCapability, frame.CapabilityCode);
        AssertNoClarification(frame, prepared.Registry);
    }

    [Fact]
    public async Task Bug001_ShgolOwnIndustryComparisonResolvesTheAuthoritativeGroup()
    {
        var prepared = await PrepareAsync("نماد شگل را با صنعت خودش مقایسه کن", "شگل");
        var frame = Assert.IsType<ValidatedQueryFrame>(prepared.Result.Request.SemanticFrame);

        Assert.Equal("symbol_vs_industry_relative_valuation", frame.CapabilityCode);
        var group = Assert.Single(frame.Slots, slot => slot.Type == QuerySlotType.IndustryGroup);
        Assert.Equal(ShgolGroupId.ToString("D"), group.Value);
        AssertNoClarification(frame, prepared.Registry);
    }

    private static async Task<PreparedRouting> PrepareAsync(string query, params string[] symbols)
    {
        await using var db = new FinancialIngestionDbContext(
            new DbContextOptionsBuilder<FinancialIngestionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var industryId = Guid.NewGuid();
        var isShgol = symbols.Contains("شگل", StringComparer.Ordinal);
        var groupId = isShgol ? ShgolGroupId : Guid.NewGuid();
        var companyIds = symbols.Select(_ => Guid.NewGuid()).ToArray();
        db.Industries.Add(new NormalizedIndustryRow
        {
            Id = industryId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "44",
            Name = "استخراج کانه‌های فلزی"
        });
        db.IndustryGroups.Add(new NormalizedIndustryGroupRow
        {
            Id = groupId,
            ProviderName = "NoavaranCurrentApi",
            ExternalId = "97",
            Name = isShgol ? "تولید محصولات آرایشی و بهداشتی" : "استخراج سنگ آهن"
        });
        db.Companies.AddRange(symbols.Select((symbol, index) => new NormalizedCompanyRow
        {
            Id = companyIds[index],
            ProviderName = "NoavaranCurrentApi",
            IndustryId = industryId,
            GroupId = groupId,
            Ticker = symbol,
            Name = symbol
        }));
        db.NoavaranEligibleCompanies.AddRange(symbols.Select((symbol, index) => new NoavaranEligibleCompanyRow
        {
            Id = companyIds[index],
            ProviderName = "NoavaranCurrentApi",
            ExternalCompanyId = (index + 1).ToString(),
            Name = symbol,
            IndustryId = industryId,
            GroupId = groupId,
            CompanySymbol = symbol
        }));
        await db.SaveChangesAsync();

        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var entityResolver = new CanonicalQueryEntityResolver(
            db,
            Options.Create(new CanonicalEntityResolutionOptions()));
        var stateService = new ConversationTaskStateService(
            new InMemoryTaskStateRepository(),
            TimeProvider.System,
            new ConversationTaskStateOptions());
        var gate = new ConversationDialogueGate(
            stateService,
            new DeterministicCapabilityInterpreter(registry),
            entityResolver,
            new CapabilitySlotValidator(registry),
            new EmptyDirectMetricRoutingRegistry(),
            new SemanticRoutingRolloutCoordinator(new SemanticRoutingOptions(), new NullSemanticRoutingTelemetrySink()),
            new EmptyMessageRepository(),
            TimeProvider.System,
            industryRelativeValuationResolver: new IndustryRelativeValuationSemanticAdapter(entityResolver, entityResolver, db));
        var request = new AiQueryRequest(query, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"));

        var result = await gate.PrepareAsync(request, Guid.NewGuid(), CancellationToken.None);

        return new(result, registry, industryId, groupId, companyIds);
    }

    private static void AssertNoClarification(
        ValidatedQueryFrame frame,
        IConversationalCapabilityRegistry registry)
    {
        var dispatcher = new SemanticCapabilityDispatcher(registry, [new StubExecutor(frame.CapabilityCode)]);
        Assert.Null(dispatcher.Validate(frame));
    }

    private sealed record PreparedRouting(
        ConversationDialogueGateResult Result,
        IConversationalCapabilityRegistry Registry,
        Guid IndustryId,
        Guid GroupId,
        IReadOnlyList<Guid> CompanyIds);

    private sealed class StubExecutor(string capabilityCode) : IConversationalCapabilityExecutor
    {
        public string CapabilityCode { get; } = capabilityCode;

        public Task<CapabilityExecutionResult> ExecuteAsync(
            ValidatedQueryFrame frame,
            QueryExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityExecutionResult(
                frame.CapabilityCode,
                frame.RegistryVersion,
                CapabilityExecutionStatus.Executed,
                DialogueOutcomeReasonCodes.None));
    }

    private sealed class InMemoryTaskStateRepository : IConversationTaskStateRepository
    {
        private readonly Dictionary<ConversationTaskStateScope, ConversationTaskState> states = [];

        public Task<ConversationTaskState?> FindAsync(
            ConversationTaskStateScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(states.GetValueOrDefault(scope));

        public Task<ConversationTaskStateWriteResult> TryWriteAsync(
            ConversationTaskState state,
            long? expectedVersion,
            CancellationToken cancellationToken)
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
        public Task<Guid> AppendAsync(
            Guid conversationId,
            MessageRole role,
            string content,
            string? scannerQueryPlanJson,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken) =>
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
