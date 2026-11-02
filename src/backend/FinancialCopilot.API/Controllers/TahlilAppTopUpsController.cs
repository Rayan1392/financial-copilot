using System.Security.Claims;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/billing")]
public sealed class TahlilAppTopUpsController(TahlilAppTopUpService topUps, ICurrentActorContext actorContext, IConfiguration configuration) : ControllerBase
{
    [HttpGet("partners/tahlilapp/top-ups/eligibility")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.TahlilAppTopUpPartner, Policy = "TahlilAppTopUpPartner")]
    [EnableRateLimiting(RateLimitPolicies.TahlilAppTopUpPartner)]
    public async Task<IActionResult> Eligibility([FromQuery] string userId, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Billing:TahlilAppTopUps:Enabled")) return TopUpProblem(503, "topup-temporary-failure");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 160) return TopUpProblem(400, "invalid-topup-event");
        try
        {
            var result = await topUps.GetEligibilityAsync(userId, ct);
            return result is null ? TopUpProblem(404, "topup-user-unresolved") : Ok(result);
        }
        catch (TopUpContractException exception) { return TopUpProblem(exception.Status, exception.Code); }
    }

    [HttpPost("partners/tahlilapp/top-ups")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.TahlilAppTopUpPartner, Policy = "TahlilAppTopUpPartner")]
    [EnableRateLimiting(RateLimitPolicies.TahlilAppTopUpPartner)]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Deliver([FromBody] TopUpDeliveryRequest request, [FromHeader(Name = "X-Contract-Version")] string? contractVersion, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Billing:TahlilAppTopUps:Enabled")) return TopUpProblem(503, "topup-temporary-failure");
        if (contractVersion != "1") return TopUpProblem(400, "invalid-topup-event");
        try { return Ok(await topUps.DeliverAsync(request, ct)); }
        catch (TopUpContractException exception) { return TopUpProblem(exception.Status, exception.Code); }
    }

    [HttpGet("top-ups/{purchaseId}")]
    [Authorize(Policy = AuthorizationPolicies.UsageReadSelf)]
    [EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
    public async Task<IActionResult> Status(string purchaseId, CancellationToken ct)
    {
        var actor = actorContext.Actor;
        if (actor.UserId is not { } userId) return NotFound();
        var result = await topUps.GetStatusAsync(userId, actor.TenantId, purchaseId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    private ObjectResult TopUpProblem(int status, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = code, Type = $"urn:financial-copilot:problem:{code}" };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return StatusCode(status, problem);
    }
}
