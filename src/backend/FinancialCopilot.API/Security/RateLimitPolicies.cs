using System.Security.Claims;
using System.Threading.RateLimiting;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.API.Security;

public static class RateLimitPolicies
{
    public const string AuthenticatedActor = "AuthenticatedActor";
    public const string PublicAuthentication = "PublicAuthentication";

    public static RateLimitPartition<string> PublicIp(HttpContext context, PublicAuthenticationRateLimitOptions options) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown-ip",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = options.PermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(options.WindowSeconds)
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
