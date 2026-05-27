using FinancialCopilot.Application.Conversations;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationRepository(ConversationDbContext dbContext) : IConversationRepository
{
    public async Task<Guid> CreateAsync(
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
            MessageCount = 0
        };
        dbContext.Conversations.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<ConversationSummary?> FindAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == conversationId && c.TenantId == tenantId,
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

    private static ConversationSummary MapSummary(ConversationRow row) =>
        new(row.Id, row.TenantId, row.ActorId, row.StartedAt, row.UpdatedAt, row.MessageCount);
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
            row.CreatedAt);
}
