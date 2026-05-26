using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class FinancialProviderDbContext(DbContextOptions<FinancialProviderDbContext> options) : DbContext(options)
{
    public DbSet<ProviderRawPayloadRow> ProviderRawPayloads => Set<ProviderRawPayloadRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FinancialProviderDbContext).Assembly,
            type => type.Namespace == typeof(FinancialProviderDbContext).Namespace);
}
