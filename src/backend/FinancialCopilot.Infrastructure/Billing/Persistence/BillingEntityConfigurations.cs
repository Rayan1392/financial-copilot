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
    }
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
    }
}
