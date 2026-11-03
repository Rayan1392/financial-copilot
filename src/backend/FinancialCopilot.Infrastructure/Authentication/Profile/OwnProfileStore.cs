using FinancialCopilot.Application.Authentication.Profile;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Authentication.Profile;

/// <summary>Reads the caller's profile and decrypts a single field on explicit reveal (Design §13.3).</summary>
public sealed class OwnProfileStore(
    AuthDbContext dbContext,
    TahlilAppProfileProtector protector,
    ILogger<OwnProfileStore> logger) : IOwnProfileStore
{
    public async Task<OwnProfileView?> GetMaskedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var mobile = profile.MobileEncrypted is null ? null : protector.TryUnprotect(TahlilAppProfileProtector.MobilePurpose, profile.MobileEncrypted);
        return new OwnProfileView(
            profile.FirstName,
            profile.LastName,
            ProfileMasking.MaskNationalId(profile.NationalIdLast4),
            ProfileMasking.MaskMobile(mobile, profile.MobileLast4),
            profile.ProfileSyncedAtUtc,
            profile.DisplayName);
    }

    public async Task<string?> RevealAsync(Guid userId, RevealableProfileField field, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var (purpose, ciphertext, fieldName) = field switch
        {
            RevealableProfileField.NationalId => (TahlilAppProfileProtector.NationalIdPurpose, profile?.NationalIdEncrypted, "nationalId"),
            _ => (TahlilAppProfileProtector.MobilePurpose, profile?.MobileEncrypted, "mobile")
        };
        if (ciphertext is null)
        {
            return null;
        }

        var value = protector.TryUnprotect(purpose, ciphertext);
        if (value is null)
        {
            logger.LogError("ProfileDecryptionFailed. LocalUserId: {LocalUserId}; Field: {Field}", userId, fieldName);
            return null;
        }

        // Field name only; the value is never logged (Design §13.3, AC-72).
        logger.LogInformation("ProfilePiiSelfRevealed. LocalUserId: {LocalUserId}; Field: {Field}", userId, fieldName);
        return value;
    }
}
