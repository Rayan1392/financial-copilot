using FinancialCopilot.Domain.Identity;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.UnitTests;

public sealed class IdentityModelTests
{
    [Fact]
    public void User_RequiresTenantContext()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new User(Guid.NewGuid(), Guid.Empty, "external-user", "user@example.com"));

        Assert.Equal("tenantId", exception.ParamName);
    }

    [Fact]
    public void ApiClient_PreservesTenantAndActiveState()
    {
        var tenantId = Guid.NewGuid();
        var client = new ApiClient(Guid.NewGuid(), tenantId, "Partner Client", "HASH");

        Assert.Equal(tenantId, client.TenantId);
        Assert.True(client.IsActive);
    }

    [Fact]
    public void CurrentActor_ApiClientDoesNotRequireUserId()
    {
        var tenantId = Guid.NewGuid();
        var apiClientId = Guid.NewGuid();

        var actor = new CurrentActor(
            ActorType.ApiClient,
            apiClientId,
            tenantId,
            AuthenticationMode.ApiClient,
            ApiClientId: apiClientId);

        Assert.Equal(ActorType.ApiClient, actor.ActorType);
        Assert.Equal(apiClientId, actor.ActorId);
        Assert.Null(actor.UserId);
        Assert.Equal(apiClientId, actor.ApiClientId);
        Assert.Equal(tenantId, actor.TenantId);
    }
}
