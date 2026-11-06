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
    public async Task Purchase_history_lists_only_the_callers_own_receipts_newest_first()
    {
        var (billingOptions, authOptions, request, userId, tenantId, _) = await SeedAsync(500, "TA-TOPUP-500");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());
        await service.DeliverAsync(request, CancellationToken.None);

        var mine = await service.ListPurchasesAsync(userId, tenantId, 20, CancellationToken.None);
        var purchase = Assert.Single(mine);
        Assert.Equal(request.InvoiceId, purchase.InvoiceId);
        Assert.Equal(500, purchase.CreditsPurchased);
        Assert.Equal("granted", purchase.Status);
        Assert.Equal(WindowEnd, purchase.ExpiresAtUtc);

        Assert.Empty(await service.ListPurchasesAsync(Guid.NewGuid(), tenantId, 20, CancellationToken.None));
        Assert.Empty(await service.ListPurchasesAsync(userId, Guid.NewGuid(), 20, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ListPurchasesAsync(userId, tenantId, TahlilAppTopUpService.MaxPurchaseHistory + 1, CancellationToken.None));
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
    public async Task Partner_request_serializes_exactly_the_four_contract_fields()
    {
        var (_, _, request, _, _, _) = await SeedAsync(500, "TA-TOPUP-500");
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(request, options);
        Assert.Equal(new[] { "creditsPurchased", "invoiceId", "paidAtUtc", "userId" }, json.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<TopUpDeliveryRequest>(
            """{"invoiceId":"ta-invoice-001","userId":"tahlil-test-001","creditsPurchased":500,"paidAtUtc":"2026-11-01T08:30:00Z","subscription":{"slug":"sapio_pro"}}""", options));
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
        var futurePaidAt = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request with { InvoiceId = "ta-invoice-future-paid-at", PaidAtUtc = Now.AddSeconds(1) }, CancellationToken.None));
        Assert.Equal("invalid-topup-event", futurePaidAt.Code);
        var quantity = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request with { InvoiceId = "ta-invoice-unsupported", CreditsPurchased = 200 }, CancellationToken.None));
        Assert.Equal("topup-quantity-invalid", quantity.Code);
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task Payment_outside_every_stored_paid_window_is_persisted_as_ineligible_without_credit()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());
        var beforeWindow = request with { InvoiceId = "ta-invoice-before-window", PaidAtUtc = WindowStart.AddSeconds(-1) };

        var rejected = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(beforeWindow, CancellationToken.None));
        Assert.Equal("topup-ineligible", rejected.Code);
        var receipt = await billing.TahlilAppTopUpPurchases.SingleAsync(x => x.InvoiceId == "ta-invoice-before-window");
        Assert.Equal("ineligible-unfulfilled", receipt.Status);
        Assert.Null(receipt.GrantId);
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
        Assert.Empty(await billing.TopUpCreditSources.ToListAsync());
    }

    [Fact]
    public async Task Pro_only_account_is_eligible_and_grant_expires_with_the_pro_window()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        var proEnd = WindowEnd.AddDays(5);
        await using (var setup = new BillingDbContext(billingOptions))
        {
            setup.ExternalPlanSubscriptions.RemoveRange(await setup.ExternalPlanSubscriptions.ToListAsync());
            (await setup.CustomerAccounts.SingleAsync(x => x.Id == accountId)).SubscriptionPlanCode = "Pro";
            await setup.SaveChangesAsync();
        }
        var proId = await AddWindowAsync(billingOptions, accountId, "Pro", WindowStart, proEnd);
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());

        var outcome = await service.DeliverAsync(request, CancellationToken.None);

        Assert.Equal("granted", outcome.Status);
        Assert.Equal(proEnd, outcome.ExpiresAtUtc);
        Assert.Equal(proId, (await billing.TopUpCreditSources.SingleAsync()).ExternalPlanSubscriptionId);
        Assert.Equal(117, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Theory]
    [InlineData(10, "Pro")]   // Pro expires later: latest expiry wins.
    [InlineData(-10, "Plus")] // Plus expires later: latest expiry wins even over Pro.
    [InlineData(0, "Pro")]    // Exact expiry tie: established Pro-over-Plus choice.
    public async Task Overlapping_plus_and_pro_are_eligible_and_grant_from_the_window_chosen_by_overlap_precedence(int proEndOffsetDays, string expectedPlan)
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(500, "TA-TOPUP-500");
        var proEnd = WindowEnd.AddDays(proEndOffsetDays);
        var proId = await AddWindowAsync(billingOptions, accountId, "Pro", WindowStart.AddDays(3), proEnd);
        Guid plusId;
        await using (var lookup = new BillingDbContext(billingOptions))
            plusId = (await lookup.ExternalPlanSubscriptions.SingleAsync(x => x.PlanCode == "Plus")).Id;
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());

        var outcome = await service.DeliverAsync(request, CancellationToken.None);

        var expectedWindowId = expectedPlan == "Pro" ? proId : plusId;
        var expectedExpiry = expectedPlan == "Pro" ? proEnd : WindowEnd;
        Assert.Equal("granted", outcome.Status);
        Assert.Equal(500, outcome.CreditsGranted);
        Assert.Equal(expectedExpiry, outcome.ExpiresAtUtc);
        var receipt = await billing.TahlilAppTopUpPurchases.SingleAsync();
        Assert.Equal(expectedWindowId, receipt.ExternalPlanSubscriptionId);
        Assert.Equal(expectedPlan == "Pro" ? "sapio_pro" : "sapio_plus", receipt.PlanSlug);
        var source = await billing.TopUpCreditSources.SingleAsync();
        Assert.Equal(expectedWindowId, source.ExternalPlanSubscriptionId);
        Assert.Equal(expectedExpiry, source.ExpiresAtUtc);
        Assert.Equal(517, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);

        var replay = await service.DeliverAsync(request, CancellationToken.None);
        Assert.Equal(outcome, replay);
        Assert.Equal(1, await billing.TopUpCreditSources.CountAsync());
    }

    [Fact]
    public async Task Active_max_at_payment_time_is_ineligible_even_when_plus_and_pro_overlap()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        await AddWindowAsync(billingOptions, accountId, "Pro", WindowStart, WindowEnd.AddDays(10));
        await AddWindowAsync(billingOptions, accountId, "Max", Now.AddDays(-1), Now.AddDays(20));
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());

        var rejected = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request, CancellationToken.None));

        Assert.Equal("topup-ineligible", rejected.Code);
        Assert.Equal("ineligible-unfulfilled", (await billing.TahlilAppTopUpPurchases.SingleAsync()).Status);
        Assert.Empty(await billing.TopUpCreditSources.ToListAsync());
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task Free_account_without_paid_window_is_ineligible()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        await using (var setup = new BillingDbContext(billingOptions))
        {
            setup.ExternalPlanSubscriptions.RemoveRange(await setup.ExternalPlanSubscriptions.ToListAsync());
            (await setup.CustomerAccounts.SingleAsync(x => x.Id == accountId)).SubscriptionPlanCode = "Free";
            await setup.SaveChangesAsync();
        }
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(Now), new NoOpRefresher());

        var rejected = await Assert.ThrowsAsync<TopUpContractException>(() => service.DeliverAsync(request, CancellationToken.None));

        Assert.Equal("topup-ineligible", rejected.Code);
        Assert.Empty(await billing.TopUpCreditSources.ToListAsync());
        Assert.Equal(17, (await billing.WalletProjections.SingleAsync(x => x.CustomerAccountId == accountId)).Balance);
    }

    [Fact]
    public async Task Overlap_delivered_after_the_selected_window_expired_is_persisted_without_credit()
    {
        var (billingOptions, authOptions, request, _, _, accountId) = await SeedAsync(100, "TA-TOPUP-100");
        var proEnd = WindowEnd.AddDays(10);
        await AddWindowAsync(billingOptions, accountId, "Pro", WindowStart, proEnd);
        await using var billing = new BillingDbContext(billingOptions);
        await using var auth = new AuthDbContext(authOptions);
        var service = new TahlilAppTopUpService(billing, auth, new FixedTimeProvider(proEnd.AddSeconds(1)), new NoOpRefresher());

        var outcome = await service.DeliverAsync(request, CancellationToken.None);

        Assert.Equal("expired-unfulfilled", outcome.Status);
        Assert.Equal(0, outcome.CreditsGranted);
        Assert.Empty(await billing.TopUpCreditSources.ToListAsync());
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

    private static async Task<Guid> AddWindowAsync(DbContextOptions<BillingDbContext> billingOptions, Guid accountId,
        string planCode, DateTimeOffset startsAt, DateTimeOffset expiresAt)
    {
        var id = Guid.NewGuid();
        await using var billing = new BillingDbContext(billingOptions);
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = id, CustomerAccountId = accountId, PlanCode = planCode, StartsAtUtc = startsAt, ExpiresAtUtc = expiresAt,
            IdentityKey = $"sapio_{planCode.ToLowerInvariant()}:{startsAt.ToUnixTimeMilliseconds()}:{expiresAt.ToUnixTimeMilliseconds()}"
        });
        await billing.SaveChangesAsync();
        return id;
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
