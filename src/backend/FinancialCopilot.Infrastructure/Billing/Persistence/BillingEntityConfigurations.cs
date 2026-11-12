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
        builder.Property(row => row.ExternalSubscriptionManaged).IsRequired();
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
        builder.ToTable("billing_usage_reservations", table => table.HasCheckConstraint(
            "CK_billing_usage_reservations_quota_mode",
            "(\"QuotaMode\" = 'Finite' AND \"ReservedCredits\" > 0 AND \"SourceSubscriptionId\" IS NULL) OR (\"QuotaMode\" = 'Unlimited' AND \"ReservedCredits\" = 0 AND \"SourceSubscriptionId\" IS NOT NULL AND \"SourceSubscriptionExpiresAtUtc\" IS NOT NULL)"));
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(row => row.RequestFingerprint).HasMaxLength(64);
        builder.Property(row => row.OperationCode).HasMaxLength(128).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(row => row.FinalizationReason).HasMaxLength(500);
        builder.Property(row => row.ReservedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.CommittedCredits).HasPrecision(18, 4);
        builder.Property(row => row.QuotaMode).HasMaxLength(16).HasDefaultValue("Finite").IsRequired();
        builder.HasOne<ExternalPlanSubscriptionRow>().WithMany().HasForeignKey(row => row.SourceSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsageLedgerEntryRowConfiguration : IEntityTypeConfiguration<UsageLedgerEntryRow>
{
    public void Configure(EntityTypeBuilder<UsageLedgerEntryRow> builder)
    {
        builder.ToTable("billing_usage_ledger_entries", table => table.HasCheckConstraint(
            "CK_billing_usage_ledger_entries_quota_mode",
            "\"QuotaMode\" = 'Finite' OR (\"QuotaMode\" = 'Unlimited' AND \"CreditsCharged\" = 0 AND \"SourceSubscriptionId\" IS NOT NULL AND \"SourceSubscriptionExpiresAtUtc\" IS NOT NULL)"));
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
        builder.Property(row => row.SubscriptionCreditsApplied).HasPrecision(18, 4);
        builder.Property(row => row.QuotaMode).HasMaxLength(16).HasDefaultValue("Finite").IsRequired();
        builder.HasOne<ExternalPlanSubscriptionRow>().WithMany().HasForeignKey(row => row.SourceSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubscriptionCreditPeriodRowConfiguration : IEntityTypeConfiguration<SubscriptionCreditPeriodRow>
{
    public void Configure(EntityTypeBuilder<SubscriptionCreditPeriodRow> builder)
    {
        builder.ToTable("billing_subscription_credit_periods");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => new { row.CustomerAccountId, row.SubscriptionIdentity, row.PeriodNumber }).IsUnique();
        builder.HasIndex(row => new { row.Status, row.ExpiresAtUtc });
        builder.Property(row => row.PlanCode).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SubscriptionIdentity).HasMaxLength(180).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(row => row.GrantedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.AllowanceCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.RemainingCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
        builder.HasOne<CustomerAccountRow>()
            .WithMany()
            .HasForeignKey(row => row.CustomerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UsageCreditAllocationRowConfiguration : IEntityTypeConfiguration<UsageCreditAllocationRow>
{
    public void Configure(EntityTypeBuilder<UsageCreditAllocationRow> builder)
    {
        builder.ToTable("billing_usage_credit_allocations");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.UsageLedgerEntryId, row.SubscriptionCreditPeriodId }).IsUnique();
        builder.HasIndex(row => row.GrantLedgerEntryId);
        builder.Property(row => row.CreditsApplied).HasPrecision(18, 4).IsRequired();
        builder.HasOne<UsageLedgerEntryRow>().WithMany().HasForeignKey(row => row.UsageLedgerEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SubscriptionCreditPeriodRow>().WithMany().HasForeignKey(row => row.SubscriptionCreditPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TopUpCreditSourceRowConfiguration : IEntityTypeConfiguration<TopUpCreditSourceRow>
{
    public void Configure(EntityTypeBuilder<TopUpCreditSourceRow> builder)
    {
        builder.ToTable("billing_topup_credit_sources");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.PurchaseId).IsUnique();
        builder.HasIndex(row => new { row.CustomerAccountId, row.Status, row.ExpiresAtUtc });
        builder.Property(row => row.GrantedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.RemainingCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
        builder.HasOne<CustomerAccountRow>().WithMany().HasForeignKey(row => row.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ExternalPlanSubscriptionRow>().WithMany().HasForeignKey(row => row.ExternalPlanSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TahlilAppTopUpPurchaseRowConfiguration : IEntityTypeConfiguration<TahlilAppTopUpPurchaseRow>
{
    public void Configure(EntityTypeBuilder<TahlilAppTopUpPurchaseRow> builder)
    {
        builder.ToTable("billing_tahlilapp_topup_purchases");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.Partner, row.InvoiceId }).IsUnique();
        // PostgreSQL allows multiple NULLs, preserving historical payment-ID uniqueness while new contract receipts omit it.
        builder.HasIndex(row => new { row.Partner, row.PaymentId }).IsUnique();
        builder.HasIndex(row => new { row.CustomerAccountId, row.ReceivedAtUtc });
        builder.Property(row => row.Partner).HasMaxLength(32).IsRequired();
        builder.Property(row => row.InvoiceId).HasMaxLength(160).IsRequired();
        builder.Property(row => row.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.EventId).HasMaxLength(160);
        builder.Property(row => row.PurchaseId).HasMaxLength(160);
        builder.Property(row => row.PaymentId).HasMaxLength(160);
        builder.Property(row => row.ExternalUserId).HasMaxLength(160).IsRequired();
        builder.Property(row => row.PlanSlug).HasMaxLength(100);
        builder.Property(row => row.PackageId).HasMaxLength(100);
        builder.Property(row => row.PackageVersion).HasMaxLength(40);
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.HasOne<CustomerAccountRow>().WithMany().HasForeignKey(row => row.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExternalPlanSubscriptionRow>().WithMany().HasForeignKey(row => row.ExternalPlanSubscriptionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConsumedPartnerTokenRowConfiguration : IEntityTypeConfiguration<ConsumedPartnerTokenRow>
{
    public void Configure(EntityTypeBuilder<ConsumedPartnerTokenRow> builder)
    {
        builder.ToTable("billing_consumed_partner_tokens");
        builder.HasKey(row => new { row.Issuer, row.Jti });
        builder.Property(row => row.Issuer).HasMaxLength(200).IsRequired();
        builder.Property(row => row.Jti).HasMaxLength(200).IsRequired();
        builder.HasIndex(row => row.ExpiresAtUtc);
    }
}

public sealed class TopUpUsageCreditAllocationRowConfiguration : IEntityTypeConfiguration<TopUpUsageCreditAllocationRow>
{
    public void Configure(EntityTypeBuilder<TopUpUsageCreditAllocationRow> builder)
    {
        builder.ToTable("billing_topup_usage_credit_allocations");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.UsageLedgerEntryId, row.TopUpCreditSourceId }).IsUnique();
        builder.HasIndex(row => row.GrantLedgerEntryId);
        builder.Property(row => row.CreditsApplied).HasPrecision(18, 4).IsRequired();
        builder.HasOne<UsageLedgerEntryRow>().WithMany().HasForeignKey(row => row.UsageLedgerEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TopUpCreditSourceRow>().WithMany().HasForeignKey(row => row.TopUpCreditSourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TopUpUsageCreditHoldRowConfiguration : IEntityTypeConfiguration<TopUpUsageCreditHoldRow>
{
    public void Configure(EntityTypeBuilder<TopUpUsageCreditHoldRow> builder)
    {
        builder.ToTable("billing_topup_usage_credit_holds");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.UsageReservationId, row.TopUpCreditSourceId }).IsUnique();
        builder.Property(row => row.CreditsHeld).HasPrecision(18, 4).IsRequired();
        builder.HasOne<UsageReservationRow>().WithMany().HasForeignKey(row => row.UsageReservationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TopUpCreditSourceRow>().WithMany().HasForeignKey(row => row.TopUpCreditSourceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TopUpOfferRowConfiguration : IEntityTypeConfiguration<TopUpOfferRow>
{
    public void Configure(EntityTypeBuilder<TopUpOfferRow> builder)
    {
        builder.ToTable("billing_topup_offers");
        builder.HasKey(row => new { row.OfferId, row.Version });
        builder.HasIndex(row => new { row.Credits, row.IsActive });
        builder.Property(row => row.OfferId).HasMaxLength(100);
        builder.Property(row => row.Version).HasMaxLength(40);
        builder.HasData(
            new TopUpOfferRow { OfferId = "TA-TOPUP-100", Version = "v1", Credits = 100, IsActive = true },
            new TopUpOfferRow { OfferId = "TA-TOPUP-500", Version = "v1", Credits = 500, IsActive = true },
            new TopUpOfferRow { OfferId = "TA-TOPUP-1000", Version = "v1", Credits = 1000, IsActive = true });
    }
}

public sealed class FreeCreditGrantEvidenceRowConfiguration : IEntityTypeConfiguration<FreeCreditGrantEvidenceRow>
{
    public void Configure(EntityTypeBuilder<FreeCreditGrantEvidenceRow> builder)
    {
        builder.ToTable("billing_free_credit_grant_evidence");
        builder.HasKey(row => row.UserId);
        builder.HasIndex(row => row.CustomerAccountId).IsUnique();
        builder.HasIndex(row => row.GrantLedgerEntryId).IsUnique();
        builder.Property(row => row.EvidenceSource).HasMaxLength(32).IsRequired();
        builder.HasOne<CustomerAccountRow>().WithMany().HasForeignKey(row => row.CustomerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UsageCreditHoldRowConfiguration : IEntityTypeConfiguration<UsageCreditHoldRow>
{
    public void Configure(EntityTypeBuilder<UsageCreditHoldRow> builder)
    {
        builder.ToTable("billing_usage_credit_holds");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.UsageReservationId, row.SubscriptionCreditPeriodId }).IsUnique();
        builder.Property(row => row.CreditsHeld).HasPrecision(18, 4).IsRequired();
        builder.HasOne<UsageReservationRow>().WithMany().HasForeignKey(row => row.UsageReservationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SubscriptionCreditPeriodRow>().WithMany().HasForeignKey(row => row.SubscriptionCreditPeriodId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExternalPlanSubscriptionRowConfiguration : IEntityTypeConfiguration<ExternalPlanSubscriptionRow>
{
    public void Configure(EntityTypeBuilder<ExternalPlanSubscriptionRow> builder)
    {
        builder.ToTable("billing_external_plan_subscriptions");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.CustomerAccountId, row.IdentityKey }).IsUnique();
        builder.HasIndex(row => new { row.CustomerAccountId, row.ExpiresAtUtc });
        builder.Property(row => row.IdentityKey).HasMaxLength(180).IsRequired();
        builder.Property(row => row.PlanCode).HasMaxLength(64).IsRequired();
        builder.HasOne<CustomerAccountRow>().WithMany().HasForeignKey(row => row.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PaidExpiryCooldownRowConfiguration : IEntityTypeConfiguration<PaidExpiryCooldownRow>
{
    public void Configure(EntityTypeBuilder<PaidExpiryCooldownRow> builder)
    {
        builder.ToTable("billing_paid_expiry_cooldowns");
        builder.HasKey(row => row.CustomerAccountId);
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
        builder.HasOne<CustomerAccountRow>().WithOne().HasForeignKey<PaidExpiryCooldownRow>(row => row.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
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
        builder.ToTable("billing_subscription_plans", table => table.HasCheckConstraint(
            "CK_billing_subscription_plans_quota_mode",
            "(\"QuotaMode\" = 'Finite' AND \"IncludedCredits\" >= 0) OR (\"QuotaMode\" = 'Unlimited' AND \"IncludedCredits\" = 0)"));
        builder.HasKey(row => row.Code);
        builder.Property(row => row.Code).HasMaxLength(64);
        builder.Property(row => row.Name).HasMaxLength(160).IsRequired();
        builder.Property(row => row.IncludedCredits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.QuotaMode).HasMaxLength(16).HasDefaultValue("Finite").IsRequired();
        builder.Property(row => row.PricingPolicyVersion).HasMaxLength(64).IsRequired();
        builder.HasMany<CustomerAccountRow>()
            .WithOne()
            .HasForeignKey(row => row.SubscriptionPlanCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasData(
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 250m, QuotaMode = "Finite", PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Pro", Name = "Pro", IncludedCredits = 2600m, QuotaMode = "Finite", PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Plus", Name = "Plus", IncludedCredits = 260m, QuotaMode = "Finite", PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Max", Name = "Max", IncludedCredits = 0m, QuotaMode = "Unlimited", PricingPolicyVersion = "v1" });
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
        Disabled("Free", "Page.Disclosures"),
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
        Enabled("Pro", "Page.Disclosures"),
        Enabled("Pro", "Watchlist.Symbols", 20),
        Enabled("Pro", "Portfolio.Records", 10),
        Enabled("Pro", "Tracker.Rules", 20),
        Enabled("Pro", "Radar.Symbols", 20),
        Enabled("Pro", "MarketPulse.Read"),
        Enabled("Pro", "AiQuery.PersonalDigest", 30),
        Enabled("Pro", "Notifications.Telegram"),
        Enabled("Max", "AiQuery.Scanner"),
        Enabled("Max", "AiQuery.StockAnalysis"),
        Enabled("Max", "AiQuery.FinancialComparison"),
        Enabled("Max", "AiQuery.CodalAnalysis", 30),
        Enabled("Max", "AiQuery.PortfolioAnalysis", 10),
        Enabled("Max", "Reports.Read"),
        Enabled("Max", "Page.Disclosures"),
        Enabled("Max", "Watchlist.Symbols", 20),
        Enabled("Max", "Portfolio.Records", 10),
        Enabled("Max", "Tracker.Rules", 20),
        Enabled("Max", "Radar.Symbols", 20),
        Enabled("Max", "MarketPulse.Read"),
        Enabled("Max", "AiQuery.PersonalDigest", 30),
        Enabled("Max", "Notifications.Telegram"),
        // Feature 159 change «max-plan-full-analysis-text»: only Max sees the whole TahlilApp analysis text.
        Enabled("Max", "ComprehensiveAnalysis.FullText"),
        Enabled("Plus", "AiQuery.Scanner"),
        Enabled("Plus", "AiQuery.StockAnalysis"),
        Enabled("Plus", "AiQuery.FinancialComparison"),
        Enabled("Plus", "AiQuery.CodalAnalysis"),
        Enabled("Plus", "AiQuery.DeepResearch", 10),
        Enabled("Plus", "AiQuery.PortfolioAnalysis", 50),
        Enabled("Plus", "Reports.Read"),
        Enabled("Plus", "Page.Disclosures"),
        Enabled("Plus", "Watchlist.Symbols", 50),
        Enabled("Plus", "Portfolio.Records", 50),
        Enabled("Plus", "Tracker.Rules", 50),
        Enabled("Plus", "Radar.Symbols", 50),
        Enabled("Plus", "MarketPulse.Read"),
        Enabled("Plus", "AiQuery.PersonalDigest"),
        Enabled("Plus", "Notifications.Telegram")
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

    private static PlanCapabilityRow Disabled(string planCode, string capabilityCode) =>
        new()
        {
            PlanCode = planCode,
            CapabilityCode = capabilityCode,
            PolicyVersion = "v1",
            IsEnabled = false,
            Limit = null
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

public sealed class BillingPurchaseProductRowConfiguration : IEntityTypeConfiguration<BillingPurchaseProductRow>
{
    public void Configure(EntityTypeBuilder<BillingPurchaseProductRow> builder)
    {
        builder.ToTable("billing_purchase_products");
        builder.HasKey(row => row.Code);
        builder.HasIndex(row => new { row.Channel, row.IsActive, row.SortOrder });
        builder.Property(row => row.Code).HasMaxLength(96);
        builder.Property(row => row.ProductType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Version).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(row => row.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Currency).HasMaxLength(8).IsRequired();
        builder.Property(row => row.Credits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.PlanCode).HasMaxLength(64);
        builder.Property(row => row.Channel).HasMaxLength(32).IsRequired();
        builder.HasOne<SubscriptionPlanRow>()
            .WithMany()
            .HasForeignKey(row => row.PlanCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasData(
            Product("TG-CREDITS-50", "CreditPack", "v1", "Telegram 50 AI credits", 250000m, "IRR", 50m, null, null, 10),
            Product("TG-CREDITS-150", "CreditPack", "v1", "Telegram 150 AI credits", 690000m, "IRR", 150m, null, null, 20),
            Product("TG-PRO-30D", "Subscription", "v1", "Telegram Pro 30 days", 1200000m, "IRR", 0m, "Pro", 30, 30),
            Product("TG-PLUS-30D", "Subscription", "v1", "Telegram Plus 30 days", 2200000m, "IRR", 0m, "Plus", 30, 40));
    }

    private static BillingPurchaseProductRow Product(
        string code,
        string productType,
        string version,
        string displayName,
        decimal amount,
        string currency,
        decimal credits,
        string? planCode,
        int? durationDays,
        int sortOrder) =>
        new()
        {
            Code = code,
            ProductType = productType,
            Version = version,
            DisplayName = displayName,
            Amount = amount,
            Currency = currency,
            Credits = credits,
            PlanCode = planCode,
            DurationDays = durationDays,
            Channel = "Telegram",
            IsActive = true,
            SortOrder = sortOrder,
            CreatedAtUtc = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero)
        };
}

public sealed class BillingCheckoutIntentRowConfiguration : IEntityTypeConfiguration<BillingCheckoutIntentRow>
{
    public void Configure(EntityTypeBuilder<BillingCheckoutIntentRow> builder)
    {
        builder.ToTable("billing_checkout_intents");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => row.PaymentReference).IsUnique();
        builder.HasIndex(row => row.ProviderReferenceHash).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.Status, row.CreatedAtUtc });
        builder.HasIndex(row => new { row.Status, row.ExpiresAtUtc });
        builder.HasIndex(row => row.FulfillmentLedgerEntryId).IsUnique();
        builder.HasIndex(row => row.FulfillmentFinancialTransactionId).IsUnique();
        builder.Property(row => row.ProductType).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ProductCode).HasMaxLength(96).IsRequired();
        builder.Property(row => row.ProductVersion).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ProductDisplayName).HasMaxLength(160).IsRequired();
        builder.Property(row => row.Amount).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.Currency).HasMaxLength(8).IsRequired();
        builder.Property(row => row.Credits).HasPrecision(18, 4).IsRequired();
        builder.Property(row => row.PlanCode).HasMaxLength(64);
        builder.Property(row => row.Channel).HasMaxLength(32).IsRequired();
        builder.Property(row => row.PaymentReference).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(160).IsRequired();
        builder.Property(row => row.ReceiptIdempotencyKey).HasMaxLength(160);
        builder.Property(row => row.ReviewIdempotencyKey).HasMaxLength(160);
        builder.Property(row => row.ProviderName).HasMaxLength(80);
        builder.Property(row => row.ProviderReferenceHash).HasMaxLength(128);
        builder.Property(row => row.ReceiptAttachmentKind).HasMaxLength(32);
        builder.Property(row => row.ReceiptAttachmentReference).HasMaxLength(500);
        builder.Property(row => row.ReceiptContentHash).HasMaxLength(128);
        builder.Property(row => row.ReviewReason).HasMaxLength(500);
        builder.Property(row => row.ConcurrencyToken).IsConcurrencyToken();
        builder.HasOne<CustomerAccountRow>()
            .WithMany()
            .HasForeignKey(row => row.CustomerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BillingPurchaseProductRow>()
            .WithMany()
            .HasForeignKey(row => row.ProductCode)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UsageLedgerEntryRow>()
            .WithMany()
            .HasForeignKey(row => row.FulfillmentLedgerEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancialTransactionRow>()
            .WithMany()
            .HasForeignKey(row => row.FulfillmentFinancialTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
