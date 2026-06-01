namespace FinancialCopilot.API.Contracts;

public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
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
