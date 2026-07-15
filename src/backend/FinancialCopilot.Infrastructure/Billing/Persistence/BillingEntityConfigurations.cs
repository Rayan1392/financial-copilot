using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Billing.Persistence;

public sealed class CustomerAccountRowConfiguration : IEntityTypeConfiguration<CustomerAccountRow>
{
    public void Configure(EntityTypeBuilder<CustomerAccountRow> builder)
    {
        builder.ToTable("billing_customer_accounts");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.TenantId, row.UserId }).IsUnique();
        builder.Property(row => row.AccountType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.BillingMode).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SubscriptionPlanCode).HasMaxLength(64);
        builder.Property(row => row.CreditLineApprovedLimit).HasPrecision(18, 4);
        builder.Property(row => row.CreditLineWarningThreshold).HasPrecision(18, 4);
        builder.Property(row => row.SubscriptionRevision).IsConcurrencyToken();
    }
}

public sealed class WalletProjectionRowConfiguration : IEntityTypeConfiguration<WalletProjectionRow>
{
    public void Configure(EntityTypeBuilder<WalletProjectionRow> builder)
    {
        builder.ToTable("billing_wallet_projections");
        builder.HasKey(row => row.CustomerAccountId);
        builder.Property(row => row.Balance).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.ReservedAmount).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
    }
}

public sealed class UsageReservationRowConfiguration : IEntityTypeConfiguration<UsageReservationRow>
{
    public void Configure(EntityTypeBuilder<UsageReservationRow> builder)
    {
        builder.ToTable("billing_usage_reservations");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(row => row.OperationCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.FinalizationReason).HasMaxLength(500);
        builder.Property(row => row.ReservedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.CommittedCredits).HasPrecision(18, 4);
    }
}

public sealed class UsageLedgerEntryRowConfiguration : IEntityTypeConfiguration<UsageLedgerEntryRow>
{
    public void Configure(EntityTypeBuilder<UsageLedgerEntryRow> builder)
    {
        builder.ToTable("billing_usage_ledger_entries");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => new { row.CustomerAccountId, row.OccurredAt });
        builder.HasIndex(row => row.RelatedEntryId);
        builder.Property(row => row.EntryType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.OperationCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.CreditsCharged).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.PricingPolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(row => row.ExternalUserId).HasMaxLength(160);
        builder.Property(row => row.AuditDescription).HasMaxLength(500);
        builder.Property(row => row.CompletionStatus).HasMaxLength(64);
        builder.Property(row => row.ProviderName).HasMaxLength(80);
        builder.Property(row => row.ModelName).HasMaxLength(160);
        builder.Property(row => row.EstimatedCost).HasPrecision(18, 8);
        builder.Property(row => row.AllocationSource).HasMaxLength(80);
        builder.Property(row => row.AllowanceDateKey).HasMaxLength(32);
    }
}

public sealed class DailyFreeAllowanceGrantRowConfiguration : IEntityTypeConfiguration<DailyFreeAllowanceGrantRow>
{
    public void Configure(EntityTypeBuilder<DailyFreeAllowanceGrantRow> builder)
    {
        builder.ToTable("billing_daily_free_allowance_grants");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.CustomerAccountId, row.ActorId, row.AllowanceDateKey, row.PolicyVersion })
            .IsUnique();
        builder.HasIndex(row => new { row.ExpiresAtUtc, row.ExpiredAtUtc });
        builder.Property(row => row.AllowanceDateKey).HasMaxLength(32).IsRequired();
        builder.Property(row => row.PolicyVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.ExpiredCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
        builder.HasOne<CustomerAccountRow>()
            .WithMany()
            .HasForeignKey(row => row.CustomerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<UsageLedgerEntryRow>()
            .WithMany()
            .HasForeignKey(row => row.LedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FinancialTransactionRowConfiguration : IEntityTypeConfiguration<FinancialTransactionRow>
{
    public void Configure(EntityTypeBuilder<FinancialTransactionRow> builder)
    {
        builder.ToTable("billing_financial_transactions");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.Property(row => row.Type).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Currency).HasMaxLength(8).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
    }
}

public sealed class SubscriptionPlanRowConfiguration : IEntityTypeConfiguration<SubscriptionPlanRow>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlanRow> builder)
    {
        builder.ToTable("billing_subscription_plans");
        builder.HasKey(row => row.Code);
        builder.Property(row => row.Code).HasMaxLength(64);
        builder.Property(row => row.Name).HasMaxLength(160).IsRequired();
        builder.Property(row => row.IncludedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.PricingPolicyVersion).HasMaxLength(64).IsRequired();
        builder.HasMany<CustomerAccountRow>()
            .WithOne()
            .HasForeignKey(row => row.SubscriptionPlanCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasData(
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 10000m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Pro", Name = "Pro", IncludedCredits = 100m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Plus", Name = "Plus", IncludedCredits = 300m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Premium", Name = "Premium", IncludedCredits = 1000m, PricingPolicyVersion = "v1" });
    }
}

public sealed class BillingAdminAuditRowConfiguration : IEntityTypeConfiguration<BillingAdminAuditRow>
{
    public void Configure(EntityTypeBuilder<BillingAdminAuditRow> builder)
    {
        builder.ToTable("billing_admin_audits");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.TenantId, row.OccurredAt });
        builder.Property(row => row.ActionCode).HasMaxLength(160).IsRequired();
        builder.Property(row => row.TargetType).HasMaxLength(80).IsRequired();
        builder.Property(row => row.TargetId).HasMaxLength(160).IsRequired();
        builder.Property(row => row.Reason).HasMaxLength(500).IsRequired();
        builder.Property(row => row.CorrelationId).HasMaxLength(160).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160);
        builder.Property(row => row.Before).HasMaxLength(2000);
        builder.Property(row => row.After).HasMaxLength(2000);
    }
}

