using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/admin/ai")]
[Route("api/v1/admin/ai")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminAiController(
    ICurrentActorContext actorContext,
    IAiModelProviderDiagnostics diagnostics) : ControllerBase
{
    [HttpGet("provider")]
    public ActionResult<AdminAiProviderResponse> GetProvider()
    {
        var active = diagnostics.GetActiveProvider(actorContext.Actor.TenantId);
        return Ok(new AdminAiProviderResponse(
            active.ConfiguredProviderKey,
            active.ProviderKey,
            active.ModelKey,
            active.Capabilities.ToString(),
            active.Available));
    }
}
