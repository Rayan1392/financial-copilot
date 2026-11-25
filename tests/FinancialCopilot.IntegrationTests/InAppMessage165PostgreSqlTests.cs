using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.InAppMessages;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class InAppMessage165PostgreSqlTests(PostgreSqlIntegrationFixture postgres)
{
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private static CancellationToken Ct => CancellationToken.None;

    [SkippableFact]
    public async Task Staff_retry_queue_concurrency_read_state_and_membership_removal()
    {
        await using var database = await postgres.CreateBillingDatabaseAsync("feature165");
        await using var auth = database.CreateAuthContext();
        await auth.Database.MigrateAsync(Ct);
        var tenant = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        var sender = Actor(tenant);
        var recipient = Actor(tenant);
        var unrelated = Actor(tenant);
        var secondOperator = Actor(tenant);
        var outside = Actor(otherTenant);
        await SeedAsync(auth, sender, recipient, unrelated, secondOperator, outside);
        var messages = UseCases(auth);
        var command = new StartMessage(recipient.ActorId, " subject ", " private body ", true, Guid.NewGuid());

        await using var otherAuth = database.CreateAuthContext();
        var other = UseCases(otherAuth);
        var sends = await Task.WhenAll(messages.StartAsync(sender, command, "send", Ct),
            other.StartAsync(sender, command, "retry", Ct));
        Assert.Equal(sends[0], sends[1]);
        var id = sends[0].ConversationId;
        var firstMessage = sends[0].MessageId;
        Assert.Single(await auth.SecurityAdminAudits.Where(row => row.ActionCode == "in-app-message.start").ToListAsync(Ct));

        Assert.Equal(1, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);
        Assert.Equal(0, (await messages.GetSummaryAsync(unrelated, Ct)).UnreadCount);
        var inbox = await messages.ListAsync(recipient, MessageAudience.Recipient, new(MessageListFilter.Unread, 25, null), Ct);
        Assert.True(Assert.Single(inbox.Items).Unread);
        Assert.Equal("subject", inbox.Items[0].Subject);
        var detail = await messages.DetailAsync(recipient, MessageAudience.Recipient, id, 25, null, "open", Ct);
        Assert.Equal("private body", Assert.Single(detail.Messages.Items).Body);
        Assert.Equal(1, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);
        Assert.Equal(MessageFailure.NotFound, (await Assert.ThrowsAsync<InAppMessageException>(() =>
            messages.DetailAsync(unrelated, MessageAudience.Recipient, id, 25, null, "forbidden", Ct))).Failure);
        Assert.Equal(MessageFailure.NotFound, (await Assert.ThrowsAsync<InAppMessageException>(() =>
            messages.DetailAsync(outside, MessageAudience.Staff, id, 25, null, "forbidden", Ct))).Failure);
        await Assert.ThrowsAsync<InAppMessageException>(() => messages.AcknowledgeAsync(recipient, id, [firstMessage, Guid.NewGuid()], Ct));
        Assert.Equal(1, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);
        await messages.AcknowledgeAsync(recipient, id, [firstMessage], Ct);
        var firstRead = Assert.Single((await messages.DetailAsync(recipient, MessageAudience.Recipient, id, 25, null, "read", Ct)).Messages.Items).ReadAtUtc;
        await other.AcknowledgeAsync(recipient, id, [firstMessage], Ct);
        Assert.NotNull(firstRead);
        Assert.Equal(firstRead, Assert.Single((await messages.DetailAsync(recipient, MessageAudience.Recipient, id, 25, null, "read", Ct)).Messages.Items).ReadAtUtc);
        Assert.Equal(0, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);

        await messages.ReplyAsync(recipient, new(id, "user reply", Guid.NewGuid(), MessageAudience.Recipient), "reply", Ct);
        var queue = new MessageListRequest(MessageListFilter.Pending, 25, null);
        Assert.Equal(id, Assert.Single((await messages.ListAsync(secondOperator, MessageAudience.Staff, queue, Ct)).Items).ConversationId);
        var reviewed = await messages.DetailAsync(secondOperator, MessageAudience.Staff, id, 25, null, "reviewed", Ct);
        Assert.Single((await messages.ListAsync(secondOperator, MessageAudience.Staff, queue, Ct)).Items);

        async Task<string> TryReply(InAppMessageUseCases from, CurrentActor actor)
        {
            try
            {
                await from.ReplyAsync(actor, new(id, "staff reply", Guid.NewGuid(), MessageAudience.Staff, reviewed.Revision), "reply", Ct);
                return "sent";
            }
            catch (InAppMessageException error)
            {
                return error.Code;
            }
        }

        var replies = await Task.WhenAll(TryReply(messages, secondOperator), TryReply(other, sender));
        Assert.Contains("sent", replies);
        Assert.Contains("message-revision-conflict", replies);
        Assert.Empty((await messages.ListAsync(secondOperator, MessageAudience.Staff, queue, Ct)).Items);
        Assert.Equal(1, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);
        var page = await messages.DetailAsync(recipient, MessageAudience.Recipient, id, 1, null, "page", Ct);
        var cursor = Assert.IsType<string>(page.Messages.NextCursor);
        var older = await messages.DetailAsync(recipient, MessageAudience.Recipient, id, 1, cursor, "older", Ct);
        Assert.False(Assert.Single(older.Messages.Items).FromStaff);
        await messages.ReplyAsync(recipient, new(id, "new question", Guid.NewGuid(), MessageAudience.Recipient), "reply", Ct);
        Assert.Single((await messages.ListAsync(secondOperator, MessageAudience.Staff, queue, Ct)).Items);

        auth.ChangeTracker.Clear();
        await auth.UserTenants.Where(row => row.TenantId == tenant && row.UserId == recipient.ActorId).ExecuteDeleteAsync(Ct);
        await Assert.ThrowsAsync<InAppMessageException>(() => messages.GetSummaryAsync(recipient, Ct));
        auth.UserTenants.Add(new UserTenantRow { TenantId = tenant, UserId = recipient.ActorId });
        await auth.SaveChangesAsync(Ct);
        Assert.Equal(0, (await messages.GetSummaryAsync(recipient, Ct)).UnreadCount);
        Assert.Empty((await messages.ListAsync(secondOperator, MessageAudience.Staff, queue, Ct)).Items);
        Assert.NotEmpty(await auth.SecurityAdminAudits.ToListAsync(Ct));

        var frozenMessages = new InAppMessageUseCases(new EfCoreInAppMessageStore(auth, new FixedClock(), protection));
        await frozenMessages.StartAsync(sender, new(recipient.ActorId, "One", "Body", false, Guid.NewGuid()), "page", Ct);
        await frozenMessages.StartAsync(sender, new(recipient.ActorId, "Two", "Body", false, Guid.NewGuid()), "page", Ct);
        var firstPage = await frozenMessages.ListAsync(recipient, MessageAudience.Recipient,
            new(MessageListFilter.All, 1, null), Ct);
        var listCursor = Assert.IsType<string>(firstPage.NextCursor);
        var secondPage = await frozenMessages.ListAsync(recipient, MessageAudience.Recipient,
            new(MessageListFilter.All, 1, listCursor), Ct);
        Assert.NotEqual(Assert.Single(firstPage.Items).ConversationId, Assert.Single(secondPage.Items).ConversationId);
        Assert.Null(secondPage.NextCursor);
        var mismatchedCursor = await Assert.ThrowsAsync<InAppMessageException>(() => frozenMessages.ListAsync(unrelated,
            MessageAudience.Recipient, new(MessageListFilter.All, 1, listCursor), Ct));
        Assert.Equal("invalid-message-cursor", mismatchedCursor.Code);
    }

    [SkippableFact]
    public async Task User_start_enters_shared_queue_once_and_tracks_replies()
    {
        await using var database = await postgres.CreateBillingDatabaseAsync("feature165_user_start");
        await using var auth = database.CreateAuthContext();
        await auth.Database.MigrateAsync(Ct);
        var user = Actor(Guid.NewGuid());
        var staff = Actor(user.TenantId);
        var other = Actor(user.TenantId);
        var foreign = Actor(Guid.NewGuid());
        await SeedAsync(auth, user, staff, other, foreign);
        var request = new StartSupportRequest("  Subject  ", "  Body  ", Guid.NewGuid());
        var service = UseCases(auth);
        var older = await service.StartSupportAsync(other, new("Earlier", "Earlier body", Guid.NewGuid()), Ct);
        await using var attemptOne = database.CreateAuthContext();
        await using var attemptTwo = database.CreateAuthContext();
        var receipts = await Task.WhenAll(
            UseCases(attemptOne).StartSupportAsync(user, request, Ct),
            UseCases(attemptTwo).StartSupportAsync(user, request, Ct));
        var receipt = receipts[0];
        Assert.Equal(receipt, receipts[1]);
        Assert.Equal(receipt, await service.StartSupportAsync(user, request with { Body = "Body" }, Ct));

        var altered = await Assert.ThrowsAsync<InAppMessageException>(() =>
            service.StartSupportAsync(user, request with { Body = "Different" }, Ct));
        Assert.Equal("message-idempotency-conflict", altered.Code);
        var staffStartCollision = await Assert.ThrowsAsync<InAppMessageException>(() =>
            service.StartAsync(user, new(user.ActorId, "Subject", "Body", true, request.IdempotencyKey), "staff", Ct));
        Assert.Equal("message-idempotency-conflict", staffStartCollision.Code);
        var replyCollision = await Assert.ThrowsAsync<InAppMessageException>(() =>
            service.ReplyAsync(user, new(receipt.ConversationId, "Body", request.IdempotencyKey, MessageAudience.Recipient), "reply", Ct));
        Assert.Equal("message-idempotency-conflict", replyCollision.Code);

        var queue = new MessageListRequest(MessageListFilter.Pending, 25, null);
        var inbox = new MessageListRequest(MessageListFilter.All, 25, null);
        var pending = await service.ListAsync(staff, MessageAudience.Staff, queue, Ct);
        Assert.Equal(new[] { older.ConversationId, receipt.ConversationId }, pending.Items.Select(row => row.ConversationId));
        Assert.Equal(pending.Items[1].LatestActivityUtc, pending.Items[1].PendingSinceUtc);
        Assert.Empty((await service.ListAsync(foreign, MessageAudience.Staff, queue, Ct)).Items);
        Assert.Equal(0, (await service.GetSummaryAsync(user, Ct)).UnreadCount);
        Assert.Equal("waitingForSupport", Assert.Single((await service.ListAsync(user, MessageAudience.Recipient, inbox, Ct)).Items).SupportStatus);
        Assert.Equal(MessageFailure.NotFound, (await Assert.ThrowsAsync<InAppMessageException>(() =>
            service.DetailAsync(other, MessageAudience.Recipient, receipt.ConversationId, 25, null, "unrelated", Ct))).Failure);

        var review = await service.DetailAsync(staff, MessageAudience.Staff, receipt.ConversationId, 25, null, "review", Ct);
        var first = Assert.Single(review.Messages.Items);
        Assert.False(first.FromStaff);
        Assert.Equal("Body", first.Body);
        Assert.Contains((await service.ListAsync(staff, MessageAudience.Staff, queue, Ct)).Items, row => row.ConversationId == receipt.ConversationId);
        var answer = await service.ReplyAsync(staff,
            new(receipt.ConversationId, "Answer", Guid.NewGuid(), MessageAudience.Staff, review.Revision), "answer", Ct);
        Assert.DoesNotContain((await service.ListAsync(staff, MessageAudience.Staff, queue, Ct)).Items, row => row.ConversationId == receipt.ConversationId);

        await using var reopened = database.CreateAuthContext();
        var fresh = UseCases(reopened);
        var answered = await fresh.DetailAsync(user, MessageAudience.Recipient, receipt.ConversationId, 25, null, "reopen", Ct);
        Assert.Equal("answered", answered.Conversation.SupportStatus);
        Assert.Equal(2, answered.Messages.Items.Count);
        Assert.Equal(1, (await fresh.GetSummaryAsync(user, Ct)).UnreadCount);
        await fresh.AcknowledgeAsync(user, receipt.ConversationId, [answer.MessageId], Ct);
        Assert.Equal(0, (await fresh.GetSummaryAsync(user, Ct)).UnreadCount);
        await fresh.ReplyAsync(user,
            new(receipt.ConversationId, "Follow-up", Guid.NewGuid(), MessageAudience.Recipient), "follow-up", Ct);
        Assert.Equal("waitingForSupport", Assert.Single((await fresh.ListAsync(user, MessageAudience.Recipient, inbox, Ct)).Items).SupportStatus);
        Assert.Contains((await fresh.ListAsync(staff, MessageAudience.Staff, queue, Ct)).Items, row => row.ConversationId == receipt.ConversationId);
    }

    [SkippableFact]
    public async Task Failure_to_write_audit_rolls_back_the_message()
    {
        await using var database = await postgres.CreateBillingDatabaseAsync("feature165_audit");
        await using var auth = database.CreateAuthContext();
        await auth.Database.MigrateAsync(Ct);
        var staff = Actor(Guid.NewGuid());
        var recipient = Actor(staff.TenantId);
        await SeedAsync(auth, staff, recipient);
        await auth.Database.ExecuteSqlRawAsync(
            """ALTER TABLE auth_security_admin_audits ADD CONSTRAINT "reject_send" CHECK ("ActionCode" <> 'in-app-message.start')""", Ct);

        await Assert.ThrowsAsync<DbUpdateException>(() => UseCases(auth).StartAsync(staff,
            new(recipient.ActorId, "Subject", "Message body", false, Guid.NewGuid()), "failed", Ct));

        await using var clean = database.CreateAuthContext();
        Assert.Equal(0, (await UseCases(clean).GetSummaryAsync(recipient, Ct)).UnreadCount);
        Assert.Empty(await clean.SecurityAdminAudits.ToListAsync(Ct));

        await clean.Database.ExecuteSqlRawAsync(
            """ALTER TABLE auth_in_app_messages ADD CONSTRAINT "reject_user_start" CHECK ("FromStaff" OR "Sequence" <> 1)""", Ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => UseCases(clean).StartSupportAsync(recipient,
            new("Subject", "Message body", Guid.NewGuid()), Ct));
        await using var reopened = database.CreateAuthContext();
        var queue = await UseCases(reopened).ListAsync(staff, MessageAudience.Staff, new(MessageListFilter.Pending, 25, null), Ct);
        var inbox = await UseCases(reopened).ListAsync(recipient, MessageAudience.Recipient, new(MessageListFilter.All, 25, null), Ct);
        Assert.Empty(queue.Items);
        Assert.Empty(inbox.Items);
    }

    [SkippableFact]
    public async Task Migration_grants_permissions_only_to_bootstrap_role_and_can_be_reapplied()
    {
        await using var database = await postgres.CreateBillingDatabaseAsync("feature165_migration");
        await using var auth = database.CreateAuthContext();
        var migrations = auth.Database.GetMigrations().ToArray();
        var featureIndex = Array.FindIndex(migrations, migration => migration.EndsWith("_Feature165InAppMessages"));
        Assert.True(featureIndex > 0);
        var oldMigration = migrations[featureIndex - 1];
        var featureMigration = migrations[featureIndex];
        await auth.Database.MigrateAsync(oldMigration, Ct);
        var admin = new FinancialCopilotRole { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = "SUPERADMIN" };
        var operatorRole = new FinancialCopilotRole { Id = Guid.NewGuid(), Name = "Operator", NormalizedName = "OPERATOR" };
        auth.Roles.AddRange(admin, operatorRole);
        await auth.SaveChangesAsync(Ct);

        var grants = from mapping in auth.RolePermissions
                     join permission in auth.Permissions on mapping.PermissionId equals permission.Id
                     where permission.Code.StartsWith("admin.in-app-messages.")
                     select mapping.RoleId;
        await auth.Database.MigrateAsync(featureMigration, Ct);
        Assert.Equal(new[] { admin.Id, admin.Id }, await grants.ToArrayAsync(Ct));

        await auth.Database.MigrateAsync(oldMigration, Ct);
        Assert.Empty(await grants.ToArrayAsync(Ct));
        await auth.Database.MigrateAsync(featureMigration, Ct);
        Assert.Equal(new[] { admin.Id, admin.Id }, await grants.ToArrayAsync(Ct));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private InAppMessageUseCases UseCases(AuthDbContext auth) =>
        new(new EfCoreInAppMessageStore(auth, TimeProvider.System, protection));

    private static CurrentActor Actor(Guid tenant) => new(ActorType.User, Guid.NewGuid(), tenant, AuthenticationMode.WebAppUser);

    private static async Task SeedAsync(AuthDbContext auth, params CurrentActor[] actors)
    {
        auth.Tenants.AddRange(actors.Select(actor => actor.TenantId).Distinct().Select(id => new TenantRow { Id = id, Name = "Test tenant" }));
        auth.Users.AddRange(actors.Select(actor => new FinancialCopilotUser { Id = actor.ActorId, IsEnabled = true }));
        auth.UserTenants.AddRange(actors.Select(actor => new UserTenantRow { TenantId = actor.TenantId, UserId = actor.ActorId }));
        await auth.SaveChangesAsync(Ct);
    }
}
