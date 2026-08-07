using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationTaskStateRepository(ConversationDbContext dbContext) : IConversationTaskStateRepository
{
    public async Task<ConversationTaskState?> FindAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
    {
        var row = await dbContext.ConversationTaskStates.AsNoTracking().SingleOrDefaultAsync(row =>
            row.ConversationId == scope.ConversationId && row.TenantId == scope.TenantId && row.ActorId == scope.ActorId, cancellationToken);
        return row is null ? null : JsonSerializer.Deserialize<ConversationTaskState>(row.StateJson);
    }

    public async Task<ConversationTaskStateWriteResult> TryWriteAsync(ConversationTaskState state, long? expectedVersion, CancellationToken cancellationToken)
    {
        var row = await dbContext.ConversationTaskStates.SingleOrDefaultAsync(candidate => candidate.ConversationId == state.ConversationId && candidate.TenantId == state.TenantId && candidate.ActorId == state.ActorId, cancellationToken);
        if (row is null)
        {
            if (expectedVersion is not null) return new(false, null);
            dbContext.ConversationTaskStates.Add(ConversationTaskStateRow.From(state));
        }
        else
        {
            if (row.Version != expectedVersion) return new(false, null);
            row.Version = state.Version;
            row.UpdatedAt = state.UpdatedAt;
            row.ExpiresAt = state.ExpiresAt;
            row.LastCorrelationId = state.LastCorrelationId;
            row.StateJson = JsonSerializer.Serialize(state);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, state);
    }

    public async Task DeleteAsync(ConversationTaskStateScope scope, CancellationToken cancellationToken)
    {
        var rows = dbContext.ConversationTaskStates.Where(row => row.ConversationId == scope.ConversationId && row.TenantId == scope.TenantId && row.ActorId == scope.ActorId);
        dbContext.ConversationTaskStates.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
