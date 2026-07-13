using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/telegram")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class TelegramMembershipController(
    ITelegramMembershipService membershipService,
    ICurrentActorContext currentActor) : ControllerBase
{
    [Authorize(Policy = AuthorizationPolicies.TelegramMembershipReadSelf)]
    [HttpPost("membership/verify")]
    public async Task<ActionResult<TelegramMembershipVerificationResult>> Verify(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await membershipService.VerifyRequiredChannelMembershipAsync(
                currentActor.Actor,
                HttpContext.TraceIdentifier,
                cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Type = "https://financialcopilot/errors/telegram-membership-unavailable",
                Title = exception.Message,
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["correlationId"] = HttpContext.TraceIdentifier }
            });
        }
    }

    [Authorize(Policy = AuthorizationPolicies.TelegramMembershipReadSelf)]
    [HttpGet("entitlement/me")]
    public async Task<ActionResult<TelegramEntitlementView>> GetMyEntitlement(CancellationToken cancellationToken) =>
        Ok(await membershipService.GetMyTelegramEntitlementAsync(
            currentActor.Actor,
            HttpContext.TraceIdentifier,
            cancellationToken));
}
