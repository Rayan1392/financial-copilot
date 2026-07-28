using System.Security.Claims;
using System.Threading.RateLimiting;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.API.Security;

public static class RateLimitPolicies
{
    public const string AuthenticatedActor = "AuthenticatedActor";

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
