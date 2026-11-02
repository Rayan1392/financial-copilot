using System.Security.Claims;
using System.Threading.RateLimiting;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.API.Security;

public static class RateLimitPolicies
{
    public const string AuthenticatedActor = "AuthenticatedActor";
    public const string PublicAuthentication = "PublicAuthentication";
    public const string TahlilAppTopUpPartner = "TahlilAppTopUpPartner";
    public const string TahlilAppCreditBalance = "TahlilAppCreditBalance";

    public static RateLimitPartition<string> PublicIp(HttpContext context, PublicAuthenticationRateLimitOptions options) =>
        PublicIp(context, options.PermitLimit, options.WindowSeconds);

    public static RateLimitPartition<string> PublicIp(HttpContext context, int permitLimit, int windowSeconds) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(windowSeconds)
            });

    public static RateLimitPartition<string> Partition(
        HttpContext context,
        AuthenticatedActorRateLimitOptions options)
    {
        var principal = context.User;
        var clientId = principal.FindFirstValue(FinancialCopilotClaimTypes.ApiClientId);
        var userId = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var partitionKey = !string.IsNullOrWhiteSpace(clientId)
            ? $"client:{clientId}"
            : $"user:{userId ?? "anonymous"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = options.PermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(options.WindowSeconds)
            });
    }
}

public sealed class PublicAuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting:PublicAuthentication";
    public int PermitLimit { get; init; } = 10;
    public int WindowSeconds { get; init; } = 60;
}

/// <summary>
/// Per-IP limit for the anonymous Feature 151 balance read. It is separate from the
/// login/registration budget so partner reads and credential brute-force protection
/// cannot starve each other.
/// </summary>
public sealed class TahlilAppCreditBalanceRateLimitOptions
{
    public const string SectionName = "RateLimiting:TahlilAppCreditBalance";
    public int PermitLimit { get; init; } = 60;
    public int WindowSeconds { get; init; } = 60;
}
