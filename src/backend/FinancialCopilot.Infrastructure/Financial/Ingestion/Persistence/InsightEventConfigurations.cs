using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class InsightEventRowConfiguration : IEntityTypeConfiguration<InsightEventRow>
{
    public void Configure(EntityTypeBuilder<InsightEventRow> builder)
    {
        builder.ToTable("InsightEvents");
        builder.HasKey(row => row.Id);

        builder.Property(row => row.ExternalCompanyId).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Symbol).HasMaxLength(64).IsRequired();
        builder.Property(row => row.IndustryCode).HasMaxLength(64);
        builder.Property(row => row.InsightType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Severity).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Title).HasMaxLength(500).IsRequired();
        builder.Property(row => row.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
        builder.Property(row => row.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.SourceProviderName).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceEntityType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceEntityId).HasMaxLength(128);
        builder.Property(row => row.SourcePeriod).HasMaxLength(64);
        builder.Property(row => row.DeduplicationKey).HasMaxLength(512).IsRequired();
        builder.Property(row => row.SuggestedActionsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(row => row.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("UIX_InsightEvents_DeduplicationKey");

        builder.HasIndex(row => row.DetectedAtUtc)
            .HasDatabaseName("IX_InsightEvents_DetectedAtUtc");

        builder.HasIndex(row => row.ExternalCompanyId)
            .HasDatabaseName("IX_InsightEvents_ExternalCompanyId");

        builder.HasIndex(row => row.Symbol)
            .HasDatabaseName("IX_InsightEvents_Symbol");

        builder.HasIndex(row => row.InsightType)
            .HasDatabaseName("IX_InsightEvents_InsightType");

        builder.HasIndex(row => row.Severity)
            .HasDatabaseName("IX_InsightEvents_Severity");

        builder.HasIndex(row => row.IndustryCode)
            .HasDatabaseName("IX_InsightEvents_IndustryCode");
    }
}
