using FinancialCopilot.Application.Authentication;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.API.Security;

/// <summary>
/// The API key clients of <see cref="ApiKeyAuthenticationOptions"/>, read on every call so configuration reloads apply.
/// Entries with an invalid client or tenant ID are ignored, exactly as authentication ignores them.
/// </summary>
public sealed class ConfiguredApiClientDirectory(IOptionsMonitor<ApiKeyAuthenticationOptions> options) : IApiClientDirectory
{
    public ApiClientDescriptor? Find(Guid tenantId, Guid clientId)
    {
        foreach (var client in options.CurrentValue.Clients)
        {
            if (Guid.TryParse(client.ClientId, out var configuredClientId) && configuredClientId == clientId &&
                Guid.TryParse(client.TenantId, out var configuredTenantId) && configuredTenantId == tenantId)
            {
                return new ApiClientDescriptor(clientId, string.IsNullOrWhiteSpace(client.Name) ? null : client.Name.Trim());
            }
        }

        return null;
    }
}
