import type { AuthUser } from "./auth";

export const adminPermissions = {
  dataSyncManage: "data.sync.manage",
  orchestrationDiagnostics: "admin.orchestration.diagnostics",
  usersRead: "admin.users.read",
  usersManage: "admin.users.manage",
  rolesRead: "admin.roles.read",
  rolesManage: "admin.roles.manage",
  permissionsRead: "admin.permissions.read",
  permissionsManage: "admin.permissions.manage",
  tenantsRead: "admin.tenants.read",
  tenantsManage: "admin.tenants.manage",
  plansRead: "admin.plans.read",
  plansManage: "admin.plans.manage",
  subscriptionsRead: "admin.subscriptions.read",
  subscriptionsManage: "admin.subscriptions.manage",
  usageLedgerRead: "admin.usage-ledger.read",
  creditsAdjust: "admin.credits.adjust",
  billingAuditRead: "admin.billing-audit.read",
  securityAuditRead: "admin.security-audit.read",
} as const;

export const adminReadPermissions = [
  adminPermissions.usersRead,
  adminPermissions.rolesRead,
  adminPermissions.permissionsRead,
  adminPermissions.tenantsRead,
  adminPermissions.plansRead,
  adminPermissions.subscriptionsRead,
  adminPermissions.usageLedgerRead,
  adminPermissions.billingAuditRead,
  adminPermissions.securityAuditRead,
] as const;

export function hasPermission(user: AuthUser | null, permission: string) {
  return user?.permissions.includes(permission) ?? false;
}

export function canAccessAdmin(user: AuthUser | null) {
  return adminReadPermissions.some((permission) => hasPermission(user, permission));
}
