using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class TahlilAppMaxUsagePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task MaxZeroBalance_StoresUnlimitedUsageWithoutWalletDebitOrRefundCredits()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature152_max");
        var now = DateTimeOffset.UtcNow;
        var (accountId, tenantId, actorId, sourceId) = await SeedMaxAsync(database, now.AddDays(10));
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var wallet = new WalletSnapshot(accountId, 0m, 0m, now, 0);
        UsageReservation reservation;
        await using (var reserveContext = database.CreateBillingContext())
        {
            var service = new UsageReservationAuthorizationService(
                reserveContext, new CreditLinePolicyService(), new FixedTimeProvider(now));
            reservation = await service.ReserveAsync(
                account, wallet, "AiQuery.Scanner", 4m, "max-zero-balance-reservation", CancellationToken.None, "payload-hash");
            var upgraded = await service.EnsureReservedCapacityAsync(account, reservation.IdempotencyKey, 40m, CancellationToken.None);
            Assert.Equal(0m, upgraded.ReservedCredits);
            Assert.Equal(CreditQuotaMode.Unlimited, upgraded.QuotaMode);
        }

        await using (var finalizeContext = database.CreateBillingContext())
        {
            var finalization = new UsageFinalizationService(finalizeContext, new FixedTimeProvider(now.AddMinutes(1)));
            var result = await finalization.CommitAsync(new UsageCommitCommand(
                accountId, actorId, tenantId, null, "tahlil-user-max", reservation.IdempotencyKey,
                "max-zero-balance-charge", new UsageChargeResult(9m, "v1", false),
                ProviderName: "Provider", ModelName: "Model", PromptTokens: 10,
                CompletionTokens: 5, TotalTokens: 15, EstimatedCost: 0.03m), CancellationToken.None);

            Assert.Equal(0m, result.Wallet.Balance);
            Assert.Equal(0m, result.Wallet.ReservedAmount);
            Assert.Equal(0m, result.LedgerEntry!.CreditsCharged);
            Assert.Equal(CreditQuotaMode.Unlimited, result.LedgerEntry.QuotaMode);
            Assert.Equal(sourceId, result.LedgerEntry.SourceSubscriptionId);
            Assert.Equal(ToPostgresTimestamp(now.AddDays(10)), result.LedgerEntry.SourceSubscriptionExpiresAtUtc);
            Assert.Equal(15, result.LedgerEntry.TotalTokens);
            Assert.Equal(0.03m, result.LedgerEntry.EstimatedCost);
        }

        await using (var refundContext = database.CreateBillingContext())
        {
            var refunds = new UsageRefundService(refundContext, new FixedTimeProvider(now.AddMinutes(2)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => refunds.RefundAsync(
                new UsageRefundCommand(accountId, actorId, tenantId, "max-zero-balance-charge", 1m,
                    "No numeric Max credits exist to refund.", "max-invalid-refund"), CancellationToken.None));
        }

        await using var verify = database.CreateBillingContext();
        var persistedWallet = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(0m, persistedWallet.Balance);
        Assert.Equal(0m, persistedWallet.ReservedAmount);
        Assert.Empty(await verify.UsageCreditAllocations.ToListAsync());
        Assert.Empty(await verify.TopUpUsageCreditAllocations.ToListAsync());
        Assert.Equal("Unlimited", (await verify.UsageLedgerEntries.SingleAsync(row => row.IdempotencyKey == "max-zero-balance-charge")).QuotaMode);
    }

    [SkippableFact]
    public async Task CancellingUnlimitedReservationDoesNotMutateNumericWalletOrCreateCreditEntries()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature152_max");
        var now = DateTimeOffset.UtcNow;
        var (accountId, tenantId, _, _) = await SeedMaxAsync(database, now.AddDays(10));
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "max-cancel-without-wallet-change";
        await using (var reserve = database.CreateBillingContext())
            await new UsageReservationAuthorizationService(reserve, new CreditLinePolicyService(), new FixedTimeProvider(now))
                .ReserveAsync(account, new WalletSnapshot(accountId, 0m, 0m, now, 0), "AiQuery.Scanner", 4m,
                    reservationKey, CancellationToken.None);

        await using (var release = database.CreateBillingContext())
            await new UsageFinalizationService(release, new FixedTimeProvider(now.AddMinutes(1))).ReleaseAsync(
                new UsageReleaseCommand(accountId, tenantId, reservationKey, "User cancelled the request."), CancellationToken.None);

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        var reservation = await verify.UsageReservations.SingleAsync(row => row.IdempotencyKey == reservationKey);
        Assert.Equal(0m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(0m, reservation.ReservedCredits);
        Assert.Equal("Released", reservation.Status);
        Assert.Empty(await verify.UsageLedgerEntries.Where(row => row.CustomerAccountId == accountId).ToListAsync());
        Assert.Empty(await verify.UsageCreditAllocations.ToListAsync());
        Assert.Empty(await verify.TopUpUsageCreditAllocations.ToListAsync());
    }

    [SkippableFact]
    public async Task MaxReservationMayFinishAfterNaturalExpiryButNewAdmissionAndRevokedFinalizationAreDenied()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature152_max");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(1);
        var (accountId, tenantId, actorId, sourceId) = await SeedMaxAsync(database, expiresAt);
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var wallet = new WalletSnapshot(accountId, 0m, 0m, now, 0);
        UsageReservation reservation;
        await using (var reserveContext = database.CreateBillingContext())
        {
            var service = new UsageReservationAuthorizationService(reserveContext, new CreditLinePolicyService(), new FixedTimeProvider(now));
            reservation = await service.ReserveAsync(account, wallet, "AiQuery.Scanner", 2m,
                "max-expiry-reservation", CancellationToken.None);
        }

        await using (var finalizeContext = database.CreateBillingContext())
        {
            var finalization = new UsageFinalizationService(finalizeContext, new FixedTimeProvider(expiresAt.AddSeconds(1)));
            var chargeCommand = new UsageCommitCommand(
                accountId, actorId, tenantId, null, null, reservation.IdempotencyKey, "max-after-expiry-charge",
                new UsageChargeResult(3m, "v1", false));
            var result = await finalization.CommitAsync(chargeCommand, CancellationToken.None);
            Assert.Equal(0m, result.LedgerEntry!.CreditsCharged);
            Assert.Equal(sourceId, result.LedgerEntry.SourceSubscriptionId);

            var changedWindow = await finalizeContext.ExternalPlanSubscriptions.SingleAsync(row => row.Id == sourceId);
            changedWindow.IsRevoked = true;
            changedWindow.RevokedAtUtc = expiresAt.AddMinutes(1);
            var changedAccount = await finalizeContext.CustomerAccounts.SingleAsync(row => row.Id == accountId);
            changedAccount.SubscriptionPlanCode = "Free";
            await finalizeContext.SaveChangesAsync();

            var replay = await new UsageFinalizationService(finalizeContext, new FixedTimeProvider(expiresAt.AddMinutes(2)))
                .CommitAsync(chargeCommand, CancellationToken.None);
            Assert.True(replay.AlreadyFinalized);
            Assert.Equal(CreditQuotaMode.Unlimited, replay.Reservation.QuotaMode);
            Assert.Equal(sourceId, replay.LedgerEntry!.SourceSubscriptionId);
            Assert.Equal(0m, replay.LedgerEntry.CreditsCharged);
        }

        await using (var postExpiryContext = database.CreateBillingContext())
        {
            var service = new UsageReservationAuthorizationService(
                postExpiryContext, new CreditLinePolicyService(), new FixedTimeProvider(expiresAt));
            await Assert.ThrowsAsync<InsufficientCreditException>(() => service.ReserveAsync(
                account, wallet, "AiQuery.Scanner", 2m, "max-after-expiry-new-request", CancellationToken.None));
        }

        var secondWindowExpiry = now.AddDays(15);
        var (revokedAccountId, revokedTenantId, revokedActorId, revokedSourceId) = await SeedMaxAsync(database, secondWindowExpiry);
        var revokedAccount = new CustomerAccount(revokedAccountId, revokedTenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        UsageReservation revokedReservation;
        await using (var reserveContext = database.CreateBillingContext())
        {
            revokedReservation = await new UsageReservationAuthorizationService(
                    reserveContext, new CreditLinePolicyService(), new FixedTimeProvider(now))
                .ReserveAsync(revokedAccount, new WalletSnapshot(revokedAccountId, 0m, 0m, now, 0),
                    "AiQuery.Scanner", 2m, "max-revocation-reservation", CancellationToken.None);
        }
        await using (var revokeContext = database.CreateBillingContext())
        {
            var accountRow = await revokeContext.CustomerAccounts.SingleAsync(row => row.Id == revokedAccountId);
            _ = await revokeContext.ExternalPlanSubscriptions.SingleAsync(row => row.Id == revokedSourceId);
            var walletRow = await revokeContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == revokedAccountId);
            await new SubscriptionCreditLifecycleService(revokeContext).ApplyExternalPlanSnapshotAsync(
                accountRow, walletRow, [], now.AddMinutes(3), CancellationToken.None);
            await revokeContext.SaveChangesAsync();
        }
        await using var revokedFinalizationContext = database.CreateBillingContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UsageFinalizationService(
                revokedFinalizationContext, new FixedTimeProvider(now.AddMinutes(4)))
            .CommitAsync(new UsageCommitCommand(revokedAccountId, revokedActorId, revokedTenantId, null, null,
                revokedReservation.IdempotencyKey, "max-revoked-charge", new UsageChargeResult(2m, "v1", false)),
                CancellationToken.None));
        await using var verifyRevocation = database.CreateBillingContext();
        Assert.Empty(await verifyRevocation.FreeCreditGrantEvidence.Where(row => row.CustomerAccountId == revokedAccountId).ToListAsync());
        Assert.Empty(await verifyRevocation.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == revokedAccountId && row.PlanCode == "Free").ToListAsync());
    }

    [SkippableFact]
    public async Task ConcurrentMaxReservationsWithIndependentConnectionsDoNotReserveOrDebitNumericWallet()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature152_max");
        var now = DateTimeOffset.UtcNow;
        var (accountId, tenantId, _, _) = await SeedMaxAsync(database, now.AddDays(30));
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var wallet = new WalletSnapshot(accountId, 0m, 0m, now, 0);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<UsageReservation> Reserve(string key)
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await gate.Task;
            return await new UsageReservationAuthorizationService(context, new CreditLinePolicyService(), new FixedTimeProvider(now))
                .ReserveAsync(account, wallet, "AiQuery.Scanner", 6m, key, CancellationToken.None);
        }

        var first = Reserve("max-concurrent-1");
        var second = Reserve("max-concurrent-2");
        gate.SetResult();
        var reservations = await Task.WhenAll(first, second);

        Assert.All(reservations, reservation =>
        {
            Assert.Equal(CreditQuotaMode.Unlimited, reservation.QuotaMode);
            Assert.Equal(0m, reservation.ReservedCredits);
        });
        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(0m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(0, walletAfter.Revision);
        Assert.Equal(2, await verify.UsageReservations.CountAsync(row => row.CustomerAccountId == accountId));
    }

    [SkippableFact]
    public async Task MaxOverlapIsUnlimitedThenLatestNonMaxPlanUsesAllUnexpiredSourcesWithoutExtendingThem()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature152_overlap");
        var now = ToPostgresTimestamp(DateTimeOffset.UtcNow);
        var plusExpires = ToPostgresTimestamp(now.AddMinutes(4));
        var maxExpires = ToPostgresTimestamp(now.AddMinutes(2));
        var proExpires = ToPostgresTimestamp(now.AddDays(30));
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var topUpId = Guid.NewGuid();
        var topUpLedgerId = Guid.NewGuid();
        var subscriptions = new[]
        {
            new TahlilAppPlanSubscription("sapio_plus", "Plus", now.AddDays(-10), plusExpires, "sapio_plus:plus-window"),
            new TahlilAppPlanSubscription("sapio_pro", "Pro", now.AddDays(-5), proExpires, "sapio_pro:pro-window"),
            new TahlilAppPlanSubscription("sapio_max", "Max", now.AddDays(-1), maxExpires, "sapio_max:max-window")
        };
        await using (var seed = database.CreateBillingContext())
        {
            var accountRow = new CustomerAccountRow
            {
                Id = accountId, TenantId = tenantId, UserId = actorId, AccountType = "Individual", BillingMode = "Prepaid",
                SubscriptionPlanCode = "Max", ExternalSubscriptionManaged = true,
                SubscriptionActivationIsAuthoritative = true
            };
            seed.CustomerAccounts.Add(accountRow);
            var walletRow = new WalletProjectionRow
            {
                CustomerAccountId = accountId, Balance = 0m, ReservedAmount = 0m, UpdatedAt = now, Revision = 0
            };
            seed.WalletProjections.Add(walletRow);
            await new SubscriptionCreditLifecycleService(seed).ApplyExternalPlanSnapshotAsync(
                accountRow, walletRow, subscriptions, now, CancellationToken.None);
            var plusId = seed.ExternalPlanSubscriptions.Local.Single(row => row.IdentityKey == subscriptions[0].IdentityKey).Id;
            seed.TopUpCreditSources.Add(new TopUpCreditSourceRow
            {
                Id = topUpId, CustomerAccountId = accountId, ExternalPlanSubscriptionId = plusId,
                PurchaseId = Guid.NewGuid(), GrantLedgerEntryId = topUpLedgerId,
                GrantedCredits = 100m, RemainingCredits = 100m, ExpiresAtUtc = plusExpires,
                CreatedAtUtc = now, Revision = 1
            });
            seed.UsageLedgerEntries.Add(new UsageLedgerEntryRow
            {
                Id = topUpLedgerId, CustomerAccountId = accountId, ActorId = actorId, TenantId = tenantId,
                EntryType = "Adjustment", OperationCode = "Billing.TahlilAppCreditTopUpGrant",
                CreditsCharged = 100m, PricingPolicyVersion = "tahlilapp-topup-v1",
                IdempotencyKey = $"overlap-topup-grant:{topUpLedgerId:N}", OccurredAt = now,
                AllocationSource = "tahlilapp-topup"
            });
            walletRow.Balance += 100m;
            walletRow.Revision++;
            await seed.SaveChangesAsync();

            var periods = await seed.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == accountId).ToListAsync();
            Assert.Equal(260m, periods.Single(row => row.PlanCode == "Plus").RemainingCredits);
            Assert.Equal(plusExpires, periods.Single(row => row.PlanCode == "Plus").ExpiresAtUtc);
            Assert.Equal(2600m, periods.Single(row => row.PlanCode == "Pro").RemainingCredits);
            Assert.Equal(proExpires, periods.Single(row => row.PlanCode == "Pro").ExpiresAtUtc);
            Assert.Equal("Max", accountRow.SubscriptionPlanCode);
            Assert.Equal(maxExpires, accountRow.SubscriptionEffectiveTo);
        }

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string unlimitedReservationKey = "overlap-max-unlimited-reservation";
        await using (var reserveMax = database.CreateBillingContext())
        {
            var wallet = await reserveMax.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            var reservation = await new UsageReservationAuthorizationService(
                    reserveMax, new CreditLinePolicyService(), new FixedTimeProvider(now))
                .ReserveAsync(account, new WalletSnapshot(accountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Scanner", 4000m, unlimitedReservationKey, CancellationToken.None);
            Assert.Equal(CreditQuotaMode.Unlimited, reservation.QuotaMode);
            Assert.Equal(0m, reservation.ReservedCredits);
        }
        await using (var finishMax = database.CreateBillingContext())
        {
            var result = await new UsageFinalizationService(finishMax, new FixedTimeProvider(now.AddMinutes(1)))
                .CommitAsync(new UsageCommitCommand(accountId, actorId, tenantId, null, null,
                    unlimitedReservationKey, "overlap-max-unlimited-charge", new UsageChargeResult(50m, "v1", false)),
                    CancellationToken.None);
            Assert.Equal(CreditQuotaMode.Unlimited, result.LedgerEntry!.QuotaMode);
            Assert.Equal(0m, result.LedgerEntry.CreditsCharged);
            Assert.Equal(2960m, result.Wallet.Balance);
        }

        var afterMax = now.AddMinutes(3);
        const string finiteReservationKey = "overlap-pro-finite-reservation";
        await using (var reserveFinite = database.CreateBillingContext())
        {
            var plan = await new SubscriptionPlanRepository(reserveFinite, new FixedTimeProvider(afterMax))
                .FindForCustomerAsync(accountId, CancellationToken.None);
            Assert.Equal("Pro", plan!.Code);
            Assert.Equal(CreditQuotaMode.Finite, plan.QuotaMode);
            Assert.Equal(proExpires, plan.SubscriptionExpiresAtUtc);

            var wallet = await reserveFinite.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            var reservation = await new UsageReservationAuthorizationService(
                    reserveFinite, new CreditLinePolicyService(), new FixedTimeProvider(afterMax))
                .ReserveAsync(account, new WalletSnapshot(accountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Scanner", 2960m, finiteReservationKey, CancellationToken.None);
            Assert.Equal(CreditQuotaMode.Finite, reservation.QuotaMode);
            Assert.Equal(2960m, reservation.ReservedCredits);
            Assert.Equal(2960m, (await reserveFinite.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
        }

        await using (var settleFinite = database.CreateBillingContext())
        {
            var result = await new UsageFinalizationService(settleFinite, new FixedTimeProvider(afterMax.AddSeconds(1)))
                .CommitAsync(new UsageCommitCommand(accountId, actorId, tenantId, null, null,
                    finiteReservationKey, "overlap-finite-charge", new UsageChargeResult(2960m, "v1", false)),
                    CancellationToken.None);
            Assert.Equal(2960m, result.LedgerEntry!.CreditsCharged);
            Assert.Equal(0m, result.Wallet.Balance);
        }

        await using (var refund = database.CreateBillingContext())
        {
            var result = await new UsageRefundService(refund, new FixedTimeProvider(plusExpires.AddMinutes(1)))
                .RefundAsync(new UsageRefundCommand(accountId, actorId, tenantId, "overlap-finite-charge", 2960m,
                    "Return only credits that still belong to a live source.", "overlap-source-expiry-refund"), CancellationToken.None);
            Assert.Equal(2600m, result.Wallet.Balance);
        }

        await using var verify = database.CreateBillingContext();
        Assert.Equal(2600m, (await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
        Assert.Equal(2600m, (await verify.SubscriptionCreditPeriods.SingleAsync(row => row.PlanCode == "Pro")).RemainingCredits);
        Assert.Equal(0m, (await verify.SubscriptionCreditPeriods.SingleAsync(row => row.PlanCode == "Plus")).RemainingCredits);
        Assert.Equal(0m, (await verify.TopUpCreditSources.SingleAsync(row => row.Id == topUpId)).RemainingCredits);
        Assert.Equal(2860m, await verify.UsageCreditAllocations.Where(row => row.UsageLedgerEntryId ==
            verify.UsageLedgerEntries.Where(entry => entry.IdempotencyKey == "overlap-finite-charge").Select(entry => entry.Id).Single())
            .SumAsync(row => row.CreditsApplied));
        Assert.Equal(100m, await verify.TopUpUsageCreditAllocations.Where(row => row.UsageLedgerEntryId ==
            verify.UsageLedgerEntries.Where(entry => entry.IdempotencyKey == "overlap-finite-charge").Select(entry => entry.Id).Single())
            .SumAsync(row => row.CreditsApplied));
    }

    private static async Task<(Guid AccountId, Guid TenantId, Guid ActorId, Guid SourceId)> SeedMaxAsync(
        PostgreSqlTestDatabase database,
        DateTimeOffset expiresAt)
    {
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var context = database.CreateBillingContext();
        context.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId, TenantId = tenantId, UserId = actorId,
            AccountType = "Individual", BillingMode = "Prepaid", SubscriptionPlanCode = "Max",
            SubscriptionEffectiveFrom = now.AddDays(-1), SubscriptionEffectiveTo = expiresAt,
            SubscriptionActivationIsAuthoritative = true, ExternalSubscriptionManaged = true
        });
        context.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = accountId, Balance = 0m, ReservedAmount = 0m, UpdatedAt = now, Revision = 0
        });
        context.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = sourceId, CustomerAccountId = accountId, IdentityKey = $"max:{Guid.NewGuid():N}",
            PlanCode = "Max", StartsAtUtc = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAtUtc = expiresAt
        });
        await context.SaveChangesAsync();
        return (accountId, tenantId, actorId, sourceId);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static DateTimeOffset ToPostgresTimestamp(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
