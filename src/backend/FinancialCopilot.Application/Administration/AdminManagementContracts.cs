namespace FinancialCopilot.Application.Administration;

public sealed record AdminUserView(
    Guid UserId,
    string Email,
    bool IsEnabled,
    bool IsLockedOut,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> TenantIds);

public sealed record AdminRoleView(
    Guid RoleId,
    string Name,
    bool IsEnabled,
    IReadOnlyCollection<string> Permissions);

public sealed record AdminTenantView(Guid TenantId, string Name);

public sealed record AdminTenantMemberView(Guid UserId, string Email, bool IsDefault);

public sealed record AdminPlanView(
    string Code,
    string Name,
    decimal IncludedCredits,
    string PricingPolicyVersion);

public sealed record AdminPlanCapabilityView(
    string CapabilityCode,
    string PolicyVersion,
    bool IsEnabled,
    decimal? Limit);

public sealed record AdminSubscriptionView(
    Guid CustomerAccountId,
    Guid TenantId,
    string AccountType,
    string BillingMode,
    string? PlanCode,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    long Revision);

public sealed record AdminUsageLedgerView(
    Guid Id,
    Guid CustomerAccountId,
    Guid ActorId,
    Guid TenantId,
    Guid? ApiClientId,
    string EntryType,
    string OperationCode,
    decimal Credits,
    string PricingPolicyVersion,
    string IdempotencyKey,
    DateTimeOffset OccurredAt,
    string? CompletionStatus,
    string? AuditDescription);

public sealed record AdminSecurityAuditView(
    Guid AuditId,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid ActorId,
    string ActorType,
    string PermissionCode,
    string ActionCode,
    string TargetType,
    string TargetId,
    string? Reason,
    string CorrelationId,
    string? Before,
    string? After,
    string? IdempotencyKey);

public sealed record AdminBillingAuditView(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    Guid ActorId,
    string ActionCode,
    string TargetType,
    string TargetId,
    string Reason,
    string CorrelationId,
    string? IdempotencyKey,
    string? Before,
    string? After);

public sealed record AdminCreditAdjustmentView(
    Guid LedgerEntryId,
    decimal Credits,
    decimal UpdatedBalance,
    decimal ReservedAmount,
    bool AlreadyApplied);

public sealed record AdminMutationContext(
    Guid ActorId,
    Guid TenantId,
    string ActorType,
    string PermissionCode,
    string CorrelationId,
    string? Reason = null,
    string? IdempotencyKey = null);

public sealed record AdminRoleUpsert(string Name, string? Reason);
public sealed record AdminRoleChange(string Name, bool IsEnabled, string Reason);
public sealed record AdminUserStatusChange(bool IsEnabled, bool Unlock, string Reason);
public sealed record AdminTenantMembershipChange(bool IsDefault, string? Reason);
public sealed record AdminPlanUpsert(string Code, string Name, decimal IncludedCredits, string PricingPolicyVersion, string Reason);
public sealed record AdminCapabilityUpsert(string CapabilityCode, string PolicyVersion, bool IsEnabled, decimal? Limit);
public sealed record AdminSubscriptionChange(string? PlanCode, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, long ExpectedRevision, string Reason);

public interface IAdminManagementService
{
    Task<IReadOnlyCollection<AdminUserView>> SearchUsersAsync(Guid tenantId, string? search, int limit, CancellationToken cancellationToken);
    Task<AdminUserView> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<AdminUserView> ChangeUserStatusAsync(Guid userId, AdminUserStatusChange change, AdminMutationContext context, CancellationToken cancellationToken);
    Task<int> RevokeUserSessionsAsync(Guid userId, AdminMutationContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminRoleView>> GetRolesAsync(CancellationToken cancellationToken);
    Task<AdminRoleView> CreateRoleAsync(AdminRoleUpsert change, AdminMutationContext context, CancellationToken cancellationToken);
    Task<AdminRoleView> UpdateRoleAsync(Guid roleId, AdminRoleChange change, AdminMutationContext context, CancellationToken cancellationToken);
    Task<AdminUserView> SetUserRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, AdminMutationContext context, CancellationToken cancellationToken);
    Task<AdminRoleView> SetRolePermissionsAsync(Guid roleId, IReadOnlyCollection<string> permissionCodes, AdminMutationContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminTenantView>> GetTenantsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminTenantMemberView>> GetTenantMembersAsync(Guid tenantId, int limit, CancellationToken cancellationToken);
    Task SetTenantMembershipAsync(Guid tenantId, Guid userId, AdminTenantMembershipChange change, AdminMutationContext context, CancellationToken cancellationToken);
    Task RemoveTenantMembershipAsync(Guid tenantId, Guid userId, AdminMutationContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminPlanView>> GetPlansAsync(CancellationToken cancellationToken);
    Task<AdminPlanView> UpsertPlanAsync(AdminPlanUpsert change, AdminMutationContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminPlanCapabilityView>> GetPlanCapabilitiesAsync(string planCode, CancellationToken cancellationToken);
    Task SetPlanCapabilitiesAsync(string planCode, IReadOnlyCollection<AdminCapabilityUpsert> changes, AdminMutationContext context, CancellationToken cancellationToken);
    Task<AdminSubscriptionView> GetSubscriptionAsync(Guid tenantId, Guid customerAccountId, CancellationToken cancellationToken);
    Task<AdminSubscriptionView> SetSubscriptionAsync(Guid customerAccountId, AdminSubscriptionChange change, AdminMutationContext context, CancellationToken cancellationToken);
    Task<AdminCreditAdjustmentView> AdjustCreditsAsync(Guid customerAccountId, decimal credits, AdminMutationContext context, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminUsageLedgerView>> GetUsageLedgerAsync(Guid tenantId, Guid customerAccountId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminSecurityAuditView>> GetSecurityAuditsAsync(Guid tenantId, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AdminBillingAuditView>> GetBillingAuditsAsync(Guid tenantId, int limit, CancellationToken cancellationToken);
}

public sealed class AdminManagementException(
    string errorCode,
    string message,
    int statusCode = 400) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
}
