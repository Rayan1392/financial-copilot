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
    [HttpGet("partners/tahlilapp/credit-balance")]
    [EndpointSummary("Get a TahlilApp customer's current credit balance and plan status")]
    [EndpointDescription("Anonymous, read-only status lookup by stable TahlilApp userId. Returns expiry-aware finite balance or explicit Unlimited quota mode; does not evaluate checkout eligibility or mutate billing state.")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CreditBalance([FromQuery] string userId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128) return TopUpProblem(400, "invalid-topup-event");
        try
        {
            var result = await topUps.GetCreditBalanceAsync(userId, ct);
            return result is null ? TopUpProblem(404, "topup-user-unresolved") : Ok(result);
        }
        catch (TopUpContractException exception) { return TopUpProblem(exception.Status, exception.Code); }
    }

    [HttpPost("partners/tahlilapp/top-ups")]
    [EndpointSummary("Deliver a verified TahlilApp credit top-up")]
    [EndpointDescription("Requires the TahlilApp partner Bearer JWT. Accepts exactly invoiceId, userId, creditsPurchased, and paidAtUtc. Subscription source is resolved from SapioAI's authoritative synced records.")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.TahlilAppTopUpPartner, Policy = "TahlilAppTopUpPartner")]
    [EnableRateLimiting(RateLimitPolicies.TahlilAppTopUpPartner)]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Deliver([FromBody] TopUpDeliveryRequest request, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Billing:TahlilAppTopUps:Enabled")) return TopUpProblem(503, "topup-temporary-failure");
        try { return Ok(await topUps.DeliverAsync(request, ct)); }
        catch (TopUpContractException exception) { return TopUpProblem(exception.Status, exception.Code); }
    }

    [HttpGet("top-ups/{invoiceId}")]
    [Authorize(Policy = AuthorizationPolicies.UsageReadSelf)]
    [EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
    public async Task<IActionResult> Status(string invoiceId, CancellationToken ct)
    {
        var actor = actorContext.Actor;
        if (actor.UserId is not { } userId) return NotFound();
        var result = await topUps.GetStatusAsync(userId, actor.TenantId, invoiceId, ct);
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
