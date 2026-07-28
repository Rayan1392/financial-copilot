using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Memory.Persistence;

public sealed class MemoryDbContext(DbContextOptions<MemoryDbContext> options) : DbContext(options)
{
    public DbSet<MemoryConsentPolicyRow> ConsentPolicies => Set<MemoryConsentPolicyRow>();

    public DbSet<MemoryRecordRow> MemoryRecords => Set<MemoryRecordRow>();

    public DbSet<MemoryAuditEventRow> AuditEvents => Set<MemoryAuditEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(MemoryDbContext).Assembly,
            type => type.Namespace == typeof(MemoryDbContext).Namespace);
}
