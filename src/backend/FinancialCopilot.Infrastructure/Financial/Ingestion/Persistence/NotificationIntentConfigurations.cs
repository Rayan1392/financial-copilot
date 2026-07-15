using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class NotificationIntentRowConfiguration : IEntityTypeConfiguration<NotificationIntentRow>
{
    public void Configure(EntityTypeBuilder<NotificationIntentRow> builder)
    {
        builder.ToTable("NotificationIntents");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Channel).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EventType).HasMaxLength(128).IsRequired();
        builder.Property(row => row.EntityKey).HasMaxLength(256).IsRequired();
        builder.Property(row => row.DeduplicationKey).HasMaxLength(512).IsRequired();
        builder.Property(row => row.Severity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Category).HasMaxLength(128).IsRequired();
        builder.Property(row => row.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.EvidenceReference).HasMaxLength(512);
        builder.Property(row => row.CooldownKey).HasMaxLength(512);
        builder.Property(row => row.PolicyVersion).HasMaxLength(64);
        builder.Property(row => row.DecisionReason).HasMaxLength(64);
        builder.Property(row => row.DecisionExplanation).HasMaxLength(512);
        builder.Property(row => row.LastErrorCode).HasMaxLength(64);
        builder.Property(row => row.LastErrorRedacted).HasMaxLength(512);
        builder.Property(row => row.CorrelationId).HasMaxLength(128);
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Channel, row.DeduplicationKey })
            .IsUnique()
            .HasDatabaseName("UIX_NotificationIntents_Actor_Channel_Dedup");
        builder.HasIndex(row => new { row.Status, row.NotBeforeUtc, row.ExpiresAtUtc })
            .HasDatabaseName("IX_NotificationIntents_Due");
        builder.HasIndex(row => new { row.Status, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc })
            .HasDatabaseName("IX_NotificationIntents_RetryLease");
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.CooldownKey, row.DeliveredAtUtc })
            .HasDatabaseName("IX_NotificationIntents_Cooldown");
        builder.HasIndex(row => row.BatchId).HasDatabaseName("IX_NotificationIntents_BatchId");
        builder.HasOne<NotificationBatchRow>().WithMany().HasForeignKey(row => row.BatchId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.CreatedAtUtc })
            .HasDatabaseName("IX_NotificationIntents_Actor_History");
    }
}

public sealed class NotificationPreferenceRowConfiguration : IEntityTypeConfiguration<NotificationPreferenceRow>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceRow> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.DeliveryMode).HasMaxLength(32).IsRequired();
        builder.Property(row => row.MinimumSeverity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType })
            .IsUnique().HasDatabaseName("UIX_NotificationPreferences_Actor");
    }
}

public sealed class NotificationCategoryPreferenceRowConfiguration : IEntityTypeConfiguration<NotificationCategoryPreferenceRow>
{
    public void Configure(EntityTypeBuilder<NotificationCategoryPreferenceRow> builder)
    {
        builder.ToTable("NotificationCategoryPreferences");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EventType).HasMaxLength(128).IsRequired();
        builder.Property(row => row.MinimumSeverity).HasMaxLength(32);
        builder.HasIndex(row => new { row.PreferenceId, row.EventType }).IsUnique();
        builder.HasOne<NotificationPreferenceRow>().WithMany().HasForeignKey(row => row.PreferenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationSymbolPreferenceRowConfiguration : IEntityTypeConfiguration<NotificationSymbolPreferenceRow>
{
    public void Configure(EntityTypeBuilder<NotificationSymbolPreferenceRow> builder)
    {
        builder.ToTable("NotificationSymbolPreferences");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.PreferenceId, row.ExternalCompanyId }).IsUnique();
        builder.HasOne<NotificationPreferenceRow>().WithMany().HasForeignKey(row => row.PreferenceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationPreferenceAuditRowConfiguration : IEntityTypeConfiguration<NotificationPreferenceAuditRow>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceAuditRow> builder)
    {
        builder.ToTable("NotificationPreferenceAudits");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Action).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Source).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(row => new { row.PreferenceId, row.OccurredAtUtc });
    }
}

public sealed class NotificationBatchRowConfiguration : IEntityTypeConfiguration<NotificationBatchRow>
{
    public void Configure(EntityTypeBuilder<NotificationBatchRow> builder)
    {
        builder.ToTable("NotificationBatches");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Channel).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Channel, row.ScheduledForUtc, row.Status })
            .HasDatabaseName("IX_NotificationBatches_Window");
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Channel, row.ScheduledForUtc })
            .IsUnique().HasDatabaseName("UIX_NotificationBatches_ActorWindow");
    }
}

public sealed class NotificationDeliveryAttemptRowConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttemptRow>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttemptRow> builder)
    {
        builder.ToTable("NotificationDeliveryAttempts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.DeliveryPartKey).HasMaxLength(256).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(256).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ProviderMessageId).HasMaxLength(128);
        builder.Property(row => row.ErrorCode).HasMaxLength(64);
        builder.Property(row => row.ErrorRedacted).HasMaxLength(512);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => row.DeliveryPartKey)
            .IsUnique()
            .HasFilter("\"Status\" = 'Delivered'")
            .HasDatabaseName("UIX_NotificationDeliveryAttempts_DeliveredPart");
        builder.HasIndex(row => new { row.NotificationIntentId, row.PartNumber, row.Status });
        builder.HasOne<NotificationIntentRow>().WithMany().HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationOutcomeHandoffRowConfiguration : IEntityTypeConfiguration<NotificationOutcomeHandoffRow>
{
    public void Configure(EntityTypeBuilder<NotificationOutcomeHandoffRow> builder)
    {
        builder.ToTable("NotificationOutcomeHandoffs");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.TerminalStatus).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EvidenceReference).HasMaxLength(512);
        builder.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(row => new { row.NotificationIntentId, row.Sequence }).IsUnique();
        builder.HasIndex(row => new { row.Status, row.CreatedAtUtc });
        builder.HasOne<NotificationIntentRow>().WithMany().HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationOperationAuditRowConfiguration : IEntityTypeConfiguration<NotificationOperationAuditRow>
{
    public void Configure(EntityTypeBuilder<NotificationOperationAuditRow> builder)
    {
        builder.ToTable("NotificationOperationAudits");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Action).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Detail).HasMaxLength(512).IsRequired();
        builder.Property(row => row.CorrelationId).HasMaxLength(128).IsRequired();
        builder.HasIndex(row => new { row.NotificationIntentId, row.OccurredAtUtc });
        builder.HasOne<NotificationIntentRow>().WithMany().HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CodalAlertSummaryRowConfiguration : IEntityTypeConfiguration<CodalAlertSummaryRow>
{
    public void Configure(EntityTypeBuilder<CodalAlertSummaryRow> builder)
    {
        builder.ToTable("CodalAlertSummaries");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EvidenceHash).HasMaxLength(128).IsRequired();
        builder.Property(row => row.SummaryText).HasMaxLength(4000);
        builder.Property(row => row.ProviderName).HasMaxLength(64);
        builder.Property(row => row.ModelName).HasMaxLength(128);
        builder.Property(row => row.PromptPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ReservationIdempotencyKey).HasMaxLength(256);
        builder.Property(row => row.FailureReason).HasMaxLength(1000);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.InsightEventId })
            .IsUnique()
            .HasDatabaseName("UIX_CodalAlertSummaries_Actor_Insight");
        builder.HasIndex(row => row.NotificationIntentId)
            .HasDatabaseName("IX_CodalAlertSummaries_NotificationIntentId");
    }
}
