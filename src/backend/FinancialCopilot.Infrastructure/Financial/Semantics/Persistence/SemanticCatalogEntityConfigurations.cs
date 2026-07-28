using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;

public sealed class FinancialMetricDefinitionRowConfiguration :
    IEntityTypeConfiguration<FinancialMetricDefinitionRow>
{
    public void Configure(EntityTypeBuilder<FinancialMetricDefinitionRow> builder)
    {
        builder.ToTable("FinancialMetricDefinitions");
        builder.HasKey(row => new { row.MetricCode, row.MetricVersion });
        builder.Property(row => row.MetricCode).HasMaxLength(128);
        builder.Property(row => row.MetricVersion).HasMaxLength(64);
        builder.Property(row => row.DisplayName).HasMaxLength(200);
        builder.Property(row => row.PersianTitle).HasMaxLength(200);
        builder.Property(row => row.Category).HasMaxLength(64);
        builder.Property(row => row.UnitCode).HasMaxLength(32);
        builder.Property(row => row.LookupEligible).HasDefaultValue(false);
        builder.Property(row => row.ScannerEligible).HasDefaultValue(false);
        builder.Property(row => row.IsMonthlyActivityMetric).HasDefaultValue(false);
        builder.Property(row => row.IsValuationMetric).HasDefaultValue(false);
        builder.Property(row => row.IsGrowthMetric).HasDefaultValue(false);
        builder.Property(row => row.IsMarginMetric).HasDefaultValue(false);
        builder.Property(row => row.IsFundamentalMetric).HasDefaultValue(false);
        builder.Property(row => row.SuppressQuoteContext).HasDefaultValue(false);
    }
}

public sealed class MetricPeriodAliasRowConfiguration : IEntityTypeConfiguration<MetricPeriodAliasRow>
{
    public void Configure(EntityTypeBuilder<MetricPeriodAliasRow> builder)
    {
        builder.ToTable("MetricPeriodAliases");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.Language, row.Status });
        builder.HasIndex(row => new { row.NormalizedAliasText, row.Language })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
        builder.Property(row => row.AliasText).HasMaxLength(200).IsRequired();
        builder.Property(row => row.NormalizedAliasText).HasMaxLength(200).IsRequired();
        builder.Property(row => row.Language).HasMaxLength(16).IsRequired();
        builder.Property(row => row.PeriodType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.PeriodSelector).HasMaxLength(16).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(16).IsRequired();
    }
}

public sealed class MetricAliasRowConfiguration : IEntityTypeConfiguration<MetricAliasRow>
{
    public void Configure(EntityTypeBuilder<MetricAliasRow> builder)
    {
        builder.ToTable("MetricAliases");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.Language, row.Expression });
        builder.Property(row => row.Expression).HasMaxLength(300);
        builder.Property(row => row.Language).HasMaxLength(16);
        builder.Property(row => row.MetricCode).HasMaxLength(128);
        builder.Property(row => row.MetricVersion).HasMaxLength(64);
    }
}

public sealed class MetricCalculationPolicyRowConfiguration :
    IEntityTypeConfiguration<MetricCalculationPolicyRow>
{
    public void Configure(EntityTypeBuilder<MetricCalculationPolicyRow> builder)
    {
        builder.ToTable("MetricCalculationPolicies");
        builder.HasKey(row => new { row.MetricCode, row.PolicyVersion });
        builder.Property(row => row.MetricCode).HasMaxLength(128);
        builder.Property(row => row.PolicyVersion).HasMaxLength(64);
        builder.Property(row => row.DefinitionVersion).HasMaxLength(64);
    }
}

public sealed class MetricDependencyRowConfiguration : IEntityTypeConfiguration<MetricDependencyRow>
{
    public void Configure(EntityTypeBuilder<MetricDependencyRow> builder)
    {
        builder.ToTable("MetricDependencies");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.MetricCode, row.MetricVersion });
        builder.Property(row => row.MetricCode).HasMaxLength(128);
        builder.Property(row => row.MetricVersion).HasMaxLength(64);
        builder.Property(row => row.DependencyMetricCode).HasMaxLength(128);
    }
}

public sealed class DynamicMetricAliasRowConfiguration : IEntityTypeConfiguration<DynamicMetricAliasRow>
{
    public void Configure(EntityTypeBuilder<DynamicMetricAliasRow> builder)
    {
        builder.ToTable("DynamicMetricAliases");
        builder.HasKey(row => row.Id);
        // Fast per-language active alias lookup (cache fill query)
        builder.HasIndex(row => new { row.Language, row.Status });
        // Uniqueness: one active expression per metric per language
        builder.HasIndex(row => new { row.NormalizedExpression, row.Language, row.MetricCode })
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
        builder.Property(row => row.Expression).HasMaxLength(300).IsRequired();
        builder.Property(row => row.NormalizedExpression).HasMaxLength(300).IsRequired();
        builder.Property(row => row.Language).HasMaxLength(16).IsRequired();
        builder.Property(row => row.MetricCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.MetricVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Source).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedBy).HasMaxLength(256);
        builder.Property(row => row.ApprovedBy).HasMaxLength(256);
        builder.Property(row => row.DisabledBy).HasMaxLength(256);
        builder.Property(row => row.DisableReason).HasMaxLength(500);
        builder.Property(row => row.ConfidenceScore).HasPrecision(5, 4);
    }
}

public sealed class MetricAliasCandidateRowConfiguration : IEntityTypeConfiguration<MetricAliasCandidateRow>
{
    public void Configure(EntityTypeBuilder<MetricAliasCandidateRow> builder)
    {
        builder.ToTable("MetricAliasCandidates");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.Status);
        builder.HasIndex(row => new { row.NormalizedExpression, row.Language, row.SuggestedMetricCode })
            .IsUnique();
        builder.HasIndex(row => row.LastSeenAt);
        builder.Property(row => row.Expression).HasMaxLength(300).IsRequired();
        builder.Property(row => row.NormalizedExpression).HasMaxLength(300).IsRequired();
        builder.Property(row => row.Language).HasMaxLength(16).IsRequired();
        builder.Property(row => row.SuggestedMetricCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.SuggestedMetricVersion).HasMaxLength(64);
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.RejectionReason).HasMaxLength(500);
        builder.Property(row => row.EvidenceExamplesJson).HasMaxLength(2000);
        builder.Property(row => row.ConfidenceScore).HasPrecision(5, 4);
    }
}
