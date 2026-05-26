namespace FinancialCopilot.API.Security;

public sealed class ApiKeyAuthenticationOptions
{
    public const string SectionName = "Authentication:ApiKeys";

    public IList<ApiKeyClientCredential> Clients { get; init; } = [];
}

public sealed class ApiKeyClientCredential
{
    public string ClientId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string KeySha256 { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}
