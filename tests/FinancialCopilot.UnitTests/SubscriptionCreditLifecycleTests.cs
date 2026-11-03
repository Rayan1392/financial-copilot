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
    public async Task PaidWindowUsesConfiguredAllowanceOnceAndSnapshotsItAcrossRetriesAndRenewal()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var plus = await db.SubscriptionPlans.SingleAsync(row => row.Code == "Plus");
        plus.IncludedCredits = 333m;
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);
        var first = Subscription("sapio_plus", "Plus", InitialAt, InitialAt.AddDays(90));

        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([first], InitialAt), 1, CancellationToken.None);
        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var initialGrant = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus");
        Assert.Equal(333m, initialGrant.GrantedCredits);
        Assert.Equal(333m, initialGrant.AllowanceCredits);
        Assert.Equal(first.ExpiresAt, initialGrant.ExpiresAtUtc);

        plus.IncludedCredits = 444m;
        await db.SaveChangesAsync();
        time.Set(InitialAt.AddDays(30));
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([first], time.GetUtcNow()), 2, CancellationToken.None);
        time.Set(InitialAt.AddDays(60));
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([first], time.GetUtcNow()), 3, CancellationToken.None);
        Assert.Equal(1, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus"));
        Assert.Equal(333m, (await db.SubscriptionCreditPeriods.SingleAsync(row => row.Id == initialGrant.Id)).GrantedCredits);

        var renewal = Subscription("sapio_plus", "Plus", first.ExpiresAt, first.ExpiresAt.AddDays(90));
        time.Set(first.ExpiresAt);
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([renewal], time.GetUtcNow()), 4, CancellationToken.None);
        var periods = await db.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == account.Id && row.PlanCode == "Plus")
            .OrderBy(row => row.StartsAtUtc).ToListAsync();
        Assert.Equal(2, periods.Count);
        Assert.Equal(ExpiredStatus, periods[0].Status);
        Assert.Equal(333m, periods[0].GrantedCredits);
        Assert.Equal(333m, periods[0].AllowanceCredits);
        Assert.Equal(444m, periods[1].GrantedCredits);
        Assert.Equal(444m, periods[1].AllowanceCredits);
        Assert.Equal(444m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
    }

    [Fact]
    public async Task ProWindowUsesConfiguredCatalogAllowanceForItsWholeAuthoritativeWindow()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var configured = await db.SubscriptionPlans.SingleAsync(row => row.Code == "Pro");
        configured.IncludedCredits = 3125m;
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);
        var pro = Subscription("sapio_pro", "Pro", InitialAt, InitialAt.AddDays(90));

        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([pro], InitialAt), 1, CancellationToken.None);
        time.Set(InitialAt.AddDays(30));
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([pro], time.GetUtcNow()), 2, CancellationToken.None);
        time.Set(InitialAt.AddDays(60));
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([pro], time.GetUtcNow()), 3, CancellationToken.None);

        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var period = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Pro");
        Assert.Equal(3125m, period.AllowanceCredits);
        Assert.Equal(3125m, period.GrantedCredits);
        Assert.Equal(pro.ExpiresAt, period.ExpiresAtUtc);
        Assert.Equal(3125m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
    }

    [Fact]
    public async Task FreeProvisioningAssignsInternalExpiryAndGrantEvidenceOnce()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var configuredFree = await db.SubscriptionPlans.SingleAsync(row => row.Code == "Free");
        configuredFree.IncludedCredits = 275m;
        await db.SaveChangesAsync();
        var provisioner = new OwnedIdentityBillingProvisioner(db, new MutableTimeProvider(InitialAt));
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var period = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == account.Id);
        Assert.Equal(275m, period.AllowanceCredits);
        Assert.Equal(275m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(InitialAt, period.AnchorAtUtc);
        Assert.Equal(InitialAt.AddDays(30), period.ExpiresAtUtc);
        configuredFree.IncludedCredits = 300m;
        await db.SaveChangesAsync();
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        Assert.Equal(275m, period.AllowanceCredits);
        Assert.Equal(275m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id)).Balance);
        Assert.Equal(1, await db.FreeCreditGrantEvidence.CountAsync(row => row.UserId == userId));
        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(row => row.CustomerAccountId == account.Id &&
            row.OperationCode == "Billing.SubscriptionCreditGrant"));
    }

    [Fact]
    public async Task FreeGrantExpiresAtPersistedAnchorAndNeverReactivatesAfterExpiry()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);

        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([], InitialAt), 1, CancellationToken.None);
        var account = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var wallet = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == account.Id);
        var originalFree = await db.SubscriptionCreditPeriods.SingleAsync(row =>
            row.CustomerAccountId == account.Id && row.PlanCode == "Free");
        Assert.Equal(InitialAt.AddDays(30), originalFree.ExpiresAtUtc);

        time.Set(InitialAt.AddDays(30));
        Assert.Equal(1, await new SubscriptionCreditLifecycleService(db)
            .ProcessDuePeriodsAsync(10, time.GetUtcNow(), CancellationToken.None));
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([], time.GetUtcNow()), 2, CancellationToken.None);

        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(1, await db.SubscriptionCreditPeriods.CountAsync(row =>
            row.CustomerAccountId == account.Id && row.PlanCode == "Free"));
        Assert.Equal(1, await db.FreeCreditGrantEvidence.CountAsync(row => row.UserId == userId));
        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(row => row.CustomerAccountId == account.Id &&
            row.OperationCode == "Billing.SubscriptionCreditGrant"));
    }

    [Fact]
    public async Task ExhaustedFreeGrantDoesNotReactivateOnRepeatedProvisioningOrSynchronization()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        var accountRow = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var walletRow = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id);
        var account = new FinancialCopilot.Billing.Accounts.CustomerAccount(accountRow.Id, tenantId,
            FinancialCopilot.Billing.Accounts.CustomerAccountType.Individual,
            FinancialCopilot.Billing.Accounts.BillingMode.Prepaid);
        var wallet = new WalletSnapshot(walletRow.CustomerAccountId, walletRow.Balance,
            walletRow.ReservedAmount, walletRow.UpdatedAt, walletRow.Revision);
        await new UsageReservationAuthorizationService(db, new CreditLinePolicyService(), time)
            .ReserveAsync(account, wallet, "AiQuery.FreeExhaustion", 250m, "free-exhaustion-reservation", CancellationToken.None);
        await new UsageFinalizationService(db, time).CommitAsync(new UsageCommitCommand(
            accountRow.Id, userId, tenantId, null, null, "free-exhaustion-reservation", "free-exhaustion-charge",
            new UsageChargeResult(250m, "free-exhaustion-v1", Cached: false)), CancellationToken.None);

        time.Set(InitialAt.AddDays(1));
        await provisioner.EnsureProvisionedAsync(tenantId, userId, CancellationToken.None);
        await new ExternalSubscriptionSynchronizer(db, provisioner, time)
            .SynchronizeAsync(tenantId, userId, Decide([], time.GetUtcNow()), 2, CancellationToken.None);

        Assert.Equal(0m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id)).Balance);
        Assert.Equal(1, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == accountRow.Id && row.PlanCode == "Free"));
        Assert.Equal(1, await db.FreeCreditGrantEvidence.CountAsync(row => row.UserId == userId));
        Assert.Equal(250m, await db.UsageLedgerEntries.Where(row => row.CustomerAccountId == accountRow.Id &&
            row.OperationCode == "Billing.SubscriptionCreditGrant").SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task HeldAllowanceFinalizesAcrossExpiryAndLateRefundDoesNotRestoreSpendableCredits()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);
        var expiresAt = InitialAt.AddDays(90);
        var plus = Subscription("sapio_plus", "Plus", InitialAt, expiresAt);
        await synchronizer.SynchronizeAsync(tenantId, userId, Decide([plus], InitialAt), 1, CancellationToken.None);
        var accountRow = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var walletRow = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id);
        var account = new FinancialCopilot.Billing.Accounts.CustomerAccount(accountRow.Id, tenantId,
            FinancialCopilot.Billing.Accounts.CustomerAccountType.Individual,
            FinancialCopilot.Billing.Accounts.BillingMode.Prepaid);
        var wallet = new WalletSnapshot(walletRow.CustomerAccountId, walletRow.Balance, walletRow.ReservedAmount,
            walletRow.UpdatedAt, walletRow.Revision);
        var reservation = await new UsageReservationAuthorizationService(db, new CreditLinePolicyService(), time)
            .ReserveAsync(account, wallet, "AiQuery.Expiry", 50m, "expiry-held-reservation", CancellationToken.None);
        Assert.Equal(50m, await db.UsageCreditHolds.Where(row => row.UsageReservationId == reservation.Id)
            .SumAsync(row => row.CreditsHeld));

        time.Set(expiresAt.AddMinutes(1));
        var charge = await new UsageFinalizationService(db, time).CommitAsync(new UsageCommitCommand(
            accountRow.Id, userId, tenantId, null, null, "expiry-held-reservation", "expiry-held-charge",
            new UsageChargeResult(20m, "test-v1", Cached: false)), CancellationToken.None);
        Assert.Equal(20m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.LedgerEntry!.Id))
            .SubscriptionCreditsApplied);
        Assert.Empty(await db.UsageCreditHolds.Where(row => row.UsageReservationId == reservation.Id).ToListAsync());

        Assert.Equal(1, await new SubscriptionCreditLifecycleService(db)
            .ProcessDuePeriodsAsync(10, time.GetUtcNow(), CancellationToken.None));
        Assert.Equal(0m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id)).Balance);
        var refund = await new UsageRefundService(db, time).RefundAsync(new UsageRefundCommand(
            accountRow.Id, userId, tenantId, "expiry-held-charge", 20m,
            "expiry-held-late-refund", "late refund after allowance expiry"), CancellationToken.None);
        Assert.Equal(0m, refund.Wallet.Balance);
        Assert.Equal(-20m, await db.UsageLedgerEntries
            .Where(row => row.OperationCode == "Billing.SubscriptionCreditExpiredRefundOffset")
            .SumAsync(row => row.CreditsCharged));
    }

    [Fact]
    public async Task LegacyPendingReservationWithoutSourceHoldSettlesByPersistedWindowBeforeExpiryCleanup()
    {
        await using var db = CreateDbContext();
        var expiresAt = InitialAt.AddDays(90);
        var (account, wallet) = AddAccountAndWallet(db, "Plus", InitialAt, expiresAt);
        wallet.Balance = 260m;
        wallet.ReservedAmount = 50m;
        var grantId = Guid.NewGuid();
        var period = new SubscriptionCreditPeriodRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = account.Id, PlanCode = "Plus",
            SubscriptionIdentity = "legacy-plus-window", IdempotencyKey = "legacy-plus-grant",
            AnchorAtUtc = InitialAt, StartsAtUtc = InitialAt, ExpiresAtUtc = expiresAt,
            GrantedCredits = 260m, AllowanceCredits = 260m, RemainingCredits = 260m,
            GrantLedgerEntryId = grantId, Status = SubscriptionCreditLifecycleService.ActiveStatus,
            CreatedAtUtc = InitialAt
        };
        var reservationId = Guid.NewGuid();
        db.SubscriptionCreditPeriods.Add(period);
        db.UsageReservations.Add(new UsageReservationRow
        {
            Id = reservationId, CustomerAccountId = account.Id,
            IdempotencyKey = "legacy-pending-without-hold", OperationCode = "AiQuery.LegacyPending",
            ReservedCredits = 50m, CreatedAt = InitialAt.AddDays(89), ExpiresAt = expiresAt,
            Status = "Reserved"
        });
        db.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = grantId, CustomerAccountId = account.Id, ActorId = Guid.Empty, TenantId = account.TenantId,
            EntryType = "Adjustment", OperationCode = "Billing.SubscriptionCreditGrant",
            CreditsCharged = 260m, PricingPolicyVersion = "v1", IdempotencyKey = "legacy-plus-grant-ledger",
            OccurredAt = InitialAt, SubscriptionCreditsApplied = 0m
        });
        await db.SaveChangesAsync();

        var time = new MutableTimeProvider(expiresAt.AddMinutes(1));
        var result = await new UsageFinalizationService(db, time).CommitAsync(new UsageCommitCommand(
            account.Id, account.UserId!.Value, account.TenantId, null, null,
            "legacy-pending-without-hold", "legacy-pending-charge",
            new UsageChargeResult(20m, "legacy-pending-v1", Cached: false)), CancellationToken.None);

        Assert.Equal(20m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == result.LedgerEntry!.Id))
            .SubscriptionCreditsApplied);
        Assert.Empty(await db.UsageCreditHolds.Where(row => row.UsageReservationId == reservationId).ToListAsync());
        Assert.Equal(240m, period.RemainingCredits);
        Assert.Equal(1, await new SubscriptionCreditLifecycleService(db)
            .ProcessDuePeriodsAsync(10, expiresAt.AddMinutes(2), CancellationToken.None));
        Assert.Equal(SubscriptionCreditLifecycleService.ExpiredStatus, period.Status);
        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public async Task SpendableWalletReadExcludesExpiredUnheldCreditsBeforeCleanup()
    {
        await using var db = CreateDbContext();
        var (account, wallet) = AddAccountAndWallet(db, "Free", InitialAt, InitialAt.AddDays(30));
        wallet.Balance = 300m;
        wallet.ReservedAmount = 20m;
        var expiredPeriod = new SubscriptionCreditPeriodRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = account.Id, PlanCode = "Plus",
            SubscriptionIdentity = "test-window", IdempotencyKey = "test-window-grant",
            AnchorAtUtc = InitialAt.AddDays(-90), StartsAtUtc = InitialAt.AddDays(-90),
            ExpiresAtUtc = InitialAt.AddDays(-1), GrantedCredits = 100m, AllowanceCredits = 100m,
            RemainingCredits = 100m, GrantLedgerEntryId = Guid.NewGuid(), Status = SubscriptionCreditLifecycleService.ActiveStatus,
            CreatedAtUtc = InitialAt.AddDays(-90)
        };
        db.SubscriptionCreditPeriods.Add(expiredPeriod);
        var reservationId = Guid.NewGuid();
        db.UsageReservations.Add(new UsageReservationRow
        {
            Id = reservationId, CustomerAccountId = account.Id, IdempotencyKey = "held-expiring-credit",
            OperationCode = "test", ReservedCredits = 40m, CreatedAt = InitialAt.AddDays(-2),
            ExpiresAt = InitialAt.AddHours(1), Status = "Reserved"
        });
        db.UsageCreditHolds.Add(new UsageCreditHoldRow
        {
            Id = Guid.NewGuid(), UsageReservationId = reservationId,
            SubscriptionCreditPeriodId = expiredPeriod.Id, CreditsHeld = 40m
        });
        await db.SaveChangesAsync();

        var snapshot = await new WalletProjectionRepository(db, new MutableTimeProvider(InitialAt))
            .GetSpendableSnapshotAsync(account.Id, CancellationToken.None);
        Assert.Equal(240m, snapshot.Balance);
        Assert.Equal(20m, snapshot.ReservedAmount);
    }

    [Fact]
    public async Task NewFreeProvisioning_Grants250OnceAndWritesPeriodLedger()
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
        Assert.Equal(250m, wallet.Balance);
        Assert.Equal(250m, period.GrantedCredits);
        Assert.Equal(250m, period.AllowanceCredits);
        Assert.Equal(InitialAt.AddDays(30), period.ExpiresAtUtc);
        Assert.Equal(250m, grant.CreditsCharged);
        Assert.Equal(1, await db.FreeCreditGrantEvidence.CountAsync(row => row.UserId == userId));
        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.SubscriptionCreditGrant"));
    }

    [Fact]
    public async Task FirstInactiveTahlilAppLaunch_GrantsSpendableFreePeriodOnceWithoutCooldown()
    {
        await using var db = CreateDbContext();
        await SeedPlansAsync(db);
        var time = new MutableTimeProvider(InitialAt);
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var provisioner = new OwnedIdentityBillingProvisioner(db, time);
        var synchronizer = new ExternalSubscriptionSynchronizer(db, provisioner, time);

        // An authoritative inactive response with an empty plans[] array is the first launch.
        await synchronizer.SynchronizeAsync(
            tenantId, userId, Decide([], InitialAt), 1, CancellationToken.None);
        time.Set(InitialAt.AddDays(1));
        // Repeated login/status reconciliation must preserve the original period and grant.
        await synchronizer.SynchronizeAsync(
            tenantId, userId, Decide([], time.GetUtcNow()), 2, CancellationToken.None);

        var accountRow = await db.CustomerAccounts.SingleAsync(row => row.UserId == userId);
        var walletRow = await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id);
        var period = await db.SubscriptionCreditPeriods.SingleAsync(row => row.CustomerAccountId == accountRow.Id);
        var grant = await db.UsageLedgerEntries.SingleAsync(row => row.Id == period.GrantLedgerEntryId);
        Assert.Equal("Free", accountRow.SubscriptionPlanCode);
        Assert.True(accountRow.ExternalSubscriptionManaged);
        Assert.Equal(InitialAt, period.StartsAtUtc);
        Assert.Equal(InitialAt.AddDays(30), period.ExpiresAtUtc);
        Assert.Equal(250m, period.GrantedCredits);
        Assert.Equal(250m, walletRow.Balance);
        Assert.Equal("Billing.SubscriptionCreditGrant", grant.OperationCode);
        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(row =>
            row.CustomerAccountId == accountRow.Id && row.OperationCode == "Billing.SubscriptionCreditGrant"));
        Assert.Empty(await db.PaidExpiryCooldowns.Where(row => row.CustomerAccountId == accountRow.Id).ToListAsync());

        var account = new FinancialCopilot.Billing.Accounts.CustomerAccount(
            accountRow.Id, tenantId,
            FinancialCopilot.Billing.Accounts.CustomerAccountType.Individual,
            FinancialCopilot.Billing.Accounts.BillingMode.Prepaid);
        var wallet = new WalletSnapshot(
            walletRow.CustomerAccountId, walletRow.Balance, walletRow.ReservedAmount, walletRow.UpdatedAt, walletRow.Revision);
        const string reservationKey = "first-free-user-ai-question";
        var reservation = await new UsageReservationAuthorizationService(
                db, new CreditLinePolicyService(), time)
            .ReserveAsync(account, wallet, "AiQuery.Test", 1m, reservationKey, CancellationToken.None);
        var committed = await new UsageFinalizationService(db, time).CommitAsync(
            new UsageCommitCommand(
                accountRow.Id,
                userId,
                tenantId,
                ApiClientId: null,
                ExternalUserId: null,
                reservationKey,
                "first-free-user-ai-question-commit",
                new UsageChargeResult(1m, "test-v1", Cached: false)),
            CancellationToken.None);

        Assert.Equal(reservationKey, reservation.IdempotencyKey);
        Assert.False(committed.AlreadyFinalized);
        Assert.Equal(1m, committed.LedgerEntry!.CreditsCharged);
        Assert.Equal(249m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountRow.Id)).Balance);
        Assert.Equal(249m, (await db.SubscriptionCreditPeriods.SingleAsync(row => row.Id == period.Id)).RemainingCredits);
        Assert.Empty(await db.PaidExpiryCooldowns.Where(row => row.CustomerAccountId == accountRow.Id).ToListAsync());
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
    [InlineData(10, "Plus", 260, 240)]
    [InlineData(25, "Pro", 2600, 225)]
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
        var expiry = InitialAt.AddDays(90);
        var paid = Subscription(targetPlan == "Plus" ? "sapio_plus" : "sapio_pro", targetPlan, InitialAt, expiry);
        await sync.SynchronizeAsync(tenantId, userId, Decide([paid], InitialAt), 1, CancellationToken.None);

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
    public async Task FullWindowExpiryAtExactBoundary_ExpiresRemainderWithoutGrantingAnotherWindow()
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
        Assert.Single(periods);
        Assert.Equal(ExpiredStatus, periods[0].Status);
        Assert.Equal(0m, periods[0].RemainingCredits);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(-160m,
            await db.UsageLedgerEntries
                .Where(row => row.OperationCode == "Billing.SubscriptionCreditExpiry")
                .SumAsync(row => row.CreditsCharged));
        Assert.Equal(100m, (await db.UsageLedgerEntries.SingleAsync(row => row.Id == charge.Id)).CreditsCharged);
    }

    [Fact]
    public async Task ConcurrentDayThirtyWorkers_DoNotCreateAnAdditionalWindowGrant()
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
        Assert.Equal(1, await verify.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == accountId));
        Assert.Equal(1, await verify.UsageLedgerEntries.CountAsync(
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
        var decision = Decide([Subscription("sapio_pro", "Pro", upgradeAt, InitialAt.AddDays(90))], upgradeAt);
        time.Set(upgradeAt);

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
        Assert.Equal(InitialAt.AddDays(90), periods[1].ExpiresAtUtc);
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
        time.Set(InitialAt.AddDays(2));
        await new ExternalSubscriptionSynchronizer(db, new OwnedIdentityBillingProvisioner(db, time), time).SynchronizeAsync(
            account.TenantId, account.UserId!.Value,
            Decide([Subscription("sapio_pro", "Pro", InitialAt.AddDays(2), InitialAt.AddDays(90))], InitialAt.AddDays(2)),
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
        Assert.Equal(260m, wallet.Balance);
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
    public async Task LastPaidExpiryCooldownDoesNotCreateOrReactivateFreeGrant()
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
        Assert.Equal(0, await db.SubscriptionCreditPeriods.CountAsync(row => row.CustomerAccountId == account.Id && row.PlanCode == "Free"));
        Assert.Equal(0m, wallet.Balance);
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
        Assert.Equal(250m, denial.FreePlanCredits);
        Assert.Equal(10m, (await db.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task LateFirstPaidActivation_GrantsOneAllowanceForTheEntireAuthoritativeWindow()
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
        Assert.Equal(activation, period.StartsAtUtc);
        Assert.Equal(activation.AddDays(90), period.ExpiresAtUtc);
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
        Assert.Equal(0m,
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
        Assert.Contains("complete authoritative TahlilApp plan window",
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
            new SubscriptionPlanRow { Code = "Free", Name = "Free", IncludedCredits = 250m, PricingPolicyVersion = "v1" },
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
