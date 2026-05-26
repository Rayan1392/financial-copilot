using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Billing.Usage;

namespace FinancialCopilot.UnitTests;

public sealed class BillingServiceTests
{
    [Fact]
    public async Task BillableAccountResolver_ApiClientResolvesOrganizationAccount()
    {
        var tenantId = Guid.NewGuid();
        var account = new CustomerAccount(
            Guid.NewGuid(),
            tenantId,
            CustomerAccountType.Organization,
            BillingMode.Prepaid);
        var repository = new TestAccountRepository(organization: account);
        var resolver = new BillableAccountResolver(repository);

        var resolved = await resolver.ResolveAsync(
            new BillableActorContext(
                Guid.NewGuid(),
                tenantId,
                UserId: null,
                ApiClientId: Guid.NewGuid(),
                ExternalUserId: "partner-user-123"),
            CancellationToken.None);

        Assert.Same(account, resolved);
    }

    [Fact]
    public async Task BillableAccountResolver_UserResolvesIndividualAccount()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var account = new CustomerAccount(
            Guid.NewGuid(),
            tenantId,
            CustomerAccountType.Individual,
            BillingMode.Prepaid);
        var resolver = new BillableAccountResolver(new TestAccountRepository(individual: account));

        var resolved = await resolver.ResolveAsync(
            new BillableActorContext(userId, tenantId, userId, ApiClientId: null, ExternalUserId: null),
            CancellationToken.None);

