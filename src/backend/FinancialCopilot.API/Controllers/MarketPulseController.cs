using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.MarketViews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/market-pulse")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MarketPulseController(
    ICurrentActorContext actorContext,
    IMarketPulseService marketPulse) : ControllerBase
{
    [HttpGet("latest")]
    public async Task<ActionResult<MarketPulseSnapshot>> GetLatest(
        [FromQuery] string? segment,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => marketPulse.GetLatestAsync(actorContext.Actor, segment, cancellationToken));

    [HttpGet("history")]
    public async Task<ActionResult<MarketPulseHistoryPage>> GetHistory(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] MarketPulseSessionState? state,
        [FromQuery] bool? isFinal,
        [FromQuery] string? segment,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(() => marketPulse.GetHistoryAsync(
            actorContext.Actor,
            new MarketPulseHistoryQuery(from, to, state, isFinal, segment, page, pageSize),
            cancellationToken));

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (MarketPulseValidationException exception)
        {
            ModelState.AddModelError("marketPulse", exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (MarketPulseAccessDeniedException)
        {
            return Forbid();
        }
    }
}
