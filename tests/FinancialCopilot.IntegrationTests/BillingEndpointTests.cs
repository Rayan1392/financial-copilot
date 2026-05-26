using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class BillingEndpointTests : IClassFixture<BillingApiFactory>
{
    private readonly BillingApiFactory _factory;

    public BillingEndpointTests(BillingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UsageMe_WithIndividualUser_ReturnsPrepaidWalletCapacityAndUsage()
    {
        await _factory.ResetBillingDataAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync("/api/v1/usage/me", CancellationToken.None);
        using var document = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Individual", document.RootElement.GetProperty("customerType").GetString());
        Assert.Equal("Prepaid", document.RootElement.GetProperty("billingMode").GetString());
        Assert.Equal(10m, document.RootElement.GetProperty("balance").GetDecimal());
        Assert.Equal(8m, document.RootElement.GetProperty("availableSpendingCapacity").GetDecimal());
        Assert.Single(document.RootElement.GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task UsageMe_WithPartnerApiClient_ReturnsOrganizationCapacityAndAttribution()
    {
        await _factory.ResetBillingDataAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync("/api/v1/usage/me", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var entry = Assert.Single(document.RootElement.GetProperty("entries").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Organization", document.RootElement.GetProperty("customerType").GetString());
        Assert.Equal("Hybrid", document.RootElement.GetProperty("billingMode").GetString());
        Assert.Equal(120m, document.RootElement.GetProperty("availableSpendingCapacity").GetDecimal());
        Assert.Equal("partner-user-123", entry.GetProperty("externalUserId").GetString());
    }

    [Fact]
    public async Task UsageMe_WithInvalidPeriod_ReturnsBadRequest()
    {
        await _factory.ResetBillingDataAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync(
            "/api/v1/usage/me?from=2026-05-27T00:00:00Z&to=2026-05-26T00:00:00Z",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BillingTransactions_WithIndividualUser_ReturnsOnlyIndividualMoneyLedger()
    {
        await _factory.ResetBillingDataAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync("/api/v1/billing/transactions", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var transaction = Assert.Single(document.RootElement.GetProperty("transactions").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TopUp", transaction.GetProperty("type").GetString());
        Assert.Equal(500_000m, transaction.GetProperty("amount").GetDecimal());
        Assert.Equal("IRR", transaction.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task BillingTransactions_WithPartnerApiClient_ReturnsOrganizationMoneyLedger()
    {
        await _factory.ResetBillingDataAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        using var response = await client.GetAsync("/api/v1/billing/transactions", CancellationToken.None);
        using var document = await ReadJsonAsync(response);
        var transaction = Assert.Single(document.RootElement.GetProperty("transactions").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("InvoiceSettlement", transaction.GetProperty("type").GetString());
        Assert.Equal(10_000_000m, transaction.GetProperty("amount").GetDecimal());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}

public sealed class BillingApiFactory : AuthenticationApiFactory
{
    private readonly string _billingDatabaseName = $"billing-endpoints-{Guid.NewGuid():N}";

    public static readonly Guid OrganizationAccountId = Guid.Parse("b68c35fb-096f-4530-b44c-b22368cc8031");
    public static readonly Guid IndividualAccountId = Guid.Parse("0bf82036-9d36-42a0-b4af-8c48756c1765");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options =>
                options.UseInMemoryDatabase(_billingDatabaseName));
        });
    }

    public async Task ResetBillingDataAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.CustomerAccounts.AddRange(
            new CustomerAccountRow
            {
                Id = OrganizationAccountId,
                TenantId = TenantId,
                AccountType = "Organization",
                BillingMode = "Hybrid",
                CreditLineApprovedLimit = 25m,
                CreditLineWarningThreshold = 5m
            },
            new CustomerAccountRow
            {
                Id = IndividualAccountId,
                TenantId = TenantId,
                UserId = UserId,
                AccountType = "Individual",
                BillingMode = "Prepaid"
            });
        dbContext.WalletProjections.AddRange(
            new WalletProjectionRow
            {
                CustomerAccountId = OrganizationAccountId,
                Balance = 100m,
                ReservedAmount = 5m,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new WalletProjectionRow
            {
                CustomerAccountId = IndividualAccountId,
                Balance = 10m,
                ReservedAmount = 2m,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        dbContext.UsageLedgerEntries.AddRange(
            CreateUsageEntry(OrganizationAccountId, ClientId, "partner-user-123"),
            CreateUsageEntry(IndividualAccountId, UserId, externalUserId: null));
        dbContext.FinancialTransactions.AddRange(
            CreateTransaction(
                OrganizationAccountId,
                "InvoiceSettlement",
                10_000_000m,
                "organization-invoice"),
            CreateTransaction(
                IndividualAccountId,
                "TopUp",
                500_000m,
                "individual-top-up"));

        await dbContext.SaveChangesAsync();
    }

    private static UsageLedgerEntryRow CreateUsageEntry(
        Guid customerAccountId,
        Guid actorId,
        string? externalUserId) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customerAccountId,
            ActorId = actorId,
            TenantId = TenantId,
            EntryType = "Charge",
            OperationCode = "AiQuery.Scanner",
            CreditsCharged = 1m,
            PricingPolicyVersion = "v1",
            IdempotencyKey = $"usage-{customerAccountId:N}",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExternalUserId = externalUserId
        };

    private static FinancialTransactionRow CreateTransaction(
        Guid customerAccountId,
        string type,
        decimal amount,
        string idempotencyKey) =>
        new()
        {
            Id = Guid.NewGuid(),
            CustomerAccountId = customerAccountId,
            Type = type,
            Amount = amount,
            Currency = "IRR",
            IdempotencyKey = idempotencyKey,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
}