        Assert.Same(account, resolved);
    }

    [Fact]
    public async Task BillingAdministrationService_RejectsAccountOutsideAdministratorTenant()
    {
        var foreignAccount = new CustomerAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CustomerAccountType.Organization,
            BillingMode.Prepaid);
        var service = new BillingAdministrationService(
            new TestAccountRepository(organization: foreignAccount));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetTenantAccountAsync(
                Guid.NewGuid(),
                foreignAccount.Id,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreditReservationService_ReservesAndCommitsWalletProjectionOnce()
    {
        var accountId = Guid.NewGuid();
        var account = new CustomerAccount(
            accountId,
            Guid.NewGuid(),
            CustomerAccountType.Organization,
            BillingMode.Prepaid);
        var wallet = new TestWalletRepository(new WalletSnapshot(accountId, 10, 0, DateTimeOffset.UnixEpoch));
        var reservations = new TestReservationRepository();
        var service = new CreditReservationService(
            reservations,
            wallet,
            new CreditLinePolicyService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));

        var reservation = await service.ReserveAsync(
            account,
            await wallet.GetSnapshotAsync(accountId, CancellationToken.None),
            "AiQuery.Scanner",
            3,
            "request-1",
            CancellationToken.None);
        var retriedReservation = await service.ReserveAsync(
            account,
            await wallet.GetSnapshotAsync(accountId, CancellationToken.None),
            "AiQuery.Scanner",
            3,
            "request-1",
            CancellationToken.None);

        Assert.Same(reservation, retriedReservation);
        Assert.Equal(3, wallet.Snapshot.ReservedAmount);

        await service.CommitAsync(
            reservation,
            new UsageChargeResult(2.4m, "v1", Cached: false),
            CancellationToken.None);
        await service.CommitAsync(
            reservation,
            new UsageChargeResult(2.4m, "v1", Cached: false),
            CancellationToken.None);

        Assert.Equal(7.6m, wallet.Snapshot.Balance);
        Assert.Equal(0, wallet.Snapshot.ReservedAmount);
        Assert.Equal(UsageReservationStatus.Committed, reservation.Status);
    }

    [Fact]
    public async Task CreditReservationService_ReleasesFailedWorkWithoutCharge()
    {
        var accountId = Guid.NewGuid();
        var account = new CustomerAccount(
            accountId,
            Guid.NewGuid(),
            CustomerAccountType.Individual,
            BillingMode.Prepaid);
        var wallet = new TestWalletRepository(new WalletSnapshot(accountId, 4, 0, DateTimeOffset.UnixEpoch));
        var service = new CreditReservationService(
            new TestReservationRepository(),
            wallet,
            new CreditLinePolicyService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));

        var reservation = await service.ReserveAsync(
            account,
            wallet.Snapshot,
            "AiQuery.Scanner",
            1,
            "failed-request",
            CancellationToken.None);

        await service.ReleaseAsync(reservation, "Provider failure", CancellationToken.None);
        await service.ReleaseAsync(reservation, "Duplicate release", CancellationToken.None);

        Assert.Equal(4, wallet.Snapshot.Balance);
        Assert.Equal(0, wallet.Snapshot.ReservedAmount);
        Assert.Equal(UsageReservationStatus.Released, reservation.Status);
        Assert.Equal("Provider failure", reservation.FinalizationReason);
    }

    [Fact]
    public async Task CreditReservationService_ExpiresAbandonedReservationsAndReleasesCapacity()
    {
        var accountId = Guid.NewGuid();
        var repository = new TestReservationRepository();
        var wallet = new TestWalletRepository(new WalletSnapshot(accountId, 4, 1, DateTimeOffset.UnixEpoch));
        var expiredReservation = new UsageReservation(
            Guid.NewGuid(),
            accountId,
            "abandoned-request",
            "AiQuery.Scanner",
            1,
            DateTimeOffset.Parse("2026-05-26T11:00:00Z"),
            DateTimeOffset.Parse("2026-05-26T11:05:00Z"));
        await repository.SaveAsync(expiredReservation, CancellationToken.None);
        var service = new CreditReservationService(
            repository,
            wallet,
            new CreditLinePolicyService(),
            new FixedTimeProvider(DateTimeOffset.Parse("2026-05-26T12:00:00Z")));

        var expiredCount = await service.ExpireAbandonedAsync(10, CancellationToken.None);
        var retriedCount = await service.ExpireAbandonedAsync(10, CancellationToken.None);

        Assert.Equal(1, expiredCount);
        Assert.Equal(0, retriedCount);
        Assert.Equal(0, wallet.Snapshot.ReservedAmount);
        Assert.Equal(UsageReservationStatus.Expired, expiredReservation.Status);
        Assert.Equal("Reservation expired before finalization.", expiredReservation.FinalizationReason);
    }

    [Fact]
    public async Task UsageAccountingService_DoesNotAppendDuplicateLedgerEntry()
    {
        var ledger = new TestLedgerRepository();
        var service = new UsageAccountingService(ledger);
        var entry = new UsageLedgerEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            UsageLedgerEntryType.Charge,
            "AiQuery.Scanner",
            1,
            "v1",
            "request-1",
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"),
            "partner-user-123");

        await service.AppendAsync(entry, CancellationToken.None);
        await service.AppendAsync(entry, CancellationToken.None);

        Assert.Single(ledger.Entries);
        Assert.Equal("partner-user-123", ledger.Entries[0].ExternalUserId);
    }

    [Fact]
    public async Task FinancialAccountingService_DoesNotRecordDuplicatePaymentCallback()
    {
        var repository = new TestFinancialTransactionRepository();
        var service = new FinancialAccountingService(repository);
        var transaction = new FinancialTransaction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FinancialTransactionType.Payment,
            500_000m,
            "IRR",
            "gateway-callback-1",
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"));

        await service.RecordAsync(transaction, CancellationToken.None);
        await service.RecordAsync(transaction, CancellationToken.None);

        Assert.Single(repository.Transactions);
    }

    [Fact]
    public async Task FinancialAccountingService_RejectsChangedTransactionWithSameIdempotencyKey()
    {
        var repository = new TestFinancialTransactionRepository();
        var service = new FinancialAccountingService(repository);
        var transaction = new FinancialTransaction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FinancialTransactionType.Payment,
            500_000m,
            "IRR",
            "gateway-callback-1",
            DateTimeOffset.Parse("2026-05-26T12:00:00Z"));
        await service.RecordAsync(transaction, CancellationToken.None);

        var changed = transaction with { Amount = 600_000m };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordAsync(changed, CancellationToken.None));
    }

    [Fact]
    public void WalletProjectionBuilder_RebuildsBalanceFromImmutableChargeAndRefundEntries()
    {
        var accountId = Guid.NewGuid();
        var common = new
        {
            ActorId = Guid.NewGuid(),
            TenantId = Guid.NewGuid()
        };
        var entries = new[]
        {
            new UsageLedgerEntry(
                Guid.NewGuid(), accountId, common.ActorId, common.TenantId, null,
                UsageLedgerEntryType.Charge, "AiQuery.Scanner", 3, "v1", "charge-1",
                DateTimeOffset.Parse("2026-05-26T10:00:00Z")),
            new UsageLedgerEntry(
                Guid.NewGuid(), accountId, common.ActorId, common.TenantId, null,
                UsageLedgerEntryType.Refund, "AiQuery.Scanner", 1, "v1", "refund-1",
                DateTimeOffset.Parse("2026-05-26T10:05:00Z"))
        };

        var snapshot = new WalletProjectionBuilder().Rebuild(
            accountId,
            10,
            0,
            entries,
            DateTimeOffset.Parse("2026-05-26T10:06:00Z"));

        Assert.Equal(8, snapshot.Balance);
    }

    [Fact]
    public async Task WalletEntitlementService_RejectsIndividualWithoutCapacity()
    {
        var accountId = Guid.NewGuid();
        var account = new CustomerAccount(
            accountId,
            Guid.NewGuid(),
            CustomerAccountType.Individual,
            BillingMode.Prepaid);
        var policy = new PricingPolicy(
            "v1",
            new Dictionary<string, decimal> { ["AiQuery.Scanner"] = 2 },
            CachedMultiplier: 0.25m,
            ZeroChargeStatuses: new HashSet<string>());
        var service = new WalletEntitlementService(
            new TestWalletRepository(new WalletSnapshot(accountId, 1, 0, DateTimeOffset.UnixEpoch)),
            new ConfiguredPricingPolicyProvider([policy]),
            "v1");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateCanExecuteAsync(
            account,
            "AiQuery.Scanner",
            CancellationToken.None));
    }

    private sealed class TestAccountRepository(
        CustomerAccount? organization = null,
        CustomerAccount? individual = null) : ICustomerAccountRepository
    {
        public Task<CustomerAccount?> FindAsync(Guid customerAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { organization, individual }
                .FirstOrDefault(account => account?.Id == customerAccountId));

        public Task<CustomerAccount?> FindOrganizationByTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken) =>
            Task.FromResult(organization?.TenantId == tenantId ? organization : null);

        public Task<CustomerAccount?> FindIndividualByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(individual?.TenantId == tenantId ? individual : null);
    }

    private sealed class TestWalletRepository(WalletSnapshot snapshot) : IWalletProjectionRepository
    {
        public WalletSnapshot Snapshot { get; private set; } = snapshot;

        public Task<WalletSnapshot> GetSnapshotAsync(
            Guid customerAccountId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Snapshot.CustomerAccountId == customerAccountId
                ? Snapshot
                : throw new KeyNotFoundException());

        public Task SaveAsync(WalletSnapshot value, CancellationToken cancellationToken)
        {
            Snapshot = value;
            return Task.CompletedTask;
        }
    }

    private sealed class TestReservationRepository : IUsageReservationRepository
    {
        private readonly Dictionary<string, UsageReservation> _reservations = [];

        public Task<UsageReservation?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken)
        {
            _reservations.TryGetValue(idempotencyKey, out var reservation);
            return Task.FromResult(reservation);
        }

        public Task<IReadOnlyCollection<UsageReservation>> FindExpiredReservedAsync(
            DateTimeOffset asOf,
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<UsageReservation>>(
                _reservations.Values
                    .Where(reservation =>
                        reservation.Status == UsageReservationStatus.Reserved &&
                        reservation.ExpiresAt <= asOf)
                    .OrderBy(reservation => reservation.ExpiresAt)
                    .Take(maximumCount)
                    .ToArray());

        public Task SaveAsync(UsageReservation reservation, CancellationToken cancellationToken)
        {
            _reservations[reservation.IdempotencyKey] = reservation;
            return Task.CompletedTask;
        }
    }

    private sealed class TestLedgerRepository : IUsageLedgerRepository
    {
        public List<UsageLedgerEntry> Entries { get; } = [];

        public Task<UsageLedgerEntry?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.IdempotencyKey == idempotencyKey));

        public Task AppendAsync(UsageLedgerEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<UsageLedgerEntry>> QueryAsync(
            Guid customerAccountId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<UsageLedgerEntry>>(
                Entries.Where(entry =>
                    entry.CustomerAccountId == customerAccountId &&
                    entry.OccurredAt >= from &&
                    entry.OccurredAt <= to).ToArray());
    }

    private sealed class TestFinancialTransactionRepository : IFinancialTransactionRepository
    {
        public List<FinancialTransaction> Transactions { get; } = [];

        public Task<FinancialTransaction?> FindByIdempotencyKeyAsync(
            string idempotencyKey,
            CancellationToken cancellationToken) =>
            Task.FromResult(Transactions.FirstOrDefault(transaction =>
                transaction.IdempotencyKey == idempotencyKey));

        public Task AppendAsync(FinancialTransaction transaction, CancellationToken cancellationToken)
        {
            Transactions.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<FinancialTransaction>> QueryAsync(
            Guid customerAccountId,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FinancialTransaction>>(
                Transactions.Where(transaction =>
                    transaction.CustomerAccountId == customerAccountId &&
                    transaction.OccurredAt >= from &&
                    transaction.OccurredAt <= to).ToArray());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
