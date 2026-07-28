using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/disclosures")]
[Authorize(Policy = AuthorizationPolicies.MarketSummaryRead)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class DisclosuresController(IDisclosureListingUseCase disclosureListing) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DisclosureListingResult>> Get(
        [FromQuery] CompanyDisclosureType[]? types,
        [FromQuery] string? symbolOrCompany,
        [FromQuery] string[]? providerNames,
        [FromQuery] DateOnly? publishedFrom,
        [FromQuery] DateOnly? publishedTo,
        [FromQuery] DateTimeOffset? receivedFrom,
        [FromQuery] DateTimeOffset? receivedTo,
        [FromQuery] DisclosureConsolidationScope consolidationScope = DisclosureConsolidationScope.NonConsolidated,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await disclosureListing.ExecuteAsync(new DisclosureListingQuery(
                types, symbolOrCompany, providerNames, publishedFrom, publishedTo, receivedFrom, receivedTo,
                consolidationScope, page, pageSize), cancellationToken));
        }
        catch (DisclosureListingValidationException exception)
        {
            ModelState.AddModelError("disclosures", exception.Message);
            return ValidationProblem(ModelState);
        }
    }
}
