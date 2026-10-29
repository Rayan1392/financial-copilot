using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class UsageBasedCreditBillingPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task ConcurrentReservations_FromOneWalletRevision_CannotOverbookWallet()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_setting('transaction_isolation')";
            Assert.Equal("read committed", await command.ExecuteScalarAsync());
        }
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 6m,
                ReservedAmount = 0m,
                UpdatedAt = now,
                Revision = 0
            });
            await seed.SaveChangesAsync();
        }

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Organization, BillingMode.Prepaid);
        var initialWallet = new WalletSnapshot(accountId, 6m, 0m, now, Revision: 0);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(UsageReservation? Reservation, Exception? Error)> ReserveAsync(string key)
        {
            await using var context = database.CreateBillingContext();
            var service = new UsageReservationAuthorizationService(
                context, new CreditLinePolicyService(), TimeProvider.System);
            await start.Task;
            try
            {
                return (await service.ReserveAsync(
                    account, initialWallet, "AiQuery.Scanner", 4m, key, CancellationToken.None), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var first = ReserveAsync("feature147-concurrent-reservation-1");
        var second = ReserveAsync("feature147-concurrent-reservation-2");
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Reservation is not null);
        var rejected = Assert.Single(results, result => result.Error is not null);
        Assert.IsType<InvalidOperationException>(rejected.Error);
        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(6m, wallet.Balance);
        Assert.Equal(4m, wallet.ReservedAmount);
        Assert.Equal(1, await verify.UsageReservations.AsNoTracking().CountAsync());
    }

    [SkippableFact]
    public async Task ConcurrentWalletRevisionWrites_AreFencedByPostgresConcurrencyToken()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId, Balance = 10m, ReservedAmount = 0m,
                UpdatedAt = now, Revision = 1
            });
            await seed.SaveChangesAsync();
        }

        await using var firstContext = database.CreateBillingContext();
        await using var secondContext = database.CreateBillingContext();
        var firstWallet = await firstContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        var secondWallet = await secondContext.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        firstWallet.Balance = 9m;
        firstWallet.Revision++;
        secondWallet.Balance = 8m;
        secondWallet.Revision++;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<Exception?> SaveAsync(BillingDbContext context)
        {
            await start.Task;
            try
            {
                await context.SaveChangesAsync();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var first = SaveAsync(firstContext);
        var second = SaveAsync(secondContext);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, result => result is null);
        Assert.IsType<DbUpdateConcurrencyException>(Assert.Single(results, result => result is not null));

        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(2, wallet.Revision);
        Assert.Contains(wallet.Balance, new[] { 8m, 9m });
        Assert.True(wallet.Balance >= 0m);
    }

    [SkippableFact]
    public async Task ReservationUpgrade_AndConcurrentSettlement_KeepPostgresWalletAndLedgerConsistent()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var apiClientId = Guid.NewGuid();
        const string reservationKey = "feature147-postgres-operation:reservation";
        const string ledgerKey = "feature147-postgres-operation:charge";
        var now = DateTimeOffset.UtcNow;

        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 10m,
                ReservedAmount = 1m,
                UpdatedAt = now,
                Revision = 1
            });
            seed.UsageReservations.Add(new UsageReservationRow
            {
                Id = Guid.NewGuid(),
                CustomerAccountId = accountId,
                IdempotencyKey = reservationKey,
                OperationCode = "AiQuery.Scanner",
                ReservedCredits = 1m,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(5),
                Status = UsageReservationStatus.Reserved.ToString()
            });
            await seed.SaveChangesAsync();
        }

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Organization, BillingMode.Prepaid);
        await using (var upgradeContext = database.CreateBillingContext())
        {
            var upgrader = new UsageReservationAuthorizationService(
                upgradeContext,
                new CreditLinePolicyService(),
                TimeProvider.System);
            var upgraded = await upgrader.EnsureReservedCapacityAsync(
                account, reservationKey, requiredCredits: 5m, CancellationToken.None);
            Assert.Equal(5m, upgraded.ReservedCredits);
            var heldWallet = await upgradeContext.WalletProjections.AsNoTracking()
                .SingleAsync(row => row.CustomerAccountId == accountId);
            Assert.Equal(5m, heldWallet.ReservedAmount);
            Assert.Equal(2, heldWallet.Revision);
        }

        var command = new UsageCommitCommand(
            accountId,
            actorId,
            tenantId,
            apiClientId,
            ExternalUserId: null,
            reservationKey,
            ledgerKey,
            new UsageChargeResult(3m, "usage-test-v1", Cached: false),
            CompletionStatus: "Completed",
            ProviderName: "BillingTestProvider",
            ModelName: "standard-model",
            PromptTokens: 2_000,
            CompletionTokens: 500,
            TotalTokens: 2_500);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(UsageFinalizationResult? Result, Exception? Error)> CommitAsync()
        {
            await using var context = database.CreateBillingContext();
            var finalizer = new UsageFinalizationService(context, TimeProvider.System);
            await start.Task;
            try
            {
                return (await finalizer.CommitAsync(command, CancellationToken.None), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var firstAttempt = CommitAsync();
        var secondAttempt = CommitAsync();
        start.SetResult();
        var attempts = await Task.WhenAll(firstAttempt, secondAttempt);
        Assert.All(attempts, attempt =>
        {
            Assert.Null(attempt.Error);
            Assert.NotNull(attempt.Result);
        });
        Assert.Equal(1, attempts.Count(attempt => attempt.Result!.AlreadyFinalized));

        await using (var retryContext = database.CreateBillingContext())
        {
            var retry = await new UsageFinalizationService(retryContext, TimeProvider.System)
                .CommitAsync(command, CancellationToken.None);
            Assert.True(retry.AlreadyFinalized);
        }

        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        var reservation = await verify.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == reservationKey);
        var ledger = await verify.UsageLedgerEntries.AsNoTracking().SingleAsync(row => row.IdempotencyKey == ledgerKey);
        Assert.Equal(7m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        Assert.Equal(3m, reservation.CommittedCredits);
        Assert.Equal("Committed", reservation.Status);
        Assert.Equal(3m, ledger.CreditsCharged);
        Assert.Single(await verify.UsageLedgerEntries.AsNoTracking().Where(row => row.IdempotencyKey == ledgerKey).ToListAsync());
        Assert.InRange(wallet.ReservedAmount, 0m, wallet.Balance);
    }

    [SkippableFact]
    public async Task ConcurrentSameKeyReservations_ReuseOnePersistedHoldAndRejectChangedPayload()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string key = "feature147-same-question:reservation";
        const string fingerprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 10m,
                ReservedAmount = 0m,
                UpdatedAt = now,
                Revision = 0
            });
            await seed.SaveChangesAsync();
        }

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Organization, BillingMode.Prepaid);
        var initialWallet = new WalletSnapshot(accountId, 10m, 0m, now, Revision: 0);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<UsageReservation> ReserveAsync()
        {
            await using var context = database.CreateBillingContext();
            var service = new UsageReservationAuthorizationService(context, new CreditLinePolicyService(), TimeProvider.System);
            await start.Task;
            return await service.ReserveAsync(
                account, initialWallet, "AiQuery.Scanner", 4m, key, CancellationToken.None, fingerprint);
        }

        var first = ReserveAsync();
        var second = ReserveAsync();
        start.SetResult();
        var reservations = await Task.WhenAll(first, second);
        Assert.Equal(reservations[0].Id, reservations[1].Id);

        await using (var verify = database.CreateBillingContext())
        {
            var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            var persisted = await verify.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == key);
            Assert.Equal(fingerprint, persisted.RequestFingerprint);
            Assert.Equal(10m, wallet.Balance);
            Assert.Equal(4m, wallet.ReservedAmount);
            Assert.InRange(wallet.ReservedAmount, 0m, wallet.Balance);
            Assert.Equal(1, await verify.UsageReservations.AsNoTracking().CountAsync(row => row.IdempotencyKey == key));
        }

        await using var changedPayloadContext = database.CreateBillingContext();
        var changedPayloadService = new UsageReservationAuthorizationService(
            changedPayloadContext, new CreditLinePolicyService(), TimeProvider.System);
        var current = await changedPayloadContext.WalletProjections.AsNoTracking()
            .SingleAsync(row => row.CustomerAccountId == accountId);
        await Assert.ThrowsAsync<FinancialCopilot.Billing.BillingIdempotencyConflictException>(() =>
            changedPayloadService.ReserveAsync(
                account,
                new WalletSnapshot(current.CustomerAccountId, current.Balance, current.ReservedAmount, current.UpdatedAt, current.Revision),
                "AiQuery.Scanner",
                4m,
                key,
                CancellationToken.None,
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"));
    }

    [SkippableFact]
    public async Task ConcurrentReservationUpgrades_PreserveTheLargestRequiredHold()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string key = "feature147-concurrent-upgrade:reservation";
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 10m,
                ReservedAmount = 1m,
                UpdatedAt = now,
                Revision = 1
            });
            seed.UsageReservations.Add(new UsageReservationRow
            {
                Id = Guid.NewGuid(), CustomerAccountId = accountId, IdempotencyKey = key,
                OperationCode = "AiQuery.Scanner", ReservedCredits = 1m, CreatedAt = now,
                ExpiresAt = now.AddMinutes(5), Status = UsageReservationStatus.Reserved.ToString()
            });
            await seed.SaveChangesAsync();
        }

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Organization, BillingMode.Prepaid);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<UsageReservation> UpgradeAsync(decimal credits)
        {
            await using var context = database.CreateBillingContext();
            var service = new UsageReservationAuthorizationService(context, new CreditLinePolicyService(), TimeProvider.System);
            await start.Task;
            return await service.EnsureReservedCapacityAsync(account, key, credits, CancellationToken.None);
        }

        var lower = UpgradeAsync(5m);
        var higher = UpgradeAsync(8m);
        start.SetResult();
        var upgraded = await Task.WhenAll(lower, higher);
        Assert.All(upgraded, reservation => Assert.InRange(reservation.ReservedCredits, 5m, 8m));
        Assert.Contains(upgraded, reservation => reservation.ReservedCredits == 8m);

        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        var reservation = await verify.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == key);
        Assert.Equal(8m, reservation.ReservedCredits);
        Assert.Equal(8m, wallet.ReservedAmount);
        Assert.InRange(wallet.ReservedAmount, 0m, wallet.Balance);
        Assert.InRange(wallet.Revision, 2L, 3L);
        Assert.Empty(await verify.UsageLedgerEntries.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task FailedAnswer_ReleasesReservationWithoutChargingTheWallet()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string key = "feature147-failed-answer:reservation";
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId, TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(), BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId, Balance = 10m, ReservedAmount = 4m, UpdatedAt = now, Revision = 1
            });
            seed.UsageReservations.Add(new UsageReservationRow
            {
                Id = Guid.NewGuid(), CustomerAccountId = accountId, IdempotencyKey = key,
                OperationCode = "AiQuery.Scanner", ReservedCredits = 4m, CreatedAt = now,
                ExpiresAt = now.AddMinutes(5), Status = UsageReservationStatus.Reserved.ToString()
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = database.CreateBillingContext())
        {
            var finalizer = new UsageFinalizationService(context, TimeProvider.System);
            var released = await finalizer.ReleaseAsync(
                new UsageReleaseCommand(accountId, tenantId, key, "Provider failed before an answer was produced."),
                CancellationToken.None);
            Assert.False(released.AlreadyFinalized);
        }

        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        var reservation = await verify.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == key);
        Assert.Equal(10m, wallet.Balance);
        Assert.Equal(0m, wallet.ReservedAmount);
        Assert.Equal("Released", reservation.Status);
        Assert.InRange(wallet.ReservedAmount, 0m, wallet.Balance);
        Assert.Empty(await verify.UsageLedgerEntries.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task TransientLedgerWriteFailure_RollsBackAndSameOperationCanRetry()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync("feature147_billing");
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string reservationKey = "feature147-transient-settlement:reservation";
        const string ledgerKey = "feature147-transient-settlement:charge";
        await using (var seed = database.CreateBillingContext())
        {
            seed.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId, TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(), BillingMode = BillingMode.Prepaid.ToString()
            });
            seed.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId, Balance = 10m, ReservedAmount = 5m, UpdatedAt = now, Revision = 1
            });
            seed.UsageReservations.Add(new UsageReservationRow
            {
                Id = Guid.NewGuid(), CustomerAccountId = accountId, IdempotencyKey = reservationKey,
                OperationCode = "AiQuery.Scanner", ReservedCredits = 5m, CreatedAt = now,
                ExpiresAt = now.AddMinutes(5), Status = UsageReservationStatus.Reserved.ToString()
            });
            await seed.SaveChangesAsync();
        }

        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE SEQUENCE feature147_fail_first_ledger_sequence";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE FUNCTION feature147_fail_first_ledger() RETURNS trigger AS $$
                BEGIN
                    IF nextval('feature147_fail_first_ledger_sequence') = 1 THEN
                        RAISE EXCEPTION 'feature147 injected one-time ledger failure' USING ERRCODE = 'P0001';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                CREATE TRIGGER feature147_fail_first_ledger
                BEFORE INSERT ON billing_usage_ledger_entries
                FOR EACH ROW EXECUTE FUNCTION feature147_fail_first_ledger()
                """;
            await command.ExecuteNonQueryAsync();
        }

        var commit = new UsageCommitCommand(
            accountId, actorId, tenantId, ApiClientId: null, ExternalUserId: null,
            reservationKey, ledgerKey,
            new UsageChargeResult(3m, "usage-test-v1", Cached: false),
            CompletionStatus: "Completed", ProviderName: "BillingTestProvider", ModelName: "standard-model",
            PromptTokens: 2_000, CompletionTokens: 500, TotalTokens: 2_500);
        await using (var failedContext = database.CreateBillingContext())
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                new UsageFinalizationService(failedContext, TimeProvider.System)
                    .CommitAsync(commit, CancellationToken.None));
            Assert.Equal("P0001", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        await using (var afterFailure = database.CreateBillingContext())
        {
            var wallet = await afterFailure.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
            var reservation = await afterFailure.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == reservationKey);
            Assert.Equal(10m, wallet.Balance);
            Assert.Equal(5m, wallet.ReservedAmount);
            Assert.Equal("Reserved", reservation.Status);
            Assert.Empty(await afterFailure.UsageLedgerEntries.AsNoTracking().ToListAsync());
        }

        await using (var retryContext = database.CreateBillingContext())
        {
            var retry = await new UsageFinalizationService(retryContext, TimeProvider.System)
                .CommitAsync(commit, CancellationToken.None);
            Assert.False(retry.AlreadyFinalized);
        }
        await using (var replayContext = database.CreateBillingContext())
        {
            var replay = await new UsageFinalizationService(replayContext, TimeProvider.System)
                .CommitAsync(commit, CancellationToken.None);
            Assert.True(replay.AlreadyFinalized);
        }

        await using var verify = database.CreateBillingContext();
        var finalWallet = await verify.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(7m, finalWallet.Balance);
        Assert.Equal(0m, finalWallet.ReservedAmount);
        Assert.InRange(finalWallet.ReservedAmount, 0m, finalWallet.Balance);
        Assert.Equal(1, await verify.UsageLedgerEntries.AsNoTracking().CountAsync(row => row.IdempotencyKey == ledgerKey));
        Assert.Equal(3m, (await verify.UsageReservations.AsNoTracking().SingleAsync(row => row.IdempotencyKey == reservationKey)).CommittedCredits);
    }
}
