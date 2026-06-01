using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class PlanCapabilityServiceTests
{
    [Fact]
    public async Task AssignedPlan_AllowsEnabledCapability()
    {
        await using var dbContext = CreateDbContext();
        var account = await SeedAccountAsync(dbContext, enabled: true);
        var service = new PlanCapabilityService(dbContext);

        await service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None);
    }

    [Fact]
    public async Task AssignedPlan_RejectsMissingCapability()
    {
        await using var dbContext = CreateDbContext();
        var account = await SeedAccountAsync(dbContext, enabled: false);
        var service = new PlanCapabilityService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None));
    }

    [Fact]
    public async Task AssignedPlan_RejectsExhaustedMonthlyQuota()
    {
        await using var dbContext = CreateDbContext();
        var account = await SeedAccountAsync(dbContext, enabled: true, limit: 1);
        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = account.Id,
            ActorId = Guid.NewGuid(),
            TenantId = account.TenantId,
            EntryType = "Charge",
            OperationCode = "AiQuery.Scanner",
            CreditsCharged = 1,
            PricingPolicyVersion = "v1",
            IdempotencyKey = Guid.NewGuid().ToString(),
            OccurredAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var service = new PlanCapabilityService(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None));
    }

    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"plan-capability-{Guid.NewGuid():N}")
            .Options);

    private static async Task<CustomerAccount> SeedAccountAsync(
        BillingDbContext dbContext,
        bool enabled,
        decimal? limit = null)
    {
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = tenantId,
            AccountType = "Individual",
            BillingMode = "Prepaid",
            SubscriptionPlanCode = "Custom"
        });
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow
        {
            Code = "Custom",
            Name = "Custom",
            IncludedCredits = 10,
            PricingPolicyVersion = "v1"
        });
        if (enabled)
        {
            dbContext.PlanCapabilities.Add(new PlanCapabilityRow
            {
                PlanCode = "Custom",
                CapabilityCode = "AiQuery.Scanner",
                PolicyVersion = "v1",
                IsEnabled = true,
                Limit = limit
            });
        }
        await dbContext.SaveChangesAsync();
        return new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
    }
}
