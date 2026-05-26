using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;

public sealed class SemanticCatalogDbContext(DbContextOptions<SemanticCatalogDbContext> options) : DbContext(options)
{
    public DbSet<FinancialMetricDefinitionRow> MetricDefinitions => Set<FinancialMetricDefinitionRow>();

    public DbSet<MetricAliasRow> MetricAliases => Set<MetricAliasRow>();

    public DbSet<MetricCalculationPolicyRow> MetricCalculationPolicies => Set<MetricCalculationPolicyRow>();

    public DbSet<MetricDependencyRow> MetricDependencies => Set<MetricDependencyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(SemanticCatalogDbContext).Assembly,
            type => type.Namespace == typeof(SemanticCatalogDbContext).Namespace);
}
