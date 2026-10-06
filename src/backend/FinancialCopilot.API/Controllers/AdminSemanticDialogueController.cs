using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/ai/semantic-dialogue")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
public sealed class AdminSemanticDialogueController(
    ISemanticDialogueMetricsQuery metricsQuery,
    ISemanticRoutingOperationalTelemetryQuery operationalTelemetryQuery) : ControllerBase
{
    [HttpGet("metrics")]
    public ActionResult<SemanticDialogueDashboardResponse> GetMetrics() =>
        Ok(new SemanticDialogueDashboardResponse(
            metricsQuery.GetSnapshot(),
            metricsQuery.GetAlerts()));

    [HttpGet("operational")]
    public ActionResult<SemanticRoutingOperationalSnapshot> GetOperationalTelemetry(
        [FromQuery] string? capabilityCode = null,
        [FromQuery] int maximumSamples = 500) =>
        Ok(operationalTelemetryQuery.GetSnapshot(capabilityCode, maximumSamples));
}

public sealed record SemanticDialogueDashboardResponse(
    IReadOnlyCollection<SemanticCapabilityMetrics> Metrics,
    IReadOnlyCollection<SemanticQualityAlert> Alerts);
