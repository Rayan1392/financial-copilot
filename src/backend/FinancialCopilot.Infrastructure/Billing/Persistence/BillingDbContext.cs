using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<CustomerAccountRow> CustomerAccounts => Set<CustomerAccountRow>();

    public DbSet<WalletProjectionRow> WalletProjections => Set<WalletProjectionRow>();

    public DbSet<UsageReservationRow> UsageReservations => Set<UsageReservationRow>();

    public DbSet<UsageLedgerEntryRow> UsageLedgerEntries => Set<UsageLedgerEntryRow>();

    public DbSet<FinancialTransactionRow> FinancialTransactions => Set<FinancialTransactionRow>();

    public DbSet<SubscriptionPlanRow> SubscriptionPlans => Set<SubscriptionPlanRow>();

    public DbSet<PlanCapabilityRow> PlanCapabilities => Set<PlanCapabilityRow>();

    public DbSet<InvoiceAccountRow> InvoiceAccounts => Set<InvoiceAccountRow>();

    public DbSet<BillingOutboxMessageRow> OutboxMessages => Set<BillingOutboxMessageRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(BillingDbContext).Assembly,
            type => type.Namespace == typeof(BillingDbContext).Namespace);
}
