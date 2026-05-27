using FinancialCopilot.Application.Memory;

namespace FinancialCopilot.Infrastructure.Memory;

public sealed class EfCoreMemoryControlService(
    EfCoreMemoryRecordRepository recordRepository,
    IMemoryAuditService auditService,
    TimeProvider timeProvider) : IMemoryControlService
{
    public async Task<Guid> WriteAsync(
        MemorySubject owner,
        MemoryType type,
        MemoryPurpose purpose,
        MemorySensitivity sensitivity,
        string summary,
        MemoryProvenance provenance,
        MemoryRetentionPolicy? retention,
        CancellationToken cancellationToken)
    {
        var id = await recordRepository.WriteAsync(
            owner, type, purpose, sensitivity, summary, provenance, retention, cancellationToken);

        await auditService.RecordAsync(new MemoryAuditEvent(
            Guid.NewGuid(),
            owner,
            id,
            MemoryAuditAction.ConsentGranted,
            purpose,
            CorrelationId: string.Empty,
            timeProvider.GetUtcNow()),
            cancellationToken);

        return id;
    }

    public async Task<IReadOnlyCollection<OptionalMemoryRecord>> InspectAsync(
        MemorySubject subject,
        CancellationToken cancellationToken)
    {
        var records = await recordRepository.GetRecordsAsync(subject, cancellationToken);

        await auditService.RecordAsync(new MemoryAuditEvent(
            Guid.NewGuid(),
            subject,
            MemoryId: null,
            MemoryAuditAction.Inspected,
            MemoryPurpose.CurrentConversationContinuity,
            CorrelationId: string.Empty,
            timeProvider.GetUtcNow()),
            cancellationToken);

        return records;
    }

    public async Task DeleteAsync(MemoryDeletionRequest request, CancellationToken cancellationToken)
    {
        await recordRepository.SoftDeleteAsync(request.MemoryId, request.Subject, cancellationToken);

        await auditService.RecordAsync(new MemoryAuditEvent(
            Guid.NewGuid(),
            request.Subject,
            request.MemoryId,
            MemoryAuditAction.Deleted,
            MemoryPurpose.CurrentConversationContinuity,
            request.CorrelationId,
            timeProvider.GetUtcNow()),
            cancellationToken);
    }

    public async Task DeleteAllAsync(MemorySubject subject, string correlationId, CancellationToken cancellationToken)
    {
        await recordRepository.SoftDeleteAllAsync(subject, cancellationToken);

        await auditService.RecordAsync(new MemoryAuditEvent(
            Guid.NewGuid(),
            subject,
            MemoryId: null,
            MemoryAuditAction.Deleted,
            MemoryPurpose.CurrentConversationContinuity,
            correlationId,
            timeProvider.GetUtcNow(),
            Reason: "Bulk delete all records for subject"),
            cancellationToken);
    }
}
