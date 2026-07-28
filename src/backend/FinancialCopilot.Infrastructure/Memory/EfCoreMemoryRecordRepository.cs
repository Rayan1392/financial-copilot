using FinancialCopilot.Application.Memory;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Memory;

public sealed class EfCoreMemoryRecordRepository(MemoryDbContext dbContext)
{
    public async Task<IReadOnlyCollection<OptionalMemoryRecord>> GetRecordsAsync(
        MemorySubject subject,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.MemoryRecords
            .AsNoTracking()
            .Where(r => r.TenantId == subject.TenantId && r.SubjectId == subject.SubjectId && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        return rows.Select(MapRecord).ToList();
    }

    public async Task<OptionalMemoryRecord?> GetRecordAsync(
        Guid memoryId,
        MemorySubject subject,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.MemoryRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == memoryId &&
                     r.TenantId == subject.TenantId &&
                     r.SubjectId == subject.SubjectId &&
                     !r.IsDeleted,
                cancellationToken);

        return row is null ? null : MapRecord(row);
    }

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
        var row = new MemoryRecordRow
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId,
            SubjectId = owner.SubjectId,
            MemoryType = type.ToString(),
            Purpose = purpose.ToString(),
            Sensitivity = sensitivity.ToString(),
            Summary = summary,
            MemoryVersion = 1,
            PolicyVersion = "v1",
            ProvenanceSourceType = provenance.SourceType,
            ProvenanceSourceRef = provenance.AuthoritativeRecordReference,
            CapturedAt = provenance.CapturedAt,
            ExpiresAt = retention?.ExpiresAt,
            IsDeleted = false
        };
        dbContext.MemoryRecords.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<bool> SoftDeleteAsync(
        Guid memoryId,
        MemorySubject subject,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.MemoryRecords
            .FirstOrDefaultAsync(
                r => r.Id == memoryId &&
                     r.TenantId == subject.TenantId &&
                     r.SubjectId == subject.SubjectId &&
                     !r.IsDeleted,
                cancellationToken);

        if (row is null) return false;

        row.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> SoftDeleteAllAsync(
        MemorySubject subject,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.MemoryRecords
            .Where(r => r.TenantId == subject.TenantId && r.SubjectId == subject.SubjectId && !r.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
            row.IsDeleted = true;

        await dbContext.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private static OptionalMemoryRecord MapRecord(MemoryRecordRow row) =>
        new(
            row.Id,
            new MemorySubject(row.TenantId, row.SubjectId),
            Enum.Parse<MemoryType>(row.MemoryType),
            Enum.Parse<MemoryPurpose>(row.Purpose),
            Enum.Parse<MemorySensitivity>(row.Sensitivity),
            row.MemoryVersion.ToString(),
            row.PolicyVersion,
            row.Summary,
            new MemoryProvenance(row.ProvenanceSourceType, row.ProvenanceSourceRef, row.CapturedAt),
            new MemoryRetentionPolicy(row.ExpiresAt, InspectableBySubject: true, DeletableBySubject: true));
}
