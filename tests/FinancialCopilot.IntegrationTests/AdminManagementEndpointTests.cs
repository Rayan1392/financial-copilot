using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialCopilot.IntegrationTests;

public sealed class AdminManagementEndpointTests : IClassFixture<AdminManagementApiFactory>
{
    private readonly AdminManagementApiFactory _factory;

    public AdminManagementEndpointTests(AdminManagementApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PermissionCatalog_RequiresNarrowPermission_AndRejectsApiKey()
    {
        await _factory.ResetAsync();
        using var normalClient = UserClient();
        using var forbidden = await normalClient.GetAsync("/api/v1/admin/permissions");
        using var forbiddenJson = await ReadJsonAsync(forbidden);

        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);
        using var apiKeyForbidden = await apiClient.GetAsync("/api/v1/admin/permissions");

        using var adminClient = UserClient(FinancialCopilotPermissions.AdminPermissionsRead);
        using var allowed = await adminClient.GetAsync("/api/v1/admin/permissions");
        using var json = await ReadJsonAsync(allowed);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("https://financialcopilot/errors/permission-denied", forbiddenJson.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(forbiddenJson.RootElement.GetProperty("correlationId").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, apiKeyForbidden.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(FinancialCopilotPermissions.AdminCreditsAdjust, json.RootElement.EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task RoleLifecycle_CreatesAndDisablesAdministrativeGrouping()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminRolesManage);
        using var create = await client.PostAsJsonAsync("/api/v1/admin/roles", new { name = "Support", reason = "Support team bootstrap" });
        using var createJson = await ReadJsonAsync(create);
        var roleId = createJson.RootElement.GetProperty("roleId").GetGuid();
        using var update = await client.PatchAsJsonAsync($"/api/v1/admin/roles/{roleId}", new { name = "Support", isEnabled = false, reason = "Support team deactivation" });
        using var updateJson = await ReadJsonAsync(update);

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.False(updateJson.RootElement.GetProperty("isEnabled").GetBoolean());
    }

    [Fact]
    public async Task UserDisable_AppendsRedactedSecurityAudit()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminUsersManage, FinancialCopilotPermissions.AdminSecurityAuditRead);
        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/users/{AdminManagementApiFactory.ManagedUserId}/status",
            new { isEnabled = false, unlock = false, reason = "Support-requested access suspension" });
        using var audits = await client.GetAsync("/api/v1/admin/audits/security");
        using var json = await ReadJsonAsync(audits);
        var audit = Assert.Single(json.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, audits.StatusCode);
        Assert.Equal("identity.user.status-changed", audit.GetProperty("actionCode").GetString());
        Assert.Equal("Support-requested access suspension", audit.GetProperty("reason").GetString());
        Assert.DoesNotContain("Password", audit.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantMemberRead_RejectsCrossTenantScope_WithStableProblemDetails()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminTenantsRead);
        using var response = await client.GetAsync($"/api/v1/admin/tenants/{Guid.NewGuid()}/members");
        using var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("https://financialcopilot/errors/tenant-scope-violation", json.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task UserDisable_RejectsFinalActiveSuperAdmin_AndAuditsAttempt()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminUsersManage, FinancialCopilotPermissions.AdminSecurityAuditRead);
        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/users/{AuthenticationApiFactory.UserId}/status",
            new { isEnabled = false, unlock = false, reason = "Unsafe privilege removal" });
        using var problem = await ReadJsonAsync(response);
        using var audits = await client.GetAsync("/api/v1/admin/audits/security");
        using var auditJson = await ReadJsonAsync(audits);
        var audit = Assert.Single(auditJson.RootElement.EnumerateArray());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("https://financialcopilot/errors/administrator-lockout-protection", problem.RootElement.GetProperty("type").GetString());
        Assert.Equal("security.lockout-risk.rejected", audit.GetProperty("actionCode").GetString());
    }

    [Fact]
    public async Task CreditAdjustment_DelegatesToBillingLedger_AndIsIdempotent()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminCreditsAdjust, FinancialCopilotPermissions.AdminUsageLedgerRead);
        var path = $"/api/v1/admin/customers/{AdminManagementApiFactory.CustomerAccountId}/credit-adjustments";
        var request = new { credits = 7m, reason = "Service recovery credit", idempotencyKey = "admin-v1-adjustment-1" };

        using var first = await client.PostAsJsonAsync(path, request);
        using var repeated = await client.PostAsJsonAsync(path, request);
        using var repeatedJson = await ReadJsonAsync(repeated);
        using var ledger = await client.GetAsync($"/api/v1/admin/customers/{AdminManagementApiFactory.CustomerAccountId}/usage-ledger");
        using var ledgerJson = await ReadJsonAsync(ledger);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True(repeatedJson.RootElement.GetProperty("alreadyApplied").GetBoolean());
        Assert.Equal(17m, repeatedJson.RootElement.GetProperty("updatedBalance").GetDecimal());
        Assert.Single(ledgerJson.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task PlanCapabilityAndSubscriptionManagement_PublishesAppendOnlyPolicy()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminPlansManage, FinancialCopilotPermissions.AdminPlansRead, FinancialCopilotPermissions.AdminSubscriptionsManage);
        using var createPlan = await client.PostAsJsonAsync("/api/v1/admin/plans", new { code = "Enterprise-v2", name = "Enterprise", includedCredits = 2000m, pricingPolicyVersion = "v2", reason = "Commercial rollout" });
        using var capabilities = await client.PutAsJsonAsync("/api/v1/admin/plans/Enterprise-v2/capabilities", new
        {
            reason = "Commercial rollout",
            capabilities = new[] { new { capabilityCode = "AiQuery.Scanner", policyVersion = "v2", isEnabled = true, limit = (decimal?)null } }
        });
        using var subscription = await client.PutAsJsonAsync(
            $"/api/v1/admin/customers/{AdminManagementApiFactory.CustomerAccountId}/subscription",
            new { planCode = "Enterprise-v2", effectiveFrom = DateTimeOffset.UtcNow, effectiveTo = (DateTimeOffset?)null, expectedRevision = 0, reason = "Customer contract activation" });
        using var json = await ReadJsonAsync(subscription);

        Assert.Equal(HttpStatusCode.OK, createPlan.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, capabilities.StatusCode);
        Assert.Equal(HttpStatusCode.OK, subscription.StatusCode);
        Assert.Equal("Enterprise-v2", json.RootElement.GetProperty("planCode").GetString());
    }

    [Fact]
    public async Task SubscriptionManagement_RejectsStaleRevision_AndExposesBillingAudit()
    {
        await _factory.ResetAsync();
        using var client = UserClient(FinancialCopilotPermissions.AdminSubscriptionsManage, FinancialCopilotPermissions.AdminBillingAuditRead);
        var path = $"/api/v1/admin/customers/{AdminManagementApiFactory.CustomerAccountId}/subscription";
        var request = new { planCode = "Pro", effectiveFrom = DateTimeOffset.UtcNow, effectiveTo = (DateTimeOffset?)null, expectedRevision = 0, reason = "Initial paid subscription" };
        using var first = await client.PutAsJsonAsync(path, request);
        using var stale = await client.PutAsJsonAsync(path, request);
        using var staleJson = await ReadJsonAsync(stale);
        using var audits = await client.GetAsync("/api/v1/admin/audits/billing");
        using var auditsJson = await ReadJsonAsync(audits);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("https://financialcopilot/errors/concurrency-conflict", staleJson.RootElement.GetProperty("type").GetString());
        Assert.Contains(auditsJson.RootElement.EnumerateArray(), item => item.GetProperty("actionCode").GetString() == "billing.subscription.changed");
    }

    private HttpClient UserClient(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.CreateWebAppToken(includeTenant: true, permissions: permissions));
        return client;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }
}

