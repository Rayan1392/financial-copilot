using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/market")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MarketController(
    IMarketSummaryService marketSummary,
    FinancialIngestionDbContext dbContext) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<MarketSummary>> GetSummary(CancellationToken cancellationToken) =>
        Ok(await marketSummary.GetAsync(cancellationToken));

    [HttpGet("external-company-id")]
    public async Task<ActionResult<string>> GetExternalCompanyId(
        [FromQuery] string? symbol,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            ModelState.AddModelError(nameof(symbol), "Symbol is required.");
            return ValidationProblem(ModelState);
        }

        var externalCompanyId = await dbContext.NoavaranEligibleCompanies
            .AsNoTracking()
            .Where(company => company.TseSymbol == symbol)
            .Select(company => company.ExternalCompanyId)
            .FirstOrDefaultAsync(cancellationToken);

        return externalCompanyId is null ? NotFound() : Ok(externalCompanyId);
    }
}

