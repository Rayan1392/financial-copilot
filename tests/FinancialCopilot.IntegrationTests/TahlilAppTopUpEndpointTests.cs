using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.IntegrationTests;

public sealed class TahlilAppTopUpEndpointTests : IClassFixture<TahlilAppTopUpApiFactory>
{
    private readonly TahlilAppTopUpApiFactory factory;
    public TahlilAppTopUpEndpointTests(TahlilAppTopUpApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task CreditBalanceIsAnonymousReadOnlyAndUsesEffectivePlanPolicy()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = DateTimeOffset.UtcNow;
        var max = new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_max:{now.AddDays(-1).ToUnixTimeMilliseconds()}:{now.AddDays(10).ToUnixTimeMilliseconds()}",
            PlanCode = "Max", StartsAtUtc = now.AddDays(-1), ExpiresAtUtc = now.AddDays(10)
        };
        billing.ExternalPlanSubscriptions.Add(max);
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_pro:{now.AddDays(-2).ToUnixTimeMilliseconds()}:{now.AddDays(30).ToUnixTimeMilliseconds()}",
            PlanCode = "Pro", StartsAtUtc = now.AddDays(-2), ExpiresAtUtc = now.AddDays(30)
        });
        var expiredStart = now.AddDays(-40);
        var expiredEnd = now.AddDays(-1);
        billing.SubscriptionCreditPeriods.Add(new SubscriptionCreditPeriodRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            PlanCode = "Plus", SubscriptionIdentity = "expired-test-period", IdempotencyKey = "expired-test-period-key",
            AnchorAtUtc = expiredStart, ActivationRevision = 1, PeriodNumber = 0,
            StartsAtUtc = expiredStart, ExpiresAtUtc = expiredEnd, GrantedCredits = 5,
            AllowanceCredits = 5, RemainingCredits = 5, GrantLedgerEntryId = Guid.NewGuid(),
            Status = "Active", CreatedAtUtc = expiredStart, Revision = 1
        });
        await billing.SaveChangesAsync();
        using var client = factory.CreateClient();
        var initialBalance = await factory.ReadBalanceAsync();
        var whileMaxIsActive = await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=external%2Dtopup%2Duser");
        Assert.Equal(HttpStatusCode.OK, whileMaxIsActive.StatusCode);
        Assert.Equal("no-store", whileMaxIsActive.Headers.CacheControl?.ToString());
        using var maxResult = JsonDocument.Parse(await whileMaxIsActive.Content.ReadAsStringAsync());
        Assert.Equal("Max", maxResult.RootElement.GetProperty("plan").GetString());
        Assert.Equal("unlimited", maxResult.RootElement.GetProperty("quotaMode").GetString());
        Assert.Equal(JsonValueKind.Null, maxResult.RootElement.GetProperty("availableCredits").ValueKind);
        Assert.Equal(now.AddDays(-1).Date, maxResult.RootElement.GetProperty("subscriptionStartsAtUtc").GetDateTime().Date);
        Assert.Equal(now.AddDays(10).Date, maxResult.RootElement.GetProperty("subscriptionExpiresAtUtc").GetDateTime().Date);
        Assert.Equal(initialBalance, await factory.ReadBalanceAsync());

        max.ExpiresAtUtc = now.AddSeconds(-1);
        await billing.SaveChangesAsync();
        var afterMaxExpires = await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=external-topup-user");
        Assert.Equal(HttpStatusCode.OK, afterMaxExpires.StatusCode);
        using var finiteResult = JsonDocument.Parse(await afterMaxExpires.Content.ReadAsStringAsync());
        Assert.Equal("Pro", finiteResult.RootElement.GetProperty("plan").GetString());
        Assert.Equal("finite", finiteResult.RootElement.GetProperty("quotaMode").GetString());
        Assert.Equal(12m, finiteResult.RootElement.GetProperty("availableCredits").GetDecimal());
        Assert.Equal(now.AddDays(30).Date, finiteResult.RootElement.GetProperty("subscriptionExpiresAtUtc").GetDateTime().Date);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=missing")).StatusCode);
    }

    [Fact]
    public async Task OpenApiDocumentsAnonymousBalanceAndAuthenticatedFourFieldDelivery()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var balance = paths.GetProperty("/api/v1/billing/partners/tahlilapp/credit-balance").GetProperty("get");
        Assert.False(balance.TryGetProperty("security", out _));
        var delivery = paths.GetProperty("/api/v1/billing/partners/tahlilapp/top-ups").GetProperty("post");
        Assert.True(delivery.GetProperty("security").GetArrayLength() > 0);
        var schemaRef = delivery.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString()!;
        var schemaName = schemaRef.Split('/').Last();
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty(schemaName).GetProperty("properties");
        Assert.Equal(new[] { "creditsPurchased", "invoiceId", "paidAtUtc", "userId" },
            properties.EnumerateObject().Select(property => property.Name).Order().ToArray());
    }

    [Fact]
    public async Task Partner_auth_and_contract_quantities_are_enforced_and_grants_are_idempotent()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        foreach (var credits in new[] { 100, 500, 1000 })
        {
            var body = new
            {
                invoiceId = "ta-invoice-" + credits,
                userId = "external-topup-user", creditsPurchased = credits,
                paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            };
            var unauthorized = await PostAsync(client, body, null);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            var forbidden = await PostAsync(client, body, factory.CreatePartnerToken("wrong.scope"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var granted = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
            Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
            using var document = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
            Assert.Equal("granted", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("ta-invoice-" + credits, document.RootElement.GetProperty("invoiceId").GetString());
            Assert.Equal(credits, document.RootElement.GetProperty("creditsGranted").GetInt32());
            var duplicate = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

            var replayedToken = factory.CreatePartnerToken("topup.deliver");
            var firstUse = await PostAsync(client, body, replayedToken);
            Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);
            var replay = await PostAsync(client, body, replayedToken);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        }

        var invalidQuantity = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-invalid",
            userId = "external-topup-user", creditsPurchased = 200,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal((HttpStatusCode)422, invalidQuantity.StatusCode);
        Assert.Equal(1617m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Legacy_request_fields_and_missing_business_fields_are_rejected()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var valid = new
        {
            invoiceId = "ta-invoice-exact-fields", userId = "external-topup-user", creditsPurchased = 100,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var withLegacyField = await PostAsync(client, new { valid.invoiceId, valid.userId, valid.creditsPurchased, valid.paidAtUtc, paymentId = "must-not-be-sent" }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.BadRequest, withLegacyField.StatusCode);
        var missingInvoice = await PostAsync(client, new { userId = valid.userId, valid.creditsPurchased, valid.paidAtUtc }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.BadRequest, missingInvoice.StatusCode);
        Assert.Contains("\"code\":\"invalid-topup-event\"", await missingInvoice.Content.ReadAsStringAsync());
        using var malformedJson = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/partners/tahlilapp/top-ups")
        {
            Content = new StringContent("{\"invoiceId\":", Encoding.UTF8, "application/json")
        };
        malformedJson.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreatePartnerToken("topup.deliver"));
        using var malformedResponse = await client.SendAsync(malformedJson);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Contains("\"code\":\"invalid-topup-event\"", await malformedResponse.Content.ReadAsStringAsync());
        var futurePaidAt = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-future-paid-at", userId = valid.userId, valid.creditsPurchased,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.BadRequest, futurePaidAt.StatusCode);
        var missingJti = await PostAsync(client, valid, factory.CreatePartnerToken("topup.deliver", includeJti: false));
        Assert.Equal(HttpStatusCode.Unauthorized, missingJti.StatusCode);
        Assert.Equal(17m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Overlapping_plus_and_pro_windows_are_eligible_and_grant_from_the_latest_expiring_window()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var start = factory.WindowStart.AddHours(-1);
        var expiry = factory.WindowEnd.AddDays(1);
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_pro:{start.ToUnixTimeMilliseconds()}:{expiry.ToUnixTimeMilliseconds()}",
            PlanCode = "Pro", StartsAtUtc = start, ExpiresAtUtc = expiry
        });
        await billing.SaveChangesAsync();

        using var client = factory.CreateClient();
        var body = new
        {
            invoiceId = "ta-invoice-overlap", userId = "external-topup-user", creditsPurchased = 500,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var response = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("granted", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(500, document.RootElement.GetProperty("creditsGranted").GetInt32());
        Assert.Equal(expiry.ToUnixTimeMilliseconds(),
            document.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(517m, await factory.ReadBalanceAsync());

        var replay = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(document.RootElement.GetProperty("grantId").GetString(), replayDocument.RootElement.GetProperty("grantId").GetString());
        Assert.Equal(517m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Delivery_paid_during_active_max_is_rejected_as_ineligible()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var expiry = DateTimeOffset.UtcNow.AddDays(10);
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_max:{start.ToUnixTimeMilliseconds()}:{expiry.ToUnixTimeMilliseconds()}",
            PlanCode = "Max", StartsAtUtc = start, ExpiresAtUtc = expiry
        });
        // Plus (from reset) and Pro overlap the active Max; Max still makes the payment ineligible.
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_pro:{factory.WindowStart.ToUnixTimeMilliseconds()}:{factory.WindowEnd.AddDays(5).ToUnixTimeMilliseconds()}",
            PlanCode = "Pro", StartsAtUtc = factory.WindowStart, ExpiresAtUtc = factory.WindowEnd.AddDays(5)
        });
        await billing.SaveChangesAsync();

        using var client = factory.CreateClient();
        var response = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-paid-during-max", userId = "external-topup-user", creditsPurchased = 100,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(17m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Delivery_for_free_account_is_rejected_even_when_old_paid_window_exists()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var account = await billing.CustomerAccounts.SingleAsync(row => row.Id == TahlilAppTopUpApiFactory.AccountId);
        account.SubscriptionPlanCode = "Free";
        await billing.SaveChangesAsync();

        using var client = factory.CreateClient();
        var response = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-free-account", userId = "external-topup-user", creditsPurchased = 100,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(17m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Billing_admin_reconciliation_lists_purchase_outcomes_and_identity_conflicts()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var anonymous = await client.GetAsync("/api/v1/admin/billing/top-ups/reconciliation");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var body = new
        {
            invoiceId = "ta-invoice-reconcile-1",
            userId = "external-topup-user", creditsPurchased = 100,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var granted = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var conflicting = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-reconcile-1",
            userId = "external-topup-user", creditsPurchased = 500,
            paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/billing/top-ups/reconciliation?limit=50");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateBillingAdminToken());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.GetProperty("purchases").EnumerateArray());
        Assert.Equal("granted", document.RootElement.GetProperty("purchases")[0].GetProperty("status").GetString());
        Assert.Single(document.RootElement.GetProperty("identityConflicts").EnumerateArray());
    }

    [Fact]
    public async Task CreditBalance_uses_latest_expiring_finite_plan_and_keeps_pro_on_exact_tie()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var proStart = DateTimeOffset.UtcNow.AddDays(-1);
        var pro = new ExternalPlanSubscriptionRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId,
            IdentityKey = $"sapio_pro:{proStart.ToUnixTimeMilliseconds()}:{factory.WindowEnd.AddDays(-20).ToUnixTimeMilliseconds()}",
            PlanCode = "Pro", StartsAtUtc = proStart, ExpiresAtUtc = factory.WindowEnd.AddDays(-20)
        };
        billing.ExternalPlanSubscriptions.Add(pro);
        await billing.SaveChangesAsync();
        using var client = factory.CreateClient();

        using (var laterPlus = await ReadBalanceDocumentAsync(client, "external-topup-user"))
        {
            Assert.Equal("Plus", laterPlus.RootElement.GetProperty("plan").GetString());
            Assert.Equal("finite", laterPlus.RootElement.GetProperty("quotaMode").GetString());
            Assert.Equal(17m, laterPlus.RootElement.GetProperty("availableCredits").GetDecimal());
            Assert.Equal(factory.WindowStart.ToUnixTimeMilliseconds(),
                laterPlus.RootElement.GetProperty("subscriptionStartsAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
            Assert.Equal(factory.WindowEnd.ToUnixTimeMilliseconds(),
                laterPlus.RootElement.GetProperty("subscriptionExpiresAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        }

        pro.ExpiresAtUtc = factory.WindowEnd;
        await billing.SaveChangesAsync();
        using var tie = await ReadBalanceDocumentAsync(client, "external-topup-user");
        Assert.Equal("Pro", tie.RootElement.GetProperty("plan").GetString());
        Assert.Equal(proStart.ToUnixTimeMilliseconds(),
            tie.RootElement.GetProperty("subscriptionStartsAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(factory.WindowEnd.ToUnixTimeMilliseconds(),
            tie.RootElement.GetProperty("subscriptionExpiresAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task CreditBalance_excludes_expired_top_up_sources_and_reserved_credits()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var window = await billing.ExternalPlanSubscriptions.SingleAsync(row => row.CustomerAccountId == TahlilAppTopUpApiFactory.AccountId);
        var now = DateTimeOffset.UtcNow;
        billing.TopUpCreditSources.Add(new TopUpCreditSourceRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId, ExternalPlanSubscriptionId = window.Id,
            PurchaseId = Guid.NewGuid(), GrantLedgerEntryId = Guid.NewGuid(), GrantedCredits = 4, RemainingCredits = 4,
            ExpiresAtUtc = now.AddMinutes(-1), CreatedAtUtc = now.AddDays(-5), Revision = 1
        });
        billing.TopUpCreditSources.Add(new TopUpCreditSourceRow
        {
            Id = Guid.NewGuid(), CustomerAccountId = TahlilAppTopUpApiFactory.AccountId, ExternalPlanSubscriptionId = window.Id,
            PurchaseId = Guid.NewGuid(), GrantLedgerEntryId = Guid.NewGuid(), GrantedCredits = 6, RemainingCredits = 6,
            ExpiresAtUtc = window.ExpiresAtUtc, CreatedAtUtc = now.AddDays(-1), Revision = 1
        });
        var wallet = await billing.WalletProjections.SingleAsync(row => row.CustomerAccountId == TahlilAppTopUpApiFactory.AccountId);
        wallet.ReservedAmount = 3;
        await billing.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var document = await ReadBalanceDocumentAsync(client, "external-topup-user");
        Assert.Equal("finite", document.RootElement.GetProperty("quotaMode").GetString());
        // Wallet 17, minus the expired 4-credit top-up source, minus 3 reserved; the unexpired 6 remain spendable.
        Assert.Equal(10m, document.RootElement.GetProperty("availableCredits").GetDecimal());
        Assert.Equal(17m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task CreditBalance_for_free_account_reports_persisted_free_window()
    {
        await factory.ResetAsync();
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        billing.ExternalPlanSubscriptions.RemoveRange(await billing.ExternalPlanSubscriptions.ToListAsync());
        var account = await billing.CustomerAccounts.SingleAsync(row => row.Id == TahlilAppTopUpApiFactory.AccountId);
        var freeStart = DateTimeOffset.UtcNow.AddDays(-3);
        var freeEnd = DateTimeOffset.UtcNow.AddDays(4);
        account.SubscriptionPlanCode = "Free";
        account.SubscriptionEffectiveFrom = freeStart;
        account.SubscriptionEffectiveTo = freeEnd;
        await billing.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var document = await ReadBalanceDocumentAsync(client, "external-topup-user");
        Assert.Equal("Free", document.RootElement.GetProperty("plan").GetString());
        Assert.Equal("finite", document.RootElement.GetProperty("quotaMode").GetString());
        Assert.Equal(17m, document.RootElement.GetProperty("availableCredits").GetDecimal());
        Assert.Equal(freeStart.ToUnixTimeMilliseconds(),
            document.RootElement.GetProperty("subscriptionStartsAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
        Assert.Equal(freeEnd.ToUnixTimeMilliseconds(),
            document.RootElement.GetProperty("subscriptionExpiresAtUtc").GetDateTimeOffset().ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task CreditBalance_is_read_only_ignores_bearer_tokens_and_rejects_malformed_ids()
    {
        await factory.ResetAsync();
        var before = await factory.ReadBillingFootprintAsync();
        using var client = factory.CreateClient();

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/billing/partners/tahlilapp/credit-balance?userId=external-topup-user"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreatePartnerToken("topup.deliver"));
            using var withPartnerToken = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, withPartnerToken.StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/billing/partners/tahlilapp/credit-balance?userId=external-topup-user"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
            using var withGarbageToken = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, withGarbageToken.StatusCode);
        }

        using (var missingUserId = await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, missingUserId.StatusCode);
            Assert.Equal("no-store", missingUserId.Headers.CacheControl?.ToString());
            Assert.Contains("\"code\":\"invalid-topup-event\"", await missingUserId.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=%20")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=" + new string('a', 129))).StatusCode);
        using var unknown = await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=external%2Dtopup%2Dother");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.Contains("topup-user-unresolved", unknownBody);
        Assert.DoesNotContain("plan", unknownBody, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, await factory.ReadBillingFootprintAsync());
    }

    [Fact]
    public async Task CreditBalance_has_its_own_ip_rate_limit_separate_from_login()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:TahlilAppCreditBalance:PermitLimit"] = "2",
                ["RateLimiting:TahlilAppCreditBalance:WindowSeconds"] = "3600"
            })));
        using var client = limited.CreateClient();
        const string path = "/api/v1/billing/partners/tahlilapp/credit-balance?userId=external-topup-user";
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync(path)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(path)).StatusCode);

        using var login = await client.PostAsync("/api/auth/v1/login", JsonContent.Create(new { }));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, login.StatusCode);
    }

    [Fact]
    public async Task Delivery_rejects_subscription_and_other_removed_body_fields()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var removedFields = new Dictionary<string, object>
        {
            ["subscription"] = new { slug = "sapio_plus", startsAtUtc = factory.WindowStart, expiresAtUtc = factory.WindowEnd },
            ["amount"] = 1500000,
            ["currency"] = "IRR",
            ["packageId"] = "TA-TOPUP-100",
            ["packageVersion"] = 1,
            ["eventId"] = "evt-1",
            ["paymentId"] = "pay-1",
            ["jwt"] = "token-in-body"
        };
        foreach (var (name, value) in removedFields)
        {
            var body = new Dictionary<string, object>
            {
                ["invoiceId"] = "ta-invoice-removed-" + name, ["userId"] = "external-topup-user",
                ["creditsPurchased"] = 100, ["paidAtUtc"] = paidAtUtc, [name] = value
            };
            var response = await PostAsync(client, body, factory.CreatePartnerToken("topup.deliver"));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Field '{name}' returned {(int)response.StatusCode}.");
            Assert.Contains("\"code\":\"invalid-topup-event\"", await response.Content.ReadAsStringAsync());
        }

        var exact = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-exact", userId = "external-topup-user", creditsPurchased = 100, paidAtUtc
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(117m, await factory.ReadBalanceAsync());
    }

    [Fact]
    public async Task Invoice_reuse_with_changed_user_or_paid_time_conflicts_without_changing_the_grant()
    {
        await factory.ResetAsync();
        using var client = factory.CreateClient();
        var paidAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        var original = new { invoiceId = "ta-invoice-immutable", userId = "external-topup-user", creditsPurchased = 100, paidAtUtc };
        var granted = await PostAsync(client, original, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using var grantedDocument = JsonDocument.Parse(await granted.Content.ReadAsStringAsync());
        var grantId = grantedDocument.RootElement.GetProperty("grantId").GetString();

        var changedUser = await PostAsync(client, original with { userId = "external-topup-other" }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, changedUser.StatusCode);
        Assert.Contains("topup-identity-conflict", await changedUser.Content.ReadAsStringAsync());
        var changedPaidAt = await PostAsync(client, original with { paidAtUtc = paidAtUtc.AddSeconds(1) }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.Conflict, changedPaidAt.StatusCode);

        var replay = await PostAsync(client, original, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(grantId, replayDocument.RootElement.GetProperty("grantId").GetString());
        Assert.Equal(100, replayDocument.RootElement.GetProperty("creditsGranted").GetInt32());
        Assert.Equal(117m, await factory.ReadBalanceAsync());

        var unknownUser = await PostAsync(client, new
        {
            invoiceId = "ta-invoice-unknown-user", userId = "external-topup-other", creditsPurchased = 100, paidAtUtc
        }, factory.CreatePartnerToken("topup.deliver"));
        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
        Assert.Equal(117m, await factory.ReadBalanceAsync());
    }

    private static async Task<JsonDocument> ReadBalanceDocumentAsync(HttpClient client, string userId)
    {
        using var response = await client.GetAsync("/api/v1/billing/partners/tahlilapp/credit-balance?userId=" + Uri.EscapeDataString(userId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/billing/partners/tahlilapp/top-ups")
        {
            Content = JsonContent.Create(body)
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}

public sealed class TahlilAppTopUpApiFactory : WebApplicationFactory<Program>
{
    private const string PartnerIssuer = "TahlilApp.Tests";
    private const string PartnerSigningKey = "integration-test-tahlilapp-topup-key-material-32-bytes";
    private const string PartnerKeyId = "partner-test-key";
    private readonly string billingDatabase = "topup-api-billing-" + Guid.NewGuid().ToString("N");
    private readonly string authDatabase = "topup-api-auth-" + Guid.NewGuid().ToString("N");
    public static readonly Guid UserId = Guid.Parse("43444444-4444-4444-8444-444444444444");
    public static readonly Guid TenantId = AuthenticationApiFactory.TenantId;
    public static readonly Guid AccountId = Guid.Parse("53555555-5555-4555-8555-555555555555");
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset WindowEnd { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Billing:TahlilAppTopUps:Enabled"] = "true",
            ["Billing:TahlilAppTopUps:Authentication:Issuer"] = PartnerIssuer,
            ["Billing:TahlilAppTopUps:Authentication:Audience"] = "sapio-tahlilapp-topup",
            [$"Billing:TahlilAppTopUps:Authentication:SigningKeys:{PartnerKeyId}"] = PartnerSigningKey,
            ["Authentication:JwtBearer:Issuer"] = "FinancialCopilot.Tests",
            ["Authentication:JwtBearer:Audience"] = "FinancialCopilot.Tests.Web",
            ["Authentication:JwtBearer:SigningKey"] = "integration-test-signing-key-with-more-than-32-characters",
            ["Authentication:JwtBearer:RequireHttpsMetadata"] = "false",
            // Top-up tests do not send email; keep them independent of the tracked SMTP defaults.
            ["Authentication:Email:Enabled"] = "false",
            ["Database:ApplyMigrationsOnStartup"] = "false"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(billingDatabase));
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(authDatabase));
            services.RemoveAll<ITahlilAppEntitlementRefresher>();
            services.AddSingleton<ITahlilAppEntitlementRefresher, NoOpRefresher>();
        });
    }

    public string CreatePartnerToken(string scope, bool includeJti = true)
    {
        var claims = new List<Claim> { new("sub", "tahlilapp-test"), new("scope", scope) };
        if (includeJti) claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));
        var token = new JwtSecurityToken(PartnerIssuer, "sapio-tahlilapp-topup",
            claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PartnerSigningKey)), SecurityAlgorithms.HmacSha256));
        token.Header["kid"] = PartnerKeyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateBillingAdminToken()
    {
        var token = new JwtSecurityToken("FinancialCopilot.Tests", "FinancialCopilot.Tests.Web",
            [new Claim(JwtRegisteredClaimNames.Sub, UserId.ToString()),
             new Claim(FinancialCopilotClaimTypes.TenantId, TenantId.ToString()),
             new Claim(FinancialCopilotClaimTypes.AuthenticationMode, "WebAppUser"),
             new Claim(FinancialCopilotClaimTypes.Permission, FinancialCopilotPermissions.AdminBillingAuditRead),
             new Claim("role", "BillingAdmin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-with-more-than-32-characters")), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await billing.Database.EnsureDeletedAsync(); await billing.Database.EnsureCreatedAsync();
        await auth.Database.EnsureDeletedAsync(); await auth.Database.EnsureCreatedAsync();
        var start = WindowStart = DateTimeOffset.UtcNow.AddDays(-2);
        var end = WindowEnd = DateTimeOffset.UtcNow.AddDays(30);
        var slug = "sapio_plus";
        billing.CustomerAccounts.Add(new CustomerAccountRow { Id = AccountId, TenantId = TenantId, UserId = UserId, AccountType = "Individual", BillingMode = "Prepaid", SubscriptionPlanCode = "Plus", SubscriptionActivationIsAuthoritative = true, SubscriptionEffectiveFrom = start, SubscriptionEffectiveTo = end });
        billing.WalletProjections.Add(new WalletProjectionRow { CustomerAccountId = AccountId, Balance = 17, ReservedAmount = 0, UpdatedAt = DateTimeOffset.UtcNow, Revision = 1 });
        billing.ExternalPlanSubscriptions.Add(new ExternalPlanSubscriptionRow { Id = Guid.NewGuid(), CustomerAccountId = AccountId, IdentityKey = $"{slug}:{start.ToUnixTimeMilliseconds()}:{end.ToUnixTimeMilliseconds()}", PlanCode = "Plus", StartsAtUtc = start, ExpiresAtUtc = end });
        auth.Users.Add(new FinancialCopilotUser { Id = UserId, UserName = "topup-api-test", NormalizedUserName = "TOPUP-API-TEST", IsEnabled = true });
        auth.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow { Id = Guid.NewGuid(), Provider = "TahlilApp", ExternalUserId = "external-topup-user", LocalUserId = UserId, TenantId = TenantId, Status = "Active", LinkedAtUtc = DateTimeOffset.UtcNow });
        await auth.SaveChangesAsync();
        await billing.SaveChangesAsync();
    }

    public async Task<decimal> ReadBalanceAsync()
    {
        using var scope = Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        return await billing.WalletProjections.Where(row => row.CustomerAccountId == AccountId).Select(row => row.Balance).SingleAsync();
    }

    /// <summary>Counts every billing row a read could plausibly create or change.</summary>
    public async Task<(int Ledger, int Audits, int Outbox, int Purchases, int ConsumedTokens, int Sources, long WalletRevision, decimal Balance, decimal Reserved)> ReadBillingFootprintAsync()
    {
        using var scope = Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var wallet = await billing.WalletProjections.AsNoTracking().SingleAsync(row => row.CustomerAccountId == AccountId);
        return (await billing.UsageLedgerEntries.CountAsync(), await billing.AdminAudits.CountAsync(),
            await billing.OutboxMessages.CountAsync(), await billing.TahlilAppTopUpPurchases.CountAsync(),
            await billing.ConsumedPartnerTokens.CountAsync(), await billing.TopUpCreditSources.CountAsync(),
            wallet.Revision, wallet.Balance, wallet.ReservedAmount);
    }

    private sealed class NoOpRefresher : ITahlilAppEntitlementRefresher
    {
        public Task RefreshIfDueAsync(Guid tenantId, Guid localUserId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
