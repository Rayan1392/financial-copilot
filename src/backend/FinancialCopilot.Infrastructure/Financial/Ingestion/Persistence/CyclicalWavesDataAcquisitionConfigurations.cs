using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CyclicalWavesMetricSnapshotRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesMetricSnapshotRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesMetricSnapshotRow> builder)
    {
        builder.ToTable(
            "CyclicalWavesMetricSnapshots",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CyclicalWavesMetricSnapshots_ProviderName",
                    "\"ProviderName\" = 'CyclicalWaves'");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesMetricSnapshots_MetricType",
                    "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesMetricSnapshots_ResponseHash",
                    "length(\"ResponseHash\") = 64 AND lower(\"ResponseHash\") = \"ResponseHash\"");
            });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.SymbolIsin).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ProviderName).HasMaxLength(64).IsRequired();
        builder.Property(row => row.MetricType).HasMaxLength(16).IsRequired();
        builder.Property(row => row.RawResponseJson).HasColumnType("text").IsRequired();
        builder.Property(row => row.ResponseHash).HasColumnType("char(64)").IsRequired();
        builder.Property(row => row.SourceEndpoint).HasMaxLength(512).IsRequired();

        builder.HasOne<NormalizedCompanyRow>()
            .WithMany()
            .HasForeignKey(row => row.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CyclicalWavesMetricSnapshotRow>()
            .WithMany()
            .HasForeignKey(row => row.PreviousSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(row => new
            {
                row.CompanyId,
                row.ProviderName,
                row.MetricType,
                row.AcquisitionDateUtc,
                row.CreatedAtUtc
            })
            .IsDescending(false, false, false, true, true)
            .HasDatabaseName("IX_CyclicalWavesMetricSnapshots_Latest");

        builder.HasIndex(row => new
            {
                row.CompanyId,
                row.ProviderName,
                row.MetricType,
                row.ResponseHash
            })
            .HasDatabaseName("IX_CyclicalWavesMetricSnapshots_Hash");

        builder.HasIndex(row => new
            {
                row.CompanyId,
                row.ProviderName,
                row.MetricType,
                row.PreviousSnapshotId
            })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("UX_CyclicalWavesMetricSnapshots_Predecessor");
    }
}

public sealed class CyclicalWavesAcquisitionCheckRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesAcquisitionCheckRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesAcquisitionCheckRow> builder)
    {
        builder.ToTable(
            "CyclicalWavesAcquisitionChecks",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CyclicalWavesAcquisitionChecks_ProviderName",
                    "\"ProviderName\" = 'CyclicalWaves'");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesAcquisitionChecks_MetricType",
                    "\"MetricType\" IN ('PS', 'PE', 'Equilibrium')");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesAcquisitionChecks_Result",
                    "\"Result\" IN ('Changed', 'NoChange', 'Failed')");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesAcquisitionChecks_Consistency",
                    "((\"Result\" IN ('Changed', 'NoChange') AND \"ResponseHash\" IS NOT NULL " +
                    "AND \"SnapshotId\" IS NOT NULL AND \"FailureCode\" IS NULL) OR " +
                    "(\"Result\" = 'Failed' AND \"ResponseHash\" IS NULL " +
                    "AND \"SnapshotId\" IS NULL AND \"FailureCode\" IS NOT NULL))");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesAcquisitionChecks_AttemptCount",
                    "\"AttemptCount\" >= 0");
            });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.CycleDateUtc).HasColumnType("date");
        builder.Property(row => row.SymbolIsin).HasMaxLength(32);
        builder.Property(row => row.ProviderName).HasMaxLength(64).IsRequired();
        builder.Property(row => row.MetricType).HasMaxLength(16).IsRequired();
        builder.Property(row => row.ResponseHash).HasColumnType("char(64)");
        builder.Property(row => row.Result).HasMaxLength(16).IsRequired();
        builder.Property(row => row.SourceEndpoint).HasMaxLength(512).IsRequired();
        builder.Property(row => row.FailureCode).HasMaxLength(64);
        builder.Property(row => row.FailureMessage).HasMaxLength(1_000);

        builder.HasOne<NormalizedCompanyRow>()
            .WithMany()
            .HasForeignKey(row => row.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CyclicalWavesMetricSnapshotRow>()
            .WithMany()
            .HasForeignKey(row => row.SnapshotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(row => new
            {
                row.CycleDateUtc,
                row.CompanyId,
                row.MetricType,
                row.Result
            })
            .HasDatabaseName("IX_CyclicalWavesAcquisitionChecks_Restart");

        builder.HasIndex(row => new
            {
                row.CompanyId,
                row.MetricType,
                row.CheckedAtUtc
            })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_CyclicalWavesAcquisitionChecks_Diagnostics");
    }
}
