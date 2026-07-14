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
        builder.Property(row => row.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.CorrelationId).HasMaxLength(128);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Channel, row.DeduplicationKey })
            .IsUnique()
            .HasDatabaseName("UIX_NotificationIntents_Actor_Channel_Dedup");
        builder.HasIndex(row => new { row.Status, row.NotBeforeUtc, row.ExpiresAtUtc })
            .HasDatabaseName("IX_NotificationIntents_Due");
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.CreatedAtUtc })
            .HasDatabaseName("IX_NotificationIntents_Actor_History");
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
