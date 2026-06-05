using FinancialCopilot.Application.Memory;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

internal sealed class MemoryContextAdapter(
    IMemoryContextProvider contextProvider,
    IMemoryAuditService auditService)
{
    internal Task<AuthorizedMemoryContext> GetContextAsync(
        Guid tenantId,
        Guid actorId,
        Guid? userId,
        Guid conversationId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var subjectId = userId ?? actorId;
        return contextProvider.GetAuthorizedContextAsync(
            new MemoryContextRequest(
                new MemorySubject(tenantId, subjectId),
                conversationId,
                MemoryPurpose.CurrentConversationContinuity,
                correlationId,
                PermitProviderPromptContext: true),
            cancellationToken);
    }

    internal async Task RecordAuditAsync(
        AuthorizedMemoryContext context,
        Guid tenantId,
        Guid actorId,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var item in context.Items)
        {
            await auditService.RecordAsync(new MemoryAuditEvent(
                Guid.NewGuid(),
                item.Owner,
                item.MemoryId,
                MemoryAuditAction.UsedInAnswer,
                item.Purpose,
                correlationId,
                now),
                cancellationToken);
        }
    }
}
