namespace FinancialCopilot.Application.Authentication.Profile;

public enum RevealableProfileField
{
    NationalId,
    Mobile
}

/// <summary>Masked, self-service view of the caller's TahlilApp-sourced profile (Design §10.2, AC-68).</summary>
public sealed record OwnProfileView(
    string? FirstName,
    string? LastName,
    string? NationalIdMasked,
    string? MobileMasked,
    DateTimeOffset? ProfileSyncedAtUtc,
    string? DisplayName,
    string? ContactMobileMasked,
    bool AvatarAvailable);

public sealed record ProfileAvatar(byte[] Content, string ContentType);

public sealed class OwnProfileException(string errorCode, string message, int statusCode = 400) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}

/// <summary>Data-access port for the caller's own profile. Implemented in Infrastructure.</summary>
public interface IOwnProfileStore
{
    Task<OwnProfileView?> GetMaskedAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns>The full value of one field for the owner, or null when the field is absent or unreadable.</returns>
    Task<string?> RevealAsync(Guid userId, RevealableProfileField field, CancellationToken cancellationToken);

    Task UpdateNamesAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken);
    Task RequestMobileCodeAsync(Guid userId, string mobile, CancellationToken cancellationToken);
    Task ConfirmMobileCodeAsync(Guid userId, string code, CancellationToken cancellationToken);
    Task SaveAvatarAsync(Guid userId, byte[] upload, CancellationToken cancellationToken);
    Task RemoveAvatarAsync(Guid userId, CancellationToken cancellationToken);
    Task<ProfileAvatar?> GetAvatarAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Owner-only profile operations. The user id always comes from the authenticated subject, never from a request
/// parameter, so a caller cannot address another user (Design §10.2 self-service rule).
/// </summary>
public sealed class OwnProfileUseCase(IOwnProfileStore store)
{
    public Task<OwnProfileView?> GetAsync(Guid callerUserId, CancellationToken cancellationToken) =>
        store.GetMaskedAsync(callerUserId, cancellationToken);

    public Task<string?> RevealAsync(Guid callerUserId, RevealableProfileField field, CancellationToken cancellationToken) =>
        store.RevealAsync(callerUserId, field, cancellationToken);

    public Task UpdateNamesAsync(Guid callerUserId, string firstName, string lastName, CancellationToken cancellationToken) =>
        store.UpdateNamesAsync(callerUserId, firstName, lastName, cancellationToken);

    public Task RequestMobileCodeAsync(Guid callerUserId, string mobile, CancellationToken cancellationToken) =>
        store.RequestMobileCodeAsync(callerUserId, mobile, cancellationToken);

    public Task ConfirmMobileCodeAsync(Guid callerUserId, string code, CancellationToken cancellationToken) =>
        store.ConfirmMobileCodeAsync(callerUserId, code, cancellationToken);

    public Task SaveAvatarAsync(Guid callerUserId, byte[] upload, CancellationToken cancellationToken) =>
        store.SaveAvatarAsync(callerUserId, upload, cancellationToken);

    public Task RemoveAvatarAsync(Guid callerUserId, CancellationToken cancellationToken) =>
        store.RemoveAvatarAsync(callerUserId, cancellationToken);

    public Task<ProfileAvatar?> GetAvatarAsync(Guid callerUserId, CancellationToken cancellationToken) =>
        store.GetAvatarAsync(callerUserId, cancellationToken);
}
