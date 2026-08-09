using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/funds/{fundId:guid}/portfolio-intelligence")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class FundPortfolioIntelligenceController(
    IFundPortfolioIntelligenceReadUseCase intelligence,
    IFundPortfolioIntelligenceDetailRepository details) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<FundPortfolioIntelligenceResponse>> Get(
        Guid fundId,
        [FromQuery] DateOnly? periodEndDate,
        CancellationToken cancellationToken)
    {
        var result = await intelligence.ExecuteAsync(fundId, periodEndDate, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("details/{section}")]
    public async Task<ActionResult<FundPortfolioIntelligenceDetailPage>> GetDetails(
        Guid fundId,
        string section,
        [FromQuery] DateOnly? periodEndDate,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<FundPortfolioIntelligenceSection>(section, true, out var parsed))
            return BadRequest(new { code = "INVALID_PORTFOLIO_INTELLIGENCE_SECTION", message = "Section must be Holdings, Activity, Allocation, Sectors, IncomeAttribution, Risk, or SourceEvidence." });
        return Ok(await details.QueryAsync(new(fundId, periodEndDate, parsed, cursor, pageSize), cancellationToken));
    }
}
