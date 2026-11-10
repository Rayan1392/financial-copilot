using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 158 D3: the eligible Noavaran row outranks every other row with the same symbol (V2 only).</summary>
public sealed class Feature158CanonicalPriorityTests
{
    private const string Symbol = "کگل";
    private const string Question = "روند فروش ماهانه کگل";

    [Fact]
    public async Task LegacyTickerRows_DoNotMakeEligibleSymbolAmbiguous()
    {
        await using var db = CreateDb();
        var eligible = AddEligible(db, Symbol, "1");
        AddOther(db, "Tsetmc", Symbol, "a");
        AddOther(db, "CodalDb", Symbol, "b");
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveMentionAsync(Symbol);

        Assert.Equal(eligible.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task NonEligibleNoavaranDuplicate_DoesNotMakeEligibleSymbolAmbiguous()
    {
        await using var db = CreateDb();
        var eligible = AddEligible(db, Symbol, "1");
        AddNonEligibleNoavaran(db, Symbol, "9001");
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveMentionAsync(Symbol);

        Assert.Equal(eligible.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task OneLegacyTickerRow_DoesNotOutrankEligibleRow()
    {
        await using var db = CreateDb();
        var eligible = AddEligible(db, Symbol, "1");
        AddOther(db, "Tsetmc", Symbol, "a");
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveMentionAsync(Symbol);

        Assert.Equal(eligible.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task MonthlyTrendTurn_WithLegacyDuplicates_ProducesEligibleCompanyFrame()
    {
        await using var db = CreateDb();
        AddEligible(db, Symbol, "1");
        AddOther(db, "Tsetmc", Symbol, "a");
        AddOther(db, "CodalDb", Symbol, "b");
        await db.SaveChangesAsync();
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var gate = new ConversationDialogueGate(
            new ConversationTaskStateService(new Feature158TaskStateRepository(), TimeProvider.System, new ConversationTaskStateOptions()),
            new DeterministicCapabilityInterpreter(registry),
            Resolver(db),
            new CapabilitySlotValidator(registry),
            new Feature158NoDirectMetrics(),
            new SemanticRoutingRolloutCoordinator(
                new SemanticRoutingOptions(DefaultMode: SemanticRoutingMode.SemanticPrimary),
                new NullSemanticRoutingTelemetrySink()),
            new Feature158NoMessages(),
            TimeProvider.System);

        var prepared = await gate.PrepareAsync(
            new AiQueryRequest(Question, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N")),
            Guid.NewGuid(),
            CancellationToken.None);

        var slot = Assert.Single(prepared.Request.SemanticFrame!.Slots, item => item.Type == QuerySlotType.CompanyOrSymbol);
        Assert.Equal(QuerySlotValidationState.Valid, slot.ValidationState);
        Assert.Equal(Symbol, slot.Value);
    }

    [Fact]
    public async Task TwoEligibleRows_StayAmbiguous()
    {
        await using var db = CreateDb();
        AddEligible(db, Symbol, "1");
        AddEligible(db, Symbol, "2");
        await db.SaveChangesAsync();

        Assert.IsType<EntityResolutionResult.Ambiguous>(await Resolver(db).ResolveMentionAsync(Symbol));
    }

    [Fact]
    public async Task TwoOtherProviderRows_WithNoNoavaranRow_StayAmbiguous()
    {
        await using var db = CreateDb();
        AddOther(db, "Tsetmc", Symbol, "a");
        AddOther(db, "CodalDb", Symbol, "b");
        await db.SaveChangesAsync();

        Assert.IsType<EntityResolutionResult.Ambiguous>(await Resolver(db).ResolveMentionAsync(Symbol));
    }

    [Fact]
    public async Task FrozenV1_KeepsTodaysAmbiguousResult()
    {
        await using var db = CreateDb();
        AddEligible(db, Symbol, "1");
        AddOther(db, "Tsetmc", Symbol, "a");
        AddOther(db, "CodalDb", Symbol, "b");
        await db.SaveChangesAsync();
        var scope = Feature154RoutingScope.V2;

        using (Feature154RoutingScope.EnterFrozenV1())
        {
            Assert.False(scope.IsActive);
            Assert.IsType<EntityResolutionResult.Ambiguous>(await Resolver(db).ResolveMentionAsync(Symbol));
        }
    }

    [Fact]
    public async Task EligibilityPredicate_AgreesWithTheQueryFilter()
    {
        await using var db = CreateDb();
        AddEligible(db, Symbol, "1");
        AddNonEligibleNoavaran(db, "شپنا", "2");
        AddOther(db, "Tsetmc", "خساپا", "3");
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = NoavaranProvider, ExternalCompanyId = "4", Name = "x",
            CompanySymbol = "فخوز", PrecedencyRight = 0, MarketId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var rows = await db.Companies.AsNoTracking().ToListAsync();
        var byQuery = await NoavaranCompanyScope.EligibleCompanies(db, NoavaranProvider).Select(row => row.Id).ToListAsync();

        Assert.Equal(
            byQuery.OrderBy(id => id),
            rows.Where(row => NoavaranCompanyScope.IsEligible(row, NoavaranProvider)).Select(row => row.Id).OrderBy(id => id));
    }

    private const string NoavaranProvider = "NoavaranCurrentApi";

    private static FinancialIngestionDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CanonicalQueryEntityResolver Resolver(FinancialIngestionDbContext db) =>
        new(db, Options.Create(new CanonicalEntityResolutionOptions()));

    private static NormalizedCompanyRow AddEligible(FinancialIngestionDbContext db, string symbol, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = NoavaranProvider, ExternalCompanyId = externalId, Name = "شرکت " + externalId,
            TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 0, MarketId = NoavaranCompanyScope.BourseMarketId
        }).Entity;

    private static NormalizedCompanyRow AddNonEligibleNoavaran(FinancialIngestionDbContext db, string symbol, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = NoavaranProvider, ExternalCompanyId = externalId, Name = "حق تقدم " + externalId,
            TseSymbol = symbol, CompanySymbol = symbol, PrecedencyRight = 1, MarketId = NoavaranCompanyScope.BourseMarketId
        }).Entity;

    private static NormalizedCompanyRow AddOther(FinancialIngestionDbContext db, string provider, string symbol, string externalId) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(), ProviderName = provider, ExternalCompanyId = externalId, Name = "نام " + externalId, Ticker = symbol
        }).Entity;
}

internal sealed class Feature158TaskStateRepository : IConversationTaskStateRepository
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

internal sealed class Feature158NoMessages : IMessageRepository
{
    public Task<Guid> AppendAsync(Guid conversationId, MessageRole role, string content, string? scannerQueryPlanJson, DateTimeOffset createdAt, CancellationToken cancellationToken) =>
        Task.FromResult(Guid.NewGuid());

    public Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<MessageRecord>>([]);
}

internal sealed class Feature158NoDirectMetrics : IDirectMetricRoutingRegistry
{
    public DirectMetricRoutingMatch? TryResolve(string userMessage, DateOnly asOf) => null;
    public IReadOnlyList<DirectMetricRoutingMatch> ResolveAll(string userMessage, DateOnly asOf) => [];
    public bool ContainsDirectMetricTerm(string userMessage, DateOnly asOf) => false;
    public SymbolLookupPeriodSelector? ResolvePeriodSelector(string userMessage, MetricCode metricCode) => null;
    public string ResolveDisplayLabel(MetricCode metricCode, SymbolLookupPeriodSelector? selector) => metricCode.Value;
    public string StripResolvedPhrase(string userMessage, DirectMetricRoutingMatch match) => userMessage;
}
