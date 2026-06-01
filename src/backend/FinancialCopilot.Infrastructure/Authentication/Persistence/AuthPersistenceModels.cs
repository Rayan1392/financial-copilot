using Microsoft.AspNetCore.Identity;

namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

public sealed class FinancialCopilotUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;
}

public sealed class FinancialCopilotRole : IdentityRole<Guid>;

public sealed class PermissionRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
}

public sealed class RolePermissionRow
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public sealed class TenantRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class UserTenantRow
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class RefreshTokenRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? RevocationReason { get; set; }
}
