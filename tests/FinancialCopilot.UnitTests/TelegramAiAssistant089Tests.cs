using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramAiAssistant089Tests
{
    [Fact]
    public async Task Unlinked_user_gets_link_message_without_ai_call()
    {
        await using var db = CreateDb();
        var ai = new FakeAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(), ai: ai);

        var result = await adapter.HandleAsync(Update("P/E شغدیر"), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Unlinked, result.Status);
        Assert.Equal(0, ai.CallCount);
        Assert.Contains("تلگرام", result.Messages[0].Text);
        Assert.Single(await db.TelegramProcessedUpdates.ToListAsync());
    }

    [Fact]
    public async Task Linked_free_text_reuses_ai_orchestration_and_creates_conversation_binding()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeAiQueryOrchestrationService();
        var conversations = new FakeConversationRepository();
        var adapter = CreateAdapter(
            db,
            new FakeTelegramIdentityLinkReader(actor),
            conversations,
            ai);

        var result = await adapter.HandleAsync(Update("  P/E   شغدير  ", telegramUserId: 1001), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(1, ai.CallCount);
        Assert.Equal("P/E شغدیر", ai.LastRequest?.Message);
        Assert.Equal("telegram:1001", ai.LastRequest?.ExternalUserId);
        Assert.Equal(actor.ActorId, ai.LastRequest?.ActorId);
        Assert.NotNull(ai.LastRequest?.ConversationId);
        Assert.Single(await db.TelegramConversationBindings.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_update_replays_without_second_ai_call()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai);
        var update = Update("تحلیل شغدیر", telegramUserId: 1001, updateId: 900);

        var first = await adapter.HandleAsync(update, CancellationToken.None);
        var replay = await adapter.HandleAsync(update, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, ai.CallCount);
        Assert.Single(await db.TelegramProcessedUpdates.ToListAsync());
    }

    [Fact]
    public async Task Credits_command_uses_membership_entitlement_without_ai_call()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeAiQueryOrchestrationService();
        var membership = new FakeTelegramMembershipService();
        var adapter = CreateAdapter(
            db,
            new FakeTelegramIdentityLinkReader(actor),
            ai: ai,
            membership: membership);

        var result = await adapter.HandleAsync(Update("/credits", telegramUserId: 1001), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(0, ai.CallCount);
        Assert.Equal(1, membership.EntitlementCallCount);
        Assert.Contains("سهمیه", result.Messages[0].Text);
    }

    private static TelegramAiAssistantAdapter CreateAdapter(
        AuthDbContext db,
        ITelegramIdentityLinkReader? linkReader = null,
        IConversationRepository? conversations = null,
        IAiQueryOrchestrationService? ai = null,
        ITelegramMembershipService? membership = null) =>
        new(
            db,
            linkReader ?? new FakeTelegramIdentityLinkReader(),
            membership ?? new FakeTelegramMembershipService(),
            ai ?? new FakeAiQueryOrchestrationService(),
            conversations ?? new FakeConversationRepository(),
            TimeProvider.System,
            NullLogger<TelegramAiAssistantAdapter>.Instance);

    private static AuthDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AuthDbContext(options);
    }

    private static async Task<CurrentActor> SeedLinkedActorAsync(AuthDbContext db)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        db.Users.Add(new FinancialCopilotUser { Id = actorId, UserName = "telegram-user", Email = "telegram@example.test" });
        db.Tenants.Add(new TenantRow { Id = tenantId, Name = "Tenant" });
        db.TelegramAccountLinks.Add(new TelegramAccountLinkRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            TenantId = tenantId,
            TelegramUserId = 1001,
            TelegramChatId = 1001,
            LinkedAtUtc = DateTimeOffset.UtcNow,
            LastVerifiedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, actorId);
    }

    private static TelegramAssistantUpdate Update(
        string text,
        long telegramUserId = 44,
        long updateId = 123) =>
        new(
            updateId,
            TelegramAssistantUpdateKind.Message,
            telegramUserId,
            telegramUserId,
            null,
            10,
            null,
            null,
            text,
            "fa-IR",
            DateTimeOffset.UtcNow,
            $"corr-{updateId}");

    private sealed class FakeTelegramIdentityLinkReader(CurrentActor? actor = null) : ITelegramIdentityLinkReader
    {
        public Task<TelegramLinkView?> GetCurrentAsync(CurrentActor currentActor, CancellationToken cancellationToken) =>
            Task.FromResult<TelegramLinkView?>(new TelegramLinkView(1001, 1001, "user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        public Task<CurrentActor?> ResolveActorAsync(long telegramUserId, CancellationToken cancellationToken) =>
            Task.FromResult(telegramUserId == 1001 ? actor : null);
    }

    private sealed class FakeTelegramMembershipService : ITelegramMembershipService
    {
        public int EntitlementCallCount { get; private set; }

        public Task<TelegramMembershipVerificationResult> VerifyRequiredChannelMembershipAsync(
            CurrentActor actor,
            string correlationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TelegramMembershipVerificationResult(
                TelegramChannelMembershipStatus.Member,
                true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                "@channel",
                correlationId));

        public Task<TelegramEntitlementView> GetMyTelegramEntitlementAsync(
            CurrentActor actor,
            string correlationId,
            CancellationToken cancellationToken)
        {
            EntitlementCallCount++;
            return Task.FromResult(new TelegramEntitlementView(
                new TelegramLinkView(1001, 1001, "user", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new TelegramMembershipVerificationResult(
                    TelegramChannelMembershipStatus.Member,
                    true,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow.AddHours(1),
                    "@channel",
                    correlationId),
                new TelegramDailyFreeAllowanceView("2026-07-13", "telegram-v1", 10, 2, 8, DateTimeOffset.UtcNow.AddHours(8)),
                12,
                "Free daily allowance, then paid credits.",
                "Ready",
                [],
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeAiQueryOrchestrationService : IAiQueryOrchestrationService
    {
        public int CallCount { get; private set; }
        public AiQueryRequest? LastRequest { get; private set; }

        public Task<AiQueryResponse> ExecuteAsync(AiQueryRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var conversationId = request.ConversationId ?? Guid.NewGuid();
            return Task.FromResult(new AiQueryResponse(
                conversationId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DetectedIntent.SymbolLookup,
                null,
                null,
                null,
                null,
                null,
                "پاسخ تست",
                false,
                null,
                new UsageAccountingResult("AiQuery.StockAnalysis", "Completed", 1, 9, "v1", false)));
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public Task<Guid> CreateAsync(Guid tenantId, Guid actorId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateEmptyAsync(Guid tenantId, Guid actorId, DateTimeOffset startedAt, CancellationToken cancellationToken) =>
            Task.FromResult(Guid.NewGuid());

        public Task<ConversationSummary?> FindAsync(Guid conversationId, Guid tenantId, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult<ConversationSummary?>(null);

        public Task<IReadOnlyCollection<ConversationSummary>> ListByActorAsync(Guid tenantId, Guid actorId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ConversationSummary>>([]);

        public Task TouchAsync(Guid conversationId, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid conversationId, Guid tenantId, Guid actorId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<PersistedConversationExchange> PersistExchangeAsync(
            ConversationExchange exchange,
            bool createConversation,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PersistedConversationExchange(Guid.NewGuid(), Guid.NewGuid()));
    }
}
