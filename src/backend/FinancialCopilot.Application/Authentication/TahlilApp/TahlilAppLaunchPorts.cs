using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.Application.Authentication.TahlilApp;

// Ports owned by the policy layer (Design §22.3). Adapters live in Infrastructure.

public enum TahlilAppExchangeOutcome
{
    Succeeded,
    InvalidCode,
    Unavailable
}

/// <summary>
/// Validated, PII-normalised exchange response. Built only by the gateway adapter after schema and value validation.
/// </summary>
public sealed record TahlilAppExchangeSnapshot(
    string ExternalUserId,
    bool IsSuspended,
    string? ExternalPlan,
    bool IsSubscriptionActive,
    DateTimeOffset? SubscriptionExpiresAt,
    long? ExternalRevision,
    int ExternalPermissionCount,
    IncomingProfileField FirstName,
    IncomingProfileField LastName,
    IncomingProfileField NationalId,
    IncomingProfileField Mobile,
    DateTimeOffset? SubscriptionEffectiveFrom = null,
    IReadOnlyList<TahlilAppPlanSubscription>? PlanSubscriptions = null,
    string? Name = null)
{
    /// <summary>Redacted: contains the external id and profile PII (Design §13.3, AC-72).</summary>
    public override string ToString() =>
        $"TahlilAppExchangeSnapshot(Plan={ExternalPlan}, Active={IsSubscriptionActive}, Suspended={IsSuspended}, Permissions={ExternalPermissionCount})";
}

public sealed record TahlilAppExchangeResult(
    TahlilAppExchangeOutcome Outcome,
    TahlilAppExchangeSnapshot? Snapshot = null);

public enum TahlilAppStatusOutcome
{
    Succeeded,
    NotFound,
    Unavailable
}

/// <summary>
/// Validated entitlement status. Carries no profile fields and no PII (Design §9.1, AC-75).
/// </summary>
public sealed record TahlilAppStatusSnapshot(
    string ExternalUserId,
    bool IsSuspended,
    string? ExternalPlan,
    bool IsSubscriptionActive,
    DateTimeOffset? SubscriptionExpiresAt,
    long? ExternalRevision,
    DateTimeOffset? SubscriptionEffectiveFrom = null,
    IReadOnlyList<TahlilAppPlanSubscription>? PlanSubscriptions = null);

public sealed record TahlilAppStatusResult(
    TahlilAppStatusOutcome Outcome,
    TahlilAppStatusSnapshot? Snapshot = null);

/// <summary>
/// Server-to-server calls to TahlilApp. Exchange must never retry (Design §14); status is a single bounded read.
/// </summary>
public interface ITahlilAppIdentityGateway
{
    Task<TahlilAppExchangeResult> ExchangeAsync(string code, string correlationId, CancellationToken cancellationToken);

    /// <summary>Entitlement status for one external user. Never retried; NotFound and Unavailable are outcomes, not exceptions.</summary>
    Task<TahlilAppStatusResult> GetStatusAsync(string externalUserId, string correlationId, CancellationToken cancellationToken);
}

/// <summary>Local replay guard. A code is recorded before the exchange and is never exchanged twice.</summary>
public interface ITahlilAppLaunchAttemptStore
{
    /// <returns>False when the code was already attempted; the caller must not call the gateway.</returns>
    Task<bool> TryBeginAttemptAsync(string code, string correlationId, CancellationToken cancellationToken);

    Task CompleteAttemptAsync(string code, string outcome, CancellationToken cancellationToken);
}

public enum TahlilAppProvisioningOutcome
{
    Resolved,
    AccountDisabled
}

public sealed record TahlilAppProvisioningResult(
    TahlilAppProvisioningOutcome Outcome,
    Guid LocalUserId,
    Guid TenantId,
    bool Created);

/// <summary>Find-or-create of the local user for an external identity, plus profile synchronisation.</summary>
public interface ITahlilAppAccountProvisioner
{
    Task<TahlilAppProvisioningResult> ResolveAsync(
        TahlilAppExchangeSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Idempotent subscription synchronisation into Billing. Grants no credits.</summary>
public interface ITahlilAppSubscriptionSynchronizer
{
    Task SynchronizeAsync(
        Guid tenantId,
        Guid localUserId,
        TahlilAppSubscriptionDecision decision,
        long? externalRevision,
        CancellationToken cancellationToken);
}

public sealed record AuthenticatedSessionTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    Guid TenantId,
    string? Email,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

/// <summary>Creates a Sapino session for a resolved local user (shared with password login).</summary>
public interface ITahlilAppSessionIssuer
{
    Task<AuthenticatedSessionTokens> IssueAsync(
        Guid localUserId,
        Guid tenantId,
        string? presentedRefreshToken,
        CancellationToken cancellationToken);
}

/// <summary>
/// Policy inputs supplied by the composition root from validated configuration.
/// <see cref="StatusTtl"/> sets the next entitlement sync due time after a successful sync (Design §11, default 15 min).
/// </summary>
public sealed record TahlilAppLaunchPolicy(
    bool Enabled,
    IReadOnlyDictionary<string, string> PlanMap,
    TimeSpan? StatusTtl = null);

public enum TahlilAppLaunchOutcome
{
    Succeeded,
    Disabled,
    Invalid,
    Unavailable,
    AccountDisabled
}

public sealed record TahlilAppLaunchRequest(string Code, string? PresentedRefreshToken, string CorrelationId);

public sealed record TahlilAppLaunchResult(TahlilAppLaunchOutcome Outcome, AuthenticatedSessionTokens? Session = null);
