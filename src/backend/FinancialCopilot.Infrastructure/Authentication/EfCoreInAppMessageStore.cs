using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.InAppMessages;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialCopilot.Infrastructure.Authentication;

internal sealed class EfCoreInAppMessageStore(
    AuthDbContext auth, TimeProvider clock, IDataProtectionProvider dataProtection) : IInAppMessageStore
{
    private static readonly Meter Meter = new("FinancialCopilot.InAppMessages");
    private static readonly Counter<long> CommittedMessages = Meter.CreateCounter<long>("sapio.in_app_messages.committed");
    private static readonly Histogram<double> OldestPendingAge = Meter.CreateHistogram<double>("sapio.in_app_messages.oldest_pending_age", "s");

    private readonly IDataProtector cursors = dataProtection.CreateProtector("in-app-messages-cursor-v1");
    private DbSet<InAppConversationRow> Conversations => auth.Set<InAppConversationRow>();
    private DbSet<InAppMessageRow> Messages => auth.Set<InAppMessageRow>();

    public async Task<MessageReceipt> StartAsync(CurrentActor actor, StartMessage command, string correlationId, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var existing = await FindReplayAsync(actor, command.IdempotencyKey, ct);
        if (existing is not null)
            return StartReplay(existing, command);

        var target = await FindRecipientAsync(actor.TenantId, command.RecipientUserId, ct);
        RequireEligible(target);
        var now = UtcNow();
        var conversation = new InAppConversationRow
        {
            Id = Guid.NewGuid(),
            TenantId = actor.TenantId,
            RecipientUserId = command.RecipientUserId,
            Subject = command.Subject,
            RepliesEnabled = command.RepliesEnabled,
            Revision = 1,
            LatestActivityUtc = now
        };
        var message = CreateMessage(conversation, actor.ActorId, command.IdempotencyKey, command.Body, true, now);
        conversation.Messages.Add(message);
        Conversations.Add(conversation);
        Audit(actor, FinancialCopilotPermissions.AdminInAppMessagesSend, "in-app-message.start", conversation, message.Id, correlationId);
        try
        {
            await auth.SaveChangesAsync(ct);
            CommittedMessages.Add(1, new KeyValuePair<string, object?>("operation", "start"));
            return Receipt(message);
        }
        catch (DbUpdateException exception) when (IsIdempotencyRace(exception))
        {
            auth.ChangeTracker.Clear();
            var replay = await FindReplayAsync(actor, command.IdempotencyKey, ct);
            return StartReplay(replay ?? throw Conflict("message-idempotency-conflict"), command);
        }
    }

    public async Task<MessageReceipt> ReplyAsync(CurrentActor actor, ReplyMessage command, string correlationId, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var existing = await FindReplayAsync(actor, command.IdempotencyKey, ct);
        if (existing is not null)
            return ReplyReplay(existing, command);

        var fromStaff = command.Audience == MessageAudience.Staff;
        await using var transaction = await auth.Database.BeginTransactionAsync(ct);
        var conversation = (await Conversations.FromSqlInterpolated(
            $"""
            SELECT * FROM auth_in_app_conversations
            WHERE "Id" = {command.ConversationId} AND "TenantId" = {actor.TenantId}
              AND (({fromStaff} AND "RepliesEnabled") OR (NOT {fromStaff} AND "RecipientUserId" = {actor.ActorId}))
            FOR UPDATE
            """)
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (conversation is null)
            throw Missing();

        existing = await FindReplayAsync(actor, command.IdempotencyKey, ct);
        if (existing is not null)
            return ReplyReplay(existing, command);

        if (fromStaff)
            RequireEligible(await FindRecipientAsync(actor.TenantId, conversation.RecipientUserId, ct));

        var now = UtcNow();
        var state = new SupportConversationState(conversation.RepliesEnabled, conversation.Revision, conversation.PendingSinceUtc)
            .Append(command.Audience, command.ExpectedRevision, now);
        conversation.Revision = state.Revision;
        var message = CreateMessage(conversation, actor.ActorId, command.IdempotencyKey, command.Body, fromStaff, now);
        Messages.Add(message);
        if (fromStaff)
            Audit(actor, FinancialCopilotPermissions.AdminInAppMessagesSupport, "in-app-message.reply", conversation, message.Id, correlationId);

        try
        {
            await Conversations.Where(row => row.Id == conversation.Id && row.TenantId == actor.TenantId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Revision, state.Revision)
                    .SetProperty(row => row.PendingSinceUtc, state.PendingSinceUtc)
                    .SetProperty(row => row.LatestActivityUtc, now), ct);
            await auth.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            CommittedMessages.Add(1, new KeyValuePair<string, object?>("operation", fromStaff ? "staff_reply" : "user_reply"));
            return Receipt(message);
        }
        catch (DbUpdateException exception) when (IsIdempotencyRace(exception))
        {
            await transaction.RollbackAsync(ct);
            auth.ChangeTracker.Clear();
            var replay = await FindReplayAsync(actor, command.IdempotencyKey, ct);
            return ReplyReplay(replay ?? throw Conflict("message-idempotency-conflict"), command);
        }
    }

    public async Task<MessageSummary> GetSummaryAsync(CurrentActor actor, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var scope = VisibleConversations(actor, MessageAudience.Recipient).Select(row => row.Id);
        var unread = await Messages.Where(row => row.FromStaff && row.ReadAtUtc == null && scope.Contains(row.ConversationId))
            .Take(InAppMessageUseCases.UnreadCap).CountAsync(ct);
        return new MessageSummary(unread);
    }

    public async Task<MessagePage<ConversationSummary>> ListAsync(
        CurrentActor actor, MessageAudience audience, MessageListRequest request, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var scope = CursorScope(actor, audience, request.Filter.ToString());
        var after = ReadCursor<ListCursor>(request.Cursor, scope);
        var query = VisibleConversations(actor, audience);
        if (request.Filter == MessageListFilter.Unread)
            query = query.Where(row => row.Messages.Any(message => message.FromStaff && message.ReadAtUtc == null));

        if (request.Filter == MessageListFilter.Pending)
        {
            query = query.Where(row => row.PendingSinceUtc != null);
            if (after is not null)
                query = query.Where(row => row.PendingSinceUtc > after.Time ||
                    (row.PendingSinceUtc == after.Time && row.Id.CompareTo(after.Id) > 0));
            query = query.OrderBy(row => row.PendingSinceUtc).ThenBy(row => row.Id);
        }
        else
        {
            if (after is not null)
                query = query.Where(row => row.LatestActivityUtc < after.Time ||
                    (row.LatestActivityUtc == after.Time && row.Id.CompareTo(after.Id) < 0));
            query = query.OrderByDescending(row => row.LatestActivityUtc).ThenByDescending(row => row.Id);
        }

        var rows = await Summaries(query, audience).Take(request.PageSize + 1).ToListAsync(ct);
        if (request.Filter == MessageListFilter.Pending && after is null && rows.FirstOrDefault()?.PendingSinceUtc is { } oldest)
            OldestPendingAge.Record(Math.Max(0, (clock.GetUtcNow() - oldest).TotalSeconds));
        return Page(rows, request.PageSize, row => Encode(scope, new ListCursor(
            request.Filter == MessageListFilter.Pending ? row.PendingSinceUtc!.Value : row.LatestActivityUtc,
            row.ConversationId)));
    }

    public async Task<ConversationDetail> DetailAsync(CurrentActor actor, MessageAudience audience, Guid id,
        int pageSize, string? cursor, string correlationId, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var scope = CursorScope(actor, audience, id.ToString("D"));
        var after = ReadCursor<DetailCursor>(cursor, scope);
        var query = VisibleConversations(actor, audience).Where(row => row.Id == id);
        var row = await query.SingleOrDefaultAsync(ct) ?? throw Missing();
        var summary = await Summaries(query, audience).SingleOrDefaultAsync(ct) ?? throw Missing();
        var messages = Messages.AsNoTracking().Where(message => message.ConversationId == id &&
            message.TenantId == actor.TenantId && message.Sequence <= row.Revision);
        if (after is not null)
            messages = messages.Where(message => message.Sequence < after.Sequence);

        var entries = await messages
            .OrderByDescending(message => message.Sequence)
            .Select(message => new { message.Sequence, Message = new ConversationMessage(
                message.Id, message.FromStaff, message.Body, message.SentAtUtc, message.ReadAtUtc) })
            .Take(pageSize + 1).ToListAsync(ct);

        if (audience == MessageAudience.Staff)
        {
            Audit(actor, FinancialCopilotPermissions.AdminInAppMessagesSupport, "in-app-message.read", row, null, correlationId);
            await auth.SaveChangesAsync(ct);
        }

        var page = Page(entries, pageSize, entry => Encode(scope, new DetailCursor(entry.Sequence)));
        return new ConversationDetail(summary, row.Revision,
            new MessagePage<ConversationMessage>(page.Items.Reverse().Select(entry => entry.Message).ToArray(), page.NextCursor));
    }

    public async Task AcknowledgeAsync(CurrentActor actor, Guid id, IReadOnlyCollection<Guid> messageIds, CancellationToken ct)
    {
        await RequireMembershipAsync(actor, ct);
        var scope = VisibleConversations(actor, MessageAudience.Recipient)
            .Where(row => row.Id == id).Select(row => row.Id);
        var messages = Messages.Where(row => scope.Contains(row.ConversationId) && row.FromStaff && messageIds.Contains(row.Id));
        if (await messages.CountAsync(ct) != messageIds.Count)
            throw Missing();

        var now = UtcNow();
        await messages.Where(row => row.ReadAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ReadAtUtc, now), ct);
    }

    private IQueryable<InAppConversationRow> VisibleConversations(CurrentActor actor, MessageAudience audience)
    {
        var query = Conversations.AsNoTracking().Where(row => row.TenantId == actor.TenantId);
        return audience == MessageAudience.Staff
            ? query.Where(row => row.RepliesEnabled)
            : query.Where(row => row.RecipientUserId == actor.ActorId);
    }

    private IQueryable<ConversationSummary> Summaries(IQueryable<InAppConversationRow> query, MessageAudience audience)
    {
        if (audience == MessageAudience.Recipient)
            return query.Select(row => new ConversationSummary(row.Id, row.Subject, row.RepliesEnabled, row.LatestActivityUtc,
                row.Messages.Any(message => message.FromStaff && message.ReadAtUtc == null), null, null, null));

        return from row in query
               join user in auth.Users on row.RecipientUserId equals user.Id
               join profileRow in auth.UserProfiles on user.Id equals profileRow.UserId into profiles
               from profile in profiles.DefaultIfEmpty()
               select new ConversationSummary(row.Id, row.Subject, row.RepliesEnabled, row.LatestActivityUtc,
                   false, row.PendingSinceUtc, user.Id, profile == null ? null : profile.DisplayName);
    }

    private async Task RequireMembershipAsync(CurrentActor actor, CancellationToken ct)
    {
        var user = await FindRecipientAsync(actor.TenantId, actor.ActorId, ct);
        if (user is null || !user.IsEnabled || user.LockoutEnd > clock.GetUtcNow())
            throw new InAppMessageException(MessageFailure.Forbidden, "message-membership-required");
    }

    private Task<FinancialCopilotUser?> FindRecipientAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        (from membership in auth.UserTenants
         join user in auth.Users.AsNoTracking() on membership.UserId equals user.Id
         where membership.TenantId == tenantId && user.Id == userId
         select user).SingleOrDefaultAsync(ct);

    private void RequireEligible(FinancialCopilotUser? user)
    {
        if (user is null)
            throw Missing();
        if (!user.IsEnabled || user.LockoutEnd > clock.GetUtcNow())
            throw Conflict("message-recipient-ineligible");
    }

    private Task<Replay?> FindReplayAsync(CurrentActor actor, Guid key, CancellationToken ct) =>
        (from message in Messages.AsNoTracking()
         join row in Conversations.AsNoTracking() on message.ConversationId equals row.Id
         where message.TenantId == actor.TenantId && message.AuthorUserId == actor.ActorId && message.IdempotencyKey == key
         select new Replay(message, row.RecipientUserId, row.Subject, row.RepliesEnabled)).SingleOrDefaultAsync(ct);

    private static MessageReceipt StartReplay(Replay replay, StartMessage command) =>
        replay.Message.Sequence == 1 && replay.RecipientUserId == command.RecipientUserId &&
        replay.Subject == command.Subject && replay.RepliesEnabled == command.RepliesEnabled && replay.Message.Body == command.Body
            ? Receipt(replay.Message) : throw Conflict("message-idempotency-conflict");

    private static MessageReceipt ReplyReplay(Replay replay, ReplyMessage command) =>
        replay.Message.Sequence > 1 && replay.Message.ConversationId == command.ConversationId &&
        replay.Message.FromStaff == (command.Audience == MessageAudience.Staff) && replay.Message.Body == command.Body &&
        (command.ExpectedRevision == null || command.ExpectedRevision == replay.Message.Sequence - 1)
            ? Receipt(replay.Message) : throw Conflict("message-idempotency-conflict");

    private void Audit(CurrentActor actor, string permission, string action, InAppConversationRow row, Guid? messageId, string correlationId) =>
        auth.SecurityAdminAudits.Add(new SecurityAdminAuditRow
        {
            Id = Guid.NewGuid(),
            OccurredAt = clock.GetUtcNow(),
            TenantId = actor.TenantId,
            ActorId = actor.ActorId,
            ActorType = actor.ActorType.ToString(),
            PermissionCode = permission,
            ActionCode = action,
            TargetType = messageId.HasValue ? "in_app_message" : "in_app_conversation",
            TargetId = (messageId ?? row.Id).ToString("D"),
            CorrelationId = correlationId,
            After = JsonSerializer.Serialize(new { ConversationId = row.Id, row.RecipientUserId, MessageId = messageId })
        });

    private static InAppMessageRow CreateMessage(InAppConversationRow row, Guid author, Guid key, string body, bool fromStaff, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(), ConversationId = row.Id, TenantId = row.TenantId, AuthorUserId = author,
            IdempotencyKey = key, Sequence = row.Revision, FromStaff = fromStaff, Body = body, SentAtUtc = now
        };

    private static bool IsIdempotencyRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: InAppMessageMapping.IdempotencyIndex };

    private DateTimeOffset UtcNow()
    {
        var now = clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static MessageReceipt Receipt(InAppMessageRow message) => new(message.ConversationId, message.Id, message.SentAtUtc);
    private static InAppMessageException Missing() => new(MessageFailure.NotFound, "message-not-found");
    private static InAppMessageException Conflict(string code) => new(MessageFailure.Conflict, code);

    private static string CursorScope(CurrentActor actor, MessageAudience audience, string qualifier) =>
        $"{actor.TenantId:D}:{actor.ActorId:D}:{audience}:{qualifier}";

    private string Encode<T>(string scope, T value) => cursors.CreateProtector(scope).Protect(JsonSerializer.Serialize(value));

    private T? ReadCursor<T>(string? cursor, string scope) where T : class
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(cursors.CreateProtector(scope).Unprotect(cursor))
                ?? throw new JsonException("Missing cursor value.");
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            throw new InAppMessageException(MessageFailure.Invalid, "invalid-message-cursor");
        }
    }

    private static MessagePage<T> Page<T>(List<T> items, int size, Func<T, string> cursor)
    {
        var hasMore = items.Count > size;
        if (hasMore)
            items.RemoveAt(size);
        return new MessagePage<T>(items, hasMore ? cursor(items[^1]) : null);
    }

    private sealed record ListCursor(DateTimeOffset Time, Guid Id);
    private sealed record DetailCursor(long Sequence);
    private sealed record Replay(InAppMessageRow Message, Guid RecipientUserId, string Subject, bool RepliesEnabled);
}

