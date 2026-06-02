using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Authorize]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AdminManagementController(
    ICurrentActorContext actorContext,
    IAdminManagementService administration) : ControllerBase
{
    [HttpGet("users")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    public Task<IReadOnlyCollection<AdminUserView>> GetUsers([FromQuery] string? search, [FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        administration.SearchUsersAsync(Actor.TenantId, search, limit, cancellationToken);

    [HttpGet("users/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    public Task<AdminUserView> GetUser(Guid userId, CancellationToken cancellationToken) =>
        administration.GetUserAsync(Actor.TenantId, userId, cancellationToken);

    [HttpPatch("users/{userId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersManage)]
    public Task<AdminUserView> ChangeUserStatus(Guid userId, AdminUserStatusRequest request, CancellationToken cancellationToken) =>
        administration.ChangeUserStatusAsync(userId, new AdminUserStatusChange(request.IsEnabled, request.Unlock, request.Reason), Context(FinancialCopilotPermissions.AdminUsersManage, request.Reason), cancellationToken);

    [HttpPost("users/{userId:guid}/sessions/revoke")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersManage)]
    public Task<int> RevokeUserSessions(Guid userId, AdminReasonRequest request, CancellationToken cancellationToken) =>
        administration.RevokeUserSessionsAsync(userId, Context(FinancialCopilotPermissions.AdminUsersManage, request.Reason), cancellationToken);

    [HttpPut("users/{userId:guid}/roles")]
    [Authorize(Policy = AuthorizationPolicies.AdminRolesManage)]
    public Task<AdminUserView> SetUserRoles(Guid userId, AdminUserRolesRequest request, CancellationToken cancellationToken) =>
        administration.SetUserRolesAsync(userId, request.RoleIds, Context(FinancialCopilotPermissions.AdminRolesManage, request.Reason), cancellationToken);

    [HttpGet("roles")]
    [Authorize(Policy = AuthorizationPolicies.AdminRolesRead)]
    public Task<IReadOnlyCollection<AdminRoleView>> GetRoles(CancellationToken cancellationToken) =>
        administration.GetRolesAsync(cancellationToken);

    [HttpPost("roles")]
    [Authorize(Policy = AuthorizationPolicies.AdminRolesManage)]
    public Task<AdminRoleView> CreateRole(AdminRoleCreateRequest request, CancellationToken cancellationToken) =>
        administration.CreateRoleAsync(new AdminRoleUpsert(request.Name, request.Reason), Context(FinancialCopilotPermissions.AdminRolesManage, request.Reason), cancellationToken);

    [HttpPatch("roles/{roleId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminRolesManage)]
    public Task<AdminRoleView> UpdateRole(Guid roleId, AdminRoleUpdateRequest request, CancellationToken cancellationToken) =>
        administration.UpdateRoleAsync(roleId, new AdminRoleChange(request.Name, request.IsEnabled, request.Reason), Context(FinancialCopilotPermissions.AdminRolesManage, request.Reason), cancellationToken);

    [HttpPut("roles/{roleId:guid}/permissions")]
    [Authorize(Policy = AuthorizationPolicies.AdminPermissionsManage)]
    public Task<AdminRoleView> SetRolePermissions(Guid roleId, AdminRolePermissionsRequest request, CancellationToken cancellationToken) =>
        administration.SetRolePermissionsAsync(roleId, request.PermissionCodes, Context(FinancialCopilotPermissions.AdminPermissionsManage, request.Reason), cancellationToken);

    [HttpGet("permissions")]
    [Authorize(Policy = AuthorizationPolicies.AdminPermissionsRead)]
    public Task<IReadOnlyCollection<string>> GetPermissions(CancellationToken cancellationToken) =>
        administration.GetPermissionsAsync(cancellationToken);

    [HttpGet("tenants")]
    [Authorize(Policy = AuthorizationPolicies.AdminTenantsRead)]
    public Task<IReadOnlyCollection<AdminTenantView>> GetTenants(CancellationToken cancellationToken) =>
        administration.GetTenantsAsync(Actor.TenantId, cancellationToken);

    [HttpGet("tenants/{tenantId:guid}/members")]
    [Authorize(Policy = AuthorizationPolicies.AdminTenantsRead)]
    public Task<IReadOnlyCollection<AdminTenantMemberView>> GetTenantMembers(Guid tenantId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        RequireEffectiveTenant(tenantId);
        return administration.GetTenantMembersAsync(tenantId, limit, cancellationToken);
    }

    [HttpPut("tenants/{tenantId:guid}/members/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminTenantsManage)]
    public async Task<IActionResult> SetTenantMembership(Guid tenantId, Guid userId, AdminTenantMembershipRequest request, CancellationToken cancellationToken)
    {
        await administration.SetTenantMembershipAsync(tenantId, userId, new AdminTenantMembershipChange(request.IsDefault, request.Reason), Context(FinancialCopilotPermissions.AdminTenantsManage, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpDelete("tenants/{tenantId:guid}/members/{userId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminTenantsManage)]
    public async Task<IActionResult> RemoveTenantMembership(Guid tenantId, Guid userId, [FromBody] AdminReasonRequest request, CancellationToken cancellationToken)
    {
        await administration.RemoveTenantMembershipAsync(tenantId, userId, Context(FinancialCopilotPermissions.AdminTenantsManage, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpGet("plans")]
    [Authorize(Policy = AuthorizationPolicies.AdminPlansRead)]
    public Task<IReadOnlyCollection<AdminPlanView>> GetPlans(CancellationToken cancellationToken) =>
        administration.GetPlansAsync(cancellationToken);

    [HttpPost("plans")]
    [Authorize(Policy = AuthorizationPolicies.AdminPlansManage)]
    public Task<AdminPlanView> PublishPlan(AdminPlanPublishRequest request, CancellationToken cancellationToken) =>
        administration.UpsertPlanAsync(new AdminPlanUpsert(request.Code, request.Name, request.IncludedCredits, request.PricingPolicyVersion, request.Reason), Context(FinancialCopilotPermissions.AdminPlansManage, request.Reason), cancellationToken);

    [HttpGet("plans/{planCode}/capabilities")]
    [Authorize(Policy = AuthorizationPolicies.AdminPlansRead)]
    public Task<IReadOnlyCollection<AdminPlanCapabilityView>> GetPlanCapabilities(string planCode, CancellationToken cancellationToken) =>
        administration.GetPlanCapabilitiesAsync(planCode, cancellationToken);

    [HttpPut("plans/{planCode}/capabilities")]
    [Authorize(Policy = AuthorizationPolicies.AdminPlansManage)]
    public async Task<IActionResult> SetPlanCapabilities(string planCode, AdminPlanCapabilitiesRequest request, CancellationToken cancellationToken)
    {
        await administration.SetPlanCapabilitiesAsync(planCode, request.Capabilities, Context(FinancialCopilotPermissions.AdminPlansManage, request.Reason), cancellationToken);
        return NoContent();
    }

    [HttpGet("customers/{customerAccountId:guid}/subscription")]
    [Authorize(Policy = AuthorizationPolicies.AdminSubscriptionsRead)]
    public Task<AdminSubscriptionView> GetSubscription(Guid customerAccountId, CancellationToken cancellationToken) =>
        administration.GetSubscriptionAsync(Actor.TenantId, customerAccountId, cancellationToken);

    [HttpPut("customers/{customerAccountId:guid}/subscription")]
    [Authorize(Policy = AuthorizationPolicies.AdminSubscriptionsManage)]
    public Task<AdminSubscriptionView> SetSubscription(Guid customerAccountId, AdminSubscriptionRequest request, CancellationToken cancellationToken) =>
        administration.SetSubscriptionAsync(customerAccountId, new AdminSubscriptionChange(request.PlanCode, request.EffectiveFrom, request.EffectiveTo, request.ExpectedRevision, request.Reason), Context(FinancialCopilotPermissions.AdminSubscriptionsManage, request.Reason), cancellationToken);

    [HttpGet("customers/{customerAccountId:guid}/usage-ledger")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsageLedgerRead)]
    public Task<IReadOnlyCollection<AdminUsageLedgerView>> GetUsageLedger(Guid customerAccountId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        administration.GetUsageLedgerAsync(Actor.TenantId, customerAccountId, limit, cancellationToken);

    [HttpPost("customers/{customerAccountId:guid}/credit-adjustments")]
    [Authorize(Policy = AuthorizationPolicies.AdminCreditsAdjust)]
    public Task<AdminCreditAdjustmentView> AdjustCredits(Guid customerAccountId, AdminCreditAdjustmentV1Request request, CancellationToken cancellationToken) =>
        administration.AdjustCreditsAsync(customerAccountId, request.Credits, Context(FinancialCopilotPermissions.AdminCreditsAdjust, request.Reason, request.IdempotencyKey), cancellationToken);

    [HttpGet("audits/security")]
    [Authorize(Policy = AuthorizationPolicies.AdminSecurityAuditRead)]
    public Task<IReadOnlyCollection<AdminSecurityAuditView>> GetSecurityAudits([FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        administration.GetSecurityAuditsAsync(Actor.TenantId, limit, cancellationToken);

    [HttpGet("audits/billing")]
    [Authorize(Policy = AuthorizationPolicies.AdminBillingAuditRead)]
    public Task<IReadOnlyCollection<AdminBillingAuditView>> GetBillingAudits([FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        administration.GetBillingAuditsAsync(Actor.TenantId, limit, cancellationToken);

    private CurrentActor Actor => actorContext.Actor;

    private AdminMutationContext Context(string permission, string? reason = null, string? idempotencyKey = null) =>
        new(Actor.ActorId, Actor.TenantId, Actor.ActorType.ToString(), permission, HttpContext.TraceIdentifier, reason, idempotencyKey);

    private void RequireEffectiveTenant(Guid tenantId)
    {
        if (tenantId != Actor.TenantId)
        {
            throw new AdminManagementException("tenant-scope-violation", "The requested resource is outside the effective tenant scope.", StatusCodes.Status403Forbidden);
        }
    }
}
