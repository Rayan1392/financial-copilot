using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Application.Conversations;

public enum MessageRole { User, Assistant }

public sealed record ConversationSummary(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    int MessageCount,
    string Title);

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
    DateTimeOffset CreatedAt,
    AssistantMessagePayload? AssistantPayload = null);

public sealed record AssistantMessagePayload(
    int Version,
    DetectedIntent Intent,
    bool ClarificationRequired,
    string? ClarificationMessage,
    string? TextAnswer,
    ScannerQueryPlan? ScannerPlan,
    ScannerTableResult? ScannerTable,
    SymbolLookupTableResult? SymbolLookupTable,
    ExplainableAnswer? ExplainableAnswer,
    ConfidenceScoreResult? ConfidenceScore,
    UsageAccountingResult? Usage,
    IReadOnlyCollection<MemoryUseDisclosure>? MemoryDisclosures);

public sealed record ConversationExchange(
    Guid ConversationId,
    Guid TenantId,
    Guid ActorId,
    DateTimeOffset CreatedAt,
    string Title,
    string UserContent,
    string AssistantContent,
    string? ScannerQueryPlanJson,
    AssistantMessagePayload AssistantPayload);

public sealed record PersistedConversationExchange(
    Guid UserMessageId,
    Guid AssistantMessageId);

public sealed class ConversationNotFoundException(Guid conversationId)
    : Exception($"Conversation '{conversationId}' was not found.")
{
}

public interface IConversationRepository
{
    Task<Guid> CreateAsync(
        Guid tenantId,
        Guid actorId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<Guid> CreateEmptyAsync(
        Guid tenantId,
        Guid actorId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken);

    Task<ConversationSummary?> FindAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
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

    Task<bool> DeleteAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken);

    Task<PersistedConversationExchange> PersistExchangeAsync(
        ConversationExchange exchange,
        bool createConversation,
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
