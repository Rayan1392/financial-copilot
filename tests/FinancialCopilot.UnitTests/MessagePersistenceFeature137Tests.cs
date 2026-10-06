using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

namespace FinancialCopilot.UnitTests;

public sealed class MessagePersistenceFeature137Tests
{
    [Fact]
    public async Task AppliedFeature137Actions_AuthoritativelyPersistExplicitEmptyCollection()
    {
        var repository = new CapturingConversationRepository();
        var guidance = new Guidance();
        var persistence = Create(repository, guidance);

        var exchange = await Persist(persistence, feature137Applied: true, actions: []);

        Assert.Empty(exchange.SuggestedActions!);
        Assert.Empty(repository.Exchange!.AssistantPayload.SuggestedActions!);
        Assert.Equal(0, guidance.SuggestCalls);
    }

    [Fact]
    public async Task AppliedFeature137Actions_PreservesSuppliedOneToThreeActionCollectionExactly()
    {
        var repository = new CapturingConversationRepository();
        var guidance = new Guidance();
        var persistence = Create(repository, guidance);
        var actions = Enumerable.Range(1, 3).Select(index => new SuggestedAction(
            $"feature137:{index}", SuggestedActionKind.RunRelatedCapability,
            $"label-{index}", $"message-{index}", "monthly_product_trend",
            new Dictionary<string, string> { ["company"] = "کچاد", ["product"] = $"product-{index}" },
            "monthly_sales_product_follow_up", 1)).ToArray();

        var exchange = await Persist(persistence, feature137Applied: true, actions);

        Assert.Equal(actions, exchange.SuggestedActions);
        Assert.Equal(actions, repository.Exchange!.AssistantPayload.SuggestedActions);
        Assert.Equal(0, guidance.SuggestCalls);
    }

    [Fact]
    public async Task NonFeature137Path_PreservesGenericGuidanceBehavior()
    {
        var repository = new CapturingConversationRepository();
        var guidance = new Guidance();
        var persistence = Create(repository, guidance);

        var exchange = await Persist(persistence, feature137Applied: false, actions: []);

        Assert.Equal("generic", Assert.Single(exchange.SuggestedActions!).Id);
        Assert.Equal(1, guidance.SuggestCalls);
    }

    private static MessagePersistenceFunction Create(CapturingConversationRepository repository, Guidance guidance) =>
        new(repository, TimeProvider.System, new ProseBuilder(), guidance);

    private static Task<PersistedConversationExchange> Persist(
        MessagePersistenceFunction persistence,
        bool feature137Applied,
        IReadOnlyCollection<SuggestedAction> actions) =>
        persistence.PersistAsync(
            Guid.NewGuid(),
            new AiQueryRequest("company trend", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N")),
            DetectedIntent.MonthlyActivityTrend,
            false, null, null, null, null, null, null, null, null,
            new AuthorizedMemoryContext([], [], false),
            "Company trend answer", true, CancellationToken.None,
            feature137SuggestedActions: actions,
            feature137SuggestionsApplied: feature137Applied);

    private sealed class Guidance : ICapabilityGuidanceService
    {
        public int SuggestCalls { get; private set; }
        public IReadOnlyList<SuggestedAction> Suggest(CapabilityGuidanceRequest request)
        {
            SuggestCalls++;
            return [new("generic", SuggestedActionKind.ShowCapabilityHelp, "help", "help", "monthly_activity_trend", new Dictionary<string, string>(), "generic", 1)];
        }
        public IReadOnlyList<CapabilityHelpSummary> StarterPrompts(string language, IReadOnlyCollection<string>? actorAvailableCapabilities = null) => [];
    }

    private sealed class ProseBuilder : ISymbolLookupProseBuilder
    {
        public string Build(SymbolLookupTableResult table) => string.Empty;
    }

    private sealed class CapturingConversationRepository : IConversationRepository
    {
        public ConversationExchange? Exchange { get; private set; }
        public Task<Guid> CreateAsync(Guid tenantId, Guid actorId, DateTimeOffset startedAt, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<Guid> CreateEmptyAsync(Guid tenantId, Guid actorId, DateTimeOffset startedAt, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<ConversationSummary?> FindAsync(Guid conversationId, Guid tenantId, Guid actorId, CancellationToken cancellationToken) => Task.FromResult<ConversationSummary?>(null);
        public Task<IReadOnlyCollection<ConversationSummary>> ListByActorAsync(Guid tenantId, Guid actorId, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ConversationSummary>>([]);
        public Task TouchAsync(Guid conversationId, DateTimeOffset updatedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid conversationId, Guid tenantId, Guid actorId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<PersistedConversationExchange> PersistExchangeAsync(ConversationExchange exchange, bool createConversation, CancellationToken cancellationToken)
        {
            Exchange = exchange;
            return Task.FromResult(new PersistedConversationExchange(Guid.NewGuid(), Guid.NewGuid(), exchange.AssistantPayload.SuggestedActions));
        }
    }
}
