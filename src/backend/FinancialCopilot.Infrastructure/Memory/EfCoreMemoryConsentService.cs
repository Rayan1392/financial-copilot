using FinancialCopilot.Application.Memory;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Memory;

public sealed class EfCoreMemoryConsentService(MemoryDbContext dbContext) : IMemoryConsentService
{
    private const string PolicyVersion = "v1";

    public async Task<MemoryConsentPolicy?> GetConsentAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ConsentPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == subject.TenantId &&
                     r.SubjectId == subject.SubjectId &&
                     r.MemoryType == type.ToString() &&
                     r.Purpose == purpose.ToString(),
                cancellationToken);

        return row is null ? null : MapPolicy(row);
    }

    public async Task<MemoryConsentPolicy> GrantAsync(
        MemoryConsentPolicy policy,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.ConsentPolicies
            .FirstOrDefaultAsync(
                r => r.TenantId == policy.TenantId &&
                     r.SubjectId == policy.SubjectId &&
                     r.MemoryType == policy.MemoryType.ToString() &&
                     r.Purpose == policy.Purpose.ToString(),
                cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            existing = new MemoryConsentPolicyRow
            {
                Id = Guid.NewGuid(),
                TenantId = policy.TenantId,
                SubjectId = policy.SubjectId,
                MemoryType = policy.MemoryType.ToString(),
                Purpose = policy.Purpose.ToString(),
                PolicyVersion = PolicyVersion
            };
            dbContext.ConsentPolicies.Add(existing);
        }

        existing.Status = MemoryConsentStatus.Granted.ToString();
        existing.GrantedAt = now;
        existing.RevokedAt = null;
        existing.ExpiresAt = policy.ExpiresAt;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapPolicy(existing);
    }

    public async Task RevokeAsync(
        MemorySubject subject,
        MemoryType type,
        MemoryPurpose purpose,
        string policyVersion,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ConsentPolicies
            .FirstOrDefaultAsync(
                r => r.TenantId == subject.TenantId &&
                     r.SubjectId == subject.SubjectId &&
                     r.MemoryType == type.ToString() &&
                     r.Purpose == purpose.ToString(),
                cancellationToken);

        if (row is null) return;

        row.Status = MemoryConsentStatus.Revoked.ToString();
        row.RevokedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MemoryConsentPolicy MapPolicy(MemoryConsentPolicyRow row) =>
        new(
            row.TenantId,
            row.SubjectId,
            Enum.Parse<MemoryType>(row.MemoryType),
            Enum.Parse<MemoryPurpose>(row.Purpose),
            Enum.TryParse<MemoryConsentStatus>(row.Status, out var status)
                ? status : MemoryConsentStatus.NotFound,
            row.GrantedAt,
            row.RevokedAt,
            row.ExpiresAt,
            row.PolicyVersion);
}
