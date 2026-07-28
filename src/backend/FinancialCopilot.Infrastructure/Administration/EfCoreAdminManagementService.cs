using System.Text.Json;
using System.Data;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinancialCopilot.Infrastructure.Administration;

public sealed class EfCoreAdminManagementService(
    AuthDbContext auth,
    BillingDbContext billing,
    UserManager<FinancialCopilotUser> users,
    RoleManager<FinancialCopilotRole> roles,
    ICreditAdjustmentService adjustments,
    TimeProvider timeProvider) : IAdminManagementService
{
    private const string SuperAdminRole = "SuperAdmin";

    public async Task<IReadOnlyCollection<AdminUserView>> SearchUsersAsync(
        Guid tenantId,
        string? search,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        var normalized = search?.Trim().ToLowerInvariant();
        var userIds = auth.UserTenants.Where(row => row.TenantId == tenantId).Select(row => row.UserId);
        var query = auth.Users.AsNoTracking().Where(user => userIds.Contains(user.Id));
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            query = query.Where(user => user.Email != null && user.Email.ToLower().Contains(normalized));
        }

        var rows = await query.OrderBy(user => user.Email).Take(limit).ToArrayAsync(cancellationToken);
        return await MapUsersAsync(rows, cancellationToken);
    }

    public async Task<AdminUserView> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(tenantId, userId, cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw Missing("User");
        return await MapUserAsync(user, cancellationToken);
    }

    public async Task<AdminUserView> ChangeUserStatusAsync(
        Guid userId,
        AdminUserStatusChange change,
        AdminMutationContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        await RequireMembershipAsync(context.TenantId, userId, cancellationToken);
        RequireReason(change.Reason);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw Missing("User");
        if (!change.IsEnabled && await IsFinalActiveSuperAdminAsync(userId, cancellationToken))
        {
            await RejectLockoutAsync(context, "User", userId.ToString(), transaction, cancellationToken);
        }

        var before = new { user.IsEnabled, user.LockoutEnd };
        user.IsEnabled = change.IsEnabled;
        if (change.Unlock)
        {
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
        }
        await RequireSucceededAsync(users.UpdateAsync(user));
        await AuditAsync(context with { Reason = change.Reason }, "identity.user.status-changed", "User", userId.ToString(), before, new { user.IsEnabled, user.LockoutEnd }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await MapUserAsync(user, cancellationToken);
    }

    public async Task<int> RevokeUserSessionsAsync(Guid userId, AdminMutationContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        await RequireMembershipAsync(context.TenantId, userId, cancellationToken);
        RequireReason(context.Reason);
        var now = timeProvider.GetUtcNow();
        var tokens = await auth.RefreshTokens
            .Where(row => row.UserId == userId && row.RevokedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevocationReason = "Revoked by administrator.";
        }
        await auth.SaveChangesAsync(cancellationToken);
        await AuditAsync(context, "identity.sessions.revoked", "User", userId.ToString(), null, new { RevokedSessions = tokens.Length }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return tokens.Length;
    }

    public async Task<IReadOnlyCollection<AdminRoleView>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var result = new List<AdminRoleView>();
        foreach (var role in await auth.Roles.AsNoTracking().OrderBy(row => row.Name).ToArrayAsync(cancellationToken))
        {
            result.Add(await MapRoleAsync(role, cancellationToken));
        }
        return result;
    }

    public async Task<AdminRoleView> CreateRoleAsync(AdminRoleUpsert change, AdminMutationContext context, CancellationToken cancellationToken)
    {
        RequireReason(change.Reason);
        var name = change.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw Invalid("Role name is required.");
        if (await roles.FindByNameAsync(name) is not null) throw Conflict("Role already exists.");
        var role = new FinancialCopilotRole { Id = Guid.NewGuid(), Name = name };
        await RequireSucceededAsync(roles.CreateAsync(role));
        await AuditAsync(context with { Reason = change.Reason }, "authorization.role.created", "Role", role.Id.ToString(), null, new { role.Name }, cancellationToken);
        return await MapRoleAsync(role, cancellationToken);
    }

    public async Task<AdminRoleView> UpdateRoleAsync(Guid roleId, AdminRoleChange change, AdminMutationContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        RequireReason(change.Reason);
        var role = await auth.Roles.SingleOrDefaultAsync(row => row.Id == roleId, cancellationToken) ?? throw Missing("Role");
        if (string.IsNullOrWhiteSpace(change.Name)) throw Invalid("Role name is required.");
        if (!change.IsEnabled &&
            string.Equals(role.Name, SuperAdminRole, StringComparison.OrdinalIgnoreCase) &&
            await ActiveSuperAdminCountAsync(cancellationToken) <= 1)
        {
            await RejectLockoutAsync(context, "Role", roleId.ToString(), transaction, cancellationToken);
        }
        var before = new { role.Name, role.IsEnabled };
        role.Name = change.Name.Trim();
        role.IsEnabled = change.IsEnabled;
        await RequireSucceededAsync(roles.UpdateAsync(role));
        await AuditAsync(context with { Reason = change.Reason }, "authorization.role.changed", "Role", roleId.ToString(), before, new { role.Name, role.IsEnabled }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await MapRoleAsync(role, cancellationToken);
    }

    public async Task<AdminUserView> SetUserRolesAsync(
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        AdminMutationContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        await RequireMembershipAsync(context.TenantId, userId, cancellationToken);
        RequireReason(context.Reason);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw Missing("User");
        var selectedRoles = await auth.Roles.Where(row => roleIds.Contains(row.Id)).ToArrayAsync(cancellationToken);
        if (selectedRoles.Length != roleIds.Distinct().Count()) throw Invalid("One or more roles do not exist.");
        var currentNames = await users.GetRolesAsync(user);
        var selectedNames = selectedRoles.Select(row => row.Name!).ToArray();
        if (currentNames.Contains(SuperAdminRole) &&
            !selectedNames.Contains(SuperAdminRole) &&
            await IsFinalActiveSuperAdminAsync(userId, cancellationToken))
        {
            await RejectLockoutAsync(context, "User", userId.ToString(), transaction, cancellationToken);
        }

        await RequireSucceededAsync(users.RemoveFromRolesAsync(user, currentNames.Except(selectedNames)));
        await RequireSucceededAsync(users.AddToRolesAsync(user, selectedNames.Except(currentNames)));
        await AuditAsync(context, "authorization.user-roles.changed", "User", userId.ToString(), currentNames, selectedNames, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await MapUserAsync(user, cancellationToken);
    }

    public async Task<AdminRoleView> SetRolePermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<string> permissionCodes,
        AdminMutationContext context,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        RequireReason(context.Reason);
        var role = await auth.Roles.SingleOrDefaultAsync(row => row.Id == roleId, cancellationToken) ?? throw Missing("Role");
        var normalizedCodes = permissionCodes.Select(code => code.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var permissions = await auth.Permissions.Where(row => normalizedCodes.Contains(row.Code)).ToArrayAsync(cancellationToken);
        if (permissions.Length != normalizedCodes.Length) throw Invalid("One or more permission codes are not registered.");

        if (string.Equals(role.Name, SuperAdminRole, StringComparison.OrdinalIgnoreCase) &&
            FinancialCopilotPermissions.AdminAll.Except(normalizedCodes).Any() &&
            await ActiveSuperAdminCountAsync(cancellationToken) <= 1)
        {
            await RejectLockoutAsync(context, "Role", roleId.ToString(), transaction, cancellationToken);
        }

        var existing = await auth.RolePermissions.Where(row => row.RoleId == roleId).ToArrayAsync(cancellationToken);
        var before = await auth.Permissions.Where(row => existing.Select(item => item.PermissionId).Contains(row.Id)).Select(row => row.Code).ToArrayAsync(cancellationToken);
        auth.RolePermissions.RemoveRange(existing);
        auth.RolePermissions.AddRange(permissions.Select(permission => new RolePermissionRow { RoleId = roleId, PermissionId = permission.Id }));
        await auth.SaveChangesAsync(cancellationToken);
        await AuditAsync(context, "authorization.role-permissions.changed", "Role", roleId.ToString(), before, normalizedCodes, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await MapRoleAsync(role, cancellationToken);
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(CancellationToken cancellationToken) =>
        await auth.Permissions.AsNoTracking().OrderBy(row => row.Code).Select(row => row.Code).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<AdminTenantView>> GetTenantsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await auth.Tenants.AsNoTracking().Where(row => row.Id == tenantId).Select(row => new AdminTenantView(row.Id, row.Name)).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<AdminTenantMemberView>> GetTenantMembersAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await (
            from membership in auth.UserTenants.AsNoTracking()
            join user in auth.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.TenantId == tenantId
            orderby user.Email
            select new AdminTenantMemberView(user.Id, user.Email!, membership.IsDefault))
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    public async Task SetTenantMembershipAsync(Guid tenantId, Guid userId, AdminTenantMembershipChange change, AdminMutationContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        RequireSameTenant(tenantId, context.TenantId);
        var userExists = await auth.Users.AnyAsync(row => row.Id == userId, cancellationToken);
        if (!userExists) throw Missing("User");
        var membership = await auth.UserTenants.SingleOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken);
        if (change.IsDefault)
        {
            var defaults = await auth.UserTenants.Where(row => row.UserId == userId && row.IsDefault).ToArrayAsync(cancellationToken);
            foreach (var current in defaults) current.IsDefault = false;
        }
        membership ??= new UserTenantRow { TenantId = tenantId, UserId = userId };
        if (auth.Entry(membership).State == EntityState.Detached) auth.UserTenants.Add(membership);
        var before = new { Existing = auth.Entry(membership).State != EntityState.Added, membership.IsDefault };
        membership.IsDefault = change.IsDefault;
        await auth.SaveChangesAsync(cancellationToken);
        await AuditAsync(context with { Reason = change.Reason }, "tenancy.membership.upserted", "UserTenant", $"{tenantId}:{userId}", before, new { membership.IsDefault }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveTenantMembershipAsync(Guid tenantId, Guid userId, AdminMutationContext context, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginAuthTransactionAsync(cancellationToken);
        RequireSameTenant(tenantId, context.TenantId);
        RequireReason(context.Reason);
        var membership = await auth.UserTenants.SingleOrDefaultAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken) ?? throw Missing("Membership");
        if (await IsFinalActiveSuperAdminAsync(userId, cancellationToken))
        {
            await RejectLockoutAsync(context, "UserTenant", $"{tenantId}:{userId}", transaction, cancellationToken);
        }
        auth.UserTenants.Remove(membership);
        await auth.SaveChangesAsync(cancellationToken);
        await AuditAsync(context, "tenancy.membership.removed", "UserTenant", $"{tenantId}:{userId}", new { membership.IsDefault }, null, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AdminPlanView>> GetPlansAsync(CancellationToken cancellationToken) =>
        await billing.SubscriptionPlans.AsNoTracking().OrderBy(row => row.Code)
            .Select(row => new AdminPlanView(row.Code, row.Name, row.IncludedCredits, row.PricingPolicyVersion))
            .ToArrayAsync(cancellationToken);

    public async Task<AdminPlanView> UpsertPlanAsync(AdminPlanUpsert change, AdminMutationContext context, CancellationToken cancellationToken)
    {
        RequireReason(change.Reason);
        var code = change.Code.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(change.Name) || change.IncludedCredits < 0 || string.IsNullOrWhiteSpace(change.PricingPolicyVersion))
            throw Invalid("Plan values are invalid.");
        var row = await billing.SubscriptionPlans.SingleOrDefaultAsync(item => item.Code == code, cancellationToken);
        if (row is not null)
        {
            throw Conflict("Published plans are immutable. Create a new plan code for a new commercial version.");
        }
        row = new SubscriptionPlanRow { Code = code, Name = change.Name.Trim(), IncludedCredits = change.IncludedCredits, PricingPolicyVersion = change.PricingPolicyVersion.Trim() };
        billing.SubscriptionPlans.Add(row);
        AddBillingAudit(context with { Reason = change.Reason }, "billing.plan.published", "SubscriptionPlan", code, null, row);
        await billing.SaveChangesAsync(cancellationToken);
        await AuditAsync(context with { Reason = change.Reason }, "billing.plan.published", "SubscriptionPlan", code, null, row, cancellationToken);
        return new AdminPlanView(row.Code, row.Name, row.IncludedCredits, row.PricingPolicyVersion);
    }

    public async Task<IReadOnlyCollection<AdminPlanCapabilityView>> GetPlanCapabilitiesAsync(string planCode, CancellationToken cancellationToken) =>
        await billing.PlanCapabilities.AsNoTracking().Where(row => row.PlanCode == planCode).OrderBy(row => row.CapabilityCode).ThenBy(row => row.PolicyVersion)
            .Select(row => new AdminPlanCapabilityView(row.CapabilityCode, row.PolicyVersion, row.IsEnabled, row.Limit))
            .ToArrayAsync(cancellationToken);

    public async Task SetPlanCapabilitiesAsync(string planCode, IReadOnlyCollection<AdminCapabilityUpsert> changes, AdminMutationContext context, CancellationToken cancellationToken)
    {
        RequireReason(context.Reason);
        if (!await billing.SubscriptionPlans.AnyAsync(row => row.Code == planCode, cancellationToken)) throw Missing("Plan");
        if (changes.Count == 0 || changes.Any(item => string.IsNullOrWhiteSpace(item.CapabilityCode) || string.IsNullOrWhiteSpace(item.PolicyVersion) || item.Limit < 0))
            throw Invalid("Capability publication is invalid.");
        foreach (var change in changes)
        {
            var exists = await billing.PlanCapabilities.AnyAsync(row => row.PlanCode == planCode && row.CapabilityCode == change.CapabilityCode && row.PolicyVersion == change.PolicyVersion, cancellationToken);
            if (exists) throw Conflict("Published plan capability versions are immutable.");
            billing.PlanCapabilities.Add(new PlanCapabilityRow { PlanCode = planCode, CapabilityCode = change.CapabilityCode.Trim(), PolicyVersion = change.PolicyVersion.Trim(), IsEnabled = change.IsEnabled, Limit = change.Limit });
        }
        AddBillingAudit(context, "billing.plan-capabilities.published", "SubscriptionPlan", planCode, null, changes);
        await billing.SaveChangesAsync(cancellationToken);
        await AuditAsync(context, "billing.plan-capabilities.published", "SubscriptionPlan", planCode, null, changes, cancellationToken);
    }

    public async Task<AdminSubscriptionView> GetSubscriptionAsync(Guid tenantId, Guid customerAccountId, CancellationToken cancellationToken)
    {
        var account = await RequireAccountAsync(tenantId, customerAccountId, cancellationToken);
        return MapSubscription(account);
    }

    public async Task<AdminSubscriptionView> SetSubscriptionAsync(Guid customerAccountId, AdminSubscriptionChange change, AdminMutationContext context, CancellationToken cancellationToken)
    {
        RequireReason(change.Reason);
        var account = await RequireAccountAsync(context.TenantId, customerAccountId, cancellationToken);
        if (change.PlanCode is not null && !await billing.SubscriptionPlans.AnyAsync(row => row.Code == change.PlanCode, cancellationToken))
            throw Missing("Plan");
        if (change.EffectiveTo is not null && change.EffectiveFrom is not null && change.EffectiveTo < change.EffectiveFrom)
            throw Invalid("Subscription end must not be before its start.");
        if (account.SubscriptionRevision != change.ExpectedRevision) throw Conflict("Subscription revision does not match the current value.");
        var before = new { account.SubscriptionPlanCode, account.SubscriptionEffectiveFrom, account.SubscriptionEffectiveTo, account.SubscriptionRevision };
        account.SubscriptionPlanCode = change.PlanCode;
        account.SubscriptionEffectiveFrom = change.EffectiveFrom;
        account.SubscriptionEffectiveTo = change.EffectiveTo;
        account.SubscriptionRevision++;
        AddBillingAudit(context with { Reason = change.Reason }, "billing.subscription.changed", "CustomerAccount", customerAccountId.ToString(), before, new { account.SubscriptionPlanCode, account.SubscriptionEffectiveFrom, account.SubscriptionEffectiveTo, account.SubscriptionRevision });
        await billing.SaveChangesAsync(cancellationToken);
        await AuditAsync(context with { Reason = change.Reason }, "billing.subscription.changed", "CustomerAccount", customerAccountId.ToString(), new { PlanCode = before }, new { account.SubscriptionPlanCode }, cancellationToken);
        return MapSubscription(account);
    }

    public async Task<AdminCreditAdjustmentView> AdjustCreditsAsync(Guid customerAccountId, decimal credits, AdminMutationContext context, CancellationToken cancellationToken)
    {
        RequireReason(context.Reason);
        if (string.IsNullOrWhiteSpace(context.IdempotencyKey)) throw Invalid("Idempotency key is required.");
        await RequireAccountAsync(context.TenantId, customerAccountId, cancellationToken);
        var result = await adjustments.ApplyAsync(new CreditAdjustmentCommand(customerAccountId, context.ActorId, context.TenantId, credits, context.Reason!, context.IdempotencyKey), cancellationToken);
        if (!result.AlreadyApplied)
        {
            AddBillingAudit(context, "billing.credits.adjusted", "CustomerAccount", customerAccountId.ToString(), null, new { result.LedgerEntry.Id, Credits = result.LedgerEntry.CreditsCharged });
            await billing.SaveChangesAsync(cancellationToken);
        }
        return new AdminCreditAdjustmentView(result.LedgerEntry.Id, result.LedgerEntry.CreditsCharged, result.Wallet.Balance, result.Wallet.ReservedAmount, result.AlreadyApplied);
    }

    public async Task<IReadOnlyCollection<AdminUsageLedgerView>> GetUsageLedgerAsync(Guid tenantId, Guid customerAccountId, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        await RequireAccountAsync(tenantId, customerAccountId, cancellationToken);
        return await billing.UsageLedgerEntries.AsNoTracking().Where(row => row.TenantId == tenantId && row.CustomerAccountId == customerAccountId)
            .OrderByDescending(row => row.OccurredAt).ThenBy(row => row.Id).Take(limit)
            .Select(row => new AdminUsageLedgerView(row.Id, row.CustomerAccountId, row.ActorId, row.TenantId, row.ApiClientId, row.EntryType, row.OperationCode, row.CreditsCharged, row.PricingPolicyVersion, row.IdempotencyKey, row.OccurredAt, row.CompletionStatus, row.AuditDescription))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AdminSecurityAuditView>> GetSecurityAuditsAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await auth.SecurityAdminAudits.AsNoTracking().Where(row => row.TenantId == tenantId).OrderByDescending(row => row.OccurredAt).ThenBy(row => row.Id).Take(limit)
            .Select(row => new AdminSecurityAuditView(row.Id, row.OccurredAt, row.TenantId, row.ActorId, row.ActorType, row.PermissionCode, row.ActionCode, row.TargetType, row.TargetId, row.Reason, row.CorrelationId, row.Before, row.After, row.IdempotencyKey))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<AdminBillingAuditView>> GetBillingAuditsAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
    {
        ValidateLimit(limit);
        return await billing.AdminAudits.AsNoTracking().Where(row => row.TenantId == tenantId).OrderByDescending(row => row.OccurredAt).ThenBy(row => row.Id).Take(limit)
            .Select(row => new AdminBillingAuditView(row.Id, row.OccurredAt, row.TenantId, row.ActorId, row.ActionCode, row.TargetType, row.TargetId, row.Reason, row.CorrelationId, row.IdempotencyKey, row.Before, row.After))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<AdminUserView>> MapUsersAsync(IEnumerable<FinancialCopilotUser> rows, CancellationToken cancellationToken)
    {
        var result = new List<AdminUserView>();
        foreach (var row in rows) result.Add(await MapUserAsync(row, cancellationToken));
        return result;
    }

    private async Task<AdminUserView> MapUserAsync(FinancialCopilotUser user, CancellationToken cancellationToken) =>
        new(user.Id, user.Email!, user.IsEnabled, user.LockoutEnd > timeProvider.GetUtcNow(), (await users.GetRolesAsync(user)).OrderBy(value => value).ToArray(),
            await auth.UserTenants.AsNoTracking().Where(row => row.UserId == user.Id).OrderByDescending(row => row.IsDefault).ThenBy(row => row.TenantId).Select(row => row.TenantId).ToArrayAsync(cancellationToken));

    private async Task<AdminRoleView> MapRoleAsync(FinancialCopilotRole role, CancellationToken cancellationToken) =>
        new(role.Id, role.Name!, role.IsEnabled, await (from mapping in auth.RolePermissions.AsNoTracking() join permission in auth.Permissions.AsNoTracking() on mapping.PermissionId equals permission.Id where mapping.RoleId == role.Id orderby permission.Code select permission.Code).ToArrayAsync(cancellationToken));

    private async Task RequireMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await auth.UserTenants.AnyAsync(row => row.TenantId == tenantId && row.UserId == userId, cancellationToken)) throw TenantViolation();
    }

    private async Task<CustomerAccountRow> RequireAccountAsync(Guid tenantId, Guid customerAccountId, CancellationToken cancellationToken) =>
        await billing.CustomerAccounts.SingleOrDefaultAsync(row => row.TenantId == tenantId && row.Id == customerAccountId, cancellationToken) ?? throw TenantViolation();

    private async Task<bool> IsFinalActiveSuperAdminAsync(Guid userId, CancellationToken cancellationToken)
    {
        var targetIsSuperAdmin = await (from userRole in auth.UserRoles join role in auth.Roles on userRole.RoleId equals role.Id where userRole.UserId == userId && role.Name == SuperAdminRole select userRole).AnyAsync(cancellationToken);
        return targetIsSuperAdmin && await ActiveSuperAdminCountAsync(cancellationToken) <= 1;
    }

    private async Task<int> ActiveSuperAdminCountAsync(CancellationToken cancellationToken) =>
        await (from userRole in auth.UserRoles join role in auth.Roles on userRole.RoleId equals role.Id join user in auth.Users on userRole.UserId equals user.Id where role.Name == SuperAdminRole && user.IsEnabled select user.Id).Distinct().CountAsync(cancellationToken);

    private async Task AuditAsync(AdminMutationContext context, string actionCode, string targetType, string targetId, object? before, object? after, CancellationToken cancellationToken)
    {
        auth.SecurityAdminAudits.Add(new SecurityAdminAuditRow
        {
            Id = Guid.NewGuid(), OccurredAt = timeProvider.GetUtcNow(), TenantId = context.TenantId, ActorId = context.ActorId, ActorType = context.ActorType,
            PermissionCode = context.PermissionCode, ActionCode = actionCode, TargetType = targetType, TargetId = targetId, Reason = context.Reason?.Trim(),
            CorrelationId = context.CorrelationId, Before = Serialize(before), After = Serialize(after), IdempotencyKey = context.IdempotencyKey?.Trim()
        });
        await auth.SaveChangesAsync(cancellationToken);
    }

    private void AddBillingAudit(AdminMutationContext context, string actionCode, string targetType, string targetId, object? before, object? after)
    {
        billing.AdminAudits.Add(new BillingAdminAuditRow
        {
            Id = Guid.NewGuid(), OccurredAt = timeProvider.GetUtcNow(), TenantId = context.TenantId, ActorId = context.ActorId,
            ActionCode = actionCode, TargetType = targetType, TargetId = targetId, Reason = context.Reason!.Trim(),
            CorrelationId = context.CorrelationId, IdempotencyKey = context.IdempotencyKey?.Trim(), Before = Serialize(before), After = Serialize(after)
        });
    }

    private async Task<IDbContextTransaction?> BeginAuthTransactionAsync(CancellationToken cancellationToken) =>
        auth.Database.IsRelational()
            ? await auth.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private async Task RejectLockoutAsync(
        AdminMutationContext context,
        string targetType,
        string targetId,
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await AuditAsync(context, "security.lockout-risk.rejected", targetType, targetId, null, new { Rejected = true }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        throw Lockout();
    }

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value);
    private static AdminSubscriptionView MapSubscription(CustomerAccountRow row) => new(row.Id, row.TenantId, row.AccountType, row.BillingMode, row.SubscriptionPlanCode, row.SubscriptionEffectiveFrom, row.SubscriptionEffectiveTo, row.SubscriptionRevision);
    private static void ValidateLimit(int limit) { if (limit is < 1 or > 100) throw Invalid("Limit must be between 1 and 100."); }
    private static void RequireReason(string? reason) { if (string.IsNullOrWhiteSpace(reason)) throw Invalid("An audit reason is required."); }
    private static void RequireSameTenant(Guid requested, Guid effective) { if (requested != effective) throw TenantViolation(); }
    private static async Task RequireSucceededAsync(Task<IdentityResult> operation)
    {
        var result = await operation;
        if (!result.Succeeded) throw Invalid(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
    private static AdminManagementException Missing(string resource) => new("resource-not-found", $"{resource} was not found.", 404);
    private static AdminManagementException Invalid(string message) => new("validation-failed", message);
    private static AdminManagementException Conflict(string message) => new("concurrency-conflict", message, 409);
    private static AdminManagementException TenantViolation() => new("tenant-scope-violation", "The requested resource is outside the effective tenant scope.", 403);
    private static AdminManagementException Lockout() => new("administrator-lockout-protection", "The operation would remove the final active SuperAdmin administration path.", 409);
}
