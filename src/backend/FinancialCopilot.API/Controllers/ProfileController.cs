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
            return Ok(new OwnProfileResponse(null, null, null, null, null, null, null, false));
        }

        return Ok(new OwnProfileResponse(
            view.FirstName,
            view.LastName,
            view.NationalIdMasked,
            view.MobileMasked,
            view.ProfileSyncedAtUtc,
            view.DisplayName,
            view.ContactMobileMasked,
            view.AvatarAvailable));
    }

    [HttpPut]
    public async Task<IActionResult> UpdateNames(OwnProfileNamesRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        try
        {
            await profiles.UpdateNamesAsync(userId, request.FirstName, request.LastName, cancellationToken);
            return NoContent();
        }
        catch (OwnProfileException exception)
        {
            return ProfileProblem(exception);
        }
    }

    [EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
    [HttpPost("mobile/request")]
    public async Task<IActionResult> RequestMobileCode(OwnProfileMobileRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        try
        {
            await profiles.RequestMobileCodeAsync(userId, request.Mobile, cancellationToken);
            return Accepted();
        }
        catch (OwnProfileException exception)
        {
            return ProfileProblem(exception);
        }
    }

    [EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
    [HttpPost("mobile/confirm")]
    public async Task<IActionResult> ConfirmMobileCode(OwnProfileMobileConfirmRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        try
        {
            await profiles.ConfirmMobileCodeAsync(userId, request.Code, cancellationToken);
            return NoContent();
        }
        catch (OwnProfileException exception)
        {
            return ProfileProblem(exception);
        }
    }

    [HttpPut("avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadAvatar([FromForm] OwnProfileAvatarUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        if (request.File.Length is <= 0 or > 5 * 1024 * 1024)
            return ValidationProblem(detail: "The image must be between 1 byte and 5 MiB.");
        await using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, cancellationToken);
        try
        {
            await profiles.SaveAvatarAsync(userId, stream.ToArray(), cancellationToken);
            return NoContent();
        }
        catch (OwnProfileException exception)
        {
            return ProfileProblem(exception);
        }
    }

    [HttpGet("avatar")]
    public async Task<IActionResult> GetAvatar(CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        Response.Headers["Cache-Control"] = "no-store";
        var avatar = await profiles.GetAvatarAsync(userId, cancellationToken);
        return avatar is null ? NotFound() : File(avatar.Content, avatar.ContentType);
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> RemoveAvatar(CancellationToken cancellationToken)
    {
        if (!TryReadUserId(out var userId)) return Forbid();
        await profiles.RemoveAvatarAsync(userId, cancellationToken);
        return NoContent();
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

    private ObjectResult ProfileProblem(OwnProfileException exception) =>
        Problem(type: $"https://financialcopilot/errors/{exception.ErrorCode}", title: exception.Message, statusCode: exception.StatusCode);
}
