using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Memory.Persistence;

public sealed class MemoryConsentPolicyRowConfiguration : IEntityTypeConfiguration<MemoryConsentPolicyRow>
{
    public void Configure(EntityTypeBuilder<MemoryConsentPolicyRow> builder)
    {
        builder.ToTable("MemoryConsentPolicies");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TenantId, r.SubjectId, r.MemoryType, r.Purpose }).IsUnique();
        builder.Property(r => r.MemoryType).HasMaxLength(50);
        builder.Property(r => r.Purpose).HasMaxLength(50);
        builder.Property(r => r.Status).HasMaxLength(20);
        builder.Property(r => r.PolicyVersion).HasMaxLength(20);
    }
}

public sealed class MemoryRecordRowConfiguration : IEntityTypeConfiguration<MemoryRecordRow>
{
    public void Configure(EntityTypeBuilder<MemoryRecordRow> builder)
    {
        builder.ToTable("MemoryRecords");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TenantId, r.SubjectId, r.IsDeleted });
        builder.Property(r => r.Summary).HasColumnType("text");
        builder.Property(r => r.MemoryType).HasMaxLength(50);
        builder.Property(r => r.Purpose).HasMaxLength(50);
        builder.Property(r => r.Sensitivity).HasMaxLength(30);
        builder.Property(r => r.PolicyVersion).HasMaxLength(20);
        builder.Property(r => r.ProvenanceSourceType).HasMaxLength(50);
    }
}

public sealed class MemoryAuditEventRowConfiguration : IEntityTypeConfiguration<MemoryAuditEventRow>
{
    public void Configure(EntityTypeBuilder<MemoryAuditEventRow> builder)
    {
        builder.ToTable("MemoryAuditEvents");
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.TenantId, r.SubjectId, r.OccurredAt });
        builder.Property(r => r.Action).HasMaxLength(30);
        builder.Property(r => r.Purpose).HasMaxLength(50);
        builder.Property(r => r.CorrelationId).HasMaxLength(100);
    }
}
