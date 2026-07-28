using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.MarketViews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/market")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MarketController(IMarketSummaryService marketSummary) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<MarketSummary>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await marketSummary.GetAsync(cancellationToken));
}

