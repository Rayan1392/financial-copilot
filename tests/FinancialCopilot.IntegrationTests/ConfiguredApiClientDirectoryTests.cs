using FinancialCopilot.API.Security;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

/// <summary>Feature 164 change «unresolved-actor-identity»: naming API clients in the conversation log.</summary>
public sealed class ConfiguredApiClientDirectoryTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Gateway = Guid.NewGuid();
    private static readonly Guid Retired = Guid.NewGuid();
    private static readonly Guid Unnamed = Guid.NewGuid();

    private static ConfiguredApiClientDirectory Directory() => new(new StaticOptionsMonitor(new ApiKeyAuthenticationOptions
    {
        Clients =
        [
            new ApiKeyClientCredential { ClientId = Gateway.ToString(), TenantId = Tenant.ToString(), Name = " Telegram gateway " },
            new ApiKeyClientCredential { ClientId = Retired.ToString(), TenantId = Tenant.ToString(), Name = "Retired", IsActive = false },
            new ApiKeyClientCredential { ClientId = Unnamed.ToString(), TenantId = Tenant.ToString(), Name = "  " },
            new ApiKeyClientCredential { ClientId = "not-a-guid", TenantId = Tenant.ToString(), Name = "Broken" }
        ]
    }));

    [Fact]
    public void Finds_a_client_of_the_tenant_with_its_trimmed_name()
    {
        var found = Directory().Find(Tenant, Gateway);

        Assert.NotNull(found);
        Assert.Equal(Gateway, found.ClientId);
        Assert.Equal("Telegram gateway", found.Name);
    }

    [Fact]
    public void Finds_inactive_clients_so_past_activity_stays_identified() =>
        Assert.Equal("Retired", Directory().Find(Tenant, Retired)?.Name);

    [Fact]
    public void A_blank_name_is_reported_as_no_name()
    {
        var found = Directory().Find(Tenant, Unnamed);

        Assert.NotNull(found);
        Assert.Null(found.Name);
    }

    [Fact]
    public void Does_not_find_a_client_of_another_tenant_or_an_unknown_id()
    {
        Assert.Null(Directory().Find(OtherTenant, Gateway));
        Assert.Null(Directory().Find(Tenant, Guid.NewGuid()));
    }

    private sealed class StaticOptionsMonitor(ApiKeyAuthenticationOptions value) : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public ApiKeyAuthenticationOptions CurrentValue => value;

        public ApiKeyAuthenticationOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
