using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class RadarProfileRowConfiguration : IEntityTypeConfiguration<RadarProfileRow>
{
    public void Configure(EntityTypeBuilder<RadarProfileRow> builder)
    {
        builder.ToTable("RadarProfiles");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EventTypesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.MinimumSeverity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Sensitivity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.DeliveryMode).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.Property(row => row.LeaseOwner).HasMaxLength(128);
        builder.Property(row => row.LastFailure).HasMaxLength(1000);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType }).IsUnique()
            .HasDatabaseName("UIX_RadarProfiles_Actor");
        builder.HasIndex(row => new { row.State, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc })
            .HasDatabaseName("IX_RadarProfiles_EvaluationDue");
    }
}

public sealed class RadarSymbolOverrideRowConfiguration : IEntityTypeConfiguration<RadarSymbolOverrideRow>
{
    public void Configure(EntityTypeBuilder<RadarSymbolOverrideRow> builder)
    {
        builder.ToTable("RadarSymbolOverrides");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EventTypesJson).HasColumnType("jsonb");
        builder.Property(row => row.MinimumSeverity).HasMaxLength(32);
        builder.Property(row => row.Sensitivity).HasMaxLength(32);
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(row => new { row.RadarProfileId, row.ExternalCompanyId }).IsUnique()
            .HasDatabaseName("UIX_RadarSymbolOverrides_Profile_Company");
        builder.HasIndex(row => new { row.ExternalCompanyId, row.State })
            .HasDatabaseName("IX_RadarSymbolOverrides_Company_State");
        builder.HasOne<RadarProfileRow>().WithMany().HasForeignKey(row => row.RadarProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RadarEventMatchRowConfiguration : IEntityTypeConfiguration<RadarEventMatchRow>
{
    public void Configure(EntityTypeBuilder<RadarEventMatchRow> builder)
    {
        builder.ToTable("RadarEventMatches");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Decision).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SuppressionReason).HasMaxLength(64).IsRequired();
        builder.Property(row => row.AppliedSensitivity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.AppliedPolicyVersion).HasMaxLength(128).IsRequired();
        builder.Property(row => row.NotificationPolicyVersion).HasMaxLength(128).IsRequired();
        builder.Property(row => row.ComponentInsightEventIdsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.EvidenceReference).HasMaxLength(512).IsRequired();
        builder.Property(row => row.DeduplicationKey).HasMaxLength(512).IsRequired();
        builder.HasIndex(row => row.DeduplicationKey).IsUnique()
            .HasDatabaseName("UIX_RadarEventMatches_DeduplicationKey");
        builder.HasIndex(row => new { row.RadarProfileId, row.EvaluatedAtUtc })
            .HasDatabaseName("IX_RadarEventMatches_Profile_EvaluatedAt");
        builder.HasIndex(row => new { row.ExternalCompanyId, row.InsightEventId })
            .HasDatabaseName("IX_RadarEventMatches_Company_Insight");
        builder.HasIndex(row => row.NotificationIntentId);
        builder.HasOne<RadarProfileRow>().WithMany().HasForeignKey(row => row.RadarProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<InsightEventRow>().WithMany().HasForeignKey(row => row.InsightEventId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RadarPreferenceAuditRowConfiguration : IEntityTypeConfiguration<RadarPreferenceAuditRow>
{
    public void Configure(EntityTypeBuilder<RadarPreferenceAuditRow> builder)
    {
        builder.ToTable("RadarPreferenceAudits");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Action).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Source).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(row => new { row.RadarProfileId, row.OccurredAtUtc })
            .HasDatabaseName("IX_RadarPreferenceAudits_Profile_OccurredAt");
        builder.HasOne<RadarProfileRow>().WithMany().HasForeignKey(row => row.RadarProfileId).OnDelete(DeleteBehavior.Cascade);
    }
}
