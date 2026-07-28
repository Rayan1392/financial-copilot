using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialIngestionMigrationDiscoveryTests
{
    [Fact]
    public void Notification_schema_migrations_are_discovered_before_dependent_foreign_keys()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseNpgsql("Host=localhost;Database=migration-discovery;Username=unused;Password=unused")
            .Options;
        using var db = new FinancialIngestionDbContext(options);

        var migrations = db.Database.GetMigrations().ToArray();
        var subscriptions = Array.IndexOf(migrations, "20260713140000_AddCodalAlertSubscriptions");
        var notificationIntents = Array.IndexOf(migrations, "20260713143000_AddNotificationIntentsAndCodalAlertSummaries");
        var dependentForeignKeys = Array.IndexOf(migrations, "20260714033255_EnforceConditionalTrackerReferences");

        Assert.True(subscriptions >= 0, "The Codal alert subscription migration must be discoverable by EF Core.");
        Assert.True(notificationIntents > subscriptions,
            "The notification-intent creation migration must be discoverable after subscriptions.");
        Assert.True(dependentForeignKeys > notificationIntents,
            "NotificationIntents must be created before dependent foreign keys are applied.");
    }
}
