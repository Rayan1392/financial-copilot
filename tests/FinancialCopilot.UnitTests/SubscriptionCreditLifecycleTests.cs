using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class SubscriptionCreditLifecycleTests
{
    private static readonly DateTimeOffset InitialAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewFreeProvisioning_Grants25OnceAndWritesPeriodLedger()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);

        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);

        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        var period = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id);
        var grant = await db.UsageLedgerEntries.SingleAsync(row => row.Id == period.GrantLedgerEntryId);
        Assert.Equal(25m, wallet.Balance);
        Assert.Equal(25m, period.GrantedCredits);
        Assert.Equal(InitialAt.AddDays(30), period.ExpiresAtUtc);
        Assert.Equal(25m, grant.CreditsCharged);
        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.SubscriptionCreditGrant"));
    }

    [Theory]
    [InlineData("Plus", 260)]
    [InlineData("Pro", 2600)]
    public async Task FirstPaidActivation_GrantsTheApprovedAllowance(string planCode, decimal credits)
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var (account, wallet) = AddAccountAndWallet(db, planCode, InitialAt, InitialAt.AddDays(60));
        await db.SaveChangesAsync();

        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(
            account,
            wallet,
            InitialAt,
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(credits, wallet.Balance);
        Assert.Equal(credits, (await db.SubscriptionCreditPeriods.SingleAsync()).GrantedCredits);
    }

    [Theory]
    [InlineData(10, "Plus", 260, 15)]
    [InlineData(25, "Pro", 2600, 0)]
    public async Task FreeToPaid_ForfeitsOnlyRemainingFreeCreditsAndPreservesUsageHistory(
        decimal consumed,
        string targetPlan,
        decimal newAllowance,
        decimal expectedForfeiture)
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);

        var charge = await CommitChargeAsync(db, account, wallet, time, consumed);
        var sync = new ExternalSubscriptionSynchronizer(db, provisioner, time);
        var expiry = InitialAt.AddDays(30);
        await sync.SynchronizeAsync(
            tenantId,
            userId,
            new TahlilAppSubscriptionDecision(targetPlan, expiry, "active-paid", InitialAt),
            1,
            CancellationToken.None);

        wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(newAllowance, wallet.Balance);
        Assert.Equal(consumed, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).CreditsCharged);
        Assert.Equal(nameof(UsageLedgerEntryType.Charge),
            (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).EntryType);
        Assert.Equal(expectedForfeiture,
            -await db.UsageLedgerEntries
                .Where(row => row.OperationCode == "Billing.SubscriptionCreditForfeiture")
                .SumAsync(row => row.CreditsCharged));
        Assert.Equal(newAllowance,
            await db.UsageLedgerEntries
                .Where(row => row.OperationCode == "Billing.SubscriptionCreditGrant" &&
                              row.CustomerAccountId == account.Id && row.CreditsCharged == newAllowance)
                .SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task PeriodExpiryAtExactBoundary_ExpiresRemainderAndStartsNextPeriodWithoutCarryover()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Plus", InitialAt, InitialAt.AddDays(60));
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(
            account,
            wallet,
            InitialAt,
            CancellationToken.None);
        await db.SaveChangesAsync();
        time.Set(InitialAt.AddDays(1));
        var charge = await CommitChargeAsync(db, account, wallet, time, 100m);
        var firstPeriod = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(160m, firstPeriod.RemainingCredits);
        Assert.Equal(100m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).SubscriptionCreditsApplied);

        var expiryBoundary = firstPeriod.ExpiresAtUtc;
        time.Set(expiryBoundary);
        var lifecycle = new SubscriptionCreditLifecycleService(db);
        Assert.Equal(1, await lifecycle.ProcessDuePeriodsAsync(100, expiryBoundary, CancellationToken.None));
        Assert.Equal(0, await lifecycle.ProcessDuePeriodsAsync(100, expiryBoundary, CancellationToken.None));

        var periods = await db.SubscriptionCreditPeriods
            .Where(row => row.CustomerAccountId == account.Id)
            .OrderBy(row => row.PeriodNumber)
            .ToListAsync();
        wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(2, periods.Count);
        Assert.Equal(ExpiredStatus, periods[0].Status);
        Assert.Equal(0m, periods[0].RemainingCredits);
        Assert.Equal(expiryBoundary, periods[1].StartsAtUtc);
        Assert.Equal(expiryBoundary.AddDays(30), periods[1].ExpiresAtUtc);
        Assert.Equal(260m, wallet.Balance);
        Assert.Equal(-160m,
            await db.UsageLedgerEntries
                .Where(row => row.OperationCode == "Billing.SubscriptionCreditExpiry")
                .SumAsync(row => row.CreditsCharged));
        Assert.Equal(100m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).CreditsCharged);
    }

    [Fact]
    public async Task ConcurrentRolloverWorkers_CreateOneNextPeriodAndOneGrant()
    {
        var databaseName = $"subscription-credit-concurrency-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var time = new MutableTimeProvider(InitialAt);
        Guid accountId;
        DateTimeOffset boundary;
        await using (var setup = new BillingDbContext(options))
        {
            await SeedPlansAsync(setup);
            var (account, wallet) = AddAccountAndWallet(setup, "Plus", InitialAt, InitialAt.AddDays(90));
            accountId = account.Id;
            await setup.SaveChangesAsync();
            await new SubscriptionCreditLifecycleService(setup).InitializeNewWalletAsync(
                account,
                wallet,
                InitialAt,
                CancellationToken.None);
            await setup.SaveChangesAsync();
            boundary = InitialAt.AddDays(30);
        }

        time.Set(boundary);
        await using var firstContext = new BillingDbContext(options);
        await using var secondContext = new BillingDbContext(options);
        var first = new SubscriptionCreditLifecycleService(firstContext)
            .ProcessDuePeriodsAsync(100, boundary, CancellationToken.None);
        var second = new SubscriptionCreditLifecycleService(secondContext)
            .ProcessDuePeriodsAsync(100, boundary, CancellationToken.None);
        await Task.WhenAll(first, second);

        await using var verify = new BillingDbContext(options);
        Assert.Equal(2, await verify.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == accountId));
        Assert.Equal(2, await verify.UsageLedgerEntries.CountAsync(
            row => row.CustomerAccountId == accountId && row.OperationCode == "Billing.SubscriptionCreditGrant"));
        Assert.Equal(260m, (await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task PlusToPro_TransfersRemainingCreditsAndDuplicateUpgradeDoesNotGrantAgain()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Plus", InitialAt, InitialAt.AddDays(90));
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(
            account,
            wallet,
            InitialAt,
            CancellationToken.None);
        await db.SaveChangesAsync();
        time.Set(InitialAt.AddDays(1));
        await CommitChargeAsync(db, account, wallet, time, 200m);
        var beforeLedgerCount = await db.UsageLedgerEntries.CountAsync();
        var sync = new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time);
        var upgradeAt = InitialAt.AddDays(5);
        var decision = new TahlilAppSubscriptionDecision("Pro", InitialAt.AddDays(90), "active-paid", upgradeAt);

        await sync.SynchronizeAsync(
            account.TenantId,
            account.UserId!.Value,
            decision,
            11,
            CancellationToken.None);
        var afterUpgradeBalance = (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance;
        await sync.SynchronizeAsync(
            account.TenantId,
            account.UserId!.Value,
            decision,
            11,
            CancellationToken.None);

        Assert.Equal("Pro", account.SubscriptionPlanCode);
        Assert.Equal(2660m, afterUpgradeBalance);
        Assert.Equal(beforeLedgerCount + 3, await db.UsageLedgerEntries.CountAsync());
        var periods = await db.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == account.Id)
            .OrderBy(row => row.CreatedAtUtc).ToListAsync();
        Assert.Equal(SubscriptionCreditLifecycleService.TransferredStatus, periods[0].Status);
        Assert.Equal(0m, periods[0].RemainingCredits);
        Assert.Equal(2660m, periods[1].GrantedCredits);
        Assert.Equal(2660m, periods[1].RemainingCredits);
        Assert.Equal(upgradeAt, periods[1].StartsAtUtc);
        Assert.Equal(upgradeAt.AddDays(30), periods[1].ExpiresAtUtc);
        Assert.Equal(-60m, await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditTransferOut")
            .SumAsync(row => row.CreditsCharged));
        Assert.Equal(60m, await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditTransferIn")
            .SumAsync(row => row.CreditsCharged));
        Assert.Single(await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditGrant" && row.CreditsCharged == 2600m).ToListAsync());
    }

    [Fact]
    public async Task PlusToPro_WithNoUnusedCredits_GrantsOnlyTheProAllowance()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Plus", InitialAt, InitialAt.AddDays(90));
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(account, wallet, InitialAt, CancellationToken.None);
        await db.SaveChangesAsync();
        time.Set(InitialAt.AddDays(1));
        await CommitChargeAsync(db, account, wallet, time, 260m);
        await new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time).SynchronizeAsync(
            account.TenantId, account.UserId!.Value,
            new TahlilAppSubscriptionDecision("Pro", InitialAt.AddDays(90), "active-paid", InitialAt.AddDays(2)),
            12, CancellationToken.None);

        Assert.Equal(2600m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(0m, await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditTransferIn")
            .SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task ConcurrentPlusAndPro_TransfersOnlyRemainingPlusCredits_AndIsIdempotent()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Free", InitialAt, InitialAt.AddDays(30));
        await db.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time);
        var plans = new[]
        {
            Subscription("sapio_plus", "Plus", InitialAt.AddDays(-10), InitialAt.AddDays(30)),
            Subscription("sapio_pro", "Pro", InitialAt.AddDays(2), InitialAt.AddDays(45))
        };

        await synchronizer.SynchronizeAsync(account.TenantId, account.UserId!.Value,
            Decide(plans, InitialAt), 1, CancellationToken.None);
        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(260m, wallet.Balance);

        time.Set(InitialAt.AddDays(1));
        await CommitChargeAsync(db, account, wallet, time, 100m);
        time.Set(InitialAt.AddDays(3));
        await synchronizer.SynchronizeAsync(account.TenantId, account.UserId.Value,
            Decide(plans, time.GetUtcNow()), 2, CancellationToken.None);
        var afterUpgrade = wallet.Balance;
        await synchronizer.SynchronizeAsync(account.TenantId, account.UserId.Value,
            Decide(plans, time.GetUtcNow()), 2, CancellationToken.None);

        Assert.Equal("Pro", account.SubscriptionPlanCode);
        Assert.Equal(2760m, afterUpgrade);
        Assert.Equal(afterUpgrade, wallet.Balance);
        Assert.Equal(2, await db.ExternalPlanSubscriptions.CountAsync());
        var plus = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus");
        var pro = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Pro");
        Assert.Equal(SubscriptionCreditLifecycleService.TransferredStatus, plus.Status);
        Assert.Equal(0m, plus.RemainingCredits);
        Assert.Equal(2760m, pro.GrantedCredits);
        Assert.Equal(-160m, await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditTransferOut")
            .SumAsync(row => row.CreditsCharged));
        Assert.Equal(160m, await db.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.SubscriptionCreditTransferIn")
            .SumAsync(row => row.CreditsCharged));
        Assert.Equal(2, await db.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.SubscriptionCreditGrant"));
    }

    [Fact]
    public async Task ProExpiryWhilePlusRemainsActive_DoesNotStartCooldown()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Free", InitialAt, InitialAt.AddDays(30));
        await db.SaveChangesAsync();
        var subscriptions = new[]
        {
            Subscription("sapio_plus", "Plus", InitialAt.AddDays(-5), InitialAt.AddDays(20)),
            Subscription("sapio_pro", "Pro", InitialAt.AddDays(-2), InitialAt.AddDays(1))
        };
        var sync = new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time);
        await sync.SynchronizeAsync(account.TenantId, account.UserId!.Value,
            Decide(subscriptions, InitialAt), 1, CancellationToken.None);
        Assert.Equal("Pro", account.SubscriptionPlanCode);

        time.Set(InitialAt.AddDays(2));
        await sync.SynchronizeAsync(account.TenantId, account.UserId.Value,
            Decide(subscriptions, time.GetUtcNow()), 2, CancellationToken.None);

        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(InitialAt.AddDays(20), account.SubscriptionEffectiveTo);
        Assert.Empty(await db.PaidExpiryCooldowns.ToListAsync());
        Assert.Equal(2860m, wallet.Balance);
    }

    [Fact]
    public async Task AuthoritativeEmptyPlanSet_RevokesUnusedPaidCreditsOnceAndAnchorsCooldownAtFirstObservation()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);
        var pro = Subscription("sapio_pro", "Pro", InitialAt, InitialAt.AddDays(60));

        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([pro], InitialAt), 1, CancellationToken.None);
        var paidPeriod = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Pro");
        var charge = await CommitChargeAsync(db, account, wallet, time, 1m);
        Assert.Equal(2599m, wallet.Balance);
        var pendingKey = "reservation-before-revocation";
        wallet.ReservedAmount = 5m;
        db.UsageReservations.Add(new UsageReservationRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = account.Id, IdempotencyKey = pendingKey,
            OperationCode = "AiQuery.Test", ReservedCredits = 5m, CreatedAt = InitialAt.AddDays(1),
            ExpiresAt = InitialAt.AddDays(1).AddMinutes(5), Status = nameof(UsageReservationStatus.Reserved)
        });
        await db.SaveChangesAsync();

        var revokedAt = InitialAt.AddDays(2);
        time.Set(revokedAt);
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([], revokedAt), 2, CancellationToken.None);

        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        Assert.Equal(nameof(UsageReservationStatus.Released),
            (await db.UsageReservations.SingleAsync(row => row.IdempotencyKey == pendingKey)).Status);
        Assert.Equal(1m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).CreditsCharged);
        Assert.Equal(-2599m, await db.UsageLedgerEntries
            .Where(row => row.OperationCode == "Billing.SubscriptionCreditForfeiture" && row.RelatedEntryId == paidPeriod.GrantLedgerEntryId)
            .SumAsync(row => row.CreditsCharged));
        Assert.Equal(0, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free" && row.Status == SubscriptionCreditLifecycleService.ActiveStatus));
        var cooldown = await db.PaidExpiryCooldowns.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(revokedAt, cooldown.LastPaidExpiresAtUtc);
        Assert.Equal(revokedAt, cooldown.RevocationObservedAtUtc);
        Assert.Equal(revokedAt.AddDays(30), cooldown.CooldownUntilUtc);

        time.Set(revokedAt.AddDays(1));
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([], time.GetUtcNow()), 3, CancellationToken.None);
        var repeated = await db.PaidExpiryCooldowns.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(revokedAt, repeated.RevocationObservedAtUtc);
        Assert.Equal(revokedAt.AddDays(30), repeated.CooldownUntilUtc);
        Assert.Equal(-2599m, await db.UsageLedgerEntries
            .Where(row => row.OperationCode == "Billing.SubscriptionCreditForfeiture" && row.RelatedEntryId == paidPeriod.GrantLedgerEntryId)
            .SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task PlusExpiryWhileProRemainsActive_DoesNotStartCooldown()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Free", InitialAt, InitialAt.AddDays(30));
        await db.SaveChangesAsync();
        var subscriptions = new[]
        {
            Subscription("sapio_plus", "Plus", InitialAt.AddDays(-2), InitialAt.AddDays(1)),
            Subscription("sapio_pro", "Pro", InitialAt.AddDays(-5), InitialAt.AddDays(20))
        };
        var sync = new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time);
        await sync.SynchronizeAsync(account.TenantId, account.UserId!.Value,
            Decide(subscriptions, InitialAt), 1, CancellationToken.None);

        time.Set(InitialAt.AddDays(2));
        await sync.SynchronizeAsync(account.TenantId, account.UserId.Value,
            Decide(subscriptions, time.GetUtcNow()), 2, CancellationToken.None);

        Assert.Equal("Pro", account.SubscriptionPlanCode);
        Assert.Equal(InitialAt.AddDays(20), account.SubscriptionEffectiveTo);
        Assert.Empty(await db.PaidExpiryCooldowns.ToListAsync());
        Assert.Equal(2860m, wallet.Balance);
    }

    [Fact]
    public async Task LastPaidExpiry_StartsDurableCooldown_ThenResumesFreeGrantOnce()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var expiry = InitialAt.AddDays(5);
        var time = new MutableTimeProvider(InitialAt);
        var (account, wallet) = AddAccountAndWallet(db, "Free", InitialAt, expiry);
        await db.SaveChangesAsync();
        var synchronizer = new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time);
        var paid = new[] { Subscription("sapio_plus", "Plus", InitialAt.AddDays(-10), expiry) };
        await synchronizer.SynchronizeAsync(account.TenantId, account.UserId!.Value,
            Decide(paid, InitialAt), 1, CancellationToken.None);

        time.Set(expiry);
        await new SubscriptionCreditLifecycleService(db).EnsureCurrentPeriodTrackedAsync(
            account, wallet, expiry, CancellationToken.None);
        await db.SaveChangesAsync();
        var cooldown = await db.PaidExpiryCooldowns.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(expiry, cooldown.LastPaidExpiresAtUtc);
        Assert.Equal(expiry.AddDays(30), cooldown.CooldownUntilUtc);
        Assert.True(await new SubscriptionCreditLifecycleService(db)
            .IsPaidExpiryCooldownActiveAsync(account.Id, expiry, CancellationToken.None));
        Assert.Equal(0, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free"));

        time.Set(cooldown.CooldownUntilUtc);
        await new SubscriptionCreditLifecycleService(db).EnsureCurrentPeriodTrackedAsync(
            account, wallet, time.GetUtcNow(), CancellationToken.None);
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).EnsureCurrentPeriodTrackedAsync(
            account, wallet, time.GetUtcNow(), CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.False(await new SubscriptionCreditLifecycleService(db)
            .IsPaidExpiryCooldownActiveAsync(account.Id, time.GetUtcNow(), CancellationToken.None));
        Assert.Equal(1, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free"));
        Assert.Equal(25m, wallet.Balance);
    }

    [Fact]
    public async Task PaidExpiryCooldown_DeniesReservationEvenWhenWalletHasCredits()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var expiry = InitialAt.AddDays(-1);
        var now = InitialAt;
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        db.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId, TenantId = tenantId, UserId = Guid.NewGuid(), AccountType = "Individual",
            BillingMode = "Prepaid", SubscriptionPlanCode = "Free", ExternalSubscriptionManaged = true
        });
        db.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = accountId, Balance = 10m, ReservedAmount = 0m, UpdatedAt = now, Revision = 0
        });
        db.ExternalPlanSubscriptions.Add(SubscriptionRow(accountId, "sapio_plus", "Plus", InitialAt.AddDays(-31), expiry));
        await db.SaveChangesAsync();

        var account = new FinancialCopilot.Billing.Accounts.CustomerAccount(
            accountId, tenantId, FinancialCopilot.Billing.Accounts.CustomerAccountType.Individual,
            FinancialCopilot.Billing.Accounts.BillingMode.Prepaid);
        var wallet = new WalletSnapshot(accountId, 10m, 0m, now, 0);
        var service = new UsageReservationAuthorizationService(db, new CreditLinePolicyService(), new MutableTimeProvider(now));

        var denial = await Assert.ThrowsAsync<FinancialCopilot.Billing.InsufficientCreditException>(() => service.ReserveAsync(
            account, wallet, "AiQuery.Test", 1m, "cooldown-denied", CancellationToken.None));

        Assert.Empty(await db.UsageReservations.ToListAsync());
        var cooldown = await db.PaidExpiryCooldowns.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(expiry, cooldown.LastPaidExpiresAtUtc);
        Assert.Equal(expiry.AddDays(30), cooldown.CooldownUntilUtc);
        Assert.Equal(FinancialCopilot.Billing.InsufficientCreditException.SubscriptionCooldownActiveCode, denial.ErrorCode);
        Assert.Equal(cooldown.CooldownUntilUtc, denial.CooldownUntilUtc);
        Assert.Equal(25m, denial.FreePlanCredits);
        Assert.Equal(10m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task LateFirstPaidActivation_GrantsOnlyTheCurrentlyActiveAnchoredPeriod()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var activation = InitialAt.AddDays(-45);
        var now = InitialAt;
        var (account, wallet) = AddAccountAndWallet(db, "Plus", activation, activation.AddDays(90));
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(account, wallet, now, CancellationToken.None);
        await db.SaveChangesAsync();

        var period = await db.SubscriptionCreditPeriods.SingleAsync();
        Assert.Equal(activation.AddDays(30), period.StartsAtUtc);
        Assert.Equal(activation.AddDays(60), period.ExpiresAtUtc);
        Assert.Equal(260m, period.GrantedCredits);
        Assert.Equal(260m, wallet.Balance);
    }

    [Fact]
    public async Task ProToPlusIsRejectedAndExpiredProBecomesFreeWithoutPreservingPaidCapabilities()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var (account, wallet) = AddAccountAndWallet(db, "Pro", InitialAt, InitialAt.AddDays(30));
        await db.SaveChangesAsync();
        await new SubscriptionCreditLifecycleService(db).InitializeNewWalletAsync(
            account,
            wallet,
            InitialAt,
            CancellationToken.None);
        await db.SaveChangesAsync();
        var sync = new ExternalSubscriptionSynchronizer(db, provisioner, time);

        await sync.SynchronizeAsync(
            account.TenantId,
            account.UserId!.Value,
            new TahlilAppSubscriptionDecision("Plus", InitialAt.AddDays(30), "downgrade-request"),
            3,
            CancellationToken.None);
        Assert.Equal("Free", account.SubscriptionPlanCode);

        time.Set(InitialAt.AddDays(30));
        await sync.SynchronizeAsync(
            account.TenantId,
            account.UserId!.Value,
            new TahlilAppSubscriptionDecision("Free", null, "expired"),
            4,
            CancellationToken.None);
        Assert.Equal("Free", account.SubscriptionPlanCode);
        Assert.Null(account.SubscriptionEffectiveTo);
        Assert.Equal(1, await new SubscriptionCreditLifecycleService(db)
            .ProcessDuePeriodsAsync(100, time.GetUtcNow(), CancellationToken.None));
        Assert.Equal(25m,
            (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(-2600m,
            await db.UsageLedgerEntries
                .Where(row => row.OperationCode == "Billing.SubscriptionCreditExpiry")
                .SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task LegacyWalletAndHistoricalLedgerArePreservedUntilReconciliation()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var account = new CustomerAccountRow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            AccountType = "Individual",
            BillingMode = "Prepaid",
            SubscriptionPlanCode = "Free",
            SubscriptionEffectiveFrom = InitialAt
        };
        db.CustomerAccounts.Add(account);
        db.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = account.Id,
            Balance = 37m,
            ReservedAmount = 0,
            UpdatedAt = InitialAt,
            Revision = 0
        });
        var historicalCharge = new UsageLedgerEntryRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = account.Id,
            ActorId = Guid.NewGuid(),
            TenantId = tenantId,
            EntryType = nameof(UsageLedgerEntryType.Charge),
            OperationCode = "AiQuery.Test",
            CreditsCharged = 4m,
            PricingPolicyVersion = "v1",
            IdempotencyKey = "legacy-charge-1",
            OccurredAt = InitialAt
        };
        db.UsageLedgerEntries.Add(historicalCharge);
        await db.SaveChangesAsync();

        await new OwnedIdentityBillingProvisioner(db, time).EnsureProvisionedAsync(
            tenantId,
            userId,
            CancellationToken.None);
        await new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time)
            .SynchronizeAsync(
                tenantId,
                userId,
                new TahlilAppSubscriptionDecision("Plus", InitialAt.AddDays(30), "active-paid"),
                1,
                CancellationToken.None);

        Assert.Equal("Plus", account.SubscriptionPlanCode);
        Assert.Equal(37m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(4m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == historicalCharge.Id)).CreditsCharged);
        Assert.Equal(0, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id));
        Assert.Contains("legacy wallet reconciliation",
            (await db.AdminAudits.SingleAsync(row => row.TargetId == account.Id.ToString())).Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<UsageLedgerEntryRow> CommitChargeAsync(
        BillingDbContext db,
        CustomerAccountRow account,
        WalletProjectionRow wallet,
        MutableTimeProvider time,
        decimal credits)
    {
        var now = time.GetUtcNow();
        var reservationKey = $"test-reservation-{Guid.NewGuid():N}";
        wallet.ReservedAmount += credits;
        wallet.Revision++;
        db.UsageReservations.Add(new UsageReservationRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = account.Id,
            IdempotencyKey = reservationKey,
            OperationCode = "AiQuery.Test",
            ReservedCredits = credits,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(4),
            Status = nameof(UsageReservationStatus.Reserved)
        });
        await db.SaveChangesAsync();

        var result = await new UsageFinalizationService(db, time).CommitAsync(
            new UsageCommitCommand(
                account.Id,
                Guid.NewGuid(),
                account.TenantId,
                ApiClientId: null,
                ExternalUserId: null,
                reservationKey,
                $"test-charge-{Guid.NewGuid():N}",
                new UsageChargeResult(credits, "test-v1", Cached: false)),
            CancellationToken.None);
        Assert.False(result.AlreadyFinalized);
        Assert.Equal(credits, result.LedgerEntry!.CreditsCharged);
        return await db.UsageLedgerEntries.SingleAsync(row => row.Id == result.LedgerEntry.Id);
    }

    private static (CustomerAccountRow Account, WalletProjectionRow Wallet) AddAccountAndWallet(
        BillingDbContext db,
        string planCode,
        DateTimeOffset effectiveFrom,
        DateTimeOffset effectiveTo)
    {
        var account = new CustomerAccountRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            AccountType = "Individual",
            BillingMode = "Prepaid",
            SubscriptionPlanCode = planCode,
            SubscriptionEffectiveFrom = effectiveFrom,
            SubscriptionActivationIsAuthoritative = true,
            SubscriptionEffectiveTo = effectiveTo
        };
        var wallet = new WalletProjectionRow
        {
            CustomerAccountId = account.Id,
            Balance = 0,
            ReservedAmount = 0,
            UpdatedAt = effectiveFrom,
            Revision = 0
        };
        db.CustomerAccounts.Add(account);
        db.WalletProjections.Add(wallet);
        return (account, wallet);
    }

    private static FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppPlanSubscription Subscription(
        string slug, string plan, DateTimeOffset startsAt, DateTimeOffset expiresAt) =>
        new(slug, plan, startsAt, expiresAt,
            $"{slug}:{startsAt.ToUnixTimeMilliseconds()}:{expiresAt.ToUnixTimeMilliseconds()}");

    private static ExternalPlanSubscriptionRow SubscriptionRow(
        Guid accountId, string slug, string plan, DateTimeOffset startsAt, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(), CustomerAccountId = accountId,
        IdentityKey = $"{slug}:{startsAt.ToUnixTimeMilliseconds()}:{expiresAt.ToUnixTimeMilliseconds()}",
        PlanCode = plan, StartsAtUtc = startsAt, ExpiresAtUtc = expiresAt
    };

    private static FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppSubscriptionDecision Decide(
        IReadOnlyList<FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppPlanSubscription> plans,
        DateTimeOffset now)
    {
        var map = new Dictionary<string, string> { ["sapio_plus"] = "Plus", ["sapio_pro"] = "Pro" };
        return FinancialCopilot.Domain.Identity.TahlilApp.TahlilAppSubscriptionPolicy.Decide(
            plans.Select(item => (item.Slug, item.StartsAt, item.ExpiresAt)).ToArray(), now, map);
    }

    private static async Task SeedPlansAsync(BillingDbContext db)
    {
        db.SubscriptionPlans.AddRange(
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 25m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Plus", Name = "Plus", IncludedCredits = 260m, PricingPolicyVersion = "v1" },
            new SubscriptionPlanRow { Code = "Pro", Name = "Pro", IncludedCredits = 2600m, PricingPolicyVersion = "v1" });
        await db.SaveChangesAsync();
    }

    private static BillingDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase($"subscription-credit-{Guid.NewGuid():N}")
            .Options);

    private const string ExpiredStatus = SubscriptionCreditLifecycleService.ExpiredStatus;

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Set(DateTimeOffset value) => current = value;
    }
}
