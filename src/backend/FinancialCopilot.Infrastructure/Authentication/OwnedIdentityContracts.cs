namespace FinancialCopilot.Infrastructure.Authentication;

public sealed record OwnedIdentitySession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    OwnedIdentityProfile Profile);

public sealed record OwnedIdentityProfile(
    Guid UserId,
    string Email,
    Guid TenantId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public interface IOwnedIdentityService
{
    Task<OwnedIdentitySession> RegisterAsync(string email, string password, CancellationToken cancellationToken);
    Task<OwnedIdentitySession> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<OwnedIdentitySession> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, string reason, CancellationToken cancellationToken);
}

public sealed class OwnedIdentityException(
    string errorCode,
    string message,
    int statusCode = 400) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}