public sealed class AdminManagementApiFactory : AuthenticationApiFactory
{
    private readonly string _authDatabaseName = $"admin-auth-{Guid.NewGuid():N}";
    private readonly string _billingDatabaseName = $"admin-billing-{Guid.NewGuid():N}";
    public static readonly Guid ManagedUserId = Guid.Parse("d1408f88-af7d-4fbc-a86e-67d6d767b04b");
    public static readonly Guid CustomerAccountId = Guid.Parse("ca8ec616-884c-4d49-90d6-fabec8c399cf");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.RemoveAll<DbContextOptions<AuthDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthDbContext>>();
            services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(_authDatabaseName));
            services.RemoveAll<BillingDbContext>();
            services.RemoveAll<DbContextOptions<BillingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BillingDbContext>>();
            services.AddDbContext<BillingDbContext>(options => options.UseInMemoryDatabase(_billingDatabaseName));
        });
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await auth.Database.EnsureDeletedAsync();
        await auth.Database.EnsureCreatedAsync();
        await billing.Database.EnsureDeletedAsync();
        await billing.Database.EnsureCreatedAsync();
        auth.Tenants.Add(new TenantRow { Id = TenantId, Name = "Integration Tenant" });
        auth.Users.AddRange(
            new FinancialCopilotUser { Id = UserId, UserName = "admin@example.test", NormalizedUserName = "ADMIN@EXAMPLE.TEST", Email = "admin@example.test", NormalizedEmail = "ADMIN@EXAMPLE.TEST", SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), IsEnabled = true },
            new FinancialCopilotUser { Id = ManagedUserId, UserName = "managed@example.test", NormalizedUserName = "MANAGED@EXAMPLE.TEST", Email = "managed@example.test", NormalizedEmail = "MANAGED@EXAMPLE.TEST", SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString(), IsEnabled = true });
        auth.UserTenants.AddRange(
            new UserTenantRow { UserId = UserId, TenantId = TenantId, IsDefault = true },
            new UserTenantRow { UserId = ManagedUserId, TenantId = TenantId, IsDefault = true });
        auth.Permissions.AddRange(FinancialCopilotPermissions.All.Select(code => new PermissionRow { Id = Guid.NewGuid(), Code = code }));
        var superAdminRole = new FinancialCopilotRole { Id = Guid.NewGuid(), Name = "SuperAdmin", NormalizedName = "SUPERADMIN", ConcurrencyStamp = Guid.NewGuid().ToString() };
        auth.Roles.Add(superAdminRole);
        auth.UserRoles.Add(new IdentityUserRole<Guid> { UserId = UserId, RoleId = superAdminRole.Id });
        billing.CustomerAccounts.Add(new CustomerAccountRow { Id = CustomerAccountId, TenantId = TenantId, UserId = ManagedUserId, AccountType = "Individual", BillingMode = "Prepaid" });
        billing.WalletProjections.Add(new WalletProjectionRow { CustomerAccountId = CustomerAccountId, Balance = 10m, UpdatedAt = DateTimeOffset.UtcNow });
        await auth.SaveChangesAsync();
        await billing.SaveChangesAsync();
    }
}
