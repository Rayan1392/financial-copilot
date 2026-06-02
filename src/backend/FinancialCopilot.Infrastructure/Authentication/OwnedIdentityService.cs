using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class OwnedIdentityService(
    UserManager<FinancialCopilotUser> users,
    RoleManager<FinancialCopilotRole> roles,
    AuthDbContext dbContext,
    IOptions<OwnedIdentityOptions> ownedIdentityOptions,
    IConfiguration configuration,
    TimeProvider timeProvider) : IOwnedIdentityService
{
    private const string UserRole = "User";
    private const string DataAdminRole = "DataAdmin";
    private const string BillingAdminRole = "BillingAdmin";
    private const string SuperAdminRole = "SuperAdmin";

    public async Task<OwnedIdentitySession> RegisterAsync(
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
            IsEnabled = true
        };
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

        return await CreateSessionAsync(user, tenantId, cancellationToken);
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

    private async Task<OwnedIdentitySession> CreateSessionAsync(
        FinancialCopilotUser user,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
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
            new(FinancialCopilotClaimTypes.AuthenticationMode, AuthenticationMode.WebAppUser.ToString())
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
