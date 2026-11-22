using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.Application.InAppMessages;

public enum MessageAudience { Recipient, Staff }
public enum MessageListFilter { All, Unread, Pending }
public enum MessageFailure { Invalid, Forbidden, NotFound, Conflict }

public sealed class InAppMessageException(MessageFailure failure, string code)
    : Exception(code)
{
    public MessageFailure Failure { get; } = failure;
    public string Code { get; } = code;
}

public sealed record MessagePage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record MessageListRequest(MessageListFilter Filter, int PageSize, string? Cursor);
public sealed record MessageSummary(int UnreadCount);
public sealed record ConversationSummary(
    Guid ConversationId, string Subject, bool RepliesEnabled, DateTimeOffset LatestActivityUtc,
    bool Unread, DateTimeOffset? PendingSinceUtc, Guid? RecipientUserId = null, string? RecipientLabel = null);
public sealed record ConversationMessage(Guid Id, bool FromStaff, string Body, DateTimeOffset SentAtUtc, DateTimeOffset? ReadAtUtc);
public sealed record ConversationDetail(
    ConversationSummary Conversation, long Revision, MessagePage<ConversationMessage> Messages);
public sealed record MessageReceipt(Guid ConversationId, Guid MessageId, DateTimeOffset SentAtUtc);

public sealed record StartMessage(
    Guid RecipientUserId, string Subject, string Body, bool RepliesEnabled, Guid IdempotencyKey);
public sealed record ReplyMessage(
    Guid ConversationId, string Body, Guid IdempotencyKey, MessageAudience Audience, long? ExpectedRevision = null);

public interface IInAppMessageStore
{
    Task<MessageReceipt> StartAsync(CurrentActor actor, StartMessage command, string correlationId, CancellationToken ct);
    Task<MessageReceipt> ReplyAsync(CurrentActor actor, ReplyMessage command, string correlationId, CancellationToken ct);
    Task<MessageSummary> GetSummaryAsync(CurrentActor actor, CancellationToken ct);
    Task<MessagePage<ConversationSummary>> ListAsync(CurrentActor actor, MessageAudience audience, MessageListRequest request, CancellationToken ct);
    Task<ConversationDetail> DetailAsync(CurrentActor actor, MessageAudience audience, Guid id, int pageSize, string? cursor, string correlationId, CancellationToken ct);
    Task AcknowledgeAsync(CurrentActor actor, Guid id, IReadOnlyCollection<Guid> messageIds, CancellationToken ct);
}
