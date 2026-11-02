using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<CustomerAccountRow> CustomerAccounts => Set<CustomerAccountRow>();

    public DbSet<WalletProjectionRow> WalletProjections => Set<WalletProjectionRow>();

    public DbSet<UsageReservationRow> UsageReservations => Set<UsageReservationRow>();

    public DbSet<UsageLedgerEntryRow> UsageLedgerEntries => Set<UsageLedgerEntryRow>();
    public DbSet<SubscriptionCreditPeriodRow> SubscriptionCreditPeriods => Set<SubscriptionCreditPeriodRow>();
    public DbSet<FreeCreditGrantEvidenceRow> FreeCreditGrantEvidence => Set<FreeCreditGrantEvidenceRow>();
    public DbSet<UsageCreditAllocationRow> UsageCreditAllocations => Set<UsageCreditAllocationRow>();
    public DbSet<UsageCreditHoldRow> UsageCreditHolds => Set<UsageCreditHoldRow>();
    public DbSet<TopUpCreditSourceRow> TopUpCreditSources => Set<TopUpCreditSourceRow>();
    public DbSet<TahlilAppTopUpPurchaseRow> TahlilAppTopUpPurchases => Set<TahlilAppTopUpPurchaseRow>();
    public DbSet<TopUpUsageCreditAllocationRow> TopUpUsageCreditAllocations => Set<TopUpUsageCreditAllocationRow>();
    public DbSet<TopUpUsageCreditHoldRow> TopUpUsageCreditHolds => Set<TopUpUsageCreditHoldRow>();
    public DbSet<TopUpOfferRow> TopUpOffers => Set<TopUpOfferRow>();
    public DbSet<ExternalPlanSubscriptionRow> ExternalPlanSubscriptions => Set<ExternalPlanSubscriptionRow>();
    public DbSet<PaidExpiryCooldownRow> PaidExpiryCooldowns => Set<PaidExpiryCooldownRow>();
    public DbSet<DailyFreeAllowanceGrantRow> DailyFreeAllowanceGrants => Set<DailyFreeAllowanceGrantRow>();

    public DbSet<FinancialTransactionRow> FinancialTransactions => Set<FinancialTransactionRow>();

    public DbSet<SubscriptionPlanRow> SubscriptionPlans => Set<SubscriptionPlanRow>();

    public DbSet<PlanCapabilityRow> PlanCapabilities => Set<PlanCapabilityRow>();

    public DbSet<InvoiceAccountRow> InvoiceAccounts => Set<InvoiceAccountRow>();

    public DbSet<BillingOutboxMessageRow> OutboxMessages => Set<BillingOutboxMessageRow>();
    public DbSet<BillingAdminAuditRow> AdminAudits => Set<BillingAdminAuditRow>();
    public DbSet<BillingPurchaseProductRow> PurchaseProducts => Set<BillingPurchaseProductRow>();
    public DbSet<BillingCheckoutIntentRow> CheckoutIntents => Set<BillingCheckoutIntentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(BillingDbContext).Assembly,
            type => type.Namespace == typeof(BillingDbContext).Namespace);
}
