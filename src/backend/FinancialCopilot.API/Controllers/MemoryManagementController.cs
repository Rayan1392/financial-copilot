using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.Memory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/memory")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class MemoryManagementController(
    ICurrentActorContext actorContext,
    IMemoryConsentService consentService,
    IMemoryControlService controlService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("consent")]
    public async Task<ActionResult<IReadOnlyCollection<ConsentStatusResponse>>> GetConsent(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        var statuses = new List<ConsentStatusResponse>();
        foreach (var type in Enum.GetValues<MemoryType>())
        {
            foreach (var purpose in Enum.GetValues<MemoryPurpose>())
            {
                var consent = await consentService.GetConsentAsync(subject, type, purpose, cancellationToken);
                statuses.Add(new ConsentStatusResponse(
                    type.ToString(),
                    purpose.ToString(),
                    consent?.Status.ToString() ?? MemoryConsentStatus.NotFound.ToString(),
                    consent?.GrantedAt,
                    consent?.ExpiresAt));
            }
        }

        return Ok(statuses);
    }

    [HttpPost("consent")]
    public async Task<ActionResult<ConsentStatusResponse>> GrantConsent(
        [FromBody] GrantConsentRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        if (!Enum.TryParse<MemoryType>(httpRequest.MemoryType, out var memoryType))
        {
            ModelState.AddModelError(nameof(httpRequest.MemoryType), $"Unknown memory type: {httpRequest.MemoryType}");
            return ValidationProblem(ModelState);
        }

        if (!Enum.TryParse<MemoryPurpose>(httpRequest.Purpose, out var purpose))
        {
            ModelState.AddModelError(nameof(httpRequest.Purpose), $"Unknown purpose: {httpRequest.Purpose}");
            return ValidationProblem(ModelState);
        }

        var policy = new MemoryConsentPolicy(
            subject.TenantId,
            subject.SubjectId,
            memoryType,
            purpose,
            MemoryConsentStatus.Granted,
            timeProvider.GetUtcNow(),
            RevokedAt: null,
            httpRequest.ExpiresAt,
            PolicyVersion: "v1");

        var result = await consentService.GrantAsync(policy, cancellationToken);

        return Ok(new ConsentStatusResponse(
            result.MemoryType.ToString(),
            result.Purpose.ToString(),
            result.Status.ToString(),
            result.GrantedAt,
            result.ExpiresAt));
    }

    [HttpDelete("consent/{type}/{purpose}")]
    public async Task<IActionResult> RevokeConsent(
        string type,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        if (!Enum.TryParse<MemoryType>(type, out var memoryType))
            return NotFound();

        if (!Enum.TryParse<MemoryPurpose>(purpose, out var memoryPurpose))
            return NotFound();

        await consentService.RevokeAsync(subject, memoryType, memoryPurpose, "v1", cancellationToken);
        return NoContent();
    }

    [HttpGet("records")]
    public async Task<ActionResult<IReadOnlyCollection<MemoryRecordResponse>>> InspectRecords(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        var records = await controlService.InspectAsync(subject, cancellationToken);
        return Ok(records.Select(MapRecord).ToList());
    }

    [HttpPost("records")]
    public async Task<ActionResult<WriteMemoryRecordResponse>> WriteRecord(
        [FromBody] WriteMemoryRecordRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        if (string.IsNullOrWhiteSpace(httpRequest.Summary))
        {
            ModelState.AddModelError(nameof(httpRequest.Summary), "Summary is required.");
            return ValidationProblem(ModelState);
        }

        if (!Enum.TryParse<MemoryType>(httpRequest.Type, out var memoryType))
        {
            ModelState.AddModelError(nameof(httpRequest.Type), $"Unknown memory type: {httpRequest.Type}");
            return ValidationProblem(ModelState);
        }

        if (!Enum.TryParse<MemoryPurpose>(httpRequest.Purpose, out var purpose))
        {
            ModelState.AddModelError(nameof(httpRequest.Purpose), $"Unknown purpose: {httpRequest.Purpose}");
            return ValidationProblem(ModelState);
        }

        if (!Enum.TryParse<MemorySensitivity>(httpRequest.Sensitivity, out var sensitivity))
        {
            ModelState.AddModelError(nameof(httpRequest.Sensitivity), $"Unknown sensitivity: {httpRequest.Sensitivity}");
            return ValidationProblem(ModelState);
        }

        var id = await controlService.WriteAsync(
            subject,
            memoryType,
            purpose,
            sensitivity,
            httpRequest.Summary,
            new MemoryProvenance("UserExplicit", null, timeProvider.GetUtcNow()),
            retention: null,
            cancellationToken);

        return StatusCode(201, new WriteMemoryRecordResponse(id));
    }

    [HttpDelete("records/{memoryId:guid}")]
    public async Task<IActionResult> DeleteRecord(
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        var correlationId = HttpContext.TraceIdentifier;
        await controlService.DeleteAsync(
            new MemoryDeletionRequest(subject, memoryId, correlationId),
            cancellationToken);

        return NoContent();
    }

    [HttpDelete("records")]
    public async Task<IActionResult> DeleteAllRecords(CancellationToken cancellationToken)
    {
        if (!TryGetUserSubject(out var subject))
            return Forbid();

        var correlationId = HttpContext.TraceIdentifier;
        await controlService.DeleteAllAsync(subject, correlationId, cancellationToken);
        return NoContent();
    }

    private bool TryGetUserSubject(out MemorySubject subject)
    {
        var actor = actorContext.Actor;
        if (actor.ActorType != ActorType.User || actor.UserId is null)
        {
            subject = default!;
            return false;
        }

        subject = new MemorySubject(actor.TenantId, actor.UserId.Value);
        return true;
    }

    private static MemoryRecordResponse MapRecord(OptionalMemoryRecord record) =>
        new(
            record.MemoryId,
            record.Type.ToString(),
            record.Purpose.ToString(),
            record.Sensitivity.ToString(),
            record.Summary,
            record.Provenance.CapturedAt,
            record.Retention.ExpiresAt);
}
