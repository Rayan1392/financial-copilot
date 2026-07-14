using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class AlertRuleRowConfiguration : IEntityTypeConfiguration<AlertRuleRow>
{
    public void Configure(EntityTypeBuilder<AlertRuleRow> builder)
    {
        builder.ToTable("AlertRules");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ActorType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RuleType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.MetricOrEventCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Operator).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Unit).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Recurrence).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ResetPolicy).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SessionPolicy).HasMaxLength(32).IsRequired();
        builder.Property(row => row.State).HasMaxLength(32).IsRequired();
        builder.Property(row => row.OriginalText).HasMaxLength(500);
        builder.Property(row => row.ParserVersion).HasMaxLength(64);
        builder.Property(row => row.ConfirmationNonce).HasMaxLength(64).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(128);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.State })
            .HasDatabaseName("IX_AlertRules_Actor_State");
        builder.HasIndex(row => new { row.ExternalCompanyId, row.State, row.RuleType })
            .HasDatabaseName("IX_AlertRules_Company_State_Type");
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("UIX_AlertRules_Actor_IdempotencyKey");
    }
}

public sealed class AlertRuleEvaluationStateRowConfiguration : IEntityTypeConfiguration<AlertRuleEvaluationStateRow>
{
    public void Configure(EntityTypeBuilder<AlertRuleEvaluationStateRow> builder)
    {
        builder.ToTable("AlertRuleEvaluationStates");
        builder.HasKey(row => row.RuleId);
        builder.Property(row => row.LastEvidenceIdentity).HasMaxLength(256);
        builder.Property(row => row.LastDecision).HasMaxLength(64);
        builder.Property(row => row.LastSkipReason).HasMaxLength(500);
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasOne<AlertRuleRow>()
            .WithOne()
            .HasForeignKey<AlertRuleEvaluationStateRow>(row => row.RuleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.LastEvaluatedAtUtc).HasDatabaseName("IX_AlertRuleEvaluationStates_LastEvaluated");
    }
}

public sealed class AlertRuleTriggerRowConfiguration : IEntityTypeConfiguration<AlertRuleTriggerRow>
{
    public void Configure(EntityTypeBuilder<AlertRuleTriggerRow> builder)
    {
        builder.ToTable("AlertRuleTriggers");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.EvidenceIdentity).HasMaxLength(256).IsRequired();
        builder.Property(row => row.DeduplicationKey).HasMaxLength(512).IsRequired();
        builder.Property(row => row.Operator).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Unit).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SourceProvider).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourcePeriod).HasMaxLength(64);
        builder.Property(row => row.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.HasOne<AlertRuleRow>()
            .WithMany()
            .HasForeignKey(row => row.RuleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NotificationIntentRow>()
            .WithMany()
            .HasForeignKey(row => row.NotificationIntentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.DeduplicationKey).IsUnique()
            .HasDatabaseName("UIX_AlertRuleTriggers_DeduplicationKey");
        builder.HasIndex(row => new { row.RuleId, row.TriggeredAtUtc })
            .HasDatabaseName("IX_AlertRuleTriggers_Rule_History");
        builder.HasIndex(row => row.NotificationIntentId)
            .HasDatabaseName("IX_AlertRuleTriggers_NotificationIntentId");
    }
}
