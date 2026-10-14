using System.ComponentModel.DataAnnotations;

namespace FinancialCopilot.API.Contracts;

public sealed record RegisterRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] string Password);
public sealed record LoginRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] string Password);
public sealed record EmailTokenRequest([Required, MaxLength(256)] string Token);
public sealed record EmailRequest([Required, EmailAddress, MaxLength(256)] string Email);
public sealed record PasswordResetRequest([Required, MaxLength(256)] string Token, [Required] string NewPassword);
public sealed record RegistrationAcceptedResponse(string Message);
public sealed record RevokeSessionRequest(string? RefreshToken);

public sealed record OwnedIdentitySessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    OwnedIdentityProfileResponse User);

public sealed record OwnedIdentityProfileResponse(
    Guid UserId,
    string Email,
    Guid TenantId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
