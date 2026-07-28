namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class OwnedIdentityOptions
{
    public const string SectionName = "Authentication:OwnedIdentity";

    public string DefaultTenantId { get; init; } = "11111111-1111-1111-1111-111111111111";
    public string DefaultTenantName { get; init; } = "FinancialCopilot";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
