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
    public async Task Sync_IsIdempotent_AndGrantsNoCredits()
    {
        await using var dbContext = CreateBillingDbContext();
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25, PricingPolicyVersion = "v1" });
        await dbContext.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(dbContext, new OwnedIdentityBillingProvisioner(dbContext, TimeProvider.System), TimeProvider.System);
        var expiry = Now.AddDays(30);
        var plus = new TahlilAppSubscriptionDecision("Plus", expiry, "active-paid");

        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 17, CancellationToken.None);
        var afterFirst = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var balanceAfterFirst = (await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == afterFirst.Id)).Balance;

        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 17, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId, plus, 18, CancellationToken.None);

        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var balance = (await dbContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance;

        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(expiry, account.SubscriptionEffectiveTo);
        Assert.Equal(1, account.SubscriptionRevision);
        Assert.Equal(1, await dbContext.AdminAudits.CountAsync(row => row.TenantId == tenantId));
        Assert.Equal(balanceAfterFirst, balance);
    }

    [Fact]
    public async Task Sync_DowngradeToFree_ClearsWindowAndBumpsRevisionOnce()
    {
        await using var dbContext = CreateBillingDbContext();
        var (tenantId, userId) = (Guid.NewGuid(), Guid.NewGuid());
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25, PricingPolicyVersion = "v1" });
        await dbContext.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(dbContext, new OwnedIdentityBillingProvisioner(dbContext, TimeProvider.System), TimeProvider.System);

        await synchronizer.SynchronizeAsync(tenantId, userId, new TahlilAppSubscriptionDecision("Plus", Now.AddDays(30), "active-paid"), 1, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId, new TahlilAppSubscriptionDecision("Free", null, "expired"), 2, CancellationToken.None);

        var account = await dbContext.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Null(account.SubscriptionEffectiveTo);
        Assert.Equal(2, account.SubscriptionRevision);
    }

    private static BillingDbContext CreateBillingDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"tahlilapp-billing-{Guid.NewGuid():N}")
            .Options);
}
