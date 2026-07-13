using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Accounts;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramMembership088CompletionTests
{
    static TelegramMembership088CompletionTests() => SQLitePCL.Batteries.Init();

    [Theory]
    [InlineData(TelegramChannelMembershipStatus.Creator, true, 1)]
    [InlineData(TelegramChannelMembershipStatus.Administrator, true, 1)]
    [InlineData(TelegramChannelMembershipStatus.Member, true, 1)]
    [InlineData(TelegramChannelMembershipStatus.RestrictedMember, true, 1)]
    [InlineData(TelegramChannelMembershipStatus.Left, false, 2)]
    [InlineData(TelegramChannelMembershipStatus.Kicked, false, 2)]
    [InlineData(TelegramChannelMembershipStatus.NotFound, false, 2)]
    [InlineData(TelegramChannelMembershipStatus.UnknownProviderFailure, false, 2)]
    public async Task VerifyRequiredChannelMembershipAsync_MapsEveryMembershipState(
        TelegramChannelMembershipStatus status,
        bool expectedEligible,
        int expectedActionCount)
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var telegramUserId = 77100L;
        var now = DateTimeOffset.Parse("2026-07-13T08:30:00Z");
        var timeProvider = new MutableTimeProvider(now);
        var actor = new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, actorId);
        var linkReader = new StubLinkReader(new TelegramLinkView(telegramUserId, telegramUserId, "member", now, now));
        var provider = new StubMembershipProvider((_, _, _, _) =>
            Task.FromResult(new TelegramProviderMembershipObservation(
                status,
                timeProvider.UtcNow,
                status == TelegramChannelMembershipStatus.UnknownProviderFailure
                    ? TelegramMembershipFailureCategory.ProviderUnavailable
                    : TelegramMembershipFailureCategory.None)));
        var service = CreateService(fixture, timeProvider, linkReader, provider);

        var result = await service.VerifyRequiredChannelMembershipAsync(actor, $"state-{status}", CancellationToken.None);

        Assert.Equal(expectedEligible, result.IsEligible);
        Assert.Equal(expectedActionCount, result.Actions?.Count);
    }

    [Fact]
    public async Task EnsureAsync_ConcurrentFirstRequests_PersistsSingleDailyGrant()
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-13T08:00:00Z");
        await SeedVerificationAsync(fixture.AuthOptions, actorId, tenantId, TelegramChannelMembershipStatus.Member, true, now.AddHours(1));
        await SeedBillingAccountAsync(fixture.BillingOptions, accountId, actorId, tenantId, balance: 0m);

        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var actor = new BillableActorContext(actorId, tenantId, actorId, null, null);
        var timeProvider = new MutableTimeProvider(now);

        var firstService = CreateService(fixture, timeProvider);
        var secondService = CreateService(fixture, timeProvider);

        var firstTask = firstService.EnsureAsync(actor, account, "concurrent-a", CancellationToken.None);
        var secondTask = secondService.EnsureAsync(actor, account, "concurrent-b", CancellationToken.None);
        var results = await Task.WhenAll(firstTask, secondTask);

        await using var billingDb = new BillingDbContext(fixture.BillingOptions);
        Assert.Single(billingDb.DailyFreeAllowanceGrants);
        Assert.Single(billingDb.UsageLedgerEntries.Where(row => row.OperationCode == "Telegram.DailyFreeAllowance"));
        Assert.Equal(5m, billingDb.WalletProjections.Single(row => row.CustomerAccountId == accountId).Balance);
        Assert.Equal(1, results.Count(result => result.Granted));
        Assert.Equal(1, results.Count(result => !result.Granted));
    }

    [Fact]
    public async Task EnsureAsync_ExpiresUnusedAllowanceAtTehranMidnight_AndGrantsNewDay()
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T20:20:00Z"));
        await SeedVerificationAsync(fixture.AuthOptions, actorId, tenantId, TelegramChannelMembershipStatus.Member, true, timeProvider.UtcNow.AddHours(2));
        await SeedBillingAccountAsync(fixture.BillingOptions, accountId, actorId, tenantId, balance: 0m);

        var service = CreateService(fixture, timeProvider);
        var actor = new BillableActorContext(actorId, tenantId, actorId, null, null);
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);

        var first = await service.EnsureAsync(actor, account, "day-one", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(20));
        var second = await service.EnsureAsync(actor, account, "day-two", CancellationToken.None);

        await using var billingDb = new BillingDbContext(fixture.BillingOptions);
        var grants = billingDb.DailyFreeAllowanceGrants.ToList().OrderBy(row => row.GrantedAtUtc).ToArray();
        Assert.Equal(2, grants.Length);
        Assert.NotEqual(grants[0].AllowanceDateKey, grants[1].AllowanceDateKey);
        Assert.NotNull(grants[0].ExpiredAtUtc);
        Assert.Equal(5m, grants[0].ExpiredCredits);
        Assert.True(first.Granted);
        Assert.True(second.Granted);
        Assert.Equal(5m, billingDb.WalletProjections.Single(row => row.CustomerAccountId == accountId).Balance);
    }

    [Fact]
    public async Task EnsureAsync_ExpiredEligibleVerification_DoesNotGrant_UntilReverified()
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var telegramUserId = 99000L;
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-07-13T09:00:00Z");
        var timeProvider = new MutableTimeProvider(now);
        await SeedVerificationAsync(fixture.AuthOptions, actorId, tenantId, TelegramChannelMembershipStatus.Member, true, now.AddMinutes(-1));
        await SeedBillingAccountAsync(fixture.BillingOptions, accountId, actorId, tenantId, balance: 0m);

        var actor = new BillableActorContext(actorId, tenantId, actorId, null, null);
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var linkReader = new StubLinkReader(new TelegramLinkView(telegramUserId, telegramUserId, "member", now, now));
        var provider = new StubMembershipProvider((_, _, _, _) =>
            Task.FromResult(new TelegramProviderMembershipObservation(TelegramChannelMembershipStatus.Member, timeProvider.UtcNow)));
        var service = CreateService(fixture, timeProvider, linkReader, provider);

        var stale = await service.EnsureAsync(actor, account, "stale-cache", CancellationToken.None);
        var verified = await service.VerifyRequiredChannelMembershipAsync(
            new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, actorId),
            "refresh-cache",
            CancellationToken.None);
        var refreshed = await service.EnsureAsync(actor, account, "fresh-cache", CancellationToken.None);

        Assert.False(stale.Granted);
        Assert.True(verified.IsEligible);
        Assert.True(refreshed.Granted);

        await using var billingDb = new BillingDbContext(fixture.BillingOptions);
        Assert.Single(billingDb.DailyFreeAllowanceGrants);
    }

    [Fact]
    public async Task VerifyRequiredChannelMembershipAsync_ReturnsLocalizedJoinAndRecheckActions()
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var telegramUserId = 99001L;
        var actor = new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, actorId);
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T09:00:00Z"));
        var provider = new StubMembershipProvider((_, _, _, _) =>
            Task.FromResult(new TelegramProviderMembershipObservation(TelegramChannelMembershipStatus.Left, timeProvider.UtcNow)));
        var service = CreateService(
            fixture,
            timeProvider,
            new StubLinkReader(new TelegramLinkView(telegramUserId, telegramUserId, "member", timeProvider.UtcNow, timeProvider.UtcNow)),
            provider);

        var result = await service.VerifyRequiredChannelMembershipAsync(actor, "verify-actions", CancellationToken.None);

        Assert.False(result.IsEligible);
        Assert.Equal(2, result.Actions?.Count);
        Assert.Equal("ورود به کانال", result.Actions?[0].Label);
        Assert.Equal("بررسی دوباره عضویت", result.Actions?[1].Label);
        Assert.Equal("tgm.join.v1", result.Actions?[0].CallbackData);
        Assert.Equal("tgm.recheck.v1", result.Actions?[1].CallbackData);
    }

    [Fact]
    public async Task RevalidationProcessor_BackoffsOnProviderFailure_ThenDeadLettersAfterRetryLimit()
    {
        var services = new ServiceCollection();
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T10:00:00Z"));
        var provider = new StubMembershipProvider((_, _, _, _) =>
            Task.FromResult(new TelegramProviderMembershipObservation(
                TelegramChannelMembershipStatus.UnknownProviderFailure,
                timeProvider.UtcNow,
                TelegramMembershipFailureCategory.ProviderUnavailable)));
        var authDatabaseName = Guid.NewGuid().ToString("N");
        var billingDatabaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(authDatabaseName));
        services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(billingDatabaseName));
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<ITelegramChannelMembershipProvider>(provider);
        services.AddSingleton<ITelegramIdentityLinkReader, StubLinkReader>();
        services.AddSingleton<IBillableAccountResolver, StubAccountResolver>();
        services.AddSingleton<IWalletService, StubWalletService>();
        services.AddSingleton<IOptions<TelegramMembershipOptions>>(Options.Create(new TelegramMembershipOptions
        {
            RequiredChannelId = "@test_channel",
            VerificationCacheMinutes = 60,
            ProviderFailureCacheMinutes = 5,
            DailyFreeCredits = 5,
            PolicyVersion = "telegram-free-daily-v1"
        }));
        services.AddSingleton<IOptions<TelegramMembershipRevalidationOptions>>(Options.Create(new TelegramMembershipRevalidationOptions
        {
            Enabled = true,
            BatchSize = 10,
            MaxConcurrency = 2,
            LeaseSeconds = 60,
            RetryCount = 3,
            InitialBackoffSeconds = 30,
            MaxBackoffSeconds = 120
        }));
        services.AddLogging();
        services.AddScoped<TelegramMembershipService>();
        services.AddScoped<TelegramMembershipRevalidationProcessor>();

        using (var seedRoot = services.BuildServiceProvider())
        using (var seedScope = seedRoot.CreateScope())
        {
            var authDb = seedScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var billingDb = seedScope.ServiceProvider.GetRequiredService<BillingDbContext>();
            await authDb.Database.EnsureCreatedAsync();
            await billingDb.Database.EnsureCreatedAsync();
            authDb.Set<TelegramAccountLinkRow>().Add(new TelegramAccountLinkRow
            {
                Id = Guid.NewGuid(),
                ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TelegramUserId = 88001,
                TelegramChatId = 88001,
                LinkedAtUtc = timeProvider.UtcNow,
                LastVerifiedAtUtc = timeProvider.UtcNow
            });
            authDb.TelegramMembershipRevalidations.Add(new TelegramMembershipRevalidationRow
            {
                Id = Guid.NewGuid(),
                ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TelegramUserId = 88001,
                ChannelId = "@test_channel",
                NextDueAtUtc = timeProvider.UtcNow.AddMinutes(-1),
                CorrelationId = "seed"
            });
            await authDb.SaveChangesAsync();
        }

        using var providerRoot = services.BuildServiceProvider();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var scope = providerRoot.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<TelegramMembershipRevalidationProcessor>();
            var processed = await processor.ProcessDueAsync("worker-1", CancellationToken.None);
            Assert.Equal(1, processed);
            timeProvider.Advance(TimeSpan.FromMinutes(6));
        }

        using var assertScope = providerRoot.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await assertDb.TelegramMembershipRevalidations.SingleAsync();
        Assert.Equal(3, row.AttemptCount);
        Assert.NotNull(row.DeadLetteredAtUtc);
        Assert.Equal("ProviderUnavailable", row.LastFailureCategory);
    }

    [Fact]
    public async Task ReservationBurst_ConcurrentRequests_DoNotOverspendDailyAllowanceWallet()
    {
        using var fixture = new SqliteFixture();
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await SeedBillingAccountAsync(fixture.BillingOptions, accountId, actorId, tenantId, balance: 5m);
        var account = new CustomerAccount(accountId, tenantId, CustomerAccountType.Individual, BillingMode.Prepaid);
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-13T10:30:00Z"));

        var attempts = Enumerable.Range(0, 8)
            .Select(index => ReserveOnceAsync(fixture.BillingOptions, account, timeProvider, $"burst-{index}"))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        await using var assertDb = new BillingDbContext(fixture.BillingOptions);
        var wallet = await assertDb.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);

        Assert.InRange(results.Count(result => result), 1, 5);
        Assert.True(wallet.ReservedAmount <= 5m);
        Assert.True(await assertDb.UsageReservations.CountAsync(row => row.CustomerAccountId == accountId) <= 5);
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        return connection;
    }

    private static TelegramMembershipService CreateService(
        SqliteFixture fixture,
        TimeProvider timeProvider,
        ITelegramIdentityLinkReader? linkReader = null,
        ITelegramChannelMembershipProvider? provider = null)
    {
        var authDb = new AuthDbContext(fixture.AuthOptions);
        var billingDb = new BillingDbContext(fixture.BillingOptions);
        authDb.Database.EnsureCreated();
        billingDb.Database.EnsureCreated();
        return new TelegramMembershipService(
            authDb,
            billingDb,
            linkReader ?? new StubLinkReader(),
            provider ?? new StubMembershipProvider((_, _, _, _) => Task.FromResult(new TelegramProviderMembershipObservation(TelegramChannelMembershipStatus.Member, timeProvider.GetUtcNow()))),
            new StubAccountResolver(),
            new StubWalletService(),
            Options.Create(new TelegramMembershipOptions
            {
                RequiredChannelId = "@test_channel",
                VerificationCacheMinutes = 60,
                ProviderFailureCacheMinutes = 5,
                DailyFreeCredits = 5,
                PolicyVersion = "telegram-free-daily-v1"
            }),
            timeProvider,
            NullLogger<TelegramMembershipService>.Instance);
    }

    private static async Task SeedVerificationAsync(
        DbContextOptions<AuthDbContext> options,
        Guid actorId,
        Guid tenantId,
        TelegramChannelMembershipStatus status,
        bool eligible,
        DateTimeOffset expiresAtUtc)
    {
        await using var authDb = new AuthDbContext(options);
        await authDb.Database.EnsureCreatedAsync();
        authDb.TelegramChannelMembershipVerifications.Add(new TelegramChannelMembershipVerificationRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            TenantId = tenantId,
            TelegramUserId = 1,
            ChannelId = "@test_channel",
            Status = status.ToString(),
            IsEligible = eligible,
            ProviderObservedAtUtc = expiresAtUtc.AddMinutes(-10),
            VerifiedAtUtc = expiresAtUtc.AddMinutes(-10),
            ExpiresAtUtc = expiresAtUtc,
            FailureCategory = TelegramMembershipFailureCategory.None.ToString(),
            CorrelationId = "seed",
            IsLatest = true
        });
        await authDb.SaveChangesAsync();
    }

    private static async Task SeedBillingAccountAsync(
        DbContextOptions<BillingDbContext> options,
        Guid accountId,
        Guid actorId,
        Guid tenantId,
        decimal balance)
    {
        await using var billingDb = new BillingDbContext(options);
        await billingDb.Database.EnsureCreatedAsync();
        billingDb.CustomerAccounts.Add(new CustomerAccountRow
        {
            Id = accountId,
            TenantId = tenantId,
            UserId = actorId,
            AccountType = "Individual",
            BillingMode = "Prepaid"
        });
        billingDb.WalletProjections.Add(new WalletProjectionRow
        {
            CustomerAccountId = accountId,
            Balance = balance,
            ReservedAmount = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await billingDb.SaveChangesAsync();
    }

    private static async Task<bool> ReserveOnceAsync(
        DbContextOptions<BillingDbContext> options,
        CustomerAccount account,
        TimeProvider timeProvider,
        string idempotencyKey)
    {
        try
        {
            await using var billingDb = new BillingDbContext(options);
            var wallet = await billingDb.WalletProjections
                .AsNoTracking()
                .Where(row => row.CustomerAccountId == account.Id)
                .Select(row => new WalletSnapshot(row.CustomerAccountId, row.Balance, row.ReservedAmount, row.UpdatedAt, row.Revision))
                .SingleAsync();
            var reservationService = new UsageReservationAuthorizationService(
                billingDb,
                new FinancialCopilot.Billing.Services.CreditLinePolicyService(),
                timeProvider);
            await reservationService.ReserveAsync(
                account,
                wallet,
                "AiQuery.Scanner",
                1m,
                idempotencyKey,
                CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class SqliteFixture : IDisposable
    {
        private readonly SqliteConnection _authConnection = CreateConnection();
        private readonly SqliteConnection _billingConnection = CreateConnection();

        public SqliteFixture()
        {
            AuthOptions = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(_authConnection).Options;
            BillingOptions = new DbContextOptionsBuilder<BillingDbContext>().UseSqlite(_billingConnection).Options;
        }

        public DbContextOptions<AuthDbContext> AuthOptions { get; }
        public DbContextOptions<BillingDbContext> BillingOptions { get; }

        public void Dispose()
        {
            _authConnection.Dispose();
            _billingConnection.Dispose();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }

    private sealed class StubLinkReader(TelegramLinkView? link = null) : ITelegramIdentityLinkReader
    {
        private readonly TelegramLinkView? _link = link;

        public Task<TelegramLinkView?> GetCurrentAsync(CurrentActor actor, CancellationToken cancellationToken) =>
            Task.FromResult(_link);

        public Task<CurrentActor?> ResolveActorAsync(long telegramUserId, CancellationToken cancellationToken) =>
            Task.FromResult<CurrentActor?>(null);
    }

    private sealed class StubMembershipProvider(
        Func<long, string, string, CancellationToken, Task<TelegramProviderMembershipObservation>> handler) : ITelegramChannelMembershipProvider
    {
        public Task<TelegramProviderMembershipObservation> GetMembershipAsync(long telegramUserId, string channelId, string correlationId, CancellationToken cancellationToken) =>
            handler(telegramUserId, channelId, correlationId, cancellationToken);
    }

    private sealed class StubAccountResolver : IBillableAccountResolver
    {
        public Task<CustomerAccount> ResolveAsync(BillableActorContext actor, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerAccount(Guid.NewGuid(), actor.TenantId, CustomerAccountType.Individual, BillingMode.Prepaid));
    }

    private sealed class StubWalletService : IWalletService
    {
        public Task<WalletSnapshot> GetSnapshotAsync(Guid customerAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(new WalletSnapshot(customerAccountId, 0m, 0m, DateTimeOffset.UtcNow, 0));
    }
}
