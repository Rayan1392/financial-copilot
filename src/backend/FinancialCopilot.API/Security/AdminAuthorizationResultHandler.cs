using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Security;

public sealed class AdminAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/admin") ||
            authorizeResult.Succeeded)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var status = authorizeResult.Challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        var code = authorizeResult.Challenged ? "authentication-required" : "permission-denied";
        var problem = new ProblemDetails
        {
            Type = $"https://financialcopilot/errors/{code}",
            Title = code,
            Status = status,
            Detail = authorizeResult.Challenged
                ? "Authentication is required for this admin operation."
                : "The authenticated actor does not have permission for this admin operation."
        };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
