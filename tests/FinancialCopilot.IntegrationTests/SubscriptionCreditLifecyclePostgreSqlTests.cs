using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class SubscriptionCreditLifecyclePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static readonly DateTimeOffset Activation = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [SkippableFact]
    public async Task ConcurrentPlusToProEventsAcrossIndependentConnections_TransferCreditsExactlyOnce()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var seed = database.CreateBillingContext())
        {
            var account = new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                UserId = userId,
                AccountType = "Individual",
                BillingMode = "Prepaid",
                SubscriptionPlanCode = "Plus",
                SubscriptionEffectiveFrom = Activation,
                SubscriptionActivationIsAuthoritative = true,
                SubscriptionEffectiveTo = Activation.AddDays(90)
            };
            seed.CustomerAccounts.Add(account);
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = account.Id,
                Balance = 0,
                ReservedAmount = 0,
                UpdatedAt = Activation,
                Revision = 0
            });
            await seed.SaveChangesAsync();
            await new SubscriptionCreditLifecycleService(seed).InitializeNewWalletAsync(
                account, await seed.WalletProjections.SingleAsync(), Activation, CancellationToken.None);
            await seed.SaveChangesAsync();

            var plusPeriod = await seed.SubscriptionCreditPeriods.SingleAsync();
            plusPeriod.RemainingCredits = 60m;
            var wallet = await seed.WalletProjections.SingleAsync();
            wallet.Balance = 60m;
            seed.UsageLedgerEntries.Add(new UsageLedgerEntryRow
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = accountId,
                ActorId = userId,
                TenantId = tenantId,
                EntryType = "Charge",
                OperationCode = "AiQuery.IntegrationTest",
                CreditsCharged = 200m,
                PricingPolicyVersion = "test-v1",
                IdempotencyKey = $"test-consumption-{Guid.NewGuid():N}",
                OccurredAt = Activation.AddDays(1),
                SubscriptionCreditsApplied = 200m,
                RelatedEntryId = plusPeriod.GrantLedgerEntryId
            });
            await seed.SaveChangesAsync();
        }

        var decision = new TahlilAppSubscriptionDecision("Pro", Activation.AddDays(90), "active-paid", Activation.AddDays(5));
        await using var firstContext = database.CreateBillingContext();
        await using var secondContext = database.CreateBillingContext();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = SynchronizeAfterGateAsync(firstContext, tenantId, userId, decision, gate.Task);
        var second = SynchronizeAfterGateAsync(secondContext, tenantId, userId, decision, gate.Task);
        gate.SetResult();
        await Task.WhenAll(first, second);

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(2660m, walletAfter.Balance);
        Assert.Single(await verify.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == accountId && row.PlanCode == "Pro").ToListAsync());
        Assert.Equal(-60m, await verify.UsageLedgerEntries.Where(row => row.CustomerAccountId == accountId && row.OperationCode == "Billing.SubscriptionCreditTransferOut").SumAsync(row => row.CreditsCharged));
        Assert.Equal(60m, await verify.UsageLedgerEntries.Where(row => row.CustomerAccountId == accountId && row.OperationCode == "Billing.SubscriptionCreditTransferIn").SumAsync(row => row.CreditsCharged));
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.CustomerAccountId == accountId && row.OperationCode == "Billing.SubscriptionCreditGrant" && row.CreditsCharged == 2600m).ToListAsync());
    }

    private static async Task SynchronizeAfterGateAsync(
        BillingDbContext context,
        Guid tenantId,
        Guid userId,
        TahlilAppSubscriptionDecision decision,
        Task gate)
    {
        await gate;
        var time = new FixedTimeProvider(Activation.AddDays(5));
        await new ExternalSubscriptionSynchronizer(context, new OwnedIdentityBillingProvisioner(context, time), time)
            .SynchronizeAsync(tenantId, userId, decision, 9, CancellationToken.None);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
