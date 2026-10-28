using System.ComponentModel.DataAnnotations;

namespace FinancialCopilot.API.Contracts;

public sealed record RegisterRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] string Password);
public sealed record LoginRequest([Required, EmailAddress, MaxLength(256)] string Email, [Required] string Password);
public sealed record EmailTokenRequest([Required, MaxLength(256)] string Token);
public sealed record EmailRequest([Required, EmailAddress, MaxLength(256)] string Email);
public sealed record PasswordResetRequest([Required, MaxLength(256)] string Token, [Required] string NewPassword);
public sealed record RegistrationAcceptedResponse(string Message);
public sealed record RevokeSessionRequest(string? RefreshToken);

/// <summary>Native email enrollment for the authenticated caller. Trimmed and normalised by the service.</summary>
public sealed record EnrollEmailRequest([Required, MaxLength(320)] string? Email);
public sealed record EmailEnrollmentConfirmRequest([Required, MaxLength(256)] string? Token);
public sealed record AddPasswordRequest([Required] string? NewPassword);

/// <summary>Account facts. <c>AccountAuthState</c> is one of A, B, C, D (Design §10).</summary>
public sealed record AccountResponse(
    string? Email,
    bool EmailConfirmed,
    bool HasPassword,
    bool HasTahlilAppIdentity,
    string AccountAuthState);

/// <summary>Launch body. Only <c>code</c> is read; unknown properties are ignored and never influence identity (AC-24).</summary>
public sealed record TahlilAppLaunchBody(string? Code);

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

/// <summary>Masked profile view. Names are shown in full; national id and mobile are masked (Design §10.2).</summary>
public sealed record OwnProfileResponse(
    string? FirstName,
    string? LastName,
    string? NationalIdMasked,
    string? MobileMasked,
    DateTimeOffset? ProfileSyncedAtUtc,
    string? DisplayName);

public sealed record ProfileRevealRequest(string? Field);

public sealed record ProfileRevealResponse(string Field, string Value);