public sealed class PlanCapabilityRowConfiguration : IEntityTypeConfiguration<PlanCapabilityRow>
{
    public void Configure(EntityTypeBuilder<PlanCapabilityRow> builder)
    {
        builder.ToTable("billing_plan_capabilities");
        builder.HasKey(row => new { row.PlanCode, row.CapabilityCode, row.PolicyVersion });
        builder.Property(row => row.PlanCode).HasMaxLength(64);
        builder.Property(row => row.CapabilityCode).HasMaxLength(160);
        builder.Property(row => row.PolicyVersion).HasMaxLength(64);
        builder.Property(row => row.Limit).HasPrecision(18, 4);
        builder.HasOne<SubscriptionPlanRow>()
            .WithMany()
            .HasForeignKey(row => row.PlanCode)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasData(BaselinePlanCapabilities.All);
    }
}

internal static class BaselinePlanCapabilities
{
    public static readonly PlanCapabilityRow[] All =
    [
        Enabled("Free", "AiQuery.Scanner", 10),
        Enabled("Free", "AiQuery.StockAnalysis", 5),
        Enabled("Free", "AiQuery.FinancialComparison", 5),
        Enabled("Free", "Reports.Read", 100),
        Enabled("Free", "Watchlist.Symbols", 5),
        Enabled("Free", "Tracker.Rules", 3),
        Enabled("Free", "Radar.Symbols", 5),
        Enabled("Free", "MarketPulse.Read"),
        Enabled("Free", "Notifications.Telegram"),
        Enabled("Pro", "AiQuery.Scanner"),
        Enabled("Pro", "AiQuery.StockAnalysis"),
        Enabled("Pro", "AiQuery.FinancialComparison"),
        Enabled("Pro", "AiQuery.CodalAnalysis", 30),
        Enabled("Pro", "AiQuery.PortfolioAnalysis", 10),
        Enabled("Pro", "Reports.Read"),
        Enabled("Pro", "Watchlist.Symbols", 20),
        Enabled("Pro", "Portfolio.Records", 10),
        Enabled("Pro", "Tracker.Rules", 20),
        Enabled("Pro", "Radar.Symbols", 20),
        Enabled("Pro", "MarketPulse.Read"),
        Enabled("Pro", "AiQuery.PersonalDigest", 30),
        Enabled("Pro", "Notifications.Telegram"),
        Enabled("Plus", "AiQuery.Scanner"),
        Enabled("Plus", "AiQuery.StockAnalysis"),
        Enabled("Plus", "AiQuery.FinancialComparison"),
        Enabled("Plus", "AiQuery.CodalAnalysis"),
        Enabled("Plus", "AiQuery.DeepResearch", 10),
        Enabled("Plus", "AiQuery.PortfolioAnalysis", 50),
        Enabled("Plus", "Reports.Read"),
        Enabled("Plus", "Watchlist.Symbols", 50),
        Enabled("Plus", "Portfolio.Records", 50),
        Enabled("Plus", "Tracker.Rules", 50),
        Enabled("Plus", "Radar.Symbols", 50),
        Enabled("Plus", "MarketPulse.Read"),
        Enabled("Plus", "AiQuery.PersonalDigest"),
        Enabled("Plus", "Notifications.Telegram"),
        Enabled("Premium", "AiQuery.Scanner"),
        Enabled("Premium", "AiQuery.StockAnalysis"),
        Enabled("Premium", "AiQuery.FinancialComparison"),
        Enabled("Premium", "AiQuery.CodalAnalysis"),
        Enabled("Premium", "AiQuery.DeepResearch"),
        Enabled("Premium", "AiQuery.PortfolioAnalysis"),
        Enabled("Premium", "Reports.Read"),
        Enabled("Premium", "Watchlist.Symbols", 100),
        Enabled("Premium", "Portfolio.Records", 100),
        Enabled("Premium", "Tracker.Rules", 100),
        Enabled("Premium", "Radar.Symbols", 100),
        Enabled("Premium", "MarketPulse.Read"),
        Enabled("Premium", "AiQuery.PersonalDigest"),
        Enabled("Premium", "Notifications.Telegram")
    ];

    private static PlanCapabilityRow Enabled(string planCode, string capabilityCode, decimal? limit = null) =>
        new()
        {
            PlanCode = planCode,
            CapabilityCode = capabilityCode,
            PolicyVersion = "v1",
            IsEnabled = true,
            Limit = limit
        };
}

public sealed class InvoiceAccountRowConfiguration : IEntityTypeConfiguration<InvoiceAccountRow>
{
    public void Configure(EntityTypeBuilder<InvoiceAccountRow> builder)
    {
        builder.ToTable("billing_invoice_accounts");
        builder.HasKey(row => row.CustomerAccountId);
        builder.Property(row => row.LegalName).HasMaxLength(250).IsRequired();
        builder.Property(row => row.BillingEmail).HasMaxLength(250).IsRequired();
        builder.Property(row => row.SettlementTerms).HasMaxLength(250).IsRequired();
    }
}

public sealed class BillingOutboxMessageRowConfiguration : IEntityTypeConfiguration<BillingOutboxMessageRow>
{
    public void Configure(EntityTypeBuilder<BillingOutboxMessageRow> builder)
    {
        builder.ToTable("billing_outbox_messages");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => new { row.ProcessedAt, row.OccurredAt });
        builder.Property(row => row.AggregateType).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EventType).HasMaxLength(128).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(220).IsRequired();
        builder.Property(row => row.Payload).IsRequired();
        builder.Property(row => row.AttemptCount).IsRequired();
        builder.Property(row => row.LastError).HasMaxLength(1000);
    }
}
