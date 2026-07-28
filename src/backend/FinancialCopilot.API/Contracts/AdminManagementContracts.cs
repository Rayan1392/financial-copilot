using FinancialCopilot.Application.Administration;

namespace FinancialCopilot.API.Contracts;

public sealed record AdminUserStatusRequest(bool IsEnabled, bool Unlock, string Reason);
public sealed record AdminReasonRequest(string Reason);
public sealed record AdminRoleCreateRequest(string Name, string Reason);
public sealed record AdminRoleUpdateRequest(string Name, bool IsEnabled, string Reason);
public sealed record AdminUserRolesRequest(IReadOnlyCollection<Guid> RoleIds, string Reason);
public sealed record AdminRolePermissionsRequest(IReadOnlyCollection<string> PermissionCodes, string Reason);
public sealed record AdminTenantMembershipRequest(bool IsDefault, string? Reason);
public sealed record AdminPlanPublishRequest(string Code, string Name, decimal IncludedCredits, string PricingPolicyVersion, string Reason);
public sealed record AdminPlanCapabilitiesRequest(IReadOnlyCollection<AdminCapabilityUpsert> Capabilities, string Reason);
public sealed record AdminSubscriptionRequest(string? PlanCode, DateTimeOffset? EffectiveFrom, DateTimeOffset? EffectiveTo, long ExpectedRevision, string Reason);
public sealed record AdminCreditAdjustmentV1Request(decimal Credits, string Reason, string IdempotencyKey);
