using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class MarketReportRowConfiguration : IEntityTypeConfiguration<MarketReportRow>
{
    public void Configure(EntityTypeBuilder<MarketReportRow> builder)
    {
        builder.ToTable("MarketReports");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Scope).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ActorType).HasMaxLength(32);
        builder.Property(row => row.WindowKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ReportVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.EvidenceSchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.PromptPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RenderingPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SafetyPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EvidenceHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.GenerationIdempotencyKey).HasMaxLength(300).IsRequired();
        builder.Property(row => row.ReservationIdempotencyKey).HasMaxLength(300);
        builder.Property(row => row.LeaseOwner).HasMaxLength(160);
        builder.Property(row => row.Confidence).HasPrecision(5, 4);
        builder.HasIndex(row => row.GenerationIdempotencyKey).IsUnique();
        builder.HasIndex(row => new
        {
            row.Scope,
            row.TenantId,
            row.ActorId,
            row.ActorType,
            row.TradingDate,
            row.WindowKey,
            row.Revision
        }).IsUnique();
        builder.HasIndex(row => new { row.Scope, row.IsCurrent, row.PublishedAtUtc });
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ActorType, row.Scope, row.IsCurrent, row.PublishedAtUtc });
        builder.HasIndex(row => new { row.Status, row.NextAttemptAtUtc, row.LeaseExpiresAtUtc });
        builder.HasOne<MarketReportRow>()
            .WithMany()
            .HasForeignKey(row => row.SupersedesReportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
