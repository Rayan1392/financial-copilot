using System.Text.Json;
using FinancialCopilot.Application.AI.ReportDiscussion;
using FinancialCopilot.Application.Conversations;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

/// <summary>
/// Feature 162: bounded, owner-scoped read of a conversation's newest messages. The conversation must belong to the
/// tenant and actor; a payload that cannot be read is returned without it (the turn then falls back to normal routing).
/// </summary>
public sealed class ReportDiscussionMessageReader(ConversationDbContext dbContext) : IReportDiscussionMessageReader
{
    public async Task<IReadOnlyList<MessageRecord>> ListRecentAsync(
        Guid conversationId,
        Guid tenantId,
        Guid actorId,
        int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0) return [];

        var owned = dbContext.Conversations.AsNoTracking()
            .Where(conversation => conversation.Id == conversationId &&
                                   conversation.TenantId == tenantId &&
                                   conversation.ActorId == actorId)
            .Select(conversation => conversation.Id);

        var rows = await dbContext.Messages.AsNoTracking()
            .Where(message => owned.Contains(message.ConversationId))
            .OrderByDescending(message => message.CreatedAt)
            .ThenBy(message => message.Role == nameof(MessageRole.Assistant) ? 0 : 1)
            .Take(take)
            .ToListAsync(cancellationToken);

        rows.Reverse();
        return rows.Select(row => new MessageRecord(
                row.Id,
                row.ConversationId,
                Enum.TryParse<MessageRole>(row.Role, out var role) ? role : MessageRole.User,
                row.Content,
                row.ScannerQueryPlanJson,
                row.CreatedAt,
                TryDeserialize(row.AssistantPayloadJson)))
            .ToArray();
    }

    private static AssistantMessagePayload? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AssistantMessagePayload>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
