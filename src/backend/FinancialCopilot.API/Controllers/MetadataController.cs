using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Metadata;
using FinancialCopilot.Domain.Financial.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/ai/v1/metadata")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MetadataController(
    IFinancialMetricRegistry metricRegistry,
    IMetricCalculationPolicyProvider policyProvider,
    IAssistedQueryMetadataService metadataService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("metrics")]
    public ActionResult<MetricMetadataResponse> GetMetrics()
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var metrics = metricRegistry.GetSupportedMetrics(asOf)
            .Select(definition => new MetricDefinitionResponse(
                definition.Code.Value,
                definition.Version.Value,
                definition.DisplayName,
                definition.Description,
                definition.Category.ToString(),
                definition.Unit.Code,
                definition.SupportedPeriodTypes.Select(period => period.ToString()).ToArray(),
                definition.Aliases.Select(alias =>
                    new MetricAliasResponse(alias.Expression, alias.Language)).ToArray(),
                policyProvider.GetPolicies(definition.Code)
                    .Select(policy => policy.Version.Value)
                    .ToArray()))
            .ToArray();

        return Ok(new MetricMetadataResponse(metrics));
    }

    [HttpGet("periods")]
    public ActionResult<IReadOnlyCollection<PeriodMetadataResponse>> GetPeriods() =>
        Ok(metadataService.GetPeriods()
            .Select(period => new PeriodMetadataResponse(
                period.Code,
                period.DisplayName,
                period.DisplayNameFa))
            .ToArray());

    [HttpGet("symbols")]
    public async Task<ActionResult<IReadOnlyCollection<SymbolMetadataResponse>>> GetSymbols(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateSearch(search, limit)) return ValidationProblem(ModelState);

        var symbols = await metadataService.SearchSymbolsAsync(search, limit, cancellationToken);
        return Ok(symbols.Select(symbol => new SymbolMetadataResponse(
            symbol.SymbolCode,
            symbol.CompanyName,
            symbol.CompanyNameEnglish,
            symbol.IndustryName)).ToArray());
    }

    [HttpGet("industries")]
    public async Task<ActionResult<IReadOnlyCollection<IndustryMetadataResponse>>> GetIndustries(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateSearch(search, limit)) return ValidationProblem(ModelState);

        var industries = await metadataService.SearchIndustriesAsync(search, limit, cancellationToken);
        return Ok(industries.Select(industry => new IndustryMetadataResponse(
            industry.IndustryId,
            industry.DisplayName)).ToArray());
    }

    private bool ValidateSearch(string? search, int limit)
    {
        if (limit is < 1 or > 50)
        {
            ModelState.AddModelError(nameof(limit), "Limit must be between 1 and 50.");
        }

        if (search?.Length > 100)
        {
            ModelState.AddModelError(nameof(search), "Search must not exceed 100 characters.");
        }

        return ModelState.IsValid;
    }
}
