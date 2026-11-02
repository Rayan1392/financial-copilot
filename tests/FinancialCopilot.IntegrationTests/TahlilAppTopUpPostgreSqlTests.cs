using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class TahlilAppTopUpPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task Concurrent_duplicate_delivery_on_independent_connections_commits_one_grant()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_topup");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        var userId = seed.UserId; var tenantId = seed.TenantId; var accountId = seed.AccountId;
        var now = seed.Now; var externalId = seed.ExternalId; var window = seed.Window;

        await using var firstBilling = database.CreateBillingContext();
        await using var secondBilling = database.CreateBillingContext();
        await using var firstAuth = database.CreateAuthContext();
        await using var secondAuth = database.CreateAuthContext();
        await firstBilling.Database.OpenConnectionAsync();
        await secondBilling.Database.OpenConnectionAsync();
        var request = new TopUpDeliveryRequest("evt-shared", "purchase-shared", "payment-shared", externalId, 500,
            "TA-TOPUP-500", "v1", window, now.AddMinutes(-1));
        var refresher = new NoOpRefresher();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = DeliverAsync(firstBilling, firstAuth, request, refresher, gate.Task);
        var second = DeliverAsync(secondBilling, secondAuth, request, refresher, gate.Task);
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.All(outcomes, result => Assert.Equal("granted", result.Status));

        await using var verify = database.CreateBillingContext();
        Assert.Equal(509m, await verify.WalletProjections.Where(row => row.CustomerAccountId == accountId).Select(row => row.Balance).SingleAsync());
        Assert.Equal(1, await verify.TahlilAppTopUpPurchases.CountAsync(row => row.PurchaseId == "purchase-shared"));
        Assert.Equal(1, await verify.TopUpCreditSources.CountAsync(row => row.CustomerAccountId == accountId));
        Assert.Equal(1, await verify.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant"));
    }

    [SkippableFact]
    public async Task Grant_transaction_rolls_back_wallet_receipt_ledger_and_source_then_retries_safely()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_rollback");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE SEQUENCE feature151_fail_source_sequence";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE FUNCTION feature151_fail_source_once() RETURNS trigger AS $$
                BEGIN
                    IF nextval('feature151_fail_source_sequence') = 1 THEN
                        RAISE EXCEPTION 'feature151 injected source insert failure' USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE TRIGGER feature151_fail_source_once
                BEFORE INSERT ON billing_topup_credit_sources
                FOR EACH ROW EXECUTE FUNCTION feature151_fail_source_once()
                """;
            await command.ExecuteNonQueryAsync();
        }

        var request = Request(seed, "rollback-event", "rollback-purchase", "rollback-payment", 500);
        await using (var failedBilling = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                new TahlilAppTopUpService(failedBilling, auth, new FixedClock(seed.Now), new NoOpRefresher())
                    .DeliverAsync(request, CancellationToken.None));
            Assert.Equal("P0001", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        await using (var verifyRollback = database.CreateBillingContext())
        {
            Assert.Equal(9m, await verifyRollback.WalletProjections.Where(row => row.CustomerAccountId == seed.AccountId).Select(row => row.Balance).SingleAsync());
            Assert.Empty(await verifyRollback.TahlilAppTopUpPurchases.ToListAsync());
            Assert.Empty(await verifyRollback.TopUpCreditSources.ToListAsync());
            Assert.Empty(await verifyRollback.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant").ToListAsync());
        }

        await using (var retryBilling = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
        {
            var result = await new TahlilAppTopUpService(retryBilling, auth, new FixedClock(seed.Now), new NoOpRefresher())
                .DeliverAsync(request, CancellationToken.None);
            Assert.Equal("granted", result.Status);
        }
        await using var verifyRetry = database.CreateBillingContext();
        Assert.Equal(509m, await verifyRetry.WalletProjections.Where(row => row.CustomerAccountId == seed.AccountId).Select(row => row.Balance).SingleAsync());
        Assert.Single(await verifyRetry.TahlilAppTopUpPurchases.ToListAsync());
        Assert.Single(await verifyRetry.TopUpCreditSources.ToListAsync());
        Assert.Single(await verifyRetry.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant").ToListAsync());
    }

    [SkippableFact]
    public async Task Concurrent_payment_identity_collision_returns_conflict_and_commits_exactly_one_grant()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_paymentrace");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        await using var firstBilling = database.CreateBillingContext();
        await using var secondBilling = database.CreateBillingContext();
        await using var firstAuth = database.CreateAuthContext();
        await using var secondAuth = database.CreateAuthContext();
        await firstBilling.Database.OpenConnectionAsync();
        await secondBilling.Database.OpenConnectionAsync();
        var firstRequest = Request(seed, "payment-race-event-a", "payment-race-purchase-a", "payment-race-shared", 500);
        var secondRequest = Request(seed, "payment-race-event-b", "payment-race-purchase-b", "payment-race-shared", 500);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(TopUpDeliveryResponse? Result, TopUpContractException? Error)> Deliver(
            BillingDbContext billing, AuthDbContext auth, TopUpDeliveryRequest request)
        {
            await gate.Task;
            try { return (await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(request, CancellationToken.None), null); }
            catch (TopUpContractException exception) { return (null, exception); }
        }
        var first = Deliver(firstBilling, firstAuth, firstRequest);
        var second = Deliver(secondBilling, secondAuth, secondRequest);
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);
        Assert.Single(outcomes, outcome => outcome.Result?.Status == "granted");
        Assert.Single(outcomes, outcome => outcome.Error?.Code == "topup-identity-conflict");

        await using var verify = database.CreateBillingContext();
        Assert.Equal(509m, await verify.WalletProjections.Where(row => row.CustomerAccountId == seed.AccountId).Select(row => row.Balance).SingleAsync());
        Assert.Single(await verify.TahlilAppTopUpPurchases.ToListAsync());
        Assert.Single(await verify.TopUpCreditSources.ToListAsync());
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant").ToListAsync());
        Assert.Single(await verify.AdminAudits.Where(row => row.ActionCode == "billing.tahlilapp-topup.identity-conflict").ToListAsync());
        await using (var adminQuery = database.CreateBillingContext())
        await using (var adminAuth = database.CreateAuthContext())
        {
            var report = await new TahlilAppTopUpService(adminQuery, adminAuth, new FixedClock(seed.Now), new NoOpRefresher())
                .GetReconciliationAsync(seed.TenantId, 50, CancellationToken.None);
            Assert.Single(report.Purchases);
            Assert.Equal("granted", report.Purchases[0].Status);
            Assert.Single(report.IdentityConflicts);
        }
    }

    [SkippableFact]
    public async Task Usage_settlement_racing_original_window_expiry_preserves_preexpiry_hold()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_usagerace");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database, windowDuration: TimeSpan.FromHours(2));
        var firstRequest = Request(seed, "usage-race-event-first", "usage-race-purchase-first", "usage-race-payment-first", 500);
        await using (var billing = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
            await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(firstRequest, CancellationToken.None);

        var account = new CustomerAccount(seed.AccountId, seed.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "feature151-window-race:reservation";
        const string chargeKey = "feature151-window-race:charge";
        await using (var reserveContext = database.CreateBillingContext())
        {
            var wallet = await reserveContext.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            await new UsageReservationAuthorizationService(reserveContext, new CreditLinePolicyService(), new FixedClock(seed.Window.ExpiresAtUtc.AddSeconds(-2)))
                .ReserveAsync(account, new WalletSnapshot(seed.AccountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Feature151WindowRace", 20m, reservationKey, CancellationToken.None);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> ExpireAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try { await new SubscriptionCreditLifecycleService(context).ProcessDueTopUpsAsync(10, seed.Window.ExpiresAtUtc, CancellationToken.None); return null; }
            catch (Exception exception) { return exception; }
        }
        async Task<Exception?> CommitAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new UsageFinalizationService(context, new FixedClock(seed.Window.ExpiresAtUtc.AddSeconds(1))).CommitAsync(
                    new UsageCommitCommand(seed.AccountId, seed.UserId, seed.TenantId, null, null, reservationKey, chargeKey,
                        new UsageChargeResult(20m, "feature151-window-race-v1", Cached: false)), CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }
        var expiryTask = ExpireAsync();
        var commitTask = CommitAsync();
        start.SetResult();
        var race = await Task.WhenAll(expiryTask, commitTask);
        Assert.All(race, error => Assert.Null(error));
        await using (var cleanup = database.CreateBillingContext())
            await new SubscriptionCreditLifecycleService(cleanup).ProcessDueTopUpsAsync(10, seed.Window.ExpiresAtUtc.AddSeconds(2), CancellationToken.None);

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == seed.AccountId);
        var source = await verify.TopUpCreditSources.SingleAsync();
        var reservation = await verify.UsageReservations.SingleAsync(row => row.IdempotencyKey == reservationKey);
        Assert.Equal(9m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(0m, source.RemainingCredits);
        Assert.Equal(SubscriptionCreditLifecycleService.ExpiredStatus, source.Status);
        Assert.Equal("Committed", reservation.Status);
        Assert.Equal(20m, await verify.TopUpUsageCreditAllocations.SumAsync(row => row.CreditsApplied));
        Assert.Equal(1, await verify.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant"));
        var expiryAdjustments = await verify.UsageLedgerEntries
            .Where(row => row.OperationCode == "Billing.TahlilAppTopUpExpiry" ||
                row.OperationCode == "Billing.TahlilAppTopUpExpiredHoldRemainder")
            .ToListAsync();
        Assert.Single(expiryAdjustments);
        Assert.Equal(-480m, expiryAdjustments.Sum(row => row.CreditsCharged));
        Assert.InRange(walletAfter.ReservedAmount, 0m, walletAfter.Balance);
    }

    [SkippableFact]
    public async Task ReleasingExpiredTopUpHoldForfeitsTheWholeUnusedSourceExactlyOnce()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_releaserace");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database, windowDuration: TimeSpan.FromHours(2));
        await using (var billing = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
            await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher())
                .DeliverAsync(Request(seed, "release-expiry-event", "release-expiry-purchase", "release-expiry-payment", 500), CancellationToken.None);

        var account = new CustomerAccount(seed.AccountId, seed.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "feature151-expired-release:reservation";
        await using (var reserve = database.CreateBillingContext())
        {
            var wallet = await reserve.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            await new UsageReservationAuthorizationService(reserve, new CreditLinePolicyService(), new FixedClock(seed.Now))
                .ReserveAsync(account, new WalletSnapshot(seed.AccountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Feature151ExpiredRelease", 20m, reservationKey, CancellationToken.None);
        }
        await using (var release = database.CreateBillingContext())
            await new UsageFinalizationService(release, new FixedClock(seed.Window.ExpiresAtUtc.AddSeconds(1))).ReleaseAsync(
                new UsageReleaseCommand(seed.AccountId, seed.TenantId, reservationKey, "Cancelled after source expiry."), CancellationToken.None);

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == seed.AccountId);
        var source = await verify.TopUpCreditSources.SingleAsync();
        Assert.Equal(9m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(0m, source.RemainingCredits);
        Assert.Equal(SubscriptionCreditLifecycleService.ExpiredStatus, source.Status);
        Assert.Equal(-500m, await verify.UsageLedgerEntries
            .Where(row => row.OperationCode == "Billing.TahlilAppTopUpExpiredHoldRelease")
            .SumAsync(row => row.CreditsCharged));
        Assert.Equal(1, await verify.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.TahlilAppTopUpExpiredHoldRelease"));
    }

    [SkippableFact]
    public async Task TopUp_grant_racing_usage_settlement_commits_both_operations_once()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_grantusage");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        var firstRequest = Request(seed, "grant-usage-event-first", "grant-usage-purchase-first", "grant-usage-payment-first", 500);
        var secondRequest = Request(seed, "grant-usage-event-second", "grant-usage-purchase-second", "grant-usage-payment-second", 100);
        await using (var billing = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
            await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(firstRequest, CancellationToken.None);

        var account = new CustomerAccount(seed.AccountId, seed.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "feature151-grant-usage:reservation";
        const string chargeKey = "feature151-grant-usage:charge";
        await using (var reserveContext = database.CreateBillingContext())
        {
            var wallet = await reserveContext.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            await new UsageReservationAuthorizationService(reserveContext, new CreditLinePolicyService(), new FixedClock(seed.Now))
                .ReserveAsync(account, new WalletSnapshot(seed.AccountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Feature151GrantRace", 20m, reservationKey, CancellationToken.None);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> CommitAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new UsageFinalizationService(context, new FixedClock(seed.Now.AddMinutes(1))).CommitAsync(
                    new UsageCommitCommand(seed.AccountId, seed.UserId, seed.TenantId, null, null, reservationKey, chargeKey,
                        new UsageChargeResult(20m, "feature151-grant-race-v1", Cached: false)), CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }
        async Task<Exception?> GrantAsync()
        {
            await using var billing = database.CreateBillingContext();
            await using var auth = database.CreateAuthContext();
            await billing.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(secondRequest, CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }
        var commitTask = CommitAsync();
        var grantTask = GrantAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(commitTask, grantTask);
        if (outcomes[0] is not null)
        {
            await using var retry = database.CreateBillingContext();
            await new UsageFinalizationService(retry, new FixedClock(seed.Now.AddMinutes(2))).CommitAsync(
                new UsageCommitCommand(seed.AccountId, seed.UserId, seed.TenantId, null, null, reservationKey, chargeKey,
                    new UsageChargeResult(20m, "feature151-grant-race-v1", Cached: false)), CancellationToken.None);
        }
        if (outcomes[1] is not null)
        {
            await using var retryBilling = database.CreateBillingContext();
            await using var retryAuth = database.CreateAuthContext();
            await new TahlilAppTopUpService(retryBilling, retryAuth, new FixedClock(seed.Now), new NoOpRefresher())
                .DeliverAsync(secondRequest, CancellationToken.None);
        }

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == seed.AccountId);
        var sourceCredits = await verify.TopUpCreditSources.SumAsync(row => row.RemainingCredits);
        Assert.Equal(589m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(580m, sourceCredits);
        Assert.Equal("Committed", (await verify.UsageReservations.SingleAsync(row => row.IdempotencyKey == reservationKey)).Status);
        Assert.Equal(2, await verify.TahlilAppTopUpPurchases.CountAsync(row => row.Status == "granted"));
        Assert.Equal(20m, await verify.TopUpUsageCreditAllocations.SumAsync(row => row.CreditsApplied));
        Assert.Equal(2, await verify.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant"));
        Assert.InRange(walletAfter.ReservedAmount, 0m, walletAfter.Balance);
    }

    [SkippableFact]
    public async Task TopUp_grant_racing_usage_reservation_preserves_the_pending_reservation()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_reserverace");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        var request = Request(seed, "reserve-race-event", "reserve-race-purchase", "reserve-race-payment", 500);
        var account = new CustomerAccount(seed.AccountId, seed.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "feature151-reserve-race:reservation";
        WalletSnapshot initialWallet;
        await using (var read = database.CreateBillingContext())
        {
            var wallet = await read.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            initialWallet = new WalletSnapshot(seed.AccountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision);
        }
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> ReserveAsync(WalletSnapshot snapshot)
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new UsageReservationAuthorizationService(context, new CreditLinePolicyService(), new FixedClock(seed.Now))
                    .ReserveAsync(account, snapshot, "AiQuery.Feature151GrantReservationRace", 5m, reservationKey, CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }
        async Task<Exception?> GrantAsync()
        {
            await using var billing = database.CreateBillingContext();
            await using var auth = database.CreateAuthContext();
            await billing.Database.OpenConnectionAsync();
            await start.Task;
            try { await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(request, CancellationToken.None); return null; }
            catch (Exception exception) { return exception; }
        }
        var reserveTask = ReserveAsync(initialWallet);
        var grantTask = GrantAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(reserveTask, grantTask);
        if (outcomes[0] is not null)
        {
            await using var retryContext = database.CreateBillingContext();
            var current = await retryContext.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            await new UsageReservationAuthorizationService(retryContext, new CreditLinePolicyService(), new FixedClock(seed.Now))
                .ReserveAsync(account, new WalletSnapshot(seed.AccountId, current.Balance, current.ReservedAmount, current.UpdatedAt, current.Revision),
                    "AiQuery.Feature151GrantReservationRace", 5m, reservationKey, CancellationToken.None);
        }
        if (outcomes[1] is not null)
        {
            await using var retryBilling = database.CreateBillingContext();
            await using var retryAuth = database.CreateAuthContext();
            await new TahlilAppTopUpService(retryBilling, retryAuth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(request, CancellationToken.None);
        }

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == seed.AccountId);
        Assert.Equal(509m, walletAfter.Balance);
        Assert.Equal(5m, walletAfter.ReservedAmount);
        Assert.Single(await verify.UsageReservations.Where(row => row.IdempotencyKey == reservationKey && row.Status == "Reserved").ToListAsync());
        Assert.Single(await verify.TahlilAppTopUpPurchases.ToListAsync());
        Assert.Single(await verify.TopUpCreditSources.ToListAsync());
        Assert.InRange(walletAfter.ReservedAmount, 0m, walletAfter.Balance);
    }

    [SkippableFact]
    public async Task TopUp_grant_racing_usage_refund_preserves_refund_and_source_attribution()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_refundrace");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        var firstRequest = Request(seed, "refund-race-event-first", "refund-race-purchase-first", "refund-race-payment-first", 500);
        var secondRequest = Request(seed, "refund-race-event-second", "refund-race-purchase-second", "refund-race-payment-second", 100);
        await using (var billing = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
            await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(firstRequest, CancellationToken.None);

        var account = new CustomerAccount(seed.AccountId, seed.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        const string reservationKey = "feature151-refund-race:reservation";
        const string chargeKey = "feature151-refund-race:charge";
        const string refundKey = "feature151-refund-race:refund";
        await using (var reserve = database.CreateBillingContext())
        {
            var wallet = await reserve.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == seed.AccountId);
            await new UsageReservationAuthorizationService(reserve, new CreditLinePolicyService(), new FixedClock(seed.Now))
                .ReserveAsync(account, new WalletSnapshot(seed.AccountId, wallet.Balance, wallet.ReservedAmount, wallet.UpdatedAt, wallet.Revision),
                    "AiQuery.Feature151GrantRefundRace", 20m, reservationKey, CancellationToken.None);
        }
        await using (var commit = database.CreateBillingContext())
            await new UsageFinalizationService(commit, new FixedClock(seed.Now.AddMinutes(1))).CommitAsync(
                new UsageCommitCommand(seed.AccountId, seed.UserId, seed.TenantId, null, null, reservationKey, chargeKey,
                    new UsageChargeResult(20m, "feature151-refund-race-v1", Cached: false)), CancellationToken.None);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> RefundAsync()
        {
            await using var context = database.CreateBillingContext();
            await context.Database.OpenConnectionAsync();
            await start.Task;
            try
            {
                await new UsageRefundService(context, new FixedClock(seed.Now.AddMinutes(2))).RefundAsync(
                    new UsageRefundCommand(seed.AccountId, seed.UserId, seed.TenantId, chargeKey, 10m, "verified test refund", refundKey), CancellationToken.None);
                return null;
            }
            catch (Exception exception) { return exception; }
        }
        async Task<Exception?> GrantAsync()
        {
            await using var billing = database.CreateBillingContext();
            await using var auth = database.CreateAuthContext();
            await billing.Database.OpenConnectionAsync();
            await start.Task;
            try { await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(secondRequest, CancellationToken.None); return null; }
            catch (Exception exception) { return exception; }
        }
        var refundTask = RefundAsync();
        var grantTask = GrantAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(refundTask, grantTask);
        Assert.Null(outcomes[0]);
        if (outcomes[0] is not null)
        {
            await using var retry = database.CreateBillingContext();
            await new UsageRefundService(retry, new FixedClock(seed.Now.AddMinutes(3))).RefundAsync(
                new UsageRefundCommand(seed.AccountId, seed.UserId, seed.TenantId, chargeKey, 10m, "verified test refund", refundKey), CancellationToken.None);
        }
        if (outcomes[1] is not null)
        {
            await using var retryBilling = database.CreateBillingContext();
            await using var retryAuth = database.CreateAuthContext();
            await new TahlilAppTopUpService(retryBilling, retryAuth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(secondRequest, CancellationToken.None);
        }

        await using var verify = database.CreateBillingContext();
        var walletAfter = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == seed.AccountId);
        Assert.Equal(599m, walletAfter.Balance);
        Assert.Equal(0m, walletAfter.ReservedAmount);
        Assert.Equal(590m, await verify.TopUpCreditSources.SumAsync(row => row.RemainingCredits));
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.IdempotencyKey == chargeKey).ToListAsync());
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.IdempotencyKey == refundKey).ToListAsync());
        Assert.Equal(2, await verify.UsageLedgerEntries.CountAsync(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant"));
        var charge = await verify.UsageLedgerEntries.SingleAsync(row => row.IdempotencyKey == chargeKey);
        Assert.Equal(20m, await verify.TopUpUsageCreditAllocations.Where(row => row.UsageLedgerEntryId == charge.Id).SumAsync(row => row.CreditsApplied));
        Assert.InRange(walletAfter.ReservedAmount, 0m, walletAfter.Balance);
    }

    [SkippableFact]
    public async Task Matching_purchase_replays_after_expiry_and_event_change_without_regrant()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature151_replay");
        await using (var auth = database.CreateAuthContext()) await auth.Database.MigrateAsync();
        var seed = await SeedAccountAsync(database);
        var request = Request(seed, "replay-event-first", "replay-purchase", "replay-payment", 500);
        TopUpDeliveryResponse granted;
        await using (var billing = database.CreateBillingContext())
        await using (var auth = database.CreateAuthContext())
            granted = await new TahlilAppTopUpService(billing, auth, new FixedClock(seed.Now), new NoOpRefresher()).DeliverAsync(request, CancellationToken.None);

        await using (var replayBilling = database.CreateBillingContext())
        await using (var replayAuth = database.CreateAuthContext())
        {
            var replay = await new TahlilAppTopUpService(replayBilling, replayAuth,
                new FixedClock(seed.Window.ExpiresAtUtc.AddDays(1)), new NoOpRefresher()).DeliverAsync(
                    request with { EventId = "replay-event-redelivery" }, CancellationToken.None);
            Assert.Equal("granted", replay.Status);
            Assert.Equal(granted.GrantId, replay.GrantId);
            Assert.Equal(500, replay.CreditsGranted);
        }
        await using var verify = database.CreateBillingContext();
        Assert.Equal(509m, await verify.WalletProjections.Where(row => row.CustomerAccountId == seed.AccountId).Select(row => row.Balance).SingleAsync());
        Assert.Single(await verify.TahlilAppTopUpPurchases.ToListAsync());
        Assert.Single(await verify.TopUpCreditSources.ToListAsync());
        Assert.Single(await verify.UsageLedgerEntries.Where(row => row.OperationCode == "Billing.TahlilAppCreditTopUpGrant").ToListAsync());
    }

    private static async Task<TopUpDeliveryResponse> DeliverAsync(BillingDbContext billing, AuthDbContext auth,
        TopUpDeliveryRequest request, ITahlilAppEntitlementRefresher refresher, Task gate)
    {
        await gate;
        return await new TahlilAppTopUpService(billing, auth, TimeProvider.System, refresher)
            .DeliverAsync(request, CancellationToken.None);
    }

    private sealed class NoOpRefresher : ITahlilAppEntitlementRefresher
    {
        public Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record SeededAccount(Guid UserId, Guid TenantId, Guid AccountId, string ExternalId,
        DateTimeOffset Now, TopUpWindowResponse Window);

    private static async Task<SeededAccount> SeedAccountAsync(PostgreSqlTestDatabase database,
        decimal balance = 9m, TimeSpan? windowDuration = null)
    {
        var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid(); var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow; var start = now.AddDays(-1); var expiry = now.Add(windowDuration ?? TimeSpan.FromDays(40));
        var externalId = "ext-" + Guid.NewGuid().ToString("N");
        await using var billing = database.CreateBillingContext();
        await using var auth = database.CreateAuthContext();
        auth.Tenants.Add(new TenantRow { Id = tenantId, Name = "Feature 151 test" });
        auth.Users.Add(new FinancialCopilotUser { Id = userId, UserName = "topup-" + userId.ToString("N"), NormalizedUserName = userId.ToString("N").ToUpperInvariant(), IsEnabled = true });
        auth.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow { Id = Guid.NewGuid(), Provider = "TahlilApp", ExternalUserId = externalId, LocalUserId = userId, TenantId = tenantId, Status = "Active", LinkedAtUtc = now });
        await auth.SaveChangesAsync();
        billing.CustomerAccounts.Add(new CustomerAccountRow { Id = accountId, TenantId = tenantId, UserId = userId, AccountType = "Individual", BillingMode = "Prepaid", SubscriptionPlanCode = "Plus", SubscriptionActivationIsAuthoritative = true, SubscriptionEffectiveFrom = start, SubscriptionEffectiveTo = expiry });
        billing.WalletProjections.Add(new WalletProjectionRow { CustomerAccountId = accountId, Balance = balance, ReservedAmount = 0, UpdatedAt = now, Revision = 1 });
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow { Id = Guid.NewGuid(), CustomerAccountId = accountId, IdentityKey = $"sapio_plus:{start.ToUnixTimeMilliseconds()}:{expiry.ToUnixTimeMilliseconds()}", PlanCode = "Plus", StartsAtUtc = start, ExpiresAtUtc = expiry });
        await billing.SaveChangesAsync();
        return new(userId, tenantId, accountId, externalId, now, new TopUpWindowResponse("sapio_plus", start, expiry));
    }

    private static TopUpDeliveryRequest Request(SeededAccount seed, string eventId, string purchaseId, string paymentId, int credits) =>
        new(eventId, purchaseId, paymentId, seed.ExternalId, credits, $"TA-TOPUP-{credits}", "v1", seed.Window, seed.Now.AddMinutes(-1));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
