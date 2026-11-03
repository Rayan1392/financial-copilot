using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Find-or-create of the local user for <c>(TahlilApp, ExternalUserId)</c> and profile synchronisation (Design §10).
/// Email is never written for a TahlilApp user (Design §8); the provisioned user has no email and no password.
/// </summary>
public sealed class TahlilAppAccountProvisioner(
    AuthDbContext dbContext,
    UserManager<FinancialCopilotUser> users,
    IOwnedIdentityService identityService,
    IOptions<OwnedIdentityOptions> ownedIdentityOptions,
    TahlilAppProfileProtector protector,
    ILogger<TahlilAppAccountProvisioner> logger) : ITahlilAppAccountProvisioner
{
    private const string UserRole = "User";
    private const string ActiveStatus = "Active";

    public async Task<TahlilAppProvisioningResult> ResolveAsync(
        TahlilAppExchangeSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Two passes at most: a lost unique-index race is resolved by re-reading the winner (AC-28).
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await dbContext.TahlilAppExternalIdentities.SingleOrDefaultAsync(
                row => row.Provider == ExternalUserId.Provider && row.ExternalUserId == snapshot.ExternalUserId,
                cancellationToken);
            if (existing is not null)
            {
                return await ResolveExistingAsync(existing, snapshot, now, cancellationToken);
            }

            try
            {
                return await CreateAsync(snapshot, now, cancellationToken);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // The transaction was disposed without commit, so the losing user row was rolled back.
                dbContext.ChangeTracker.Clear();
                logger.LogInformation("TahlilApp first launch lost an identity race; resolving the existing link.");
            }
        }

        throw new InvalidOperationException("TahlilApp identity provisioning did not converge.");
    }

    private async Task<TahlilAppProvisioningResult> ResolveExistingAsync(
        TahlilAppExternalIdentityRow identity,
        TahlilAppExchangeSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(identity.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return Disabled(identity);
        }

        var user = await users.FindByIdAsync(identity.LocalUserId.ToString());
        if (user is null || !user.IsEnabled)
        {
            return Disabled(identity);
        }

        identity.LastLaunchAtUtc = now;
        await ApplyProfileAsync(user.Id, snapshot, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TahlilAppProvisioningResult(TahlilAppProvisioningOutcome.Resolved, user.Id, identity.TenantId, false);
    }

    private async Task<TahlilAppProvisioningResult> CreateAsync(
        TahlilAppExchangeSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Baseline (tenant, permissions, roles) is persisted outside the provisioning transaction.
        await identityService.PrepareBaselineAsync(cancellationToken);
        var tenantId = GetDefaultTenantId();

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        // Design §22.2 D5 (proven by TahlilAppLaunchIntegrationTests.FirstLaunch_CreatesOneUserWithoutEmail):
        // ASP.NET Core Identity's user validator rejects a null email when RequireUniqueEmail is set, so the
        // TahlilApp-provisioned row is written directly with explicit normalised fields. No placeholder email is written.
        var userName = $"tahlilapp-{Guid.NewGuid():N}";
        var user = new FinancialCopilotUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = users.NormalizeName(userName),
            Email = null,
            NormalizedEmail = null,
            EmailConfirmed = false,
            IsEnabled = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var userRole = await dbContext.Roles.SingleAsync(row => row.NormalizedName == UserRole.ToUpperInvariant(), cancellationToken);
        dbContext.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = userRole.Id });

        dbContext.UserTenants.Add(new UserTenantRow
        {
            UserId = user.Id,
            TenantId = tenantId,
            IsDefault = true
        });
        dbContext.UserOperationalMetadata.Add(new UserOperationalMetadataRow
        {
            UserId = user.Id,
            RegisteredAtUtc = now,
            AcquisitionSourceCode = UserOperationalMetadataCodes.TahlilApp,
            ConcurrencyVersion = 1
        });
        dbContext.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow
        {
            Id = Guid.NewGuid(),
            Provider = ExternalUserId.Provider,
            ExternalUserId = snapshot.ExternalUserId,
            LocalUserId = user.Id,
            TenantId = tenantId,
            Status = ActiveStatus,
            LinkedAtUtc = now,
            LastLaunchAtUtc = now
        });
        await ApplyProfileAsync(user.Id, snapshot, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("TahlilApp local user created. LocalUserId: {LocalUserId}", user.Id);
        return new TahlilAppProvisioningResult(TahlilAppProvisioningOutcome.Resolved, user.Id, tenantId, true);
    }

    /// <summary>
    /// Applies Design §10.2 per field. Absent, null or invalid values keep the stored value. A decryption failure
    /// of a stored value is treated as "no stored value", so the next valid value heals it (Design §17.1).
    /// </summary>
    private async Task ApplyProfileAsync(
        Guid userId,
        TahlilAppExchangeSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        var isNew = profile is null;
        profile ??= new UserProfileRow
        {
            UserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ProfileSyncedAtUtc = now
        };

        var changedFields = new List<string>();

        if (ProfileFieldMergePolicy.Decide(profile.FirstName, snapshot.FirstName) == ProfileFieldAction.Overwrite)
        {
            profile.FirstName = snapshot.FirstName.NormalizedValue;
            changedFields.Add("firstName");
        }

        if (ProfileFieldMergePolicy.Decide(profile.LastName, snapshot.LastName) == ProfileFieldAction.Overwrite)
        {
            profile.LastName = snapshot.LastName.NormalizedValue;
            changedFields.Add("lastName");
        }

        var displayName = snapshot.DisplayName ?? IncomingProfileField.Absent;
        if (displayName.Kind == IncomingProfileFieldKind.Invalid)
        {
            // Field name and reason only; the rejected value is never logged (Design §13.3).
            logger.LogWarning(
                "TahlilAppProfileFieldRejected. LocalUserId: {LocalUserId}; Field: {Field}; Reason: {Reason}",
                userId, "displayName", "invalid");
        }

        if (ProfileFieldMergePolicy.Decide(profile.DisplayName, displayName) == ProfileFieldAction.Overwrite)
        {
            profile.DisplayName = displayName.NormalizedValue;
            changedFields.Add("displayName");
        }

        var storedNationalId = Unprotect(TahlilAppProfileProtector.NationalIdPurpose, profile.NationalIdEncrypted, "nationalId", userId);
        if (ProfileFieldMergePolicy.Decide(storedNationalId, snapshot.NationalId) == ProfileFieldAction.Overwrite)
        {
            var value = snapshot.NationalId.NormalizedValue!;
            profile.NationalIdEncrypted = protector.Protect(TahlilAppProfileProtector.NationalIdPurpose, value);
            profile.NationalIdLast4 = value[^4..];
            changedFields.Add("nationalId");
        }

        var storedMobile = Unprotect(TahlilAppProfileProtector.MobilePurpose, profile.MobileEncrypted, "mobile", userId);
        if (ProfileFieldMergePolicy.Decide(storedMobile, snapshot.Mobile) == ProfileFieldAction.Overwrite)
        {
            var value = snapshot.Mobile.NormalizedValue!;
            profile.MobileEncrypted = protector.Protect(TahlilAppProfileProtector.MobilePurpose, value);
            profile.MobileLast4 = value[^4..];
            changedFields.Add("mobile");
        }

        if (changedFields.Count > 0)
        {
            profile.UpdatedAtUtc = now;
        }

        profile.ProfileSyncedAtUtc = now;
        if (isNew)
        {
            dbContext.UserProfiles.Add(profile);
        }

        if (changedFields.Count > 0)
        {
            // Field names only; values are never logged (Design §13.3).
            logger.LogInformation(
                "TahlilApp profile refreshed. LocalUserId: {LocalUserId}; ChangedFields: {ChangedFields}",
                userId, string.Join(",", changedFields));
        }
    }

    private string? Unprotect(string purpose, string? ciphertext, string fieldName, Guid userId)
    {
        if (ciphertext is null)
        {
            return null;
        }

        var value = protector.TryUnprotect(purpose, ciphertext);
        if (value is null)
        {
            logger.LogError("ProfileDecryptionFailed. LocalUserId: {LocalUserId}; Field: {Field}", userId, fieldName);
        }

        return value;
    }

    private TahlilAppProvisioningResult Disabled(TahlilAppExternalIdentityRow identity) =>
        new(TahlilAppProvisioningOutcome.AccountDisabled, identity.LocalUserId, identity.TenantId, false);

    private Guid GetDefaultTenantId() =>
        Guid.TryParse(ownedIdentityOptions.Value.DefaultTenantId, out var tenantId)
            ? tenantId
            : throw new InvalidOperationException("Authentication:OwnedIdentity:DefaultTenantId must be a GUID.");
}
