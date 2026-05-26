using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Usage;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

public sealed class BillingPersistenceTests
{
    [Fact]
    public async Task CustomerAccountRepository_ReadsOrganizationAndIndividualAccounts()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var individualId = Guid.NewGuid();
        dbContext.CustomerAccounts.AddRange(
            new CustomerAccountRow
            {
                Id = organizationId,
                TenantId = tenantId,
                AccountType = CustomerAccountType.Organization.ToString(),
                BillingMode = BillingMode.Hybrid.ToString(),
                CreditLineApprovedLimit = 10,
                CreditLineWarningThreshold = 2
            },
            new CustomerAccountRow
            {
                Id = individualId,
                TenantId = tenantId,
                UserId = userId,
                AccountType = CustomerAccountType.Individual.ToString(),
                BillingMode = BillingMode.Prepaid.ToString()
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var repository = new CustomerAccountRepository(dbContext);

        var organization = await repository.FindOrganizationByTenantAsync(tenantId, CancellationToken.None);
        var individual = await repository.FindIndividualByUserAsync(tenantId, userId, CancellationToken.None);

        Assert.NotNull(organization);
        Assert.Equal(CustomerAccountType.Organization, organization.AccountType);
        Assert.Equal(10, organization.CreditLine!.ApprovedLimit);
        Assert.NotNull(individual);
        Assert.Equal(individualId, individual.Id);
        Assert.Equal(CustomerAccountType.Individual, individual.AccountType);
    }

    [Fact]
    public async Task WalletAndReservationRepositories_PersistFinalizedUsageReservation()
    {
        await using var dbContext = CreateDbContext();
        var accountId = Guid.NewGuid();
        var walletRepository = new WalletProjectionRepository(dbContext);
        var reservationRepository = new UsageReservationRepository(dbContext);
        await walletRepository.SaveAsync(
            new WalletSnapshot(accountId, 10, 0, DateTimeOffset.UtcNow),
            CancellationToken.None);
        var reservation = new UsageReservation(
            Guid.NewGuid(),
            accountId,
            "query-1",
            "AiQuery.Scanner",
            2,
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"),
            DateTimeOffset.Parse("2026-05-26T12:05:00Z"));
        reservation.Commit(1.5m);

        await reservationRepository.SaveAsync(reservation, CancellationToken.None);
        var restored = await reservationRepository.FindByIdempotencyKeyAsync(
            "query-1",
            CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(UsageReservationStatus.Committed, restored.Status);
        Assert.Equal(1.5m, restored.CommittedCredits);
        Assert.Equal(10, (await walletRepository.GetSnapshotAsync(accountId, CancellationToken.None)).Balance);
    }

    [Fact]
    public async Task WalletProjectionRepository_RejectsStaleConcurrentReservationUpdate()
    {
        await using var dbContext = CreateDbContext();
        var repository = new WalletProjectionRepository(dbContext);
        var accountId = Guid.NewGuid();
        await repository.SaveAsync(
            new WalletSnapshot(accountId, 10, 0, DateTimeOffset.Parse("2026-05-26T12:00:00Z")),
            CancellationToken.None);
        var firstRead = await repository.GetSnapshotAsync(accountId, CancellationToken.None);
        var concurrentRead = await repository.GetSnapshotAsync(accountId, CancellationToken.None);

        await repository.SaveAsync(
            firstRead.Reserve(4, DateTimeOffset.Parse("2026-05-26T12:01:00Z")),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveAsync(
                concurrentRead.Reserve(7, DateTimeOffset.Parse("2026-05-26T12:01:01Z")),
                CancellationToken.None));

        var stored = await repository.GetSnapshotAsync(accountId, CancellationToken.None);
        Assert.Equal(4, stored.ReservedAmount);
        Assert.Equal(1, stored.Revision);
    }

    [Fact]
    public async Task UsageReservationRepository_FindsOnlyExpiredReservedCapacity()
    {
        await using var dbContext = CreateDbContext();
        var repository = new UsageReservationRepository(dbContext);
        var customerAccountId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-05-26T12:00:00Z");
        var expired = new UsageReservation(
            Guid.NewGuid(), customerAccountId, "expired", "AiQuery.Scanner", 1,
            now.AddMinutes(-10), now.AddMinutes(-5));
        var active = new UsageReservation(
            Guid.NewGuid(), customerAccountId, "active", "AiQuery.Scanner", 1,
            now.AddMinutes(-1), now.AddMinutes(4));
        await repository.SaveAsync(expired, CancellationToken.None);
        await repository.SaveAsync(active, CancellationToken.None);

        var results = await repository.FindExpiredReservedAsync(now, 10, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("expired", results.Single().IdempotencyKey);
    }

    [Fact]
    public async Task UsageReservationAuthorizationService_ReservesWalletCapacityOnlyOnce()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Organization.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 10,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:55:00Z")
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var account = new CustomerAccount(
            customerAccountId,
            tenantId,
            CustomerAccountType.Organization,
            BillingMode.Prepaid);
        var service = new UsageReservationAuthorizationService(
            dbContext,
            new FinancialCopilot.Billing.Services.CreditLinePolicyService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));
        var wallet = new WalletSnapshot(
            customerAccountId,
            10,
            0,
            DateTimeOffset.Parse("2026-05-26T11:55:00Z"));

        var created = await service.ReserveAsync(
            account, wallet, "AiQuery.Scanner", 2, "atomic-reserve", CancellationToken.None);
        var replayed = await service.ReserveAsync(
            account, wallet, "AiQuery.Scanner", 2, "atomic-reserve", CancellationToken.None);

        Assert.Equal(created.Id, replayed.Id);
        Assert.Single(dbContext.UsageReservations);
        Assert.Equal(2, dbContext.WalletProjections.Single().ReservedAmount);
        Assert.Equal(1, dbContext.WalletProjections.Single().Revision);
    }

    [Fact]
    public async Task UsageReservationAuthorizationService_ExpiresHoldAndRestoresCapacity()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Individual.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 5,
            ReservedAmount = 1,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:50:00Z"),
            Revision = 1
        });
        dbContext.UsageReservations.Add(CreateReservedRow(
            customerAccountId,
            "expired-hold",
            reservedCredits: 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UsageReservationAuthorizationService(
            dbContext,
            new FinancialCopilot.Billing.Services.CreditLinePolicyService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:10:00Z")));

        var expiredCount = await service.ExpireAbandonedAsync(10, CancellationToken.None);
        var replayedCount = await service.ExpireAbandonedAsync(10, CancellationToken.None);

        Assert.Equal(1, expiredCount);
        Assert.Equal(0, replayedCount);
        Assert.Equal(0, dbContext.WalletProjections.Single().ReservedAmount);
        Assert.Equal(2, dbContext.WalletProjections.Single().Revision);
        Assert.Equal(
            "Reservation expired before finalization.",
            dbContext.UsageReservations.Single().FinalizationReason);
    }

    [Fact]
    public async Task UsageLedgerRepository_RoundTripsUsageAndFinancialTransactions()
    {
        await using var dbContext = CreateDbContext();
        var repository = new UsageLedgerRepository(dbContext);
        var accountId = Guid.NewGuid();
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            accountId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            UsageLedgerEntryType.Charge,
            "AiQuery.Scanner",
            2.4m,
            "v1",
            "ledger-1",
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"),
            "external-user");
        var payment = new FinancialTransaction(
            Guid.NewGuid(),
            accountId,
            FinancialTransactionType.TopUp,
            100,
            "CREDIT",
            "topup-1",
            DateTimeOffset.Parse("2026-05-26T12:01:00Z"));

        await repository.AppendAsync(entry, CancellationToken.None);
        await repository.AppendAsync(payment, CancellationToken.None);

        var storedEntry = await repository.FindByIdempotencyKeyAsync("ledger-1", CancellationToken.None);
        var transactionRepository = (FinancialCopilot.Billing.Contracts.IFinancialTransactionRepository)repository;
        var storedPayment = await transactionRepository.FindByIdempotencyKeyAsync(
            "topup-1",
            CancellationToken.None);
        Assert.Equal(entry, storedEntry);
        Assert.Equal(payment, storedPayment);
        Assert.Single(await repository.QueryAsync(
            accountId,
            DateTimeOffset.Parse("2026-05-26T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-26T13:00:00Z"),
            CancellationToken.None));
        Assert.Single(await transactionRepository.QueryAsync(
            accountId,
            DateTimeOffset.Parse("2026-05-26T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-26T13:00:00Z"),
            CancellationToken.None));
    }

    [Fact]
    public async Task CreditAdjustmentService_AppliesLedgerAndWalletOnceForSameCommand()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Organization.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 50,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:00:00Z")
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new CreditAdjustmentService(
            dbContext,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));
        var command = new FinancialCopilot.Billing.Contracts.CreditAdjustmentCommand(
            customerAccountId,
            actorId,
            tenantId,
            5,
            "Compensating credit",
            "adjustment-1");

        var applied = await service.ApplyAsync(command, CancellationToken.None);
        var replayed = await service.ApplyAsync(command, CancellationToken.None);

        Assert.False(applied.AlreadyApplied);
        Assert.True(replayed.AlreadyApplied);
        Assert.Equal(55, replayed.Wallet.Balance);
        Assert.Equal(1, replayed.Wallet.Revision);
        Assert.Single(dbContext.UsageLedgerEntries);
        Assert.Equal("Compensating credit", dbContext.UsageLedgerEntries.Single().AuditDescription);
    }

    [Fact]
    public async Task UsageRefundService_RefundsCommittedChargeAndRestoresWalletOnlyOnce()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var chargeId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Organization.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 47,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:00:00Z")
        });
        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = chargeId,
            CustomerAccountId = customerAccountId,
            ActorId = actorId,
            TenantId = tenantId,
            EntryType = UsageLedgerEntryType.Charge.ToString(),
            OperationCode = "AiQuery.Scanner",
            CreditsCharged = 3,
            PricingPolicyVersion = "v1",
            IdempotencyKey = "original-charge",
            OccurredAt = DateTimeOffset.Parse("2026-05-26T11:00:00Z")
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UsageRefundService(
            dbContext,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));
        var command = new UsageRefundCommand(
            customerAccountId,
            actorId,
            tenantId,
            "original-charge",
            2,
            "Provider partial failure refund",
            "refund-1");

        var applied = await service.RefundAsync(command, CancellationToken.None);
        var replayed = await service.RefundAsync(command, CancellationToken.None);

        Assert.False(applied.AlreadyApplied);
        Assert.True(replayed.AlreadyApplied);
        Assert.Equal(49, replayed.Wallet.Balance);
        Assert.Equal(1, replayed.Wallet.Revision);
        Assert.Equal(chargeId, replayed.LedgerEntry.RelatedEntryId);
        Assert.Equal(2, dbContext.UsageLedgerEntries.Count());
    }

    [Fact]
    public async Task UsageRefundService_RejectsCumulativeRefundAboveOriginalCharge()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Individual.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 8,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:00:00Z")
        });
        dbContext.UsageLedgerEntries.Add(new UsageLedgerEntryRow
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customerAccountId,
            ActorId = actorId,
            TenantId = tenantId,
            EntryType = UsageLedgerEntryType.Charge.ToString(),
            OperationCode = "AiQuery.Scanner",
            CreditsCharged = 2,
            PricingPolicyVersion = "v1",
            IdempotencyKey = "original-charge",
            OccurredAt = DateTimeOffset.Parse("2026-05-26T11:00:00Z")
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UsageRefundService(
            dbContext,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));

        await service.RefundAsync(
            new UsageRefundCommand(
                customerAccountId, actorId, tenantId, "original-charge", 1.5m, "First refund", "refund-1"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefundAsync(
            new UsageRefundCommand(
                customerAccountId, actorId, tenantId, "original-charge", 1m, "Excess refund", "refund-2"),
            CancellationToken.None));
    }

    [Fact]
    public async Task UsageFinalizationService_CommitsReservationLedgerAndWalletOnlyOnce()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Organization.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 10,
            ReservedAmount = 2,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:55:00Z"),
            Revision = 1
        });
        dbContext.UsageReservations.Add(CreateReservedRow(
            customerAccountId,
            "reservation-commit",
            reservedCredits: 2));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UsageFinalizationService(
            dbContext,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));
        var command = new UsageCommitCommand(
            customerAccountId,
            actorId,
            tenantId,
            ApiClientId: Guid.NewGuid(),
            ExternalUserId: "partner-user-1",
            ReservationIdempotencyKey: "reservation-commit",
            LedgerIdempotencyKey: "ledger-commit",
            ActualCharge: new UsageChargeResult(1.5m, "v1", Cached: false));

        var committed = await service.CommitAsync(command, CancellationToken.None);
        var replayed = await service.CommitAsync(command, CancellationToken.None);

        Assert.False(committed.AlreadyFinalized);
        Assert.True(replayed.AlreadyFinalized);
        Assert.Equal(UsageReservationStatus.Committed, replayed.Reservation.Status);
        Assert.Equal(8.5m, replayed.Wallet.Balance);
        Assert.Equal(0, replayed.Wallet.ReservedAmount);
        Assert.Equal(2, replayed.Wallet.Revision);
        Assert.Single(dbContext.UsageLedgerEntries);
        Assert.Equal("partner-user-1", replayed.LedgerEntry!.ExternalUserId);
    }

    [Fact]
    public async Task UsageFinalizationService_ReleasesFailedReservationWithReasonOnlyOnce()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = tenantId,
            AccountType = CustomerAccountType.Individual.ToString(),
            BillingMode = BillingMode.Prepaid.ToString()
        });
        dbContext.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = customerAccountId,
            Balance = 4,
            ReservedAmount = 1,
            UpdatedAt = DateTimeOffset.Parse("2026-05-26T11:55:00Z"),
            Revision = 1
        });
        dbContext.UsageReservations.Add(CreateReservedRow(
            customerAccountId,
            "reservation-release",
            reservedCredits: 1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UsageFinalizationService(
            dbContext,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));
        var command = new UsageReleaseCommand(
            customerAccountId,
            tenantId,
            "reservation-release",
            "Provider failed before billable completion.");

        var released = await service.ReleaseAsync(command, CancellationToken.None);
        var replayed = await service.ReleaseAsync(command, CancellationToken.None);

        Assert.False(released.AlreadyFinalized);
        Assert.True(replayed.AlreadyFinalized);
        Assert.Equal(UsageReservationStatus.Released, replayed.Reservation.Status);
        Assert.Equal("Provider failed before billable completion.", replayed.Reservation.FinalizationReason);
        Assert.Equal(4, replayed.Wallet.Balance);
        Assert.Equal(0, replayed.Wallet.ReservedAmount);
        Assert.Equal(2, replayed.Wallet.Revision);
        Assert.Empty(dbContext.UsageLedgerEntries);
    }

    [Fact]
    public async Task SubscriptionAndInvoiceRepositories_ReadConfiguredBillingProfiles()
    {
        await using var dbContext = CreateDbContext();
        var customerAccountId = Guid.NewGuid();
        dbContext.SubscriptionPlans.Add(new SubscriptionPlanRow
        {
            Code = "PARTNER_STANDARD",
            Name = "Partner Standard",
            IncludedCredits = 100,
            PricingPolicyVersion = "v1"
        });
        dbContext.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = customerAccountId,
            TenantId = Guid.NewGuid(),
            AccountType = CustomerAccountType.Organization.ToString(),
            BillingMode = BillingMode.Prepaid.ToString(),
            SubscriptionPlanCode = "PARTNER_STANDARD"
        });
        dbContext.InvoiceAccounts.Add(new InvoiceAccountRow
        {
            CustomerAccountId = customerAccountId,
            LegalName = "TahlilAPP",
            BillingEmail = "billing@example.test",
            SettlementTerms = "Prepaid"
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var plan = await new SubscriptionPlanRepository(dbContext).FindForCustomerAsync(
            customerAccountId,
            CancellationToken.None);
        var invoice = await new InvoiceAccountRepository(dbContext).FindAsync(
            customerAccountId,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("PARTNER_STANDARD", plan.Code);
        Assert.NotNull(invoice);
        Assert.Equal("TahlilAPP", invoice.LegalName);
    }

    private static BillingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new BillingDbContext(options);
    }

    private static UsageReservationRow CreateReservedRow(
        Guid customerAccountId,
        string idempotencyKey,
        decimal reservedCredits) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customerAccountId,
            IdempotencyKey = idempotencyKey,
            OperationCode = "AiQuery.Scanner",
            ReservedCredits = reservedCredits,
            CreatedAt = DateTimeOffset.Parse("2026-05-26T11:55:00Z"),
            ExpiresAt = DateTimeOffset.Parse("2026-05-26T12:05:00Z"),
            Status = UsageReservationStatus.Reserved.ToString()
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
