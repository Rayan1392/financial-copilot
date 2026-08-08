using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/ai/semantic-dialogue")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
public sealed class AdminSemanticDialogueController(
    ISemanticDialogueMetricsQuery metricsQuery) : ControllerBase
{
    [HttpGet("metrics")]
    public ActionResult<SemanticDialogueDashboardResponse> GetMetrics() =>
        Ok(new SemanticDialogueDashboardResponse(
            metricsQuery.GetSnapshot(),
            metricsQuery.GetAlerts()));
}

public sealed record SemanticDialogueDashboardResponse(
    IReadOnlyCollection<SemanticCapabilityMetrics> Metrics,
    IReadOnlyCollection<SemanticQualityAlert> Alerts);
