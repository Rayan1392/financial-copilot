using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication.TahlilApp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

/// <summary>
/// Humble adapter for the TahlilApp launch (Design §9.2, §22.3). Maps the request body to the use case and the
/// outcome to HTTP. Holds no business decisions.
/// </summary>
[ApiController]
[Route("api/auth/v1/tahlilapp")]
public sealed class TahlilAppLaunchController(
    TahlilAppLaunchUseCase launchUseCase,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookieName = "financial_copilot_refresh";

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("launch")]
    public async Task<ActionResult<OwnedIdentitySessionResponse>> Launch(
        TahlilAppLaunchBody request,
        CancellationToken cancellationToken)
    {
        var presentedRefreshToken = Request.Cookies.TryGetValue(RefreshCookieName, out var token) ? token : null;
        var result = await launchUseCase.ExecuteAsync(
            new FinancialCopilot.Application.Authentication.TahlilApp.TahlilAppLaunchRequest(
                request.Code ?? string.Empty,
                presentedRefreshToken,
                HttpContext.TraceIdentifier),
            cancellationToken);

        switch (result.Outcome)
        {
            case TahlilAppLaunchOutcome.Succeeded when result.Session is { } session:
                WriteRefreshCookie(session.RefreshToken, session.RefreshTokenExpiresAt);
                return Ok(new OwnedIdentitySessionResponse(
                    session.AccessToken,
                    session.AccessTokenExpiresAt,
                    new OwnedIdentityProfileResponse(
                        session.UserId,
                        session.Email ?? string.Empty,
                        session.TenantId,
                        session.Roles,
                        session.Permissions)));

            case TahlilAppLaunchOutcome.Disabled:
                return NotFound();

            case TahlilAppLaunchOutcome.AccountDisabled:
                return LaunchProblem("launch-account-disabled", "The account is not available.", StatusCodes.Status403Forbidden);

            case TahlilAppLaunchOutcome.Unavailable:
                return LaunchProblem("launch-unavailable", "TahlilApp is not available. Try again shortly.", StatusCodes.Status503ServiceUnavailable);

            default:
                // Invalid, expired, reused or malformed codes all share one response (Design §8, no oracle).
                return LaunchProblem("launch-invalid", "The launch request is invalid or has expired.", StatusCodes.Status401Unauthorized);
        }
    }

    private ObjectResult LaunchProblem(string errorCode, string title, int statusCode) =>
        Problem(
            type: $"https://financialcopilot/errors/{errorCode}",
            title: title,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });

    private void WriteRefreshCookie(string refreshToken, DateTimeOffset expiresAt) =>
        Response.Cookies.Append(
            RefreshCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Expires = expiresAt,
                Path = "/api/auth/v1"
            });
}
