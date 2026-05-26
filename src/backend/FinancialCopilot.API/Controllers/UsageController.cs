using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/usage")]
[Authorize(Policy = AuthorizationPolicies.ApiClientOnly)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class UsageController(ICurrentActorContext actorContext) : ControllerBase
{
    [HttpGet("api-client/{clientId:guid}")]
    public IActionResult GetApiClientUsage(Guid clientId)
    {
        var actor = actorContext.Actor;

        if (actor.ApiClientId != clientId)
        {
            return Forbid();
        }

        return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
        {
            Type = "https://financialcopilot/errors/not-implemented",
            Title = "Capability is not implemented.",
            Status = StatusCodes.Status501NotImplemented,
            Detail = "Usage Accounting will be implemented in a subsequent story."
        });
    }
}
