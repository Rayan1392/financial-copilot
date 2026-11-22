using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.Application.InAppMessages;

public sealed class InAppMessageUseCases(IInAppMessageStore store)
{
    public const int SubjectLimit = 120;
    public const int BodyLimit = 2000;
    public const int MaxPageSize = 50;
    public const int UnreadCap = 100;

    public Task<MessageReceipt> StartAsync(CurrentActor actor, StartMessage command, string correlationId, CancellationToken ct)
    {
        RequireWebUser(actor);
        RequireId(command.RecipientUserId);
        RequireId(command.IdempotencyKey);
        return store.StartAsync(actor, command with
        {
            Subject = Text(command.Subject, SubjectLimit),
            Body = Text(command.Body, BodyLimit)
        }, correlationId, ct);
    }

    public Task<MessageReceipt> ReplyAsync(CurrentActor actor, ReplyMessage command, string correlationId, CancellationToken ct)
    {
        RequireWebUser(actor);
        RequireId(command.ConversationId);
        RequireId(command.IdempotencyKey);
        if (command.Audience == MessageAudience.Staff && command.ExpectedRevision is null or < 1)
            throw Invalid("invalid-message-revision");

        return store.ReplyAsync(actor, command with { Body = Text(command.Body, BodyLimit) }, correlationId, ct);
    }

    public Task<MessageSummary> GetSummaryAsync(CurrentActor actor, CancellationToken ct)
    {
        RequireWebUser(actor);
        return store.GetSummaryAsync(actor, ct);
    }

    public Task<MessagePage<ConversationSummary>> ListAsync(
        CurrentActor actor, MessageAudience audience, MessageListRequest request, CancellationToken ct)
    {
        RequireWebUser(actor);
        var validFilter = request.Filter == MessageListFilter.All ||
            (audience == MessageAudience.Staff ? request.Filter == MessageListFilter.Pending : request.Filter == MessageListFilter.Unread);
        if (!validFilter)
            throw Invalid("invalid-message-filter");

        ValidatePage(request.PageSize, request.Cursor);
        return store.ListAsync(actor, audience, request, ct);
    }

    public Task<ConversationDetail> DetailAsync(
        CurrentActor actor, MessageAudience audience, Guid id, int pageSize, string? cursor, string correlationId, CancellationToken ct)
    {
        RequireWebUser(actor);
        RequireId(id);
        ValidatePage(pageSize, cursor);
        return store.DetailAsync(actor, audience, id, pageSize, cursor, correlationId, ct);
    }

    public Task AcknowledgeAsync(CurrentActor actor, Guid id, IReadOnlyCollection<Guid>? messageIds, CancellationToken ct)
    {
        RequireWebUser(actor);
        RequireId(id);
        if (messageIds is null || messageIds.Count is < 1 or > MaxPageSize ||
            messageIds.Contains(Guid.Empty) || messageIds.Distinct().Count() != messageIds.Count)
            throw Invalid("invalid-message-acknowledgement");

        return store.AcknowledgeAsync(actor, id, messageIds, ct);
    }

    private static void RequireWebUser(CurrentActor actor)
    {
        if (actor.ActorType != ActorType.User || actor.AuthenticationMode != AuthenticationMode.WebAppUser ||
            actor.ActorId == Guid.Empty || actor.TenantId == Guid.Empty)
            throw new InAppMessageException(MessageFailure.Forbidden, "message-web-user-required");
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty)
            throw Invalid("invalid-message-identifier");
    }

    private static string Text(string? value, int limit)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) || normalized.Length > limit || normalized.Contains('\0')
            ? throw Invalid("invalid-message-content")
            : normalized;
    }

    private static void ValidatePage(int size, string? cursor)
    {
        if (size is < 1 or > MaxPageSize || cursor is { Length: > 2048 })
            throw Invalid("invalid-message-page");
    }

    private static InAppMessageException Invalid(string code) => new(MessageFailure.Invalid, code);
}
