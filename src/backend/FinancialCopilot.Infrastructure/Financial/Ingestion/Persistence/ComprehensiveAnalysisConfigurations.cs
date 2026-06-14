using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class ComprehensiveAnalysisRowConfiguration : IEntityTypeConfiguration<ComprehensiveAnalysisRow>
{
    public void Configure(EntityTypeBuilder<ComprehensiveAnalysisRow> builder)
    {
        builder.ToTable("ComprehensiveAnalyses");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Title).HasMaxLength(500);
        builder.Property(r => r.Summary).HasColumnType("text");
        builder.Property(r => r.PersianCreatedAt).HasMaxLength(30);
        builder.Property(r => r.AuthorName).HasMaxLength(200);
        builder.Property(r => r.PlainTextSummary).HasMaxLength(10_000).IsRequired(false);

        builder.HasIndex(r => r.CreatedAt);
    }
}

public sealed class ComprehensiveAnalysisTagRowConfiguration : IEntityTypeConfiguration<ComprehensiveAnalysisTagRow>
{
    public void Configure(EntityTypeBuilder<ComprehensiveAnalysisTagRow> builder)
    {
        builder.ToTable("ComprehensiveAnalysisTags");
        builder.HasKey(r => new { r.AnalysisId, r.TagId });

        builder.Property(r => r.TagName).HasMaxLength(200);
        builder.Property(r => r.TagSlug).HasMaxLength(200);

        builder.HasOne<ComprehensiveAnalysisRow>()
            .WithMany()
            .HasForeignKey(r => r.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        // Primary AI retrieval path: symbol lookup (TagTypeId=1) + sort by analysis
        builder.HasIndex(r => new { r.TagName, r.TagTypeId, r.AnalysisId });
        // Topic/keyword lookup (IsAnalytic=true)
        builder.HasIndex(r => new { r.TagName, r.IsAnalytic, r.AnalysisId });
        // Bulk fetch by type
        builder.HasIndex(r => new { r.TagTypeId, r.AnalysisId });
    }
}

public sealed class ComprehensiveAnalysisCategoryRowConfiguration : IEntityTypeConfiguration<ComprehensiveAnalysisCategoryRow>
{
    public void Configure(EntityTypeBuilder<ComprehensiveAnalysisCategoryRow> builder)
    {
        builder.ToTable("ComprehensiveAnalysisCategories");
        builder.HasKey(r => new { r.AnalysisId, r.CategoryId });

        builder.Property(r => r.CategoryName).HasMaxLength(200);

        builder.HasOne<ComprehensiveAnalysisRow>()
            .WithMany()
            .HasForeignKey(r => r.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ComprehensiveAnalysisSyncLogRowConfiguration : IEntityTypeConfiguration<ComprehensiveAnalysisSyncLogRow>
{
    public void Configure(EntityTypeBuilder<ComprehensiveAnalysisSyncLogRow> builder)
    {
        builder.ToTable("ComprehensiveAnalysisSyncLogs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).UseIdentityByDefaultColumn();

        builder.Property(r => r.JobName).HasMaxLength(100);
        builder.Property(r => r.Status).HasMaxLength(20);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(r => r.StartedAt);
    }
}
