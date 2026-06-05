using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.API;

internal static class DatabaseMigrationExtensions
{
    public static async Task ApplyPendingDatabaseMigrationsAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        await MigrateAsync<AuthDbContext>(services);
        await MigrateAsync<BillingDbContext>(services);
        await MigrateAsync<SemanticCatalogDbContext>(services);
        await MigrateAsync<FinancialProviderDbContext>(services);
        await MigrateAsync<FinancialIngestionDbContext>(services);
        await MigrateAsync<ConversationDbContext>(services);
        await MigrateAsync<MemoryDbContext>(services);
    }

    private static async Task MigrateAsync<TDbContext>(IServiceProvider services)
        where TDbContext : DbContext
    {
        var dbContext = services.GetService<TDbContext>();
        if (dbContext is null || !dbContext.Database.IsRelational())
        {
            return;
        }

        var pending = await dbContext.Database.GetPendingMigrationsAsync();
        if (!pending.Any())
        {
            return;
        }

        var logger = services.GetRequiredService<ILogger<TDbContext>>();
        logger.LogInformation(
            "Applying {Count} pending migrations for {DbContext}: {Migrations}.",
            pending.Count(),
            typeof(TDbContext).Name,
            string.Join(", ", pending));

        await dbContext.Database.MigrateAsync();
    }
}
