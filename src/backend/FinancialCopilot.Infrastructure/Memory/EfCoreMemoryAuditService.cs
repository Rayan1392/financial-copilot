using FinancialCopilot.Application.Memory;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Memory;

public sealed class EfCoreMemoryAuditService(
    MemoryDbContext dbContext,
    ILogger<EfCoreMemoryAuditService> logger) : IMemoryAuditService
{
    public async Task RecordAsync(MemoryAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            var row = new MemoryAuditEventRow
            {
                Id = auditEvent.EventId,
                TenantId = auditEvent.Subject.TenantId,
                SubjectId = auditEvent.Subject.SubjectId,
                MemoryId = auditEvent.MemoryId,
                Action = auditEvent.Action.ToString(),
                Purpose = auditEvent.Purpose.ToString(),
                CorrelationId = auditEvent.CorrelationId,
                OccurredAt = auditEvent.OccurredAt,
                Reason = auditEvent.Reason
            };
            dbContext.AuditEvents.Add(row);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist memory audit event {EventId} for subject {SubjectId}",
                auditEvent.EventId, auditEvent.Subject.SubjectId);
        }
    }
}
