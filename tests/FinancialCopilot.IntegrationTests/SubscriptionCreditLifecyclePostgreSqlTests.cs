using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;
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
    public async Task ConcurrentFirstInactiveLaunchesAcrossIndependentConnections_GrantFreeCreditsOnce()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var firstContext = database.CreateBillingContext();
        await using var secondContext = database.CreateBillingContext();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        var decision = new TahlilAppSubscriptionDecision(
            "Free", null, "free-or-no-active-paid-plan", PlanSubscriptions: Array.Empty<TahlilAppPlanSubscription>());
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = SynchronizeInactiveLaunchAsync(firstContext, tenantId, userId, decision, gate.Task);
        var second = SynchronizeInactiveLaunchAsync(secondContext, tenantId, userId, decision, gate.Task);
        gate.SetResult();
        await Task.WhenAll(first, second);

        await using var verify = database.CreateBillingContext();
        var account = await verify.CustomerAccounts.SingleAsync(row => row.TenantId == tenantId && row.UserId == userId);
        var wallet = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        var period = await verify.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Equal(250m, wallet.Balance);
        Assert.Equal(250m, period.GrantedCredits);
        Assert.Equal(Activation, period.StartsAtUtc);
        Assert.Equal(Activation.AddDays(30), period.ExpiresAtUtc);
        Assert.Equal(1, await verify.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free"));
        Assert.Equal(1, await verify.UsageLedgerEntries.CountAsync(row =>
            row.CustomerAccountId == account.Id && row.OperationCode == "Billing.SubscriptionCreditGrant"));
        Assert.Equal(1, await verify.FreeCreditGrantEvidence.CountAsync(row => row.UserId == userId));
        Assert.Empty(await verify.PaidExpiryCooldowns.Where(row => row.CustomerAccountId == account.Id).ToListAsync());
    }

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

        var proStartsAt = Activation.AddDays(5);
        var proExpiresAt = Activation.AddDays(90);
        var decision = new TahlilAppSubscriptionDecision(
            "Pro",
            proExpiresAt,
            "active-paid-plan-set",
            proStartsAt,
            [new TahlilAppPlanSubscription(
                "pro",
                "Pro",
                proStartsAt,
                proExpiresAt,
                $"pro:{proStartsAt.ToUnixTimeMilliseconds()}:{proExpiresAt.ToUnixTimeMilliseconds()}")]);
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

    [SkippableFact]
    public async Task ReservationSettlementRacingExpiryKeepsOriginalSourceAndExpiresItsRemainder()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        var (accountId, tenantId, actorId, expiresAt) = await SeedPlusAccountAsync(database);
        const string reservationKey = "feature139-expiry-race:reservation";
        const string chargeKey = "feature139-expiry-race:charge";
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);

        UsageReservation reservation;
        await using (var reserveContext = database.CreateBillingContext())
        {
            await reserveContext.Database.OpenConnectionAsync();
            var wallet = await reserveContext.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            reservation = await new UsageReservationAuthorizationService(reserveContext, new CreditLinePolicyService(),
                    new FixedTimeProvider(Activation.AddDays(89)))
                .ReserveAsync(account, new WalletSnapshot(accountId, wallet.Balance, wallet.ReservedAmount,
                    wallet.UpdatedAt, wallet.Revision), "AiQuery.ExpiryRace", 50m, reservationKey, CancellationToken.None);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> ExpireAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await gate.Task;
            try
            {
                await new SubscriptionCreditLifecycleService(context).ProcessDuePeriodsAsync(10, expiresAt, CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }

        async Task<Exception?> CommitAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await gate.Task;
            try
            {
                await new UsageFinalizationService(context, new FixedTimeProvider(expiresAt.AddMinutes(1))).CommitAsync(
                    new UsageCommitCommand(accountId, actorId, tenantId, null, null, reservationKey, chargeKey,
                        new UsageChargeResult(20m, "feature139-race-v1", Cached: false)), CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }

        var expiryAttempt = ExpireAsync();
        var commitAttempt = CommitAsync();
        gate.SetResult();
        var race = await Task.WhenAll(expiryAttempt, commitAttempt);
        Assert.All(race, error => Assert.Null(error));

        await using (var cleanup = database.CreateBillingContext())
        {
            await cleanup.Database.OpenConnectionAsync();
            await new SubscriptionCreditLifecycleService(cleanup).ProcessDuePeriodsAsync(10, expiresAt.AddMinutes(2), CancellationToken.None);
        }
        await using var verify = database.CreateBillingContext();
        var period = await verify.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == accountId && row.PlanCode == "Plus");
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        var charge = await verify.UsageLedgerEntries.SingleAsync(row => row.IdempotencyKey == chargeKey);
        Assert.Equal(20m, charge.SubscriptionCreditsApplied);
        Assert.Equal(0m, period.RemainingCredits);
        Assert.Equal(SubscriptionCreditLifecycleService.ExpiredStatus, period.Status);
        Assert.Equal(0m, walletAfter.Balance);
        Assert.Empty(await verify.UsageCreditHolds.Where(row => row.UsageReservationId == reservation.Id).ToListAsync());
    }

    [SkippableFact]
    public async Task RefundRacingExpiryOffsetsTheSourceAndDoesNotRestoreSpendableCredits()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        var (accountId, tenantId, actorId, expiresAt) = await SeedPlusAccountAsync(database);
        const string reservationKey = "feature139-refund-race:reservation";
        const string chargeKey = "feature139-refund-race:charge";
        const string refundKey = "feature139-refund-race:refund";
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        await using (var reserveContext = database.CreateBillingContext())
        {
            await reserveContext.Database.OpenConnectionAsync();
            var wallet = await reserveContext.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            await new UsageReservationAuthorizationService(reserveContext, new CreditLinePolicyService(),
                    new FixedTimeProvider(Activation.AddDays(1)))
                .ReserveAsync(account, new WalletSnapshot(accountId, wallet.Balance, wallet.ReservedAmount,
                        wallet.UpdatedAt, wallet.Revision), "AiQuery.RefundExpiryRace", 20m, reservationKey, CancellationToken.None);
        }
        await using (var commitContext = database.CreateBillingContext())
        {
            await commitContext.Database.OpenConnectionAsync();
            await new UsageFinalizationService(commitContext, new FixedTimeProvider(Activation.AddDays(1))).CommitAsync(
                new UsageCommitCommand(accountId, actorId, tenantId, null, null, reservationKey, chargeKey,
                    new UsageChargeResult(20m, "feature139-race-v1", Cached: false)), CancellationToken.None);
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> ExpireAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await gate.Task;
            try
            {
                await new SubscriptionCreditLifecycleService(context).ProcessDuePeriodsAsync(10, expiresAt, CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }

        async Task<Exception?> RefundAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await gate.Task;
            try
            {
                await new UsageRefundService(context, new FixedTimeProvider(expiresAt)).RefundAsync(
                    new UsageRefundCommand(accountId, actorId, tenantId, chargeKey, 20m,
                        "refund at subscription source expiry", refundKey), CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }

        var expiryAttempt = ExpireAsync();
        var refundAttempt = RefundAsync();
        gate.SetResult();
        var race = await Task.WhenAll(expiryAttempt, refundAttempt);
        Assert.All(race, error => Assert.Null(error));

        await using var verify = database.CreateBillingContext();
        var period = await verify.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == accountId && row.PlanCode == "Plus");
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(0m, period.RemainingCredits);
        Assert.Equal(SubscriptionCreditLifecycleService.ExpiredStatus, period.Status);
        Assert.Equal(0m, walletAfter.Balance);
        Assert.Equal(-20m, await verify.UsageLedgerEntries.Where(row =>
            row.CustomerAccountId == accountId && row.OperationCode == "Billing.SubscriptionCreditExpiredRefundOffset")
            .SumAsync(row => row.CreditsCharged));
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.IdempotencyKey == refundKey).ToListAsync());
    }

    private static async Task<(Guid AccountId, Guid TenantId, Guid ActorId, DateTimeOffset ExpiresAt)> SeedPlusAccountAsync(
        PostgreSqlTestDatabase database)
    {
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var expiresAt = Activation.AddDays(90);
        await using var context = database.CreateBillingContext();
        var account = new CustomerAccountRow
        {
            Id = accountId, TenantId = tenantId, UserId = actorId, AccountType = "Individual", BillingMode = "Prepaid",
            SubscriptionPlanCode = "Plus", SubscriptionEffectiveFrom = Activation,
            SubscriptionActivationIsAuthoritative = true, SubscriptionEffectiveTo = expiresAt
        };
        var wallet = new WalletProjectionRow
        {
            CustomerAccountId = accountId, Balance = 0m, ReservedAmount = 0m, UpdatedAt = Activation, Revision = 0
        };
        context.CustomerAccounts.Add(account);
        context.WalletProjections.Add(wallet);
        await context.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(context).InitializeNewWalletAsync(
            account, wallet, Activation, CancellationToken.None);
        await context.SaveChangesAsync();
        return (accountId, tenantId, actorId, expiresAt);
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

    private static async Task SynchronizeInactiveLaunchAsync(
        BillingDbContext context,
        Guid tenantId,
        Guid userId,
        TahlilAppSubscriptionDecision decision,
        Task gate)
    {
        await gate;
        var time = new FixedTimeProvider(Activation);
        await new ExternalSubscriptionSynchronizer(context, new OwnedIdentityBillingProvisioner(context, time), time)
            .SynchronizeAsync(tenantId, userId, decision, 1, CancellationToken.None);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
