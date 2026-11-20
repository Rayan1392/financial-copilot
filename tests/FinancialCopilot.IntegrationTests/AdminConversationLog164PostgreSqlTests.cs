using System.Data.Common;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.Profile;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 164: the conversation-log SQL against a real PostgreSQL database.</summary>
[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class AdminConversationLog164PostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-11-01T08:00:00Z");
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private readonly EphemeralDataProtectionProvider dataProtection = new();

    [SkippableFact]
    public async Task List_pairs_answers_with_their_own_questions_and_orders_pages_newest_first()
    {
        await using var database = await CreateDatabaseAsync();
        var seed = await SeedAsync(database);

        await using var conversations = database.CreateConversationContext();
        await using var auth = database.CreateAuthContext();
        var page = await Queries(conversations, auth).GetPageAsync(TenantId, new AdminConversationLogQuery(PageSize: 20), CancellationToken.None);

        Assert.Equal(seed.TenantTurnCount, page.TotalCount);
        Assert.Equal(AdminConversationLogQueryRules.TotalPages(seed.TenantTurnCount, 20), page.TotalPages);
        Assert.Equal(20, page.Items.Count);
        Assert.DoesNotContain(page.Items, item => item.TurnId == seed.OtherTenantQuestionId);
        Assert.Equal(
            page.Items.OrderByDescending(item => item.QuestionCreatedAtUtc).ThenByDescending(item => item.TurnId).Select(item => item.TurnId),
            page.Items.Select(item => item.TurnId));

        var all = await AllAsync(conversations, auth, new AdminConversationLogQuery(PageSize: 100));
        Assert.Equal(seed.TenantTurnCount, all.Count);

        // BUG-037: same CreatedAt, answer id sorts before the question id.
        var first = all.Single(item => item.TurnId == seed.FirstQuestionId);
        Assert.Equal("پاسخ اول", first.AnswerPreview);
        Assert.Equal(AdminConversationLogStatus.Answered, first.Status);
        Assert.Equal("Ali Rezaei", first.User.DisplayName);
        Assert.Equal("•••4567", first.User.MaskedMobile);
        Assert.Equal(AdminConversationLogChannel.Web, first.Channel);

        var second = all.Single(item => item.TurnId == seed.SecondQuestionId);
        Assert.Equal("پاسخ دوم ناموفق", second.AnswerPreview);
        Assert.Equal(AdminConversationLogStatus.Failed, second.Status);

        var concurrent = all.Single(item => item.TurnId == seed.ConcurrentQuestionId);
        Assert.Equal("legacy answer", concurrent.AnswerPreview);
        Assert.Equal(AdminConversationLogStatus.Answered, concurrent.Status);
        Assert.Equal(AdminConversationLogChannel.Telegram, concurrent.Channel);

        var unanswered = all.Single(item => item.TurnId == seed.UnansweredQuestionId);
        Assert.Null(unanswered.AnswerPreview);
        Assert.Null(unanswered.AnswerCreatedAtUtc);
        Assert.Equal(AdminConversationLogStatus.NoResponse, unanswered.Status);

        var malformed = all.Single(item => item.TurnId == seed.MalformedPayloadQuestionId);
        Assert.Equal(AdminConversationLogStatus.Answered, malformed.Status);

        var lastPage = await Queries(conversations, auth).GetPageAsync(TenantId,
            new AdminConversationLogQuery(Page: page.TotalPages, PageSize: 20), CancellationToken.None);
        Assert.Equal(seed.TenantTurnCount - (page.TotalPages - 1) * 20, lastPage.Items.Count);
        var beyond = await Queries(conversations, auth).GetPageAsync(TenantId,
            new AdminConversationLogQuery(Page: page.TotalPages + 1, PageSize: 20), CancellationToken.None);
        Assert.Empty(beyond.Items);
        Assert.Equal(seed.TenantTurnCount, beyond.TotalCount);
    }

    [SkippableFact]
    public async Task Filters_select_exactly_the_matching_turns()
    {
        await using var database = await CreateDatabaseAsync();
        var seed = await SeedAsync(database);
        await using var conversations = database.CreateConversationContext();
        await using var auth = database.CreateAuthContext();

        async Task<Guid[]> Ids(AdminConversationLogQuery query) =>
            (await AllAsync(conversations, auth, query)).Select(item => item.TurnId).ToArray();

        // Arabic «ي» and «ك» in the search match Persian letters in the question; % is literal.
        Assert.Equal(new[] { seed.FirstQuestionId }, await Ids(new(Text: "سود كيان")));
        Assert.Equal(new[] { seed.PercentQuestionId }, await Ids(new(Text: "50%")));
        Assert.Empty(await Ids(new(Text: "no such question")));

        Assert.Equal(seed.AliTurnIds.Order(), (await Ids(new(User: seed.AliId.ToString()))).Order());
        Assert.Equal(seed.AliTurnIds.Order(), (await Ids(new(User: "ali.rezaei@"))).Order());
        Assert.Equal(seed.AliTurnIds.Order(), (await Ids(new(User: "Rezaei"))).Order());
        Assert.Equal(seed.AliTurnIds.Order(), (await Ids(new(User: "۰۹۱۲ ۱۲۳ ۴۵۶۷"))).Order());
        var saraTurns = await Ids(new(User: "+989351112222"));
        Assert.Equal(41, saraTurns.Length);
        Assert.Contains(seed.UnansweredQuestionId, saraTurns);
        Assert.DoesNotContain(seed.FirstQuestionId, saraTurns);
        Assert.Empty(await Ids(new(User: "09129994567")));
        Assert.Empty(await Ids(new(User: seed.OtherTenantUserId.ToString())));

        Assert.Equal(new[] { seed.SecondQuestionId }, await Ids(new(FromUtc: T0.AddSeconds(50), ToUtc: T0.AddSeconds(70))));
        Assert.Equal(new[] { seed.SecondQuestionId }, await Ids(new(Status: AdminConversationLogStatus.Failed)));
        Assert.Equal(
            new[] { seed.UnansweredQuestionId, seed.FilteredBoundaryQuestionId }.Order(),
            (await Ids(new(Status: AdminConversationLogStatus.NoResponse))).Order());
        var answered = await Ids(new(Status: AdminConversationLogStatus.Answered, PageSize: 100));
        Assert.Contains(seed.FirstQuestionId, answered);
        Assert.Contains(seed.MalformedPayloadQuestionId, answered);
        Assert.DoesNotContain(seed.SecondQuestionId, answered);

        // A filtered-out next question still ends the previous turn: «alpha» has no answer of its own.
        var alpha = Assert.Single(await AllAsync(conversations, auth, new(Text: "alpha")));
        Assert.Equal(seed.FilteredBoundaryQuestionId, alpha.TurnId);
        Assert.Equal(AdminConversationLogStatus.NoResponse, alpha.Status);
        Assert.Null(alpha.AnswerPreview);

        await Assert.ThrowsAsync<AdminConversationLogUserSearchTooBroadException>(() =>
            Queries(conversations, auth).GetPageAsync(TenantId, new AdminConversationLogQuery(User: "bulk-user"), CancellationToken.None));
    }

    [SkippableFact]
    public async Task Detail_returns_the_turn_after_a_content_free_audit_and_fails_closed()
    {
        await using var database = await CreateDatabaseAsync();
        var seed = await SeedAsync(database);
        var administratorId = Guid.NewGuid();

        await using (var conversations = database.CreateConversationContext())
        await using (var auth = database.CreateAuthContext())
        {
            var queries = Queries(conversations, auth);
            var turn = await queries.GetTurnAsync(TenantId, administratorId, "User", "corr-164", seed.SecondQuestionId, CancellationToken.None);

            Assert.NotNull(turn);
            Assert.Equal("سؤال دوم", turn.Question);
            Assert.Equal(AdminConversationLogStatus.Failed, turn.Status);
            var answer = Assert.Single(turn.Answers);
            Assert.Equal("پاسخ دوم ناموفق", answer.Content);
            Assert.Equal("provider_failed", answer.OutcomeReasonCode);
            Assert.Equal("gpt-test", answer.Usage?.ModelName);
            Assert.Equal(1234, answer.Usage?.TotalTokens);
            Assert.NotNull(answer.Payload);
            Assert.Equal(DialogueOutcome.Failed, answer.Payload.Outcome);

            // BUG-037 case in detail as well: only its own answer.
            var first = await queries.GetTurnAsync(TenantId, administratorId, "User", "corr-164", seed.FirstQuestionId, CancellationToken.None);
            Assert.Equal(new[] { "پاسخ اول" }, first!.Answers.Select(item => item.Content));

            Assert.Null(await queries.GetTurnAsync(TenantId, administratorId, "User", "corr-164", seed.OtherTenantQuestionId, CancellationToken.None));
            Assert.Null(await queries.GetTurnAsync(TenantId, administratorId, "User", "corr-164", seed.FirstAnswerId, CancellationToken.None));
        }

        await using (var auth = database.CreateAuthContext())
        {
            var audits = await auth.SecurityAdminAudits.AsNoTracking().ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.All(audits, audit =>
            {
                Assert.Equal(EfCoreAdminConversationLogQueries.TurnReadActionCode, audit.ActionCode);
                Assert.Equal(administratorId, audit.ActorId);
                Assert.Equal("corr-164", audit.CorrelationId);
                Assert.Null(audit.Before);
                Assert.Null(audit.After);
                Assert.Null(audit.Reason);
            });
            Assert.DoesNotContain(audits, audit => JsonSerializer.Serialize(audit).Contains("سؤال", StringComparison.Ordinal));
        }

        var conversationCommands = new CommandLog();
        await using (var conversations = database.CreateConversationContext(conversationCommands))
        await using (var failingAuth = database.CreateAuthContext(new FailAuditInsertInterceptor()))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Queries(conversations, failingAuth)
                .GetTurnAsync(TenantId, administratorId, "User", "corr-164", seed.SecondQuestionId, CancellationToken.None));
        }
        Assert.DoesNotContain(conversationCommands.Commands, command => command.Contains("\"Content\"", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task List_uses_a_bounded_number_of_round_trips()
    {
        await using var database = await CreateDatabaseAsync();
        await SeedAsync(database);
        var conversationCommands = new CommandLog();
        var authCommands = new CommandLog();
        await using var conversations = database.CreateConversationContext(conversationCommands);
        await using var auth = database.CreateAuthContext(authCommands);

        var page = await Queries(conversations, auth).GetPageAsync(TenantId,
            new AdminConversationLogQuery(PageSize: 100, User: "Rezaei", Status: AdminConversationLogStatus.Answered),
            CancellationToken.None);

        Assert.NotEmpty(page.Items);
        Assert.True(conversationCommands.Commands.Count + authCommands.Commands.Count <= 6,
            $"Round trips: {string.Join(" | ", conversationCommands.Commands.Concat(authCommands.Commands))}");
    }

    private EfCoreAdminConversationLogQueries Queries(ConversationDbContext conversations, AuthDbContext auth) =>
        new(conversations, auth, new TahlilAppProfileProtector(dataProtection), dataProtection, TimeProvider.System,
            NullLogger<EfCoreAdminConversationLogQueries>.Instance);

    private async Task<List<AdminConversationLogItem>> AllAsync(
        ConversationDbContext conversations,
        AuthDbContext auth,
        AdminConversationLogQuery query) =>
        [.. (await Queries(conversations, auth).GetPageAsync(TenantId, query with { PageSize = 100 }, CancellationToken.None)).Items];

    private async Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        var database = await fixture.CreateBillingDatabaseAsync("feature164_logs");
        await using (var auth = database.CreateAuthContext())
            await auth.Database.MigrateAsync();
        await using (var conversations = database.CreateConversationContext())
            await conversations.Database.MigrateAsync();
        return database;
    }

    private async Task<Seed> SeedAsync(PostgreSqlTestDatabase database)
    {
        var protector = new TahlilAppProfileProtector(dataProtection);
        var contactProtector = ContactMobileProtection.CreateProtector(dataProtection);
        var ali = NewUser("ali.rezaei@example.test");
        var sara = NewUser("sara@example.test");
        var other = NewUser("other@example.test");

        await using (var auth = database.CreateAuthContext())
        {
            auth.Tenants.AddRange(new TenantRow { Id = TenantId, Name = "Tenant" }, new TenantRow { Id = OtherTenantId, Name = "Other" });
            AddMember(auth, TenantId, ali);
            AddMember(auth, TenantId, sara);
            AddMember(auth, OtherTenantId, other);
            auth.UserProfiles.Add(new UserProfileRow
            {
                UserId = ali.Id, FirstName = "Ali", LastName = "Rezaei",
                MobileEncrypted = protector.Protect(TahlilAppProfileProtector.MobilePurpose, "+989121234567"), MobileLast4 = "4567"
            });
            auth.UserProfiles.Add(new UserProfileRow
            {
                UserId = sara.Id, DisplayName = "سارا",
                ContactMobileEncrypted = contactProtector.Protect("09351112222"), ContactMobileLast4 = "2222"
            });
            // Another tenant user with the same last four digits must not match the mobile search.
            auth.UserProfiles.Add(new UserProfileRow
            {
                UserId = other.Id,
                MobileEncrypted = protector.Protect(TahlilAppProfileProtector.MobilePurpose, "+989129994567"), MobileLast4 = "4567"
            });
            for (var index = 0; index <= AdminConversationLogQueryRules.MaximumMatchedUsers; index++)
                AddMember(auth, TenantId, NewUser($"bulk-user-{index:D3}@example.test"));
            await auth.SaveChangesAsync();
        }

        var seed = new Seed { AliId = ali.Id, OtherTenantUserId = other.Id };
        await using (var context = database.CreateConversationContext())
        {
            var aliConversation = AddConversation(context, ali.Id, TenantId);
            seed.FirstQuestionId = Guid.Parse("ffffffff-ffff-ffff-ffff-fffffffffff1");
            seed.FirstAnswerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            AddMessage(context, aliConversation, seed.FirstQuestionId, "User", "سود کیان در فصل اخیر چقدر بود؟", T0);
            AddMessage(context, aliConversation, seed.FirstAnswerId, "Assistant", "پاسخ اول", T0, Payload(DialogueOutcome.Answered));
            seed.SecondQuestionId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
            AddMessage(context, aliConversation, seed.SecondQuestionId, "User", "سؤال دوم", T0.AddMinutes(1));
            AddMessage(context, aliConversation, Guid.Parse("00000000-0000-0000-0000-000000000003"), "Assistant", "پاسخ دوم ناموفق",
                T0.AddMinutes(1), Payload(DialogueOutcome.Failed, "provider_failed"));

            var telegramConversation = AddConversation(context, ali.Id, TenantId);
            seed.ConcurrentQuestionId = Guid.NewGuid();
            AddMessage(context, telegramConversation, seed.ConcurrentQuestionId, "User", "concurrent question", T0.AddSeconds(30));
            AddMessage(context, telegramConversation, Guid.NewGuid(), "Assistant", "legacy answer", T0.AddSeconds(30), "{\"Version\":1}");

            var saraConversation = AddConversation(context, sara.Id, TenantId);
            seed.UnansweredQuestionId = Guid.NewGuid();
            AddMessage(context, saraConversation, seed.UnansweredQuestionId, "User", "unanswered question", T0.AddMinutes(2));

            var boundaryConversation = AddConversation(context, ali.Id, TenantId);
            seed.FilteredBoundaryQuestionId = Guid.NewGuid();
            AddMessage(context, boundaryConversation, seed.FilteredBoundaryQuestionId, "User", "alpha question", T0.AddMinutes(3));
            AddMessage(context, boundaryConversation, Guid.NewGuid(), "User", "beta question", T0.AddMinutes(4));
            AddMessage(context, boundaryConversation, Guid.NewGuid(), "Assistant", "beta answer", T0.AddMinutes(4), Payload(DialogueOutcome.Answered));
            AddMessage(context, boundaryConversation, Guid.NewGuid(), "Tool", "tool output", T0.AddMinutes(4));

            var malformedConversation = AddConversation(context, ali.Id, TenantId);
            seed.MalformedPayloadQuestionId = Guid.NewGuid();
            AddMessage(context, malformedConversation, seed.MalformedPayloadQuestionId, "User", "malformed payload question", T0.AddMinutes(5));
            AddMessage(context, malformedConversation, Guid.NewGuid(), "Assistant", "answer with broken payload", T0.AddMinutes(5), "not json");

            seed.PercentQuestionId = Guid.NewGuid();
            AddMessage(context, malformedConversation, seed.PercentQuestionId, "User", "رشد 50% فروش", T0.AddMinutes(6));
            AddMessage(context, malformedConversation, Guid.NewGuid(), "Assistant", "percent answer", T0.AddMinutes(6), Payload(DialogueOutcome.Answered));

            var bulkConversation = AddConversation(context, sara.Id, TenantId);
            for (var index = 0; index < 40; index++)
            {
                var at = T0.AddDays(-1).AddMinutes(index);
                AddMessage(context, bulkConversation, Guid.NewGuid(), "User", $"older question {index}", at);
                AddMessage(context, bulkConversation, Guid.NewGuid(), "Assistant", $"older answer {index}", at, Payload(DialogueOutcome.NoData));
            }

            var otherConversation = AddConversation(context, other.Id, OtherTenantId);
            seed.OtherTenantQuestionId = Guid.NewGuid();
            AddMessage(context, otherConversation, seed.OtherTenantQuestionId, "User", "سود کیان other tenant", T0.AddMinutes(7));

            await context.SaveChangesAsync();
            seed.TelegramConversationId = telegramConversation;
        }

        await using (var auth = database.CreateAuthContext())
        {
            auth.TelegramConversationBindings.Add(new TelegramConversationBindingRow
            {
                Id = Guid.NewGuid(), ActorId = ali.Id, TenantId = TenantId, TelegramChatId = 42, MessageThreadKey = 0,
                ConversationId = seed.TelegramConversationId, CreatedAtUtc = T0, LastMessageAtUtc = T0, CorrelationId = "seed"
            });
            await auth.SaveChangesAsync();
        }

        seed.AliTurnIds =
        [
            seed.FirstQuestionId, seed.SecondQuestionId, seed.ConcurrentQuestionId, seed.FilteredBoundaryQuestionId,
            await BetaQuestionIdAsync(database), seed.MalformedPayloadQuestionId, seed.PercentQuestionId
        ];
        // Seven turns by Ali, one unanswered and 40 older answered turns by Sara; the other tenant's turn is excluded.
        seed.TenantTurnCount = 7 + 1 + 40;
        return seed;
    }

    private static async Task<Guid> BetaQuestionIdAsync(PostgreSqlTestDatabase database)
    {
        await using var context = database.CreateConversationContext();
        return await context.Messages.Where(message => message.Content == "beta question").Select(message => message.Id).SingleAsync();
    }

    private static string Payload(DialogueOutcome outcome, string reasonCode = "none") =>
        JsonSerializer.Serialize(new
        {
            Version = 2,
            Intent = 0,
            ClarificationRequired = false,
            Usage = new
            {
                OperationCode = "AiQuery.Standard", CompletionStatus = "Completed", CreditsCharged = 1m,
                RemainingSpendingCapacity = 9m, PricingPolicyVersion = "v1", Cached = false,
                ProviderName = "openai", ModelName = "gpt-test", PromptTokens = 1000, CompletionTokens = 234,
                TotalTokens = 1234, EstimatedCost = 0.01m, ExecutionRoute = "agent"
            },
            Outcome = (int)outcome,
            OutcomeReasonCode = reasonCode
        });

    private static Guid AddConversation(ConversationDbContext context, Guid actorId, Guid tenantId)
    {
        var id = Guid.NewGuid();
        context.Conversations.Add(new ConversationRow
        {
            Id = id, TenantId = tenantId, ActorId = actorId, StartedAt = T0, UpdatedAt = T0, Title = "New conversation"
        });
        return id;
    }

    private static void AddMessage(
        ConversationDbContext context,
        Guid conversationId,
        Guid id,
        string role,
        string content,
        DateTimeOffset createdAt,
        string? payload = null) =>
        context.Messages.Add(new MessageRow
        {
            Id = id, ConversationId = conversationId, Role = role, Content = content, CreatedAt = createdAt,
            AssistantPayloadJson = payload
        });

    private static FinancialCopilotUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        IsEnabled = true
    };

    private static void AddMember(AuthDbContext context, Guid tenantId, FinancialCopilotUser user)
    {
        context.Users.Add(user);
        context.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = user.Id });
    }

    private sealed class Seed
    {
        public Guid AliId { get; set; }
        public Guid OtherTenantUserId { get; set; }
        public Guid FirstQuestionId { get; set; }
        public Guid FirstAnswerId { get; set; }
        public Guid SecondQuestionId { get; set; }
        public Guid ConcurrentQuestionId { get; set; }
        public Guid UnansweredQuestionId { get; set; }
        public Guid FilteredBoundaryQuestionId { get; set; }
        public Guid MalformedPayloadQuestionId { get; set; }
        public Guid PercentQuestionId { get; set; }
        public Guid OtherTenantQuestionId { get; set; }
        public Guid TelegramConversationId { get; set; }
        public Guid[] AliTurnIds { get; set; } = [];
        public int TenantTurnCount { get; set; }
    }

    private sealed class CommandLog : DbCommandInterceptor
    {
        private readonly List<string> commands = [];
        public IReadOnlyList<string> Commands { get { lock (commands) return [.. commands]; } }

        private void Record(DbCommand command) { lock (commands) commands.Add(command.CommandText); }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailAuditInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("auth_security_admin_audits", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Feature 164 audit write failure.")
                : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            command.CommandText.Contains("auth_security_admin_audits", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Feature 164 audit write failure.")
                : ValueTask.FromResult(result);
    }
}
