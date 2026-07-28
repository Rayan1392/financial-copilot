using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class CompanyProductRevenueMixRowConfiguration :
    IEntityTypeConfiguration<CompanyProductRevenueMixRow>
{
    public void Configure(EntityTypeBuilder<CompanyProductRevenueMixRow> builder)
    {
        builder.ToTable("CompanyProductRevenueMix");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ExternalCompanyId).HasMaxLength(64);
        builder.Property(r => r.CompanySymbol).HasMaxLength(32);
        builder.Property(r => r.CompanyName).HasMaxLength(256);
        builder.Property(r => r.FiscalEndDate).HasMaxLength(32);
        builder.Property(r => r.ProductName).HasMaxLength(512);
        builder.Property(r => r.SourceProviderName).HasMaxLength(64);

        builder.HasIndex(r => new { r.ExternalCompanyId, r.ReportYear, r.ReportMonth })
            .HasDatabaseName("IX_CompanyProductRevenueMix_CompanyPeriod");
        builder.HasIndex(r => r.CompanySymbol);
        builder.HasIndex(r => r.ProductRank);
    }
}
