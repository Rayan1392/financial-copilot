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
        builder.Property(row => row.Category).HasMaxLength(64);
        builder.Property(row => row.UnitCode).HasMaxLength(32);
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
