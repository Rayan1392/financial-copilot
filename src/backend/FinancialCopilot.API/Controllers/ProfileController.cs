using System.Security.Claims;
using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

/// <summary>
/// Self-service profile (Design §9.2, §10.2). Every operation is scoped to the authenticated subject;
/// no endpoint accepts a user id, so a caller cannot address another user's profile.
/// </summary>
[ApiController]
[Route("api/auth/v1/profile")]
[Authorize]
public sealed class ProfileController(OwnProfileUseCase profiles) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<OwnProfileResponse>> Get(CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId))
        {
            return Forbid();
        }

        Response.Headers["Cache-Control"] = "no-store";
        var view = await profiles.GetAsync(userId, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        return Ok(new OwnProfileResponse(
            view.FirstName,
            view.LastName,
            view.NationalIdMasked,
            view.MobileMasked,
            view.ProfileSyncedAtUtc));
    }

    [EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
    [HttpPost("reveal")]
    public async Task<ActionResult<ProfileRevealResponse>> Reveal(
        ProfileRevealRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId))
        {
            return Forbid();
        }

        var field = request.Field switch
        {
            "nationalId" => RevealableProfileField.NationalId,
            "mobile" => RevealableProfileField.Mobile,
            _ => (RevealableProfileField?)null
        };
        if (field is null)
        {
            return ValidationProblem(detail: "field must be 'nationalId' or 'mobile'.");
        }

        Response.Headers["Cache-Control"] = "no-store";
        var value = await profiles.RevealAsync(userId, field.Value, cancellationToken);
        if (value is null)
        {
            return NotFound();
        }

        return Ok(new ProfileRevealResponse(request.Field!, value));
    }

    private bool TryReadUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue("sub"), out userId);
}
