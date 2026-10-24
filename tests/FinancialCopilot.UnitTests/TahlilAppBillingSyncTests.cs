using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppBillingSyncTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ExpiryRule_LapsedPaidWindowReadsAsFree()
    {
        Assert.Equal("Free", SubscriptionEffectiveness.ResolveEffectivePlanCode("Plus", Now.AddMinutes(-1), Now));
    }

    [Fact]
    public void ExpiryRule_ActiveOrOpenWindowKeepsStoredPlan()
    {
        Assert.Equal("Plus", SubscriptionEffectiveness.ResolveEffectivePlanCode("Plus", Now.AddDays(1), Now));
        Assert.Equal("Plus", SubscriptionEffectiveness.ResolveEffectivePlanCode("Plus", null, Now));
        Assert.Null(SubscriptionEffectiveness.ResolveEffectivePlanCode(null, Now.AddDays(1), Now));
    }

    [Fact]
    public async Task CapabilityCheck_LapsedPaidAccount_LosesPaidCapability()
    {
        await using var dbContext = CreateBillingDbContext();
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = tenantId,
            AccountType = "Individual",
            BillingMode = "Prepaid",
            SubscriptionPlanCode = "Custom",
            SubscriptionEffectiveTo = Now.AddMinutes(-5)
        });
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Custom", Name = "Custom", IncludedCredits = 10, PricingPolicyVersion = "v1" });
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 10, PricingPolicyVersion = "v1" });
        dbContext.PlanCapabilities.Add(new PlanCapabilityRow
        {
            PlanCode = "Custom",
            CapabilityCode = "AiQuery.Scanner",
            PolicyVersion = "v1",
            IsEnabled = true,
            Limit = null
        });
        await dbContext.SaveChangesAsync();

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var service = new PlanCapabilityService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None));
    }

    [Fact]
    public async Task SubscriptionRepository_LapsedPaidWindowReturnsFreePlan()
    {
        await using var dbContext = CreateBillingDbContext();
        var accountId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = Guid.NewGuid(),
            AccountType = "Individual",
            BillingMode = "Prepaid",
            SubscriptionPlanCode = "Plus",
            SubscriptionEffectiveTo = DateTimeOffset.UtcNow.AddDays(-1)
        });
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Plus", Name = "Plus", IncludedCredits = 260, PricingPolicyVersion = "v1" });
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25, PricingPolicyVersion = "v1" });
        await dbContext.SaveChangesAsync();

        var result = await new SubscriptionPlanRepository(dbContext).FindForCustomerAsync(accountId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Free", result.Code);
        Assert.Equal(25m, result.IncludedCredits);
    }

    [Fact]
    public async Task Sync_IsIdempotent_AndGrantsOnlyOneApprovedCreditPeriod()
    {
        await using var dbContext = CreateBillingDbContext();
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        SeedCanonicalPlans(dbContext);
        await dbContext.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(dbContext, new OwnedIdentityBillingProvisioner(dbContext, TimeProvider.System), TimeProvider.System);
        var expiry = Now.AddDays(30);
        var plus = new TahlilAppSubscriptionDecision("Plus", expiry, "active-paid", Now.AddMinutes(-1));

        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 17, CancellationToken.None);
        var afterFirst = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var balanceAfterFirst = (await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == afterFirst.Id)).Balance;

        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 17, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 18, CancellationToken.None);

        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var balance = (await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance;

        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(expiry, account.SubscriptionEffectiveTo);
        Assert.Equal(2, account.SubscriptionRevision);
        Assert.Equal(18, account.ExternalSubscriptionRevision);
        Assert.Equal(1, await dbContext.AdminAudits.CountAsync(row => row.TenantId == tenantId));
        Assert.Equal(260m, balanceAfterFirst);
        Assert.Equal(balanceAfterFirst, balance);
        Assert.Equal(1, await dbContext.UsageLedgerEntries.CountAsync(row =>
            row.CustomerAccountId == account.Id &&
            row.OperationCode == "Billing.SubscriptionCreditGrant" &&
            row.CreditsCharged == 260m));
    }

    [Fact]
    public async Task Sync_DowngradeToFree_ClearsWindowAndBumpsRevisionOnce()
    {
        await using var dbContext = CreateBillingDbContext();
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        SeedCanonicalPlans(dbContext);
        await dbContext.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(dbContext, new OwnedIdentityBillingProvisioner(dbContext, TimeProvider.System), TimeProvider.System);

        await synchronizer.SynchronizeAsync(tenantId, userId, new TahlilAppSubscriptionDecision("Plus", Now.AddDays(30), "active-paid"), 1, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId, new TahlilAppSubscriptionDecision("Free", null, "expired"), 2, CancellationToken.None);

        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Null(account.SubscriptionEffectiveTo);
        Assert.Equal(2, account.SubscriptionRevision);
    }

    [Fact]
    public async Task Sync_IgnoresOutOfOrderAndConflictingEqualExternalRevisions()
    {
        await using var dbContext = CreateBillingDbContext();
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        SeedCanonicalPlans(dbContext);
        await dbContext.SaveChangesAsync();
        var time = TimeProvider.System;
        var synchronizer = new ExternalSubscriptionSynchronizer(
            dbContext, new OwnedIdentityBillingProvisioner(dbContext, time), time);
        var activation = Now.AddMinutes(-1);
        await synchronizer.SynchronizeAsync(tenantId, userId,
            new TahlilAppSubscriptionDecision("Plus", Now.AddDays(30), "active-paid", activation), 20, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId,
            new TahlilAppSubscriptionDecision("Plus", Now.AddDays(30), "active-paid", activation), 21, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId,
            new TahlilAppSubscriptionDecision("Free", null, "stale"), 20, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId,
            new TahlilAppSubscriptionDecision("Pro", Now.AddDays(90), "conflicting-equal", activation), 21, CancellationToken.None);

        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(21, account.ExternalSubscriptionRevision);
        Assert.Equal(260m, (await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(2, await dbContext.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id));
        Assert.Single(await dbContext.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus").ToListAsync());
    }

    private static BillingDbContext CreateBillingDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"tahlilapp-billing-{Guid.NewGuid():N}")
            .Options);

    private static void SeedCanonicalPlans(BillingDbContext dbContext) =>
        dbContext.SubscriptionPlans.AddRange(
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Plus", Name = "Plus", IncludedCredits = 260m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Pro", Name = "Pro", IncludedCredits = 2600m, PricingPolicyVersion = "v1" });
}
