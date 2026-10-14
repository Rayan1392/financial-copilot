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
    Task RegisterAsync(string email, string password, CancellationToken cancellationToken);
    Task<OwnedIdentitySession> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task<OwnedIdentitySession> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, string reason, CancellationToken cancellationToken);
    Task VerifyEmailAsync(string token, CancellationToken cancellationToken);
    Task ResendVerificationAsync(string email, CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);
    Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken);
    /// <summary>
    /// Assigns the SuperAdmin role to the calling user.
    /// Throws <see cref="OwnedIdentityException"/> with status 409 if a SuperAdmin already exists.
    /// This endpoint is intentionally available to any authenticated user so the first operator
    /// can bootstrap admin access without a separate out-of-band step.
    /// </summary>
    Task BootstrapSuperAdminAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class AuthEmailOptions
{
    public const string SectionName = "Authentication:Email";
    public bool Enabled { get; init; }
    public string FrontendBaseUrl { get; init; } = "";
    public string TokenHashSecret { get; init; } = "";
    public int VerificationTokenHours { get; init; } = 24;
    public int PasswordResetTokenMinutes { get; init; } = 30;
    public int EmailCooldownSeconds { get; init; } = 60;
    public int EmailMaximumRequestsPerHour { get; init; } = 5;
}

public sealed class OwnedIdentityException(
    string errorCode,
    string message,
    int statusCode = 400) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}
