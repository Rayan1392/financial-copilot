using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class MonthlySalesQualityRankingSnapshotRowConfiguration :
    IEntityTypeConfiguration<MonthlySalesQualityRankingSnapshotRow>
{
    public void Configure(EntityTypeBuilder<MonthlySalesQualityRankingSnapshotRow> builder)
    {
        builder.ToTable("MonthlySalesQualityRankingSnapshots");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ExternalCompanyId).HasMaxLength(64);
        builder.Property(r => r.CompanySymbol).HasMaxLength(64);
        builder.Property(r => r.CompanyName).HasMaxLength(256);
        builder.Property(r => r.IndustryTitle).HasMaxLength(256);
        builder.Property(r => r.IndustryGroupTitle).HasMaxLength(256);
        builder.Property(r => r.QualityLabel).HasMaxLength(64);
        builder.Property(r => r.SourceProviderName).HasMaxLength(64);
        builder.Property(r => r.DimensionScoresJson).HasColumnType("jsonb");
        builder.Property(r => r.PositiveDriversJson).HasColumnType("jsonb");
        builder.Property(r => r.NegativeDriversJson).HasColumnType("jsonb");
        builder.Property(r => r.DataCoverageJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.ExternalCompanyId, r.ReportYear, r.ReportMonth })
            .IsUnique()
            .HasDatabaseName("UIX_MonthlySalesQualityRankingSnapshots_CompanyPeriod");

        builder.HasIndex(r => new { r.ReportYear, r.ReportMonth, r.RankMarket })
            .HasDatabaseName("IX_MonthlySalesQualityRankingSnapshots_PeriodMarketRank");

        builder.HasIndex(r => new { r.ReportYear, r.ReportMonth, r.IndustryId, r.RankIndustry })
            .HasDatabaseName("IX_MonthlySalesQualityRankingSnapshots_PeriodIndustryRank");

        builder.HasIndex(r => new { r.CompanySymbol, r.ReportYear, r.ReportMonth })
            .HasDatabaseName("IX_MonthlySalesQualityRankingSnapshots_SymbolPeriod");
    }
}
