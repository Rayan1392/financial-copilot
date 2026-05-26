using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
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
}
