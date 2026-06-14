using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Domain.Financial.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/metric-alias")]
[Authorize(Policy = AuthorizationPolicies.DataAdmin)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminMetricAliasController(
    IMetricAliasCandidateRepository candidateRepo,
    IDynamicMetricAliasRepository aliasRepo,
    IMetricAliasCacheInvalidator cacheInvalidator) : ControllerBase
{
    [HttpGet("candidates")]
    public async Task<IActionResult> GetCandidates(
        [FromQuery] string? status,
        [FromQuery] string? language,
        [FromQuery] string? metricCode,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        MetricAliasCandidateStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<MetricAliasCandidateStatus>(status, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var query = new MetricAliasCandidateQuery(statusFilter, language, metricCode, take, skip);
        var candidates = await candidateRepo.QueryAsync(query, cancellationToken);

        return Ok(candidates.Select(c => new
        {
            c.Id,
            c.Expression,
            c.NormalizedExpression,
            c.Language,
            SuggestedMetricCode = c.SuggestedMetricCode.Value,
            c.SuggestedMetricVersion,
            Status = c.Status.ToString(),
            c.ConfidenceScore,
            c.FrequencyCount,
            c.DistinctActorCount,
            c.FirstSeenAt,
            c.LastSeenAt,
            c.EvidenceExamplesJson,
            c.RejectionReason,
            c.PromotedAliasId,
        }));
    }

    [HttpPost("candidates/{id:guid}/approve")]
    public async Task<IActionResult> ApproveCandidate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var candidate = await candidateRepo.FindByIdAsync(id, cancellationToken);
        if (candidate is null)
            return NotFound();

        var aliasId = Guid.NewGuid();
        var alias = new DynamicMetricAlias(
            Id: aliasId,
            Expression: candidate.Expression,
            NormalizedExpression: candidate.NormalizedExpression,
            Language: candidate.Language,
            MetricCode: candidate.SuggestedMetricCode,
            MetricVersion: candidate.SuggestedMetricVersion ?? "v1",
            Source: MetricAliasSource.AdminApproved,
            Status: MetricAliasStatus.Active,
            ConfidenceScore: candidate.ConfidenceScore,
            FrequencyCount: candidate.FrequencyCount,
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedBy: User.Identity?.Name,
            ApprovedAt: DateTimeOffset.UtcNow,
            ApprovedBy: User.Identity?.Name,
            DisabledAt: null,
            DisabledBy: null,
            DisableReason: null);

        await aliasRepo.AddAsync(alias, cancellationToken);
        await candidateRepo.ApproveAsync(id, User.Identity?.Name ?? "admin", aliasId, cancellationToken);
        cacheInvalidator.InvalidateLanguage(candidate.Language);

        return Ok(new { AliasId = aliasId });
    }

    [HttpPost("candidates/{id:guid}/reject")]
    public async Task<IActionResult> RejectCandidate(
        Guid id,
        [FromBody] RejectCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var candidate = await candidateRepo.FindByIdAsync(id, cancellationToken);
        if (candidate is null)
            return NotFound();

        await candidateRepo.RejectAsync(id, request.Reason ?? "Rejected by admin.", cancellationToken);
        return NoContent();
    }

    [HttpPost("aliases/{id:guid}/disable")]
    public async Task<IActionResult> DisableAlias(
        Guid id,
        [FromBody] DisableAliasRequest request,
        CancellationToken cancellationToken)
    {
        var actorName = User.Identity?.Name ?? "admin";
        await aliasRepo.DisableAsync(id, actorName, request.Reason ?? "Disabled by admin.", cancellationToken);
        cacheInvalidator.InvalidateLanguage(request.Language ?? string.Empty);
        return NoContent();
    }
}

public sealed record RejectCandidateRequest(string? Reason);
public sealed record DisableAliasRequest(string? Reason, string? Language);
