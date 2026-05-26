using FinancialCopilot.Domain.Identity;

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
}
