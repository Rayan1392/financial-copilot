namespace FinancialCopilot.API.Security;

public sealed class AuthenticatedActorRateLimitOptions
{
    public const string SectionName = "RateLimiting:AuthenticatedActor";

    public int PermitLimit { get; init; } = 60;

    public int WindowSeconds { get; init; } = 60;
}
