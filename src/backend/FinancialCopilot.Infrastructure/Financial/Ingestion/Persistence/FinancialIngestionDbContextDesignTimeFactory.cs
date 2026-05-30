using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

/// <summary>
/// Design-time factory so EF Core migration tooling can construct the context without the API
/// composition root. The connection string is a design-time placeholder only (never opened at
/// runtime); the real connection is configured in the composition root.
/// </summary>
public sealed class FinancialIngestionDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<FinancialIngestionDbContext>
{
    public FinancialIngestionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseNpgsql("Host=localhost;Database=financialcopilot;Username=postgres;Password=postgres")
            .Options;
        return new FinancialIngestionDbContext(options);
    }
}
