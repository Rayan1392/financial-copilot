using System.Security.Claims;
using FinancialCopilot.Application.Authentication;

namespace FinancialCopilot.API.Security;

public sealed class HttpCurrentActorContext(IHttpContextAccessor httpContextAccessor) : ICurrentActorContext
{
    public CurrentActor Actor
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new InvalidOperationException("An authenticated actor is required.");
            }

            var tenantId = ReadRequiredGuid(principal, FinancialCopilotClaimTypes.TenantId);
            var mode = ReadAuthenticationMode(principal);

            return mode switch
            {
                AuthenticationMode.WebAppUser => new CurrentActor(
                    ActorType.User,
                    ReadSubject(principal),
                    tenantId,
                    mode,
                    UserId: ReadSubject(principal)),
                AuthenticationMode.ApiClient => new CurrentActor(
                    ActorType.ApiClient,
                    ReadRequiredGuid(principal, FinancialCopilotClaimTypes.ApiClientId),
                    tenantId,
                    mode,
                    ApiClientId: ReadRequiredGuid(principal, FinancialCopilotClaimTypes.ApiClientId)),
                _ => throw new InvalidOperationException("Unsupported authentication mode.")
            };
        }
    }

    private static AuthenticationMode ReadAuthenticationMode(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(FinancialCopilotClaimTypes.AuthenticationMode);

        if (!Enum.TryParse<AuthenticationMode>(value, ignoreCase: true, out var mode))
        {
            throw new InvalidOperationException("Authentication mode is missing or invalid.");
        }

        return mode;
    }

    private static Guid ReadSubject(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
        {
            throw new InvalidOperationException("Authenticated user id is missing or invalid.");
        }

        return userId;
    }

    private static Guid ReadRequiredGuid(ClaimsPrincipal principal, string claimType)
    {
        if (!Guid.TryParse(principal.FindFirstValue(claimType), out var value))
        {
            throw new InvalidOperationException($"Required claim '{claimType}' is missing or invalid.");
        }

        return value;
    }
}
