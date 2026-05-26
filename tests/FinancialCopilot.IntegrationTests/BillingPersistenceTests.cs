using FinancialCopilot.Billing.Accounts;
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
}
