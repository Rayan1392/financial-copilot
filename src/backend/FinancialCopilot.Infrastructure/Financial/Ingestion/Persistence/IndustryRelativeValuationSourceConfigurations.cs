using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class IndustryRelativeValuationSourceFactRowConfiguration
    : IEntityTypeConfiguration<IndustryRelativeValuationSourceFactRow>
{
    public void Configure(EntityTypeBuilder<IndustryRelativeValuationSourceFactRow> builder)
    {
        builder.ToTable("IndustryRelativeValuationSourceFacts");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ProviderName).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SourceKind).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SourceObservationId).HasMaxLength(512).IsRequired();
        builder.Property(row => row.SourceEndpoint).HasMaxLength(256).IsRequired();
        builder.Property(row => row.SourceWatermark).HasMaxLength(1024).IsRequired();
        builder.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Readiness).HasMaxLength(64).IsRequired();
        builder.Property(row => row.QualityCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.IdentityEvidence).HasMaxLength(512).IsRequired();
        builder.Property(row => row.CurrentValue).HasPrecision(28, 14);
        builder.Property(row => row.ReferenceValue).HasPrecision(28, 14);
        builder.Property(row => row.RawPayload).HasColumnType("text");
        builder.HasIndex(row => new { row.ProviderName, row.SourceKind, row.SourceObservationId }).IsUnique();
        builder.HasIndex(row => new { row.CompanyId, row.SourceKind, row.FetchedAtUtc });
        builder.HasOne<NormalizedCompanyRow>().WithMany().HasForeignKey(row => row.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IndustryRelativeValuationSourceLeaseRowConfiguration
    : IEntityTypeConfiguration<IndustryRelativeValuationSourceLeaseRow>
{
    public void Configure(EntityTypeBuilder<IndustryRelativeValuationSourceLeaseRow> builder)
    {
        builder.ToTable("IndustryRelativeValuationSourceLeases");
        builder.HasKey(row => row.LeaseName);
        builder.Property(row => row.LeaseName).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Owner).HasMaxLength(128).IsRequired();
        builder.Property(row => row.CurrentRunId).HasMaxLength(128);
        builder.Property(row => row.SupersededRunId).HasMaxLength(128);
    }
}
