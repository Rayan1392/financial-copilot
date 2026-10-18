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
    DateTimeOffset? ProfileSyncedAtUtc);

/// <summary>Data-access port for the caller's own profile. Implemented in Infrastructure.</summary>
public interface IOwnProfileStore
{
    Task<OwnProfileView?> GetMaskedAsync(Guid userId, CancellationToken cancellationToken);

    /// <returns>The full value of one field for the owner, or null when the field is absent or unreadable.</returns>
    Task<string?> RevealAsync(Guid userId, RevealableProfileField field, CancellationToken cancellationToken);
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
}
