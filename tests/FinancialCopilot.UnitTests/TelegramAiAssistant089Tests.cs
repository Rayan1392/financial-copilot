using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Purchases;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Domain.Financial.ConditionalTrackers;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Radar;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramAiAssistant089Tests
{
    [Fact]
    public async Task Start_shows_supported_natural_language_question_examples()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor));

        var result = await adapter.HandleAsync(Update("/start", telegramUserId: 1001), CancellationToken.None);
        var text = string.Join("\n", result.Messages.Select(message => message.Text));

        Assert.Contains("روند فروش ماهانه کگهر را نشان بده", text);
        Assert.Contains("روند تولید و فروش شگویا را در ۱۲ ماه اخیر بررسی کن", text);
        Assert.Contains("شگویا را با گروه خودش مقایسه کن", text);
        Assert.Contains("ترکیب فروش محصولات شغدیر را نشان بده", text);
        Assert.DoesNotContain("/scanners", text);
        Assert.DoesNotContain("/scanner FILTER_CODE", text);
    }

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
        Assert.Equal(first.RenderVersion, replay.RenderVersion);
        Assert.Single(await db.TelegramProcessedUpdates.ToListAsync());
    }

    [Fact]
    public async Task Disclosure_pagination_callback_preserves_query_filters_and_replays_idempotently()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new TelegramDisclosurePaginationStateStore(cache);
        var ai = new FakeDisclosureAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai, disclosurePaginationStates: store);
        var query = "فهرست تولید و فروش منتشر شده فولاد";

        var first = await adapter.HandleAsync(Update(query, telegramUserId: 1001, updateId: 920), CancellationToken.None);
        var callbackData = Assert.Single(Assert.Single(first.Messages).Actions!).CallbackData;
        var callback = new TelegramAssistantUpdate(921, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 10,
            "callback-921", callbackData, null, "fa-IR", DateTimeOffset.UtcNow, "corr-921");

        var next = await adapter.HandleAsync(callback, CancellationToken.None);
        var replay = await adapter.HandleAsync(callback, CancellationToken.None);

        Assert.True(callbackData.Length <= 64);
        Assert.Equal(TelegramAssistantResultStatus.Accepted, next.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(2, ai.CallCount);
        Assert.Equal(query, ai.LastRequest?.Message);
        Assert.Equal(2, ai.LastRequest?.DisclosurePage);
        Assert.Equal(8, ai.LastRequest?.DisclosurePageSize);
        Assert.Equal(first.ConversationId, ai.LastRequest?.ConversationId);
    }

    [Theory]
    [InlineData("dlp1:too-short")]
    [InlineData("dlp1:00000000000000000000000000000000:0")]
    public async Task Disclosure_pagination_rejects_malformed_or_tampered_callbacks(string callbackData)
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeDisclosureAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai);
        var callback = new TelegramAssistantUpdate(922, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 10,
            "callback-922", callbackData, null, "fa-IR", DateTimeOffset.UtcNow, "corr-922");

        var result = await adapter.HandleAsync(callback, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.ValidationError, result.Status);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task Disclosure_pagination_rejects_cross_chat_callback_without_invoking_ai()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeDisclosureAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai);
        var first = await adapter.HandleAsync(Update("فهرست تولید و فروش منتشر شده", telegramUserId: 1001, updateId: 923), CancellationToken.None);
        var callbackData = Assert.Single(Assert.Single(first.Messages).Actions!).CallbackData;
        var crossChat = new TelegramAssistantUpdate(924, TelegramAssistantUpdateKind.CallbackQuery, 1001, 9999, null, 10,
            "callback-924", callbackData, null, "fa-IR", DateTimeOffset.UtcNow, "corr-924");

        var result = await adapter.HandleAsync(crossChat, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.ValidationError, result.Status);
        Assert.Equal(1, ai.CallCount);
    }

    [Fact]
    public async Task Disclosure_pagination_rejects_expired_callback_without_invoking_ai()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new TelegramDisclosurePaginationStateStore(cache);
        var token = store.Create(new TelegramDisclosurePaginationState(actor.ActorId, actor.TenantId, 1001, 1001,
            null, Guid.NewGuid(), "فهرست تولید و فروش", 2, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var ai = new FakeDisclosureAiQueryOrchestrationService();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai, disclosurePaginationStates: store);
        var callback = new TelegramAssistantUpdate(925, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 10,
            "callback-925", $"dlp1:{token}:2", null, "fa-IR", DateTimeOffset.UtcNow, "corr-925");

        var result = await adapter.HandleAsync(callback, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.ValidationError, result.Status);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task Duplicate_update_replays_the_persisted_photo_without_rerendering()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeAiQueryOrchestrationService();
        var renderer = new FakeMediaResponseRenderer();
        var adapter = CreateAdapter(
            db,
            new FakeTelegramIdentityLinkReader(actor),
            ai: ai,
            responseRenderer: renderer);
        var update = Update("روند فروش ماهانه سکرد", telegramUserId: 1001, updateId: 901);

        var first = await adapter.HandleAsync(update, CancellationToken.None);
        var replay = await adapter.HandleAsync(update, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, ai.CallCount);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(first.Messages[0].Media, replay.Messages[0].Media);
        Assert.Equal("persisted-base64", replay.Messages[0].Media?.ContentBase64);
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

    [Fact]
    public async Task Track_command_returns_compact_expiring_confirmation_callback()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var trackers = new FakeConditionalTrackerUseCases();
        var adapter = CreateAdapter(
            db,
            new FakeTelegramIdentityLinkReader(actor),
            trackers: trackers);

        var result = await adapter.HandleAsync(
            Update("/track 123 price above 5000 toman", telegramUserId: 1001),
            CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(1, trackers.ParseCallCount);
        var action = Assert.Single(Assert.Single(result.Messages).Actions!, item => item.Text == "Confirm");
        Assert.StartsWith("tr.c1:", action.CallbackData, StringComparison.Ordinal);
        Assert.True(action.CallbackData.Length <= 64);
        Assert.Contains("Confirmation expires", result.Messages[0].Text);
    }

    [Fact]
    public async Task Tracker_confirmation_callback_passes_actor_version_and_token_to_use_case()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var trackers = new FakeConditionalTrackerUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), trackers: trackers);
        var draft = await adapter.HandleAsync(
            Update("/track 123 price above 5000 toman", telegramUserId: 1001),
            CancellationToken.None);
        var callbackData = Assert.Single(
            Assert.Single(draft.Messages).Actions!, item => item.Text == "Confirm").CallbackData;

        var confirmed = await adapter.HandleAsync(
            new TelegramAssistantUpdate(
                124,
                TelegramAssistantUpdateKind.CallbackQuery,
                1001,
                1001,
                null,
                11,
                "callback-124",
                callbackData,
                null,
                "fa-IR",
                DateTimeOffset.UtcNow,
                "corr-124"),
            CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, confirmed.Status);
        Assert.Equal(1, trackers.ConfirmCallCount);
        Assert.Equal(actor.ActorId, trackers.LastConfirmation?.Actor.ActorId);
        Assert.Equal(1, trackers.LastConfirmation?.ExpectedVersion);
        Assert.Equal("abc123def456", trackers.LastConfirmation?.ConfirmationToken);
    }

    [Fact]
    public async Task Radar_command_exposes_versioned_inline_controls()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var radar = new FakeRadarUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), radar: radar);

        var result = await adapter.HandleAsync(Update("/radar", 1001, 201), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Contains(result.Messages.SelectMany(message => message.Actions ?? []),
            action => action.CallbackData == "rd.s1:0:a");
        Assert.Contains(result.Messages.SelectMany(message => message.Actions ?? []),
            action => action.CallbackData.StartsWith("rd.c1:0:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Radar_callback_replay_updates_preferences_once()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var radar = new FakeRadarUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), radar: radar);
        var update = new TelegramAssistantUpdate(
            202, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 12,
            "callback-202", "rd.s1:0:a", null, "en", DateTimeOffset.UtcNow, "corr-202");

        var first = await adapter.HandleAsync(update, CancellationToken.None);
        var replay = await adapter.HandleAsync(update, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, radar.UpdateCallCount);
        Assert.Equal(RadarState.Active, radar.LastUpdate?.Input.State);
    }

    [Fact]
    public async Task Notification_settings_callback_is_localized_versioned_and_replayed_once()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var notifications = new FakeNotificationUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor),
            notificationUseCases: notifications);
        var menu = await adapter.HandleAsync(Update("/notifications", 1001, 301), CancellationToken.None);
        var callback = new TelegramAssistantUpdate(
            302, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 12,
            "callback-302", "nt.mode.v1:Digest:0", null, "fa-IR", DateTimeOffset.UtcNow, "corr-302");

        var first = await adapter.HandleAsync(callback, CancellationToken.None);
        var replay = await adapter.HandleAsync(callback, CancellationToken.None);

        Assert.Contains("تنظیمات اعلان", menu.Messages[0].Text);
        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, notifications.UpdateCallCount);
        Assert.Equal(NotificationDeliveryMode.Digest, notifications.Current.DeliveryMode);
    }

    [Fact]
    public async Task Plans_command_renders_billing_catalog_without_ai_call()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var ai = new FakeAiQueryOrchestrationService();
        var purchases = new FakeBillingPurchaseUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), ai: ai,
            billingPurchases: purchases);

        var result = await adapter.HandleAsync(Update("/plans", 1001, 401), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(0, ai.CallCount);
        Assert.Equal(1, purchases.CatalogCallCount);
        Assert.Contains("TG\\-CREDITS\\-50", result.Messages[0].Text);
        Assert.Contains(result.Messages.SelectMany(message => message.Actions ?? []),
            action => action.CallbackData == "bp.buy.v1:TG-CREDITS-50");
    }

    [Fact]
    public async Task Buy_command_creates_actor_owned_checkout()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var purchases = new FakeBillingPurchaseUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), billingPurchases: purchases);

        var result = await adapter.HandleAsync(Update("/buy TG-CREDITS-50", 1001, 402), CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, result.Status);
        Assert.Equal(1, purchases.CreateCallCount);
        Assert.Equal(actor.ActorId, purchases.LastCreate?.Actor.ActorId);
        Assert.Equal("TG-CREDITS-50", purchases.LastCreate?.ProductCode);
        Assert.Contains("Payment reference", result.Messages[0].Text);
    }

    [Fact]
    public async Task Buy_callback_replays_without_second_checkout()
    {
        await using var db = CreateDb();
        var actor = await SeedLinkedActorAsync(db);
        var purchases = new FakeBillingPurchaseUseCases();
        var adapter = CreateAdapter(db, new FakeTelegramIdentityLinkReader(actor), billingPurchases: purchases);
        var update = new TelegramAssistantUpdate(
            403, TelegramAssistantUpdateKind.CallbackQuery, 1001, 1001, null, 12,
            "callback-403", "bp.buy.v1:TG-CREDITS-50", null, "fa-IR", DateTimeOffset.UtcNow, "corr-403");

        var first = await adapter.HandleAsync(update, CancellationToken.None);
        var replay = await adapter.HandleAsync(update, CancellationToken.None);

        Assert.Equal(TelegramAssistantResultStatus.Accepted, first.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay.Status);
        Assert.Equal(1, purchases.CreateCallCount);
    }

    private static TelegramAiAssistantAdapter CreateAdapter(
        AuthDbContext db,
        ITelegramIdentityLinkReader? linkReader = null,
        IConversationRepository? conversations = null,
        IAiQueryOrchestrationService? ai = null,
        ITelegramMembershipService? membership = null,
        IConditionalTrackerUseCases? trackers = null,
        IRadarUseCases? radar = null,
        IProfessionalScannerUseCases? professionalScanners = null,
        IMarketReportService? marketReports = null,
        INotificationUseCases? notificationUseCases = null,
        IAlertHistoryUseCases? alertHistoryUseCases = null,
        IBillingPurchaseUseCases? billingPurchases = null,
        ITelegramAssistantResponseRenderer? responseRenderer = null,
        ITelegramDisclosurePaginationStateStore? disclosurePaginationStates = null) =>
        new(
            db,
            linkReader ?? new FakeTelegramIdentityLinkReader(),
            membership ?? new FakeTelegramMembershipService(),
            new FakeCodalAlertSummaryUseCase(),
            trackers ?? new FakeConditionalTrackerUseCases(),
            radar ?? new FakeRadarUseCases(),
            professionalScanners ?? new FakeProfessionalScannerUseCases(),
            marketReports ?? new FakeMarketReportService(),
            notificationUseCases ?? new FakeNotificationUseCases(),
            alertHistoryUseCases ?? new FakeAlertHistoryUseCases(),
            billingPurchases ?? new FakeBillingPurchaseUseCases(),
            ai ?? new FakeAiQueryOrchestrationService(),
            conversations ?? new FakeConversationRepository(),
            responseRenderer ?? new TelegramAssistantResponseRenderer(
                new TelegramMonthlyTrendChartRenderer(),
                NullLogger<TelegramAssistantResponseRenderer>.Instance),
            disclosurePaginationStates ?? new TelegramDisclosurePaginationStateStore(new MemoryCache(new MemoryCacheOptions())),
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

    private sealed class FakeDisclosureAiQueryOrchestrationService : IAiQueryOrchestrationService
    {
        public int CallCount { get; private set; }
        public AiQueryRequest? LastRequest { get; private set; }

        public Task<AiQueryResponse> ExecuteAsync(AiQueryRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var page = request.DisclosurePage;
            var result = new DisclosureListingResult(
                [new CompanyDisclosureFeedItem($"d-{page}", "logical", CompanyDisclosureType.MonthlyProductionSales,
                    "ProviderA", "company", null, "FOLAD", "Foolad", "Monthly report", null, null,
                    DateTimeOffset.UtcNow, "source", 1, false, DisclosureCoverageStatus.Complete, "PersistedNormalizedData")],
                new DisclosureListingAppliedFilters([CompanyDisclosureType.MonthlyProductionSales], "FOLAD", [], null, null, null, null,
                    DisclosureConsolidationScope.NonConsolidated),
                page, 8, page > 1, page < 3, 24, 3, DateTimeOffset.UtcNow,
                DisclosureCoverageStatus.Complete, "PersistedNormalizedData");
            return Task.FromResult(new AiQueryResponse(request.ConversationId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                DetectedIntent.DisclosureListing, null, null, null, null, null, null, false, null, null,
                DisclosureListingResult: result));
        }
    }

    private sealed class FakeMediaResponseRenderer : ITelegramAssistantResponseRenderer
    {
        public int CallCount { get; private set; }
        public string Version => "telegram-media-test-v1";

        public IReadOnlyList<TelegramAssistantRenderedMessage> Render(AiQueryResponse response, string locale)
        {
            CallCount++;
            return
            [
                new TelegramAssistantRenderedMessage(
                    1,
                    1,
                    "caption",
                    Media: new TelegramAssistantMediaAttachment(
                        "photo",
                        "image/png",
                        "trend.png",
                        "persisted-base64",
                        "persisted-hash",
                        "monthly-trend-chart-test-v1"))
            ];
        }
    }

    private sealed class FakeCodalAlertSummaryUseCase : IGenerateCodalAlertSummaryUseCase
    {
        public Task<CodalAlertSummaryDto> ExecuteAsync(
            GenerateCodalAlertSummaryCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CodalAlertSummaryDto(
                Guid.NewGuid(),
                command.InsightEventId,
                "Unavailable",
                null,
                "stub",
                "codal-alert-summary-v1",
                null,
                null,
                "Not configured in this unit test.",
                DateTimeOffset.UtcNow));
    }

    private sealed class FakeConditionalTrackerUseCases : IConditionalTrackerUseCases
    {
        private readonly AlertRuleDto rule = new(
            Id: Guid.NewGuid(),
            ExternalCompanyId: "123",
            Symbol: "TEST",
            CompanyName: "Test Company",
            RuleType: AlertRuleType.Price,
            MetricOrEventCode: "LATEST_PRICE",
            Operator: AlertRuleOperator.GreaterThan,
            Threshold: 5000m,
            Unit: AlertRuleUnit.Toman,
            BaselineWindow: null,
            Recurrence: AlertRuleRecurrence.OneShot,
            CooldownMinutes: 0,
            ResetPolicy: AlertRuleResetPolicy.CrossBack,
            SessionPolicy: AlertRuleSessionPolicy.Any,
            Hysteresis: null,
            State: AlertRuleState.Draft,
            Version: 1,
            ConfirmationToken: "abc123def456",
            ConfirmationExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15),
            ConfirmationText: "Confirm test rule",
            OriginalText: "price above 5000 toman",
            ParserVersion: "test-v1",
            LastObservedValue: null,
            LastObservedAtUtc: null,
            LastTriggeredAtUtc: null,
            NextEligibleAtUtc: null,
            TriggerSequence: 0,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        public int ParseCallCount { get; private set; }
        public int ConfirmCallCount { get; private set; }
        public ConfirmAlertRuleCommand? LastConfirmation { get; private set; }

        public Task<IReadOnlyCollection<AlertRuleDto>> GetAsync(GetMyAlertRulesQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AlertRuleDto>>([rule]);

        public Task<AlertRuleDto?> GetAsync(CurrentActor actor, Guid ruleId, CancellationToken cancellationToken) =>
            Task.FromResult<AlertRuleDto?>(rule.Id == ruleId ? rule : null);

        public Task<AlertRuleDto> CreateAsync(CreateAlertRuleCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(rule);

        public Task<AlertRuleDto> ParseAsync(ParseNaturalLanguageAlertRuleCommand command, CancellationToken cancellationToken)
        {
            ParseCallCount++;
            return Task.FromResult(rule);
        }

        public Task<AlertRuleDto> ConfirmAsync(ConfirmAlertRuleCommand command, CancellationToken cancellationToken)
        {
            ConfirmCallCount++;
            LastConfirmation = command;
            return Task.FromResult(rule with { State = AlertRuleState.Active, Version = rule.Version + 1 });
        }

        public Task<AlertRuleDto> ParseUpdateAsync(
            ParseNaturalLanguageAlertRuleUpdateCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(rule with { Version = rule.Version + 1, OriginalText = command.Text });

        public Task<AlertRuleDto> UpdateAsync(UpdateAlertRuleCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(rule with { State = command.State ?? rule.State, Version = rule.Version + 1 });

        public Task RemoveAsync(RemoveAlertRuleCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRadarUseCases : IRadarUseCases
    {
        public int UpdateCallCount { get; private set; }
        public UpdateMyRadarCommand? LastUpdate { get; private set; }

        private static RadarProfileDto Profile => new(
            Guid.Empty, RadarState.Paused, [InsightType.PriceMovement], InsightSeverity.Notice,
            50, RadarSensitivity.Balanced, RadarDeliveryMode.Immediate, 0, [], 30,
            null, null, "Radar evaluates every 30 seconds; source freshness is unavailable.",
            DateTimeOffset.MinValue, DateTimeOffset.MinValue);

        public Task<RadarProfileDto> GetAsync(GetMyRadarQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(Profile);

        public Task<RadarProfileDto> UpdateAsync(UpdateMyRadarCommand command, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            LastUpdate = command;
            return Task.FromResult(Profile with { State = command.Input.State, Version = command.ExpectedVersion + 1 });
        }

        public Task RemoveAsync(RemoveMyRadarCommand command, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RadarProfileDto> UpsertOverrideAsync(
            UpsertRadarSymbolOverrideCommand command, CancellationToken cancellationToken) => Task.FromResult(Profile);

        public Task<RadarProfileDto> RemoveOverrideAsync(
            RemoveRadarSymbolOverrideCommand command, CancellationToken cancellationToken) => Task.FromResult(Profile);

        public Task<Guid> SendTestNotificationAsync(
            SendRadarTestNotificationCommand command, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
    }

    private sealed class FakeProfessionalScannerUseCases : IProfessionalScannerUseCases
    {
        public ProfessionalCatalogPage ListCatalog(ProfessionalCatalogQuery query) =>
            new([], [], 1, Math.Clamp(query.PageSize, 1, 100), 0, 1);
        public ProfessionalFilterDefinition GetFilter(string code, string? version = null) => throw new NotSupportedException();
        public ProfessionalAliasResolution ResolveAlias(string text) => new(false, false, null, [], null);
        public Task<ProfessionalScannerExecutionResult> ExecuteAsync(ProfessionalExecuteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<SavedFilterDto>> ListSavedAsync(CurrentActor actor, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<SavedFilterDto>>([]);
        public Task<SavedFilterDto> SaveAsync(SaveProfessionalFilterCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SavedFilterDto> UpdateAsync(UpdateProfessionalFilterCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(DeleteProfessionalFilterCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ProfessionalScannerExecutionResult> RunSavedAsync(RunSavedProfessionalFilterCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeMarketReportService : IMarketReportService
    {
        public Task<MarketReportView?> GetLatestPublicAsync(CancellationToken cancellationToken) =>
            Task.FromResult<MarketReportView?>(null);
        public Task<MarketReportHistoryPage> GetPublicHistoryAsync(MarketReportHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new MarketReportHistoryPage([], 1, 20, 0));
        public Task<MarketReportView?> GetPublicVersionAsync(Guid reportId, CancellationToken cancellationToken) =>
            Task.FromResult<MarketReportView?>(null);
        public Task<MarketReportView> GeneratePublicAsync(GeneratePublicMarketReportCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<MarketReportView?> GetLatestPersonalAsync(CurrentActor actor, CancellationToken cancellationToken) =>
            Task.FromResult<MarketReportView?>(null);
        public Task<MarketReportHistoryPage> GetPersonalHistoryAsync(CurrentActor actor, MarketReportHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new MarketReportHistoryPage([], 1, 20, 0));
        public Task<MarketReportView?> GetPersonalVersionAsync(CurrentActor actor, Guid reportId, CancellationToken cancellationToken) =>
            Task.FromResult<MarketReportView?>(null);
        public Task<MarketReportView> GeneratePersonalAsync(GeneratePersonalDigestCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeNotificationUseCases : INotificationUseCases
    {
        public int UpdateCallCount { get; private set; }
        public NotificationPreferenceDto Current { get; private set; } = Default();

        public Task<NotificationPreferenceDto> GetPreferencesAsync(CurrentActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<NotificationPreferenceDto> UpdatePreferencesAsync(
            UpdateNotificationPreferenceCommand command,
            CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            Current = new NotificationPreferenceDto(Guid.NewGuid(), command.Input.TimeZoneId,
                command.Input.DeliveryMode, command.Input.QuietHoursStart, command.Input.QuietHoursEnd,
                command.Input.MinimumSeverity, command.Input.DailyCap, command.Input.DigestTime,
                command.Input.CooldownMinutes, command.ExpectedVersion + 1,
                command.Input.Categories.Select(item => new NotificationCategoryPreferenceDto(
                    item.EventType, item.Enabled, item.MinimumSeverity, item.CooldownMinutes)).ToArray(),
                command.Input.Symbols.Select(item => new NotificationSymbolPreferenceDto(
                    item.ExternalCompanyId, item.Muted)).ToArray(), NotificationPreferencePolicy.Version,
                "test", DateTimeOffset.UtcNow);
            return Task.FromResult(Current);
        }

        public Task<NotificationHistoryPage> GetHistoryAsync(
            CurrentActor actor,
            int offset,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationHistoryPage([], offset, pageSize, false));

        private static NotificationPreferenceDto Default() => new(Guid.Empty, "Asia/Tehran",
            NotificationDeliveryMode.Immediate, new TimeOnly(23, 0), new TimeOnly(7, 0),
            InsightSeverity.Notice, 20, new TimeOnly(18, 0), 30, 0, [], [],
            NotificationPreferencePolicy.Version, "test", DateTimeOffset.UtcNow);
    }

    private sealed class FakeAlertHistoryUseCases : IAlertHistoryUseCases
    {
        public Task<AlertHistoryPage> GetHistoryAsync(AlertHistoryQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new AlertHistoryPage([], null, query.PageSize, false, "test retention"));

        public Task<UserAlertDetailDto?> GetDetailAsync(CurrentActor actor, Guid alertId, CancellationToken cancellationToken) =>
            Task.FromResult<UserAlertDetailDto?>(null);

        public Task<AlertWhyDto?> GetWhyAsync(CurrentActor actor, Guid alertId, CancellationToken cancellationToken) =>
            Task.FromResult<AlertWhyDto?>(null);

        public Task<UserAlertDetailDto?> DismissAsync(DismissAlertCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<UserAlertDetailDto?>(null);

        public Task<UserAlertDetailDto?> RestoreAsync(RestoreAlertCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<UserAlertDetailDto?>(null);

        public Task<UserAlertDetailDto?> RecordFeedbackAsync(FeedbackAlertCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<UserAlertDetailDto?>(null);

        public Task<UserAlertDetailDto?> MuteAsync(MuteAlertCommand command, CancellationToken cancellationToken) =>
            Task.FromResult<UserAlertDetailDto?>(null);

        public Task<IReadOnlyCollection<AlertReactionDto>> RefreshReactionAsync(
            RefreshAlertReactionCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AlertReactionDto>>([]);

        public Task<string?> BuildAiContextAsync(CurrentActor actor, Guid alertId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FakeBillingPurchaseUseCases : IBillingPurchaseUseCases
    {
        public int CatalogCallCount { get; private set; }
        public int CreateCallCount { get; private set; }
        public CreateBillingCheckoutCommand? LastCreate { get; private set; }
        private readonly Guid checkoutId = Guid.NewGuid();

        public Task<BillingCatalogView> GetCatalogAsync(string channel, CancellationToken cancellationToken)
        {
            CatalogCallCount++;
            return Task.FromResult(new BillingCatalogView(
                [
                    new BillingCatalogProductView("TG-CREDITS-50", BillingPurchaseProductType.CreditPack,
                        "v1", "Telegram 50 AI credits", 250000m, "IRR", 50m, null, null, "Telegram")
                ],
                DateTimeOffset.UtcNow));
        }

        public Task<BillingCheckoutView> CreateCheckoutAsync(
            CreateBillingCheckoutCommand command,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            LastCreate = command;
            return Task.FromResult(Checkout(command.Actor.ActorId, command.Actor.TenantId));
        }

        public Task<BillingCheckoutView?> GetCheckoutAsync(
            BillableActorContext actor,
            Guid checkoutId,
            CancellationToken cancellationToken) =>
            Task.FromResult<BillingCheckoutView?>(Checkout(actor.ActorId, actor.TenantId));

        public Task<BillingCheckoutPage> GetMyCheckoutsAsync(
            BillableActorContext actor,
            int offset,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BillingCheckoutPage([Checkout(actor.ActorId, actor.TenantId)], offset, pageSize, false));

        public Task<BillingCheckoutView> SubmitReceiptAsync(
            SubmitBillingReceiptCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Checkout(command.Actor.ActorId, command.Actor.TenantId) with
            {
                Status = BillingCheckoutStatus.UnderReview,
                Version = command.ExpectedVersion + 2
            });

        public Task<BillingCheckoutView> CancelCheckoutAsync(
            CancelBillingCheckoutCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Checkout(command.Actor.ActorId, command.Actor.TenantId) with
            {
                Status = BillingCheckoutStatus.Cancelled,
                Version = command.ExpectedVersion + 1
            });

        public Task<BillingCheckoutView> ReviewReceiptAsync(
            ReviewBillingReceiptCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Checkout(command.ReviewerActorId, command.TenantId) with
            {
                Status = command.Approved ? BillingCheckoutStatus.Fulfilled : BillingCheckoutStatus.Rejected,
                Version = command.ExpectedVersion + 2
            });

        public Task<PaymentCallbackResult> ProcessPaymentCallbackAsync(
            PaymentCallbackCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentCallbackResult("NotConfigured", null, false, "test"));

        public Task<BillingReconciliationSummary> ReconcileAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new BillingReconciliationSummary(1, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow));

        private BillingCheckoutView Checkout(Guid actorId, Guid tenantId) =>
            new(
                checkoutId,
                Guid.NewGuid(),
                actorId,
                tenantId,
                BillingPurchaseProductType.CreditPack,
                "TG-CREDITS-50",
                "v1",
                "Telegram 50 AI credits",
                250000m,
                "IRR",
                "TG202607150001",
                BillingCheckoutStatus.AwaitingPayment,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(48),
                null,
                null,
                null,
                null,
                null,
                null,
                1);
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
