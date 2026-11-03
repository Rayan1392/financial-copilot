namespace FinancialCopilot.Infrastructure.Authentication.Persistence;

/// <summary>
/// Link from the stable external key (Provider, ExternalUserId) to one local user (Design §10).
/// Contains no profile PII and is never matched by email, phone or national id.
/// </summary>
public sealed class TahlilAppExternalIdentityRow
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalUserId { get; set; } = string.Empty;
    public Guid LocalUserId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Active | Suspended | Revoked (mirrors TahlilApp status plus local admin unlink).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset LinkedAtUtc { get; set; }
    public DateTimeOffset? LastLaunchAtUtc { get; set; }

    // Entitlement sync state (Design §10, §11). Plan and window are written only through the Billing synchroniser.
    public DateTimeOffset? LastEntitlementSyncAtUtc { get; set; }
    public DateTimeOffset? NextEntitlementSyncDueAtUtc { get; set; }
    public DateTimeOffset? LastKnownSubscriptionExpiresAtUtc { get; set; }
    public string? LastSyncedPlanCode { get; set; }
    public long? LastExternalRevision { get; set; }

    /// <summary>Per-process id of the worker holding the revalidation lease; null when unleased.</summary>
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    /// <summary>Consecutive failed status lookups. Reset on success.</summary>
    public int SyncAttemptCount { get; set; }
    public string? LastSyncFailureCategory { get; set; }
}

/// <summary>
/// Local replay guard for one-time launch codes. Only the SHA-256 digest of the code is stored (Design §8).
/// </summary>
public sealed class TahlilAppLaunchAttemptRow
{
    public Guid Id { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;

    // UTC DateTime, matching auth_email_action_tokens: SQLite cannot translate DateTimeOffset ordering in the purge query.
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>
/// TahlilApp-sourced profile data, 1:1 with the local user (Design §10.2).
/// NationalId and Mobile are encrypted with Data Protection; no PII column is indexed or unique.
/// </summary>
public sealed class UserProfileRow
{
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Official TahlilApp full name, stored as received (never split into first/last).</summary>
    public string? DisplayName { get; set; }
    public string? NationalIdEncrypted { get; set; }
    public string? NationalIdLast4 { get; set; }
    public string? MobileEncrypted { get; set; }
    public string? MobileLast4 { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset ProfileSyncedAtUtc { get; set; }
}
