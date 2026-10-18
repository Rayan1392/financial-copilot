using System.Security.Claims;
using FinancialCopilot.API.Contracts;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using FinancialCopilot.API.Security;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/auth/v1")]
public sealed class AuthController(
    IOwnedIdentityService identityService,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookieName = "financial_copilot_refresh";
    private const string GenericEmailRequestMessage = "If the address is eligible, instructions will be sent by email.";
    private const string GenericEnrollmentMessage = "If the address can be used, a verification message will be sent to it.";

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("register")]
    public async Task<ActionResult<RegistrationAcceptedResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await identityService.RegisterAsync(request.Email, request.Password, cancellationToken);
            return Accepted(new RegistrationAcceptedResponse("Check your email for next steps."));
        }
        catch (OwnedIdentityException exception)
        {
            return Problem(type: $"https://financialcopilot/errors/{exception.ErrorCode}", title: exception.Message,
                statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });
        }
    }

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("login")]
    public Task<ActionResult<OwnedIdentitySessionResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            () => identityService.LoginAsync(request.Email, request.Password, cancellationToken));

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("email/verify")]
    public async Task<IActionResult> VerifyEmail(EmailTokenRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await identityService.VerifyEmailAsync(request.Token, cancellationToken);
            return NoContent();
        }
        catch (OwnedIdentityException exception)
        {
            return Problem(type: $"https://financialcopilot/errors/{exception.ErrorCode}", title: exception.Message,
                statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });
        }
    }

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("email/resend")]
    public async Task<IActionResult> ResendVerification(EmailRequest request, CancellationToken cancellationToken)
    {
        await identityService.ResendVerificationAsync(request.Email, cancellationToken);
        return Accepted(new RegistrationAcceptedResponse(GenericEmailRequestMessage));
    }

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        await identityService.RequestPasswordResetAsync(request.Email, cancellationToken);
        return Accepted(new RegistrationAcceptedResponse(GenericEmailRequestMessage));
    }

    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await identityService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
            return NoContent();
        }
        catch (OwnedIdentityException exception)
        {
            return Problem(type: $"https://financialcopilot/errors/{exception.ErrorCode}", title: exception.Message,
                statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("refresh")]
    public Task<ActionResult<OwnedIdentitySessionResponse>> Refresh(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            () => identityService.RefreshAsync(ReadRefreshToken(), cancellationToken));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = TryReadRefreshToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            await identityService.RevokeAsync(token, "User logged out.", cancellationToken);
        }
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(
        RevokeSessionRequest request,
        CancellationToken cancellationToken)
    {
        var token = request.RefreshToken ?? ReadRefreshToken();
        await identityService.RevokeAsync(token, "Session explicitly revoked.", cancellationToken);
        DeleteRefreshCookie();
        return NoContent();
    }

    /// <summary>
    /// One-time bootstrap: assigns the SuperAdmin role to the calling authenticated user.
    /// Returns 409 if a SuperAdmin already exists. Use this endpoint once to grant the first
    /// operator admin access, then use the Admin Management API for all subsequent role changes.
    /// </summary>
    [Authorize]
    [HttpPost("bootstrap-superadmin")]
    public async Task<IActionResult> BootstrapSuperAdmin(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
            return Forbid();

        try
        {
            await identityService.BootstrapSuperAdminAsync(userId, cancellationToken);
            return NoContent();
        }
        catch (OwnedIdentityException exception)
        {
            return Problem(
                type: $"https://financialcopilot/errors/{exception.ErrorCode}",
                title: exception.Message,
                statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public ActionResult<OwnedIdentityProfileResponse> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId) ||
            !Guid.TryParse(User.FindFirstValue("financial_copilot:tenant_id"), out var tenantId))
        {
            return Forbid();
        }

        return Ok(new OwnedIdentityProfileResponse(
            userId,
            User.FindFirstValue("email") ?? string.Empty,
            tenantId,
            User.FindAll("role").Select(claim => claim.Value).Distinct().Order().ToArray(),
            User.FindAll("financial_copilot:permission").Select(claim => claim.Value).Distinct().Order().ToArray()));
    }

    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.PublicAuthentication)]
    [HttpPost("credentials/email")]
    public async Task<IActionResult> EnrollEmail(EnrollEmailRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        try
        {
            await identityService.EnrollEmailAsync(userId, request.Email!, cancellationToken);
            return Accepted(new RegistrationAcceptedResponse(GenericEnrollmentMessage));
        }
        catch (OwnedIdentityException exception)
        {
            return ProblemFor(exception);
        }
    }

    [Authorize]
    [HttpPost("credentials/email/confirm")]
    public async Task<IActionResult> ConfirmEmailEnrollment(
        EmailEnrollmentConfirmRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        try
        {
            await identityService.ConfirmEmailEnrollmentAsync(userId, request.Token!, cancellationToken);
            return NoContent();
        }
        catch (OwnedIdentityException exception)
        {
            return ProblemFor(exception);
        }
    }

    [Authorize]
    [HttpPost("credentials/password")]
    public async Task<IActionResult> AddPassword(AddPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        try
        {
            await identityService.AddPasswordAsync(userId, request.NewPassword!, cancellationToken);
            return NoContent();
        }
        catch (OwnedIdentityException exception)
        {
            return ProblemFor(exception);
        }
    }

    [Authorize]
    [HttpGet("account")]
    public async Task<ActionResult<AccountResponse>> GetAccount(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        try
        {
            var account = await identityService.GetAccountAsync(userId, cancellationToken);
            return Ok(new AccountResponse(
                account.Email,
                account.EmailConfirmed,
                account.HasPassword,
                account.HasTahlilAppIdentity,
                account.AccountAuthState));
        }
        catch (OwnedIdentityException exception)
        {
            return ProblemFor(exception);
        }
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirstValue("sub"), out userId);

    private ObjectResult ProblemFor(OwnedIdentityException exception) =>
        Problem(
            type: $"https://financialcopilot/errors/{exception.ErrorCode}",
            title: exception.Message,
            statusCode: exception.StatusCode,
            extensions: new Dictionary<string, object?> { ["correlationId"] = HttpContext.TraceIdentifier });

    private async Task<ActionResult<OwnedIdentitySessionResponse>> ExecuteSessionAsync(
        Func<Task<OwnedIdentitySession>> action)
    {
        try
        {
            var session = await action();
            WriteRefreshCookie(session.RefreshToken, session.RefreshTokenExpiresAt);
            return Ok(Map(session));
        }
        catch (OwnedIdentityException exception)
        {
            return Problem(
                type: $"https://financialcopilot/errors/{exception.ErrorCode}",
                title: exception.Message,
                statusCode: exception.StatusCode,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = HttpContext.TraceIdentifier
                });
        }
    }

    private string ReadRefreshToken() =>
        TryReadRefreshToken() ??
        throw new OwnedIdentityException("refresh-token-required", "A refresh-token cookie is required.", 401);

    private string? TryReadRefreshToken() =>
        Request.Cookies.TryGetValue(RefreshCookieName, out var token) ? token : null;

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

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(
            RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth/v1"
            });

    private static OwnedIdentitySessionResponse Map(OwnedIdentitySession session) =>
        new(
            session.AccessToken,
            session.AccessTokenExpiresAt,
            new OwnedIdentityProfileResponse(
                session.Profile.UserId,
                session.Profile.Email ?? string.Empty,
                session.Profile.TenantId,
                session.Profile.Roles,
                session.Profile.Permissions));
}
