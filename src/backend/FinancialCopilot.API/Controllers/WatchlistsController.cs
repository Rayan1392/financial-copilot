using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.MarketViews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/watchlists")]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class WatchlistsController(
    ICurrentActorContext actorContext,
    IWatchlistService watchlists) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistReadSelf)]
    public async Task<ActionResult<WatchlistView>> GetMine(CancellationToken cancellationToken) =>
        Ok(await watchlists.GetAsync(actorContext.Actor, cancellationToken));

    [HttpPut("me")]
    [Authorize(Policy = AuthorizationPolicies.WatchlistWriteSelf)]
    public async Task<ActionResult<WatchlistView>> UpdateMine(
        UpdateWatchlistRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await watchlists.UpdateAsync(
                actorContext.Actor,
                request.Symbols ?? [],
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            ModelState.AddModelError(nameof(request.Symbols), exception.Message);
            return ValidationProblem(ModelState);
        }
    }
}

