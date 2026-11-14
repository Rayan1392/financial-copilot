using FinancialCopilot.Application.Authentication.Profile;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace FinancialCopilot.Infrastructure.Authentication.Profile;

/// <summary>Reads the caller's profile and decrypts a single field on explicit reveal (Design §13.3).</summary>
public sealed class OwnProfileStore(
    AuthDbContext dbContext,
    TahlilAppProfileProtector protector,
    ILogger<OwnProfileStore> logger,
    ISmsOtpSender smsSender,
    IOptions<SmsOtpOptions> smsOptions,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider) : IOwnProfileStore
{
    private const int MaximumStoredAvatarBytes = 1024 * 1024;
    private const int MaximumAvatarDimension = 4096;
    private readonly IDataProtector _contactMobileProtector = dataProtection.CreateProtector("FinancialCopilot.OwnProfile.ContactMobile.v1");

    public async Task<OwnProfileView?> GetMaskedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new
            {
                row.FirstName,
                row.LastName,
                row.LocalFirstName,
                row.LocalLastName,
                row.NationalIdLast4,
                row.MobileEncrypted,
                row.MobileLast4,
                row.ProfileSyncedAtUtc,
                row.DisplayName,
                row.ContactMobileEncrypted,
                row.ContactMobileLast4,
                AvatarAvailable = row.AvatarBytes != null
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return null;
        var mobile = profile.MobileEncrypted is null ? null : protector.TryUnprotect(TahlilAppProfileProtector.MobilePurpose, profile.MobileEncrypted);
        var localMobile = profile.ContactMobileEncrypted is null ? null : TryUnprotectContactMobile(profile.ContactMobileEncrypted);
        var firstName = profile.LocalFirstName ?? profile.FirstName;
        var lastName = profile.LocalLastName ?? profile.LastName;
        return new OwnProfileView(
            firstName,
            lastName,
            ProfileMasking.MaskNationalId(profile.NationalIdLast4),
            ProfileMasking.MaskMobile(mobile, profile.MobileLast4),
            profile.ProfileSyncedAtUtc,
            string.Join(" ", new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } localDisplay
                ? localDisplay
                : profile.DisplayName,
            MaskContactMobile(localMobile, profile.ContactMobileLast4),
            profile.AvatarAvailable);
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

    public async Task UpdateNamesAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken)
    {
        firstName = NormalizeName(firstName);
        lastName = NormalizeName(lastName);
        var profile = await GetOrCreateProfileAsync(userId, cancellationToken);
        profile.LocalFirstName = firstName;
        profile.LocalLastName = lastName;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RequestMobileCodeAsync(Guid userId, string mobile, CancellationToken cancellationToken)
    {
        var settings = smsOptions.Value;
        if (!settings.Enabled) throw new OwnProfileException("sms-unavailable", "Mobile verification is currently unavailable.", 503);
        var normalized = NormalizeIranianMobile(mobile);
        var mobileHash = HashWithSecret("mobile:" + normalized, settings.CodeHashSecret);
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (profile?.ContactMobileHash == mobileHash) return;
        if (await dbContext.UserProfiles.AnyAsync(row => row.UserId != userId && row.ContactMobileHash == mobileHash, cancellationToken))
            throw new OwnProfileException("mobile-already-in-use", "This mobile number cannot be used.", 409);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var recent = await dbContext.AuthMobileOtps.Where(row => row.UserId == userId && row.CreatedAtUtc > now.AddHours(-1))
            .OrderByDescending(row => row.CreatedAtUtc).ToArrayAsync(cancellationToken);
        if (recent.Length >= settings.MaximumRequestsPerHour ||
            recent.FirstOrDefault() is { } last && last.CreatedAtUtc > now.AddSeconds(-settings.CooldownSeconds))
            throw new OwnProfileException("mobile-code-rate-limited", "Please wait before requesting another code.", 429);

        string providerCode;
        try
        {
            providerCode = await smsSender.SendAsync(normalized, cancellationToken);
        }
        catch (SmsDeliveryException)
        {
            logger.LogWarning("Mobile verification SMS delivery failed. UserId: {UserId}", userId);
            throw new OwnProfileException("sms-unavailable", "Mobile verification is currently unavailable.", 503);
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.AuthMobileOtps.Where(row => row.UserId == userId && row.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, now), cancellationToken);
        }
        else
        {
            var active = await dbContext.AuthMobileOtps.Where(row => row.UserId == userId && row.ConsumedAtUtc == null)
                .ToArrayAsync(cancellationToken);
            foreach (var item in active) item.ConsumedAtUtc = now;
        }
        dbContext.AuthMobileOtps.Add(new AuthMobileOtpRow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MobileHash = mobileHash,
            MobileEncrypted = _contactMobileProtector.Protect(normalized),
            CodeHash = HashWithSecret("code:" + providerCode, settings.CodeHashSecret),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(settings.CodeLifetimeMinutes)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task ConfirmMobileCodeAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var settings = smsOptions.Value;
        if (!settings.Enabled) throw new OwnProfileException("sms-unavailable", "Mobile verification is currently unavailable.", 503);
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 4 or > 10 || code.Any(character => !char.IsAsciiDigit(character)))
            throw new OwnProfileException("mobile-code-invalid", "The verification code is invalid or expired.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = await dbContext.AuthMobileOtps.Where(row => row.UserId == userId && row.ConsumedAtUtc == null)
            .OrderByDescending(row => row.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (token is null || token.ExpiresAtUtc <= now || token.AttemptCount >= settings.MaximumCodeAttempts)
            throw new OwnProfileException("mobile-code-invalid", "The verification code is invalid or expired.");

        var submittedHash = Convert.FromHexString(HashWithSecret("code:" + code, settings.CodeHashSecret));
        var storedHash = Convert.FromHexString(token.CodeHash);
        if (!CryptographicOperations.FixedTimeEquals(submittedHash, storedHash))
        {
            var attempts = token.AttemptCount + 1;
            await dbContext.AuthMobileOtps.Where(row => row.Id == token.Id && row.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.AttemptCount, attempts)
                    .SetProperty(row => row.ConsumedAtUtc, row => attempts >= settings.MaximumCodeAttempts ? now : row.ConsumedAtUtc), cancellationToken);
            throw new OwnProfileException("mobile-code-invalid", "The verification code is invalid or expired.");
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var consumed = await dbContext.AuthMobileOtps.Where(row => row.Id == token.Id && row.ConsumedAtUtc == null && row.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, now), cancellationToken);
        if (consumed != 1) throw new OwnProfileException("mobile-code-invalid", "The verification code is invalid or expired.");

        if (await dbContext.UserProfiles.AnyAsync(row => row.UserId != userId && row.ContactMobileHash == token.MobileHash, cancellationToken))
            throw new OwnProfileException("mobile-already-in-use", "This mobile number cannot be used.", 409);
        var profile = await GetOrCreateProfileAsync(userId, cancellationToken);
        var normalized = _contactMobileProtector.Unprotect(token.MobileEncrypted);
        profile.ContactMobileEncrypted = _contactMobileProtector.Protect(normalized);
        profile.ContactMobileHash = token.MobileHash;
        profile.ContactMobileLast4 = normalized[^4..];
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Local contact mobile verified. UserId: {UserId}", userId);
    }

    public async Task SaveAvatarAsync(Guid userId, byte[] upload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = new SKMemoryStream(upload);
        using var codec = SKCodec.Create(input);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) ||
            codec.Info.Width is <= 0 or > MaximumAvatarDimension || codec.Info.Height is <= 0 or > MaximumAvatarDimension ||
            (long)codec.Info.Width * codec.Info.Height > 16_000_000)
            throw new OwnProfileException("avatar-invalid", "Choose a valid JPEG, PNG, or WebP image up to 5 MiB.");
        using var bitmap = SKBitmap.Decode(upload);
        if (bitmap is null) throw new OwnProfileException("avatar-invalid", "Choose a valid JPEG, PNG, or WebP image up to 5 MiB.");
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        if (encoded is null || encoded.Size > MaximumStoredAvatarBytes)
            throw new OwnProfileException("avatar-too-large", "The processed profile photo must be at most 1 MiB.");
        var profile = await GetOrCreateProfileAsync(userId, cancellationToken);
        profile.AvatarBytes = encoded.ToArray();
        profile.AvatarContentType = "image/jpeg";
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAvatarAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (profile is null) return;
        profile.AvatarBytes = null;
        profile.AvatarContentType = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProfileAvatar?> GetAvatarAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        return profile?.AvatarBytes is { Length: > 0 } bytes
            ? new ProfileAvatar(bytes, profile.AvatarContentType ?? "image/jpeg")
            : null;
    }

    private async Task<UserProfileRow> GetOrCreateProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (profile is not null) return profile;
        profile = new UserProfileRow { UserId = userId, CreatedAtUtc = timeProvider.GetUtcNow() };
        dbContext.UserProfiles.Add(profile);
        return profile;
    }

    private string? TryUnprotectContactMobile(string ciphertext)
    {
        try { return _contactMobileProtector.Unprotect(ciphertext); }
        catch (CryptographicException)
        {
            logger.LogError("Local contact-mobile decryption failed.");
            return null;
        }
    }

    private static string NormalizeName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 100 || normalized.Any(char.IsControl))
            throw new OwnProfileException("profile-name-invalid", "Enter a valid first and last name.");
        return normalized;
    }

    private static string NormalizeIranianMobile(string input)
    {
        var digits = new string((input ?? string.Empty).Select(character =>
        {
            if (character is >= '۰' and <= '۹') return (char)('0' + character - '۰');
            if (character is >= '٠' and <= '٩') return (char)('0' + character - '٠');
            return character;
        }).Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("0098", StringComparison.Ordinal)) digits = "0" + digits[4..];
        else if (digits.StartsWith("+98", StringComparison.Ordinal)) digits = "0" + digits[3..];
        else if (digits.StartsWith("98", StringComparison.Ordinal) && digits.Length == 12) digits = "0" + digits[2..];
        else if (digits.Length == 10 && digits[0] == '9') digits = "0" + digits;
        if (digits.Length != 11 || !digits.StartsWith("09", StringComparison.Ordinal))
            throw new OwnProfileException("mobile-invalid", "Enter a valid Iranian mobile number.");
        return digits;
    }

    private static string HashWithSecret(string purposeValue, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(purposeValue))).ToLowerInvariant();

    private static string? MaskContactMobile(string? mobile, string? lastFour)
    {
        if (mobile is { Length: 11 }) return $"{mobile[..4]}***{mobile[^4..]}";
        return string.IsNullOrWhiteSpace(lastFour) ? null : $"****{lastFour}";
    }
}
