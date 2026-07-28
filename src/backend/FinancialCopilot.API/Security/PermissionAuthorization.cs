using System.Security.Claims;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace FinancialCopilot.API.Security;

public sealed record PermissionRequirement(
    string PermissionCode,
    bool AllowApiClient = false) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var isApiClient = string.Equals(
            context.User.FindFirstValue(FinancialCopilotClaimTypes.AuthenticationMode),
            AuthenticationMode.ApiClient.ToString(),
            StringComparison.OrdinalIgnoreCase);
        if ((requirement.AllowApiClient && isApiClient) ||
            context.User.HasClaim(FinancialCopilotClaimTypes.Permission, requirement.PermissionCode))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
