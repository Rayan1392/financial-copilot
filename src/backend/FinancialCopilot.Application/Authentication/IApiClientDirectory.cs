namespace FinancialCopilot.Application.Authentication;

/// <summary>A configured API client, as administrators may see it (no credentials).</summary>
public sealed record ApiClientDescriptor(Guid ClientId, string? Name);

/// <summary>
/// Read-only lookup of the API clients that authenticate with an API key. A client is found only in the tenant it is
/// configured for; inactive clients are still found so that their past activity stays identifiable.
/// </summary>
public interface IApiClientDirectory
{
    ApiClientDescriptor? Find(Guid tenantId, Guid clientId);
}

/// <summary>The directory of a host that has no API key clients configured.</summary>
public sealed class EmptyApiClientDirectory : IApiClientDirectory
{
    public ApiClientDescriptor? Find(Guid tenantId, Guid clientId) => null;
}
