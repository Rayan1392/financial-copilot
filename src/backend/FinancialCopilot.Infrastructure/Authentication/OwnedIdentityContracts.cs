namespace FinancialCopilot.Infrastructure.Authentication;

public sealed record OwnedIdentitySession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    OwnedIdentityProfile Profile);

public sealed record OwnedIdentityProfile(
    Guid UserId,
    string? Email,
    Guid TenantId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

/// <summary>Account facts for the authenticated caller (Design §10 account-state model).</summary>
public sealed record OwnedAccountSnapshot(
    string? Email,
    bool EmailConfirmed,
    bool HasPassword,
    bool HasTahlilAppIdentity,
    string AccountAuthState);

public interface IOwnedIdentityService
{
    /// <summary>
    /// Starts native email enrollment for an existing user. Returns normally in every non-error outcome
    /// (address owned by another account, already this user's, cooldown, or token issued) so the caller
    /// cannot learn which applies. Throws 400 for malformed input and 503 when email delivery is disabled.
    /// </summary>
    Task EnrollEmailAsync(Guid userId, string email, CancellationToken cancellationToken);

    /// <summary>Consumes an <c>email_enrollment</c> token held by <paramref name="userId"/> and sets the email as confirmed.</summary>
    Task ConfirmEmailEnrollmentAsync(Guid userId, string token, CancellationToken cancellationToken);

    /// <summary>Adds the first password to a user whose email is confirmed. Does not create a session.</summary>
    Task AddPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);

    /// <summary>Reads the account facts and computed auth state for the caller.</summary>
    Task<OwnedAccountSnapshot> GetAccountAsync(Guid userId, CancellationToken cancellationToken);

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

    /// <summary>Ensures roles, permissions and tenant baseline exist before an external identity is provisioned.</summary>
    Task PrepareBaselineAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a session for a local user resolved by an external identity (TahlilApp launch).
    /// Revokes <paramref name="presentedRefreshToken"/> when supplied (session fixation defence).
    /// </summary>
    Task<OwnedIdentitySession> CreateSessionForLinkedUserAsync(
        Guid userId,
        Guid tenantId,
        string authenticationMethod,
        string? presentedRefreshToken,
        CancellationToken cancellationToken);
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
