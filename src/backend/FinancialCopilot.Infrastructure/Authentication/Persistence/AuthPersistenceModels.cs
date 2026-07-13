using Microsoft.AspNetCore.Identity;

namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

public sealed class FinancialCopilotUser : IdentityUser<Guid>
{
    public bool IsEnabled { get; set; } = true;
}

public sealed class FinancialCopilotRole : IdentityRole<Guid>
{
    public bool IsEnabled { get; set; } = true;
}

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

public sealed class SecurityAdminAuditRow
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? IdempotencyKey { get; set; }
}

public sealed class TelegramAccountLinkRow
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid TenantId { get; set; }
    public long TelegramUserId { get; set; }
    public long TelegramChatId { get; set; }
    public string? Username { get; set; }
    public DateTimeOffset LinkedAtUtc { get; set; }
    public DateTimeOffset LastVerifiedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public uint Version { get; set; }
}

public sealed class TelegramLinkTokenRow
{
    public Guid Id { get; set; }
    public Guid? ActorId { get; set; }
    public Guid TenantId { get; set; }
    public long? TelegramUserId { get; set; }
    public long? TelegramChatId { get; set; }
    public string? Username { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public long? ConsumedByTelegramUserId { get; set; }
    public long? TelegramUpdateId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public uint Version { get; set; }
}

public sealed class TelegramLinkAuditRow
{
    public Guid Id { get; set; }
    public Guid? ActorId { get; set; }
    public Guid TenantId { get; set; }
    public long? TelegramUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class TelegramChannelMembershipVerificationRow
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public Guid TenantId { get; set; }
    public long TelegramUserId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsEligible { get; set; }
    public DateTimeOffset ProviderObservedAtUtc { get; set; }
    public DateTimeOffset VerifiedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string FailureCategory { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public bool IsLatest { get; set; }
}
