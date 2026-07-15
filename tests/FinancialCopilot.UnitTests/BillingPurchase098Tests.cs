using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Purchases;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Domain.Notifications;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class BillingPurchase098Tests
{
    [Fact]
    public void Checkout_lifecycle_rejects_invalid_transition()
    {
        var intent = new BillingCheckoutIntent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BillingPurchaseProductType.CreditPack,
            "TG-CREDITS-50",
            "v1",
            250000,
            "IRR",
            "TG-1",
            BillingCheckoutStatus.AwaitingPayment,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            1);

        Assert.Throws<InvalidOperationException>(() => intent.TransitionTo(BillingCheckoutStatus.Fulfilled));
    }

    [Fact]
    public async Task Create_checkout_is_idempotent_per_actor_account_and_product()
    {
        await using var db = CreateDb();
        var (actor, account) = SeedBilling(db);
        var service = new BillingPurchaseUseCases(db, new FixedAccountResolver(account),
            new FakeNotificationIntentPublisher(), TimeProvider.System);
        var command = new CreateBillingCheckoutCommand(actor, "TG-CREDITS-50", "checkout-1", "corr-1");

        var first = await service.CreateCheckoutAsync(command, CancellationToken.None);
        var replay = await service.CreateCheckoutAsync(command, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(BillingCheckoutStatus.AwaitingPayment, first.Status);
        Assert.Single(db.CheckoutIntents);
    }

    [Fact]
    public async Task Approved_credit_pack_fulfills_one_payment_and_one_wallet_grant()
    {
        await using var db = CreateDb();
        var (actor, account) = SeedBilling(db);
        var notifications = new FakeNotificationIntentPublisher();
        var service = new BillingPurchaseUseCases(db, new FixedAccountResolver(account),
            notifications, TimeProvider.System);
        var checkout = await service.CreateCheckoutAsync(
            new CreateBillingCheckoutCommand(actor, "TG-CREDITS-50", "checkout-2", "corr-2"),
            CancellationToken.None);
        var submitted = await service.SubmitReceiptAsync(
            new SubmitBillingReceiptCommand(actor, checkout.Id, checkout.Version, "Image", "secure-object-ref",
                "bank-ref-1", "receipt-2", "corr-2"),
            CancellationToken.None);

        var approved = await service.ReviewReceiptAsync(
            new ReviewBillingReceiptCommand(actor.ActorId, actor.TenantId, checkout.Id, submitted.Version,
                Approved: true, "Receipt matched payment reference.", "review-2", "corr-2"),
            CancellationToken.None);
        var replay = await service.ReviewReceiptAsync(
            new ReviewBillingReceiptCommand(actor.ActorId, actor.TenantId, checkout.Id, submitted.Version,
                Approved: true, "Receipt matched payment reference.", "review-2", "corr-2"),
            CancellationToken.None);

        Assert.Equal(BillingCheckoutStatus.Fulfilled, approved.Status);
        Assert.True(replay.AlreadyApplied);
        Assert.Single(db.FinancialTransactions);
        Assert.Single(db.UsageLedgerEntries);
        Assert.Single(notifications.Requests);
        Assert.Equal("BillingCheckoutFulfilled", notifications.Requests.Single().EventType);
        Assert.Equal(FinancialTransactionType.Payment.ToString(), db.FinancialTransactions.Single().Type);
        Assert.Equal(60m, db.WalletProjections.Single().Balance);
        Assert.Equal("Billing.PurchasedCredits", db.UsageLedgerEntries.Single().OperationCode);
    }

    [Fact]
    public async Task Approved_subscription_updates_existing_account_plan_without_credit_grant()
    {
        await using var db = CreateDb();
        var (actor, account) = SeedBilling(db);
        var service = new BillingPurchaseUseCases(db, new FixedAccountResolver(account),
            new FakeNotificationIntentPublisher(), TimeProvider.System);
        var checkout = await service.CreateCheckoutAsync(
            new CreateBillingCheckoutCommand(actor, "TG-PRO-30D", "checkout-3", "corr-3"),
            CancellationToken.None);
        var submitted = await service.SubmitReceiptAsync(
            new SubmitBillingReceiptCommand(actor, checkout.Id, checkout.Version, "Document", "secure-doc-ref",
                null, "receipt-3", "corr-3"),
            CancellationToken.None);

        var approved = await service.ReviewReceiptAsync(
            new ReviewBillingReceiptCommand(actor.ActorId, actor.TenantId, checkout.Id, submitted.Version,
                Approved: true, "Receipt approved.", "review-3", "corr-3"),
            CancellationToken.None);

        Assert.Equal(BillingCheckoutStatus.Fulfilled, approved.Status);
        Assert.Single(db.FinancialTransactions);
        Assert.Empty(db.UsageLedgerEntries);
        var row = db.CustomerAccounts.Single();
        Assert.Equal("Pro", row.SubscriptionPlanCode);
        Assert.NotNull(row.SubscriptionEffectiveFrom);
        Assert.NotNull(row.SubscriptionEffectiveTo);
        Assert.True(row.SubscriptionEffectiveTo > row.SubscriptionEffectiveFrom);
    }

    private static BillingDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new BillingDbContext(options);
        return db;
    }

    private static (BillableActorContext Actor, CustomerAccount Account) SeedBilling(BillingDbContext db)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        db.SubscriptionPlans.AddRange(
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 0, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Pro", Name = "Pro", IncludedCredits = 100, PricingPolicyVersion = "v1" });
        db.PurchaseProducts.AddRange(
            new BillingPurchaseProductRow
            {
                Code = "TG-CREDITS-50",
                ProductType = BillingPurchaseProductType.CreditPack.ToString(),
                Version = "v1",
                DisplayName = "Telegram 50 AI credits",
                Amount = 250000,
                Currency = "IRR",
                Credits = 50,
                Channel = "Telegram",
                IsActive = true,
                SortOrder = 10,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new BillingPurchaseProductRow
            {
                Code = "TG-PRO-30D",
                ProductType = BillingPurchaseProductType.Subscription.ToString(),
                Version = "v1",
                DisplayName = "Telegram Pro 30 days",
                Amount = 1200000,
                Currency = "IRR",
                Credits = 0,
                PlanCode = "Pro",
                DurationDays = 30,
                Channel = "Telegram",
                IsActive = true,
                SortOrder = 20,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        db.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = tenantId,
            UserId = actorId,
            AccountType = CustomerAccountType.Individual.ToString(),
            BillingMode = BillingMode.Prepaid.ToString(),
            SubscriptionPlanCode = "Free"
        });
        db.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = accountId,
            Balance = 10,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        var actor = new BillableActorContext(actorId, tenantId, actorId, null, null);
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        return (actor, account);
    }

    private sealed class FixedAccountResolver(CustomerAccount account) : IBillableAccountResolver
    {
        public Task<CustomerAccount> ResolveAsync(BillableActorContext actor, CancellationToken cancellationToken) =>
            Task.FromResult(account);
    }

    private sealed class FakeNotificationIntentPublisher : INotificationIntentPublisher
    {
        public List<NotificationIntentRequest> Requests { get; } = [];

        public Task<NotificationIntentDto> EnqueueAsync(
            NotificationIntentRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new NotificationIntentDto(
                Guid.NewGuid(),
                request.Actor,
                request.Channel,
                request.EventType,
                request.EntityKey,
                request.DeduplicationKey,
                request.Severity,
                NotificationIntentState.Pending,
                DateTimeOffset.UtcNow,
                request.NotBeforeUtc,
                request.ExpiresAtUtc));
        }
    }
}
