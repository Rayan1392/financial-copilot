using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FINANCIAL_COPILOT_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "FINANCIAL_COPILOT_CONNECTION_STRING is required for AuthDbContext design-time operations.");
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AuthDbContext(options);
    }
}
