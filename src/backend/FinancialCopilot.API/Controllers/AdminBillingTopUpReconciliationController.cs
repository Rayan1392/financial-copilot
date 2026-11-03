using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/billing/top-ups")]
[Authorize(Policy = AuthorizationPolicies.AdminBillingAuditRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminBillingTopUpReconciliationController(
    TahlilAppTopUpService topUps,
    ICurrentActorContext actorContext) : ControllerBase
{
    [HttpGet("reconciliation")]
    public async Task<ActionResult<TopUpReconciliationResponse>> GetReconciliation(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 100.");
            return ValidationProblem(ModelState);
        }

        var result = await topUps.GetReconciliationAsync(actorContext.Actor.TenantId, limit, cancellationToken);
        return Ok(result);
    }
}
