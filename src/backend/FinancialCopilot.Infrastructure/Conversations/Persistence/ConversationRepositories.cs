using FinancialCopilot.Application.Conversations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationRepository(ConversationDbContext dbContext) : IConversationRepository
{
    private const string DefaultTitle = "New conversation";

    public async Task<Guid> CreateAsync(
        Guid tenantId,
        Guid actorId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
        => await CreateEmptyAsync(tenantId, actorId, startedAt, cancellationToken);

    public async Task<Guid> CreateEmptyAsync(
        Guid tenantId,
        Guid actorId,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var row = new ConversationRow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            StartedAt = startedAt,
            UpdatedAt = startedAt,
            MessageCount = 0,
            Title = DefaultTitle
        };
        dbContext.Conversations.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<ConversationSummary?> FindAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == conversationId && c.TenantId == tenantId && c.ActorId == actorId,
                cancellationToken);

        return row is null ? null : MapSummary(row);
    }

    public async Task<IReadOnlyCollection<ConversationSummary>> ListByActorAsync(
        Guid tenantId,
        Guid actorId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.ActorId == actorId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(MapSummary).ToList();
    }

    public async Task TouchAsync(
        Guid conversationId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);

        if (row is null) return;

        row.UpdatedAt = updatedAt;
        row.MessageCount += 1;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Conversations
            .FirstOrDefaultAsync(
                c => c.Id == conversationId && c.TenantId == tenantId && c.ActorId == actorId,
                cancellationToken);

        if (row is null) return false;

        var messages = dbContext.Messages.Where(message => message.ConversationId == conversationId);
        var taskStates = dbContext.ConversationTaskStates.Where(state => state.ConversationId == conversationId);
        dbContext.Messages.RemoveRange(messages);
        dbContext.ConversationTaskStates.RemoveRange(taskStates);
        dbContext.Conversations.Remove(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PersistedConversationExchange> PersistExchangeAsync(
        ConversationExchange exchange,
        bool createConversation,
        CancellationToken cancellationToken)
    {
        ConversationRow row;
        if (createConversation)
        {
            row = new ConversationRow
            {
                Id = exchange.ConversationId,
                TenantId = exchange.TenantId,
                ActorId = exchange.ActorId,
                StartedAt = exchange.CreatedAt,
                UpdatedAt = exchange.CreatedAt,
                MessageCount = 0,
                Title = exchange.Title
            };
            dbContext.Conversations.Add(row);
        }
        else
        {
            row = await dbContext.Conversations.FirstOrDefaultAsync(
                conversation =>
                    conversation.Id == exchange.ConversationId &&
                    conversation.TenantId == exchange.TenantId &&
                    conversation.ActorId == exchange.ActorId,
                cancellationToken) ?? throw new ConversationNotFoundException(exchange.ConversationId);

            if (string.IsNullOrWhiteSpace(row.Title) ||
                string.Equals(row.Title, DefaultTitle, StringComparison.Ordinal))
            {
                row.Title = exchange.Title;
            }
        }

        var userMessage = new MessageRow
        {
            Id = Guid.NewGuid(),
            ConversationId = exchange.ConversationId,
            Role = MessageRole.User.ToString(),
            Content = exchange.UserContent,
            CreatedAt = exchange.CreatedAt
        };
        var assistantMessage = new MessageRow
        {
            Id = Guid.NewGuid(),
            ConversationId = exchange.ConversationId,
            Role = MessageRole.Assistant.ToString(),
            Content = exchange.AssistantContent,
            ScannerQueryPlanJson = exchange.ScannerQueryPlanJson,
            AssistantPayloadJson = JsonSerializer.Serialize(exchange.AssistantPayload),
            CreatedAt = exchange.CreatedAt
        };

        dbContext.Messages.AddRange(userMessage, assistantMessage);
        row.UpdatedAt = exchange.CreatedAt;
        row.MessageCount += 2;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PersistedConversationExchange(userMessage.Id, assistantMessage.Id);
    }

    private static ConversationSummary MapSummary(ConversationRow row) =>
        new(
            row.Id,
            row.TenantId,
            row.ActorId,
            row.StartedAt,
            row.UpdatedAt,
            row.MessageCount,
            string.IsNullOrWhiteSpace(row.Title) ? DefaultTitle : row.Title);
}

public sealed class MessageRepository(ConversationDbContext dbContext) : IMessageRepository
{
    public async Task<Guid> AppendAsync(
        Guid conversationId,
        MessageRole role,
        string content,
        string? scannerQueryPlanJson,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var row = new MessageRow
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = role.ToString(),
            Content = content,
            ScannerQueryPlanJson = scannerQueryPlanJson,
            CreatedAt = createdAt
        };
        dbContext.Messages.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyCollection<MessageRecord>> ListByConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Role == nameof(MessageRole.User) ? 0 : 1)
            .ToListAsync(cancellationToken);

        return rows.Select(MapRecord).ToList();
    }

    private static MessageRecord MapRecord(MessageRow row) =>
        new(
            row.Id,
            row.ConversationId,
            Enum.TryParse<MessageRole>(row.Role, out var role) ? role : MessageRole.User,
            row.Content,
            row.ScannerQueryPlanJson,
            row.CreatedAt,
            DeserializePayload(row.AssistantPayloadJson));

    private static AssistantMessagePayload? DeserializePayload(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AssistantMessagePayload>(json);
}
