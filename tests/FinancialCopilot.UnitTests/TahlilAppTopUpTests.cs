using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppTopUpTests
{
    [Theory]
    [InlineData(100, "TA-TOPUP-100")]
    [InlineData(500, "TA-TOPUP-500")]
    [InlineData(1000, "TA-TOPUP-1000")]
    public async Task Deliver_grants_database_allowlisted_quantity_once_and_replays_after_plan_change(int credits, string package)
    {
        var (billingOptions, authOptions, request, userId, tenantId, accountId) = await SeedAsync(credits, package);
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());

        var first = await service.DeliverAsync(request, CancellationToken.None);
        Assert.Equal("granted", first.Status);
        Assert.Equal(credits, first.CreditsGranted);
        Assert.Equal(WindowEnd, first.ExpiresAtUtc);
        var wallet = await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId);
        Assert.Equal(credits + 17, wallet.Balance);

        var account = await billing.CustomerAccounts.SingleAsync(x => x.Id == accountId);
        account.SubscriptionPlanCode = "Max";
        await billing.SaveChangesAsync();
        var replay = await service.DeliverAsync(request, CancellationToken.None); // fresh JWT, same invoice and business body
        Assert.Equal(first, replay);
        Assert.Equal(credits + 17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
        Assert.Equal(1, await billing.TopUpCreditSources.CountAsync());
        Assert.Equal(1, await billing.UsageLedgerEntries.CountAsync(x => x.OperationCode == "Billing.TahlilAppCreditTopUpGrant"));

        Assert.NotNull(await service.GetStatusAsync(userId, tenantId, request.InvoiceId, CancellationToken.None));
        Assert.Null(await service.GetStatusAsync(Guid.NewGuid(), tenantId, request.InvoiceId, CancellationToken.None));
    }

    [Fact]
    public async Task Delayed_delivery_after_original_window_expiry_is_persisted_without_credit()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(WindowEnd.AddSeconds(1)), new NoOpRefresher());
        var outcome = await service.DeliverAsync(request, CancellationToken.None);
        Assert.Equal("expired-unfulfilled", outcome.Status);
        Assert.Equal(0, outcome.CreditsGranted);
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
        Assert.Empty(await billing.TopUpCreditSources.ToListAsync());
    }

    [Fact]
    public async Task Conflicting_reuse_of_invoice_identity_does_not_change_wallet()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(500, "TA-TOPUP-500");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());
        await service.DeliverAsync(request, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<TopUpContractException>(() =>
            service.DeliverAsync(request with { CreditsPurchased = 100 }, CancellationToken.None));
        Assert.Equal("topup-identity-conflict", exception.Code);
        Assert.Equal(517, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task Partner_request_serializes_exactly_the_five_contract_fields()
    {
        var (_, _, request, _, _, _) = await SeedAsync(500, "TA-TOPUP-500");
        var json = System.Text.Json.JsonSerializer.SerializeToElement(request, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(new[] { "creditsPurchased", "invoiceId", "paidAtUtc", "subscription", "userId" }, json.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "expiresAtUtc", "slug", "startsAtUtc" }, json.GetProperty("subscription").EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Invalid_invoice_or_unoffered_quantity_never_grants()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(500, "TA-TOPUP-500");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());
        var invalid = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request with { InvoiceId = " " }, CancellationToken.None));
        Assert.Equal("invalid-topup-event", invalid.Code);
        var outsideWindow = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request with { InvoiceId = "ta-invoice-outside-window", PaidAtUtc = WindowEnd }, CancellationToken.None));
        Assert.Equal("invalid-topup-event", outsideWindow.Code);
        var quantity = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request with { InvoiceId = "ta-invoice-unsupported", CreditsPurchased = 200 }, CancellationToken.None));
        Assert.Equal("topup-quantity-invalid", quantity.Code);
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task Partner_jti_is_consumed_once_and_duplicate_token_is_rejected()
    {
        var (billingOptions, authOptions, _, _, _, _) = await SeedAsync(100, "TA-TOPUP-100");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());
        var expiry = Now.AddMinutes(2);
        Assert.True(await service.TryConsumeTokenJtiAsync("configured-issuer", "unique-random-jti-0001", expiry, CancellationToken.None));
        Assert.False(await service.TryConsumeTokenJtiAsync("configured-issuer", "unique-random-jti-0001", expiry, CancellationToken.None));
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowStart = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2027, 1, 18, 0, 0, 0, TimeSpan.Zero);

    private static async Task<(DbContextOptions<BillingDbContext>, DbContextOptions<AuthDbContext>, TopUpDeliveryRequest, Guid, Guid, Guid)> SeedAsync(int credits, string package)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var billingOptions = new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase("topup-b-" + suffix).Options;
        var authOptions = new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase("topup-a-" + suffix).Options;
        var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid(); var accountId = Guid.NewGuid();
        var window = new TopUpWindowResponse("sapio_plus", WindowStart, WindowEnd);
        await using (var billing = new BillingDbContext(billingOptions))
        {
            billing.CustomerAccounts.Add(new CustomerAccountRow { Id = accountId, TenantId = tenantId, UserId = userId, AccountType = "Individual", BillingMode = "Prepaid", SubscriptionPlanCode = "Plus", SubscriptionActivationIsAuthoritative = true, SubscriptionEffectiveFrom = WindowStart, SubscriptionEffectiveTo = WindowEnd });
            billing.WalletProjections.Add(new WalletProjectionRow { CustomerAccountId = accountId, Balance = 17, ReservedAmount = 0, UpdatedAt = Now, Revision = 1 });
            var key = $"sapio_plus:{WindowStart.ToUnixTimeMilliseconds()}:{WindowEnd.ToUnixTimeMilliseconds()}";
            billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow { Id = Guid.NewGuid(), CustomerAccountId = accountId, IdentityKey = key, PlanCode = "Plus", StartsAtUtc = WindowStart, ExpiresAtUtc = WindowEnd });
            foreach (var amount in new[] { 100, 500, 1000 }) billing.TopUpOffers.Add(new TopUpOfferRow { OfferId = $"TA-TOPUP-{amount}", Version = "v1", Credits = amount, IsActive = true });
            await billing.SaveChangesAsync();
        }
        await using (var auth = new AuthDbContext(authOptions))
        {
            auth.Users.Add(new FinancialCopilotUser { Id = userId, UserName = "topup-test", NormalizedUserName = "TOPUP-TEST", IsEnabled = true });
            auth.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow { Id = Guid.NewGuid(), Provider = "TahlilApp", ExternalUserId = "tahlil-test-001", LocalUserId = userId, TenantId = tenantId, Status = "Active", LinkedAtUtc = Now });
            await auth.SaveChangesAsync();
        }
        return (billingOptions, authOptions,
            new TopUpDeliveryRequest("ta-invoice-001", "tahlil-test-001", credits, Now),
            userId, tenantId, accountId);
    }

    private sealed class NoOpRefresher : ITahlilAppEntitlementRefresher
    {
        public Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
