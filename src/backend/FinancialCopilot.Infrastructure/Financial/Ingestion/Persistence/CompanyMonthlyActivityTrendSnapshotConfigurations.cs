using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyMonthlyActivityTrendSnapshotRowConfiguration :
    IEntityTypeConfiguration<CompanyMonthlyActivityTrendSnapshotRow>
{
    public void Configure(EntityTypeBuilder<CompanyMonthlyActivityTrendSnapshotRow> builder)
    {
        builder.ToTable("CompanyMonthlyActivityTrendSnapshots");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ExternalCompanyId).HasMaxLength(64);
        builder.Property(r => r.CompanySymbol).HasMaxLength(32);
        builder.Property(r => r.CompanyName).HasMaxLength(256);
        builder.Property(r => r.IndustryTitle).HasMaxLength(256);
        builder.Property(r => r.CategoryTitle).HasMaxLength(256);
        builder.Property(r => r.FiscalEndDate).HasMaxLength(32);
        builder.Property(r => r.FiscalMonthNameFa).HasMaxLength(32);
        builder.Property(r => r.ProductUnitSummary).HasMaxLength(512);
        builder.Property(r => r.SourceProviderName).HasMaxLength(64);
        builder.Property(r => r.SourceReportId).HasMaxLength(256);
        builder.Property(r => r.SourceRawPayloadId).HasMaxLength(256);

        // Unique: one snapshot row per company/month
        builder.HasIndex(r => new { r.ExternalCompanyId, r.ReportYear, r.ReportMonth })
            .IsUnique()
            .HasDatabaseName("UIX_CompanyMonthlyActivityTrendSnapshots_CompanyPeriod");

        builder.HasIndex(r => new { r.CompanySymbol, r.ReportYear, r.ReportMonth })
            .HasDatabaseName("IX_CompanyMonthlyActivityTrendSnapshots_SymbolPeriod");

        builder.HasIndex(r => new { r.ExternalCompanyId, r.FiscalYear, r.FiscalMonthIndex })
            .HasDatabaseName("IX_CompanyMonthlyActivityTrendSnapshots_FiscalPeriod");

        builder.HasIndex(r => new { r.SourceProviderName, r.CalculatedAtUtc })
            .HasDatabaseName("IX_CompanyMonthlyActivityTrendSnapshots_ProviderCalculated");
    }
}
