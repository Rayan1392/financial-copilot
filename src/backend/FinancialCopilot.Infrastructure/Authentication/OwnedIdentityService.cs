using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class OwnedIdentityService(
    UserManager<FinancialCopilotUser> users,
    RoleManager<FinancialCopilotRole> roles,
    AuthDbContext dbContext,
    OwnedIdentityBillingProvisioner billingProvisioner,
    IOptions<OwnedIdentityOptions> ownedIdentityOptions,
    IOptions<AuthEmailOptions> emailOptions,
    IEmailSender emailSender,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<OwnedIdentityService> logger) : IOwnedIdentityService
{
    private const string UserRole = "User";
    private const string DataAdminRole = "DataAdmin";
    private const string BillingAdminRole = "BillingAdmin";
    private const string SuperAdminRole = "SuperAdmin";
    private const string VerificationPurpose = "email_verification";
    private const string PasswordResetPurpose = "password_reset";

    public async Task RegisterAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await EnsureBaselineAsync(cancellationToken);

        var normalizedEmail = email.Trim();
        if (await users.FindByEmailAsync(normalizedEmail) is not null)
        {
            throw new OwnedIdentityException("email-already-registered", "The email address is already registered.", 409);
        }

        var user = new FinancialCopilotUser
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            UserName = normalizedEmail,
            IsEnabled = true,
            EmailVerificationRequired = true
        };
        await using (var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null)
        {
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new OwnedIdentityException(
                    "registration-rejected",
                    string.Join(" ", result.Errors.Select(error => error.Description)));
            }

            await users.AddToRoleAsync(user, UserRole);
            var tenantId = GetDefaultTenantId();
            dbContext.UserTenants.Add(new UserTenantRow
            {
                UserId = user.Id,
                TenantId = tenantId,
                IsDefault = true
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }

        await IssueEmailTokenAndSendAsync(user, VerificationPurpose, cancellationToken);
    }

    public async Task<OwnedIdentitySession> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await EnsureBaselineAsync(cancellationToken);
        var user = await users.FindByEmailAsync(email.Trim());

        if (user is null || !user.IsEnabled)
        {
            throw InvalidCredentials();
        }

        if (await users.IsLockedOutAsync(user))
        {
            throw new OwnedIdentityException("user-locked-out", "The user account is locked.", 423);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            throw InvalidCredentials();
        }

        await users.ResetAccessFailedCountAsync(user);
        if (user.EmailVerificationRequired && !user.EmailConfirmed)
        {
            throw new OwnedIdentityException("email-not-verified", "Verify your email before signing in.", 403);
        }
        var tenantId = await ResolveDefaultTenantAsync(user.Id, cancellationToken);
        return await CreateSessionAsync(user, tenantId, cancellationToken);
    }

    public async Task<OwnedIdentitySession> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tokenHash = Hash(refreshToken);
        var current = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (current is null)
        {
            throw InvalidRefreshToken();
        }

        if (current.RevokedAt is not null)
        {
            if (current.ReplacedByTokenId is not null)
            {
                await RevokeTokenFamilyAsync(current.UserId, "Refresh token replay detected.", cancellationToken);
            }

            throw InvalidRefreshToken();
        }

        if (current.ExpiresAt <= now)
        {
            current.RevokedAt = now;
            current.RevocationReason = "Refresh token expired.";
            await dbContext.SaveChangesAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        var user = await users.FindByIdAsync(current.UserId.ToString())
            ?? throw InvalidRefreshToken();
        if (!user.IsEnabled || await users.IsLockedOutAsync(user))
        {
            throw InvalidRefreshToken();
        }
        if (user.EmailVerificationRequired && !user.EmailConfirmed)
        {
            throw InvalidRefreshToken();
        }

        var tenantId = await ResolveDefaultTenantAsync(user.Id, cancellationToken);
        var replacement = await CreateSessionAsync(user, tenantId, cancellationToken);
        var replacementHash = Hash(replacement.RefreshToken);
        var replacementRow = await dbContext.RefreshTokens
            .SingleAsync(token => token.TokenHash == replacementHash, cancellationToken);
        current.RevokedAt = now;
        current.RevocationReason = "Refresh token rotated.";
        current.ReplacedByTokenId = replacementRow.Id;
        await dbContext.SaveChangesAsync(cancellationToken);
        return replacement;
    }

    public async Task RevokeAsync(
        string refreshToken,
        string reason,
        CancellationToken cancellationToken)
    {
        var tokenHash = Hash(refreshToken);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);
        if (token is null || token.RevokedAt is not null)
        {
            return;
        }

        token.RevokedAt = timeProvider.GetUtcNow();
        token.RevocationReason = reason;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task VerifyEmailAsync(string token, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var tokenRow = await FindEmailTokenAsync(token, VerificationPurpose, cancellationToken);
        await ConsumeEmailTokenRowAsync(tokenRow, cancellationToken);
        var user = await users.FindByIdAsync(tokenRow.UserId!.Value.ToString())
            ?? throw InvalidEmailToken();
        user.EmailConfirmed = true;
        user.EmailVerificationRequired = false;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            throw new OwnedIdentityException("email-verification-failed", "Email verification could not be completed.", 400);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Email verification completed. UserId: {UserId}", user.Id);
    }

    public async Task ResendVerificationAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        var user = await users.FindByEmailAsync(normalizedEmail);
        await IssueEmailTokenAndSendAsync(
            user is { IsEnabled: true, EmailConfirmed: false } ? user : null,
            VerificationPurpose,
            cancellationToken,
            normalizedEmail);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim();
        var user = await users.FindByEmailAsync(normalizedEmail);
        await IssueEmailTokenAndSendAsync(
            user is { IsEnabled: true } ? user : null,
            PasswordResetPurpose,
            cancellationToken,
            normalizedEmail);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken)
    {
        var tokenRow = await FindEmailTokenAsync(token, PasswordResetPurpose, cancellationToken);
        var user = await users.FindByIdAsync(tokenRow.UserId!.Value.ToString())
            ?? throw InvalidEmailToken();

        foreach (var validator in users.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(users, user, newPassword);
            if (!validation.Succeeded)
            {
                throw new OwnedIdentityException("password-rejected", string.Join(" ", validation.Errors.Select(error => error.Description)), 400);
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await ConsumeEmailTokenRowAsync(tokenRow, cancellationToken);
        user.PasswordHash = users.PasswordHasher.HashPassword(user, newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            throw new OwnedIdentityException("password-reset-failed", "Password reset could not be completed.", 400);
        }

        var now = timeProvider.GetUtcNow();
        int revokedCount;
        if (dbContext.Database.IsRelational())
        {
            revokedCount = await dbContext.RefreshTokens
                .Where(refresh => refresh.UserId == user.Id && refresh.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(refresh => refresh.RevokedAt, now)
                    .SetProperty(refresh => refresh.RevocationReason, "Password reset."), cancellationToken);
        }
        else
        {
            var sessions = await dbContext.RefreshTokens
                .Where(refresh => refresh.UserId == user.Id && refresh.RevokedAt == null)
                .ToArrayAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.RevokedAt = now;
                session.RevocationReason = "Password reset.";
            }
            revokedCount = sessions.Length;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Password reset completed and refresh sessions revoked. UserId: {UserId}; RefreshSessionCount: {RefreshSessionCount}", user.Id, revokedCount);
    }

    private async Task IssueEmailTokenAndSendAsync(
        FinancialCopilotUser? user,
        string purpose,
        CancellationToken cancellationToken,
        string? submittedEmail = null)
    {
        var email = user?.Email ?? submittedEmail ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email)) return;
        var settings = emailOptions.Value;
        if (!settings.Enabled)
        {
            logger.LogWarning("Authentication email delivery is disabled. Purpose: {Purpose}; UserId: {UserId}", purpose, user?.Id);
            return;
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.AuthEmailActionTokens
                .Where(row => row.ExpiresAtUtc < now.AddDays(-30))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var expiredRows = await dbContext.AuthEmailActionTokens
                .Where(row => row.ExpiresAtUtc < now.AddDays(-30))
                .ToArrayAsync(cancellationToken);
            dbContext.AuthEmailActionTokens.RemoveRange(expiredRows);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var emailKeyHash = HashEmailKey(email);
        var recent = await dbContext.AuthEmailActionTokens
            .Where(row => row.EmailKeyHash == emailKeyHash && row.Purpose == purpose && row.CreatedAtUtc > now.AddHours(-1))
            .OrderByDescending(row => row.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);
        if (recent.Length >= settings.EmailMaximumRequestsPerHour ||
            recent.FirstOrDefault() is { } last && last.CreatedAtUtc > now.AddSeconds(-settings.EmailCooldownSeconds))
        {
            return;
        }

        var rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var lifetime = purpose == VerificationPurpose
            ? TimeSpan.FromHours(settings.VerificationTokenHours)
            : TimeSpan.FromMinutes(settings.PasswordResetTokenMinutes);
        var tokenRow = new AuthEmailActionTokenRow
        {
            Id = Guid.NewGuid(),
            UserId = user?.Id,
            Purpose = purpose,
            TokenHash = Hash(rawToken),
            EmailKeyHash = emailKeyHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(lifetime)
        };

        await using (var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null)
        {
            if (dbContext.Database.IsRelational())
            {
                await dbContext.AuthEmailActionTokens
                    .Where(row => row.EmailKeyHash == emailKeyHash && row.Purpose == purpose && row.ConsumedAtUtc == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAtUtc, now), cancellationToken);
            }
            else
            {
                var activeTokens = await dbContext.AuthEmailActionTokens
                    .Where(row => row.EmailKeyHash == emailKeyHash && row.Purpose == purpose && row.ConsumedAtUtc == null)
                    .ToArrayAsync(cancellationToken);
                foreach (var activeToken in activeTokens) activeToken.ConsumedAtUtc = now;
            }
            dbContext.AuthEmailActionTokens.Add(tokenRow);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }

        if (user is null) return;
        var link = BuildEmailLink(purpose, rawToken);
        var verification = purpose == VerificationPurpose;
        var subject = verification ? "تایید نشانی ایمیل" : "بازیابی گذرواژه";
        var action = verification ? "تایید ایمیل" : "انتخاب گذرواژه جدید";
        var plain = verification
            ? $"برای تایید نشانی ایمیل خود این پیوند را باز کنید: {link}\nاین پیوند تا ۲۴ ساعت معتبر است."
            : $"برای بازیابی گذرواژه خود این پیوند را باز کنید: {link}\nاین پیوند تا ۳۰ دقیقه معتبر است. اگر شما این درخواست را نداده‌اید، این پیام را نادیده بگیرید.";
        var html = $"<!doctype html><html lang=\"fa\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"no-referrer\"></head><body style=\"font-family:Tahoma,Arial,sans-serif;direction:rtl;text-align:right\"><h2>{action}</h2><p>برای ادامه روی پیوند زیر بزنید:</p><p><a rel=\"noreferrer\" href=\"{HtmlEncoder.Default.Encode(link)}\">{action}</a></p><p>اگر شما این درخواست را نداده‌اید، این پیام را نادیده بگیرید.</p></body></html>";
        try
        {
            await emailSender.SendAsync(new EmailMessage(user.Email!, subject, html, plain), cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            // The generic public response and persisted resend cooldown avoid enumeration and duplicate sends.
            logger.LogWarning("Authentication email delivery was unavailable. Purpose: {Purpose}; UserId: {UserId}", purpose, user.Id);
        }
    }

    private async Task<AuthEmailActionTokenRow> FindEmailTokenAsync(
        string token,
        string purpose,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256) throw InvalidEmailToken();
        var tokenHash = Hash(token);
        var row = await dbContext.AuthEmailActionTokens.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == tokenHash && candidate.Purpose == purpose, cancellationToken);
        if (row is null || row.UserId is null) throw InvalidEmailToken();
        return row;
    }

    private async Task ConsumeEmailTokenRowAsync(AuthEmailActionTokenRow row, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (dbContext.Database.IsRelational())
        {
            var affected = await dbContext.AuthEmailActionTokens
                .Where(candidate => candidate.Id == row.Id && candidate.ConsumedAtUtc == null && candidate.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.ConsumedAtUtc, now), cancellationToken);
            if (affected != 1) throw InvalidEmailToken();
            return;
        }

        var tracked = await dbContext.AuthEmailActionTokens.SingleOrDefaultAsync(
            candidate => candidate.Id == row.Id && candidate.ConsumedAtUtc == null && candidate.ExpiresAtUtc > now,
            cancellationToken);
        if (tracked is null) throw InvalidEmailToken();
        tracked.ConsumedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string BuildEmailLink(string purpose, string token)
    {
        var path = purpose == VerificationPurpose ? "verify-email" : "reset-password";
        return $"{emailOptions.Value.FrontendBaseUrl.TrimEnd('/')}/{path}#token={Uri.EscapeDataString(token)}";
    }

    private string HashEmailKey(string email)
    {
        var key = emailOptions.Value.TokenHashSecret;
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant()))).ToLowerInvariant();
    }

    private static OwnedIdentityException InvalidEmailToken() =>
        new("invalid-or-expired-token", "The token is invalid or expired.", 400);

    private async Task<OwnedIdentitySession> CreateSessionAsync(
        FinancialCopilotUser user,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await billingProvisioner.EnsureProvisionedAsync(tenantId, user.Id, cancellationToken);

        var settings = ownedIdentityOptions.Value;
        var now = timeProvider.GetUtcNow();
        var accessExpiresAt = now.AddMinutes(settings.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(settings.RefreshTokenDays);
        var userRoles = (await users.GetRolesAsync(user)).ToArray();
        var permissions = await ResolvePermissionsAsync(userRoles, cancellationToken);
        var accessToken = CreateAccessToken(user, tenantId, userRoles, permissions, now, accessExpiresAt);
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        dbContext.RefreshTokens.Add(new RefreshTokenRow
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            TokenHash = Hash(refreshToken),
            CreatedAt = now,
            ExpiresAt = refreshExpiresAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new OwnedIdentitySession(
            accessToken,
            accessExpiresAt,
            refreshToken,
            refreshExpiresAt,
            new OwnedIdentityProfile(user.Id, user.Email!, tenantId, userRoles.ToArray(), permissions));
    }

    private string CreateAccessToken(
        FinancialCopilotUser user,
        Guid tenantId,
        IReadOnlyCollection<string> userRoles,
        IReadOnlyCollection<string> permissions,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt)
    {
        var jwtSettings = configuration.GetSection("Authentication:JwtBearer");
        var signingKey = jwtSettings["SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Authentication:JwtBearer:SigningKey is required for owned Identity.");
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(FinancialCopilotClaimTypes.TenantId, tenantId.ToString()),
            new(FinancialCopilotClaimTypes.AuthenticationMode, AuthenticationMode.WebAppUser.ToString()),
            new("financial_copilot:security_stamp", user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(userRoles.Select(role => new Claim("role", role)));
        claims.AddRange(permissions.Select(permission => new Claim(FinancialCopilotClaimTypes.Permission, permission)));

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<IReadOnlyCollection<string>> ResolvePermissionsAsync(
        IReadOnlyCollection<string> userRoles,
        CancellationToken cancellationToken)
    {
        var normalizedNames = userRoles.Select(roles.NormalizeKey).ToArray();
        return await (
            from role in dbContext.Roles
            join rolePermission in dbContext.RolePermissions on role.Id equals rolePermission.RoleId
            join permission in dbContext.Permissions on rolePermission.PermissionId equals permission.Id
            where normalizedNames.Contains(role.NormalizedName!) && role.IsEnabled
            select permission.Code)
            .Distinct()
            .OrderBy(code => code)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<Guid> ResolveDefaultTenantAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memberships = await dbContext.UserTenants
            .Where(membership => membership.UserId == userId)
            .OrderByDescending(membership => membership.IsDefault)
            .ThenBy(membership => membership.TenantId)
            .ToArrayAsync(cancellationToken);
        if (memberships.Length == 0)
        {
            throw new OwnedIdentityException("tenant-membership-required", "The user has no active tenant membership.", 403);
        }

        return memberships[0].TenantId;
    }

    private async Task EnsureBaselineAsync(CancellationToken cancellationToken)
    {
        var tenantId = GetDefaultTenantId();
        if (!await dbContext.Tenants.AnyAsync(tenant => tenant.Id == tenantId, cancellationToken))
        {
            dbContext.Tenants.Add(new TenantRow
            {
                Id = tenantId,
                Name = ownedIdentityOptions.Value.DefaultTenantName
            });
        }

        foreach (var permissionCode in FinancialCopilotPermissions.All)
        {
            if (!await dbContext.Permissions.AnyAsync(permission => permission.Code == permissionCode, cancellationToken))
            {
                dbContext.Permissions.Add(new PermissionRow { Id = Guid.NewGuid(), Code = permissionCode });
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        await EnsureRoleAsync(UserRole, FinancialCopilotPermissions.WebUserDefaults, cancellationToken);
        await EnsureRoleAsync(DataAdminRole, [FinancialCopilotPermissions.DataSyncManage], cancellationToken);
        await EnsureRoleAsync(BillingAdminRole, [FinancialCopilotPermissions.BillingManage], cancellationToken);
        await EnsureRoleAsync(
            SuperAdminRole,
            FinancialCopilotPermissions.AdminAll
                .Concat([FinancialCopilotPermissions.DataSyncManage, FinancialCopilotPermissions.BillingManage])
                .ToArray(),
            cancellationToken);
    }

    private async Task EnsureRoleAsync(
        string roleName,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken)
    {
        var role = await roles.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new FinancialCopilotRole { Id = Guid.NewGuid(), Name = roleName };
            var result = await roles.CreateAsync(role);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not seed role '{roleName}'.");
            }
        }

        var permissionIds = await dbContext.Permissions
            .Where(permission => permissionCodes.Contains(permission.Code))
            .Select(permission => permission.Id)
            .ToArrayAsync(cancellationToken);
        var assigned = await dbContext.RolePermissions
            .Where(mapping => mapping.RoleId == role.Id)
            .Select(mapping => mapping.PermissionId)
            .ToArrayAsync(cancellationToken);
        dbContext.RolePermissions.AddRange(permissionIds
            .Except(assigned)
            .Select(permissionId => new RolePermissionRow { RoleId = role.Id, PermissionId = permissionId }));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task BootstrapSuperAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var superAdminRole = await roles.FindByNameAsync(SuperAdminRole)
            ?? throw new InvalidOperationException("SuperAdmin role has not been seeded.");

        var existingSuperAdmins = await dbContext.UserRoles
            .AnyAsync(ur => ur.RoleId == superAdminRole.Id, cancellationToken);

        if (existingSuperAdmins)
        {
            throw new OwnedIdentityException(
                "superadmin-already-exists",
                "A SuperAdmin already exists. Bootstrap is only permitted when no SuperAdmin has been assigned.",
                409);
        }

        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new OwnedIdentityException("user-not-found", "User not found.", 404);

        var result = await users.AddToRoleAsync(user, SuperAdminRole);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not assign SuperAdmin role: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }
    }

    private async Task RevokeTokenFamilyAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var activeTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
            token.RevocationReason = reason;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Guid GetDefaultTenantId() =>
        Guid.TryParse(ownedIdentityOptions.Value.DefaultTenantId, out var tenantId)
            ? tenantId
            : throw new InvalidOperationException("Authentication:OwnedIdentity:DefaultTenantId must be a GUID.");

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static OwnedIdentityException InvalidCredentials() =>
        new("invalid-credentials", "The supplied credentials are invalid.", 401);

    private static OwnedIdentityException InvalidRefreshToken() =>
        new("invalid-refresh-token", "The refresh token is invalid, expired, revoked, or replayed.", 401);
}
