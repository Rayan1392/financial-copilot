namespace FinancialCopilot.Application.Conversations;

public enum MessageRole { User, Assistant }

public sealed record ConversationSummary(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount);

public sealed record ConversationDetail(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<MessageRecord> Messages);

public sealed record MessageRecord(
    Guid MessageId,
    Guid ConversationId,
    MessageRole Role,
    string Content,
    string? ScannerQueryPlanJson,
    DateTimeOffset CreatedAt);

public interface IConversationRepository
{
    Task<Guid> CreateAsync(
        Guid tenantId,
        Guid actorId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<ConversationSummary?> FindAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ConversationSummary>> ListByActorAsync(
        Guid tenantId,
        Guid actorId,
        int limit,
        CancellationToken cancellationToken);

    Task TouchAsync(
        Guid conversationId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}

public interface IMessageRepository
{
    Task<Guid> AppendAsync(
        Guid conversationId,
        MessageRole role,
        string content,
        string? scannerQueryPlanJson,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken);
}
