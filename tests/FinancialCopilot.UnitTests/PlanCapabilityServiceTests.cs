using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FinancialCopilot.UnitTests;

public sealed class PlanCapabilityServiceTests
{
    // Feature 152: Max has Pro's matrix. Feature 159 change «max-plan-full-analysis-text» (owner, 2026-11-11) adds one
    // Max-only row: the whole TahlilApp analysis text.
    [Fact]
    public void MaxSeededCapabilityMatrixMatchesPro_PlusOnlyTheFullAnalysisText()
    {
        using var dbContext = CreateDbContext();
        var capabilityEntity = dbContext.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PlanCapabilityRow));
        Assert.NotNull(capabilityEntity);
        var seedRows = capabilityEntity!.GetSeedData().ToList();
        var pro = ReadPlanCapabilities(seedRows, "Pro");
        var max = ReadPlanCapabilities(seedRows, "Max");

        Assert.NotEmpty(pro);
        Assert.Equal((true, (decimal?)null), max["ComprehensiveAnalysis.FullText"]);
        Assert.Equal(pro.OrderBy(item => item.Key),
            max.Where(item => item.Key != "ComprehensiveAnalysis.FullText").OrderBy(item => item.Key));
        Assert.Single(seedRows, row => Equals(row[nameof(PlanCapabilityRow.CapabilityCode)], "ComprehensiveAnalysis.FullText"));
    }

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

    [Fact]
    public async Task ActiveMaxRequiresAnExplicitCapabilityAndRevokedMaxFallsBackToFinitePlan()
    {
        await using var dbContext = CreateDbContext();
        var account = await SeedAccountAsync(dbContext, enabled: false);
        var row = await dbContext.CustomerAccounts.SingleAsync(item => item.Id == account.Id);
        row.ExternalSubscriptionManaged = true;
        row.SubscriptionPlanCode = "Max";
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow
        {
            Code = "Max", Name = "Max", IncludedCredits = 0, QuotaMode = "Unlimited", PricingPolicyVersion = "v1"
        });
        dbContext.PlanCapabilities.Add(new PlanCapabilityRow
        {
            PlanCode = "Max", CapabilityCode = "AiQuery.Scanner", PolicyVersion = "approved-v1", IsEnabled = true
        });
        var maxWindow = new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = account.Id, IdentityKey = "max:window",
            PlanCode = "Max", StartsAtUtc = DateTimeOffset.UtcNow.AddDays(-1), ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(10)
        };
        dbContext.ExternalPlanSubscriptions.Add(maxWindow);
        await dbContext.SaveChangesAsync();

        var service = new PlanCapabilityService(dbContext);
        await service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateCanExecuteAsync(account, "AiQuery.StockAnalysis", CancellationToken.None));

        maxWindow.IsRevoked = true;
        await dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ValidateCanExecuteAsync(account, "AiQuery.Scanner", CancellationToken.None));
    }

    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"plan-capability-{Guid.NewGuid():N}")
            .Options);

    private static Dictionary<string, (bool IsEnabled, decimal? Limit)> ReadPlanCapabilities(
        IEnumerable<IDictionary<string, object?>> rows,
        string planCode) => rows
        .Where(row => Equals(row[nameof(PlanCapabilityRow.PlanCode)], planCode))
        .ToDictionary(
            row => (string)row[nameof(PlanCapabilityRow.CapabilityCode)]!,
            row => ((bool)row[nameof(PlanCapabilityRow.IsEnabled)]!,
                (decimal?)row[nameof(PlanCapabilityRow.Limit)]));

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
