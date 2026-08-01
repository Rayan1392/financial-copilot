import { financialCopilotApi } from "./api-client";

export type AdminUser = {
  userId: string;
  email: string;
  isEnabled: boolean;
  isLockedOut: boolean;
  roles: string[];
  tenantIds: string[];
};
export type AdminRole = { roleId: string; name: string; isEnabled: boolean; permissions: string[] };
export type AdminTenant = { tenantId: string; name: string };
export type AdminTenantMember = { userId: string; email: string; isDefault: boolean };
export type AdminPlan = {
  code: string;
  name: string;
  includedCredits: number;
  pricingPolicyVersion: string;
};
export type AdminCapability = {
  capabilityCode: string;
  policyVersion: string;
  isEnabled: boolean;
  limit: number | null;
};
export type AdminSubscription = {
  customerAccountId: string;
  tenantId: string;
  accountType: string;
  billingMode: string;
  planCode: string | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  revision: number;
};
export type AdminUsageLedger = {
  id: string;
  operationCode: string;
  entryType: string;
  credits: number;
  occurredAt: string;
  completionStatus: string | null;
  auditDescription: string | null;
  correlationId?: string;
};
export type AdminCreditAdjustment = {
  ledgerEntryId: string;
  credits: number;
  updatedBalance: number;
  reservedAmount: number;
  alreadyApplied: boolean;
};
export type AdminAudit = {
  auditId?: string;
  id?: string;
  occurredAt: string;
  actorId: string;
  permissionCode?: string;
  actionCode: string;
  targetType: string;
  targetId: string;
  reason: string | null;
  correlationId: string;
  before: string | null;
  after: string | null;
};
export type FundPortfolioRun = { id: string; triggerType: string; providerName: string; status: string; discoveredCount: number; importedCount: number; duplicateCount: number; partialCount: number; failedCount: number; startedAtUtc: string; completedAtUtc: string | null; correlationId: string };
export type FundPortfolioReport = { reportId: string; fundId: string; providerName: string; originalFileName: string; parseStatus: string; sourceRevision: number; periodEndDate: string | null; importedAtUtc: string; sheetCount: number; issueCount: number; errorCount: number; hasReconciliationIssues: boolean };
export type FundPortfolioReview = { id: string; reportId: string; mappingType: string; rawValue: string; normalizedValue: string; candidateJson: string; status: string; resolutionJson: string | null; resolvedByActorId: string | null; resolvedAtUtc: string | null; version: number };

const json = (method: string, body?: unknown): RequestInit => ({
  method,
  headers: body === undefined ? undefined : { "Content-Type": "application/json" },
  body: body === undefined ? undefined : JSON.stringify(body),
});

export const adminApi = {
  users: (search = "") =>
    financialCopilotApi<AdminUser[]>(
      `/api/v1/admin/users?search=${encodeURIComponent(search)}&limit=50`,
    ),
  user: (id: string) => financialCopilotApi<AdminUser>(`/api/v1/admin/users/${id}`),
  setUserStatus: (id: string, isEnabled: boolean, unlock: boolean, reason: string) =>
    financialCopilotApi<AdminUser>(
      `/api/v1/admin/users/${id}/status`,
      json("PATCH", { isEnabled, unlock, reason }),
    ),
  revokeSessions: (id: string, reason: string) =>
    financialCopilotApi<number>(
      `/api/v1/admin/users/${id}/sessions/revoke`,
      json("POST", { reason }),
    ),
  setUserRoles: (id: string, roleIds: string[], reason: string) =>
    financialCopilotApi<AdminUser>(
      `/api/v1/admin/users/${id}/roles`,
      json("PUT", { roleIds, reason }),
    ),
  roles: () => financialCopilotApi<AdminRole[]>("/api/v1/admin/roles"),
  createRole: (name: string, reason: string) =>
    financialCopilotApi<AdminRole>("/api/v1/admin/roles", json("POST", { name, reason })),
  updateRole: (id: string, name: string, isEnabled: boolean, reason: string) =>
    financialCopilotApi<AdminRole>(
      `/api/v1/admin/roles/${id}`,
      json("PATCH", { name, isEnabled, reason }),
    ),
  permissions: () => financialCopilotApi<string[]>("/api/v1/admin/permissions"),
  setRolePermissions: (id: string, permissionCodes: string[], reason: string) =>
    financialCopilotApi<AdminRole>(
      `/api/v1/admin/roles/${id}/permissions`,
      json("PUT", { permissionCodes, reason }),
    ),
  tenants: () => financialCopilotApi<AdminTenant[]>("/api/v1/admin/tenants"),
  tenantMembers: (id: string) =>
    financialCopilotApi<AdminTenantMember[]>(`/api/v1/admin/tenants/${id}/members?limit=100`),
  setTenantMember: (tenantId: string, userId: string, isDefault: boolean, reason: string) =>
    financialCopilotApi<void>(
      `/api/v1/admin/tenants/${tenantId}/members/${userId}`,
      json("PUT", { isDefault, reason }),
    ),
  removeTenantMember: (tenantId: string, userId: string, reason: string) =>
    financialCopilotApi<void>(
      `/api/v1/admin/tenants/${tenantId}/members/${userId}`,
      json("DELETE", { reason }),
    ),
  plans: () => financialCopilotApi<AdminPlan[]>("/api/v1/admin/plans"),
  publishPlan: (body: {
    code: string;
    name: string;
    includedCredits: number;
    pricingPolicyVersion: string;
    reason: string;
  }) => financialCopilotApi<AdminPlan>("/api/v1/admin/plans", json("POST", body)),
  capabilities: (code: string) =>
    financialCopilotApi<AdminCapability[]>(
      `/api/v1/admin/plans/${encodeURIComponent(code)}/capabilities`,
    ),
  publishCapabilities: (code: string, capabilities: AdminCapability[], reason: string) =>
    financialCopilotApi<void>(
      `/api/v1/admin/plans/${encodeURIComponent(code)}/capabilities`,
      json("PUT", { capabilities, reason }),
    ),
  subscription: (id: string) =>
    financialCopilotApi<AdminSubscription>(`/api/v1/admin/customers/${id}/subscription`),
  setSubscription: (
    id: string,
    body: {
      planCode: string | null;
      effectiveFrom: string | null;
      effectiveTo: string | null;
      expectedRevision: number;
      reason: string;
    },
  ) =>
    financialCopilotApi<AdminSubscription>(
      `/api/v1/admin/customers/${id}/subscription`,
      json("PUT", body),
    ),
  ledger: (id: string) =>
    financialCopilotApi<AdminUsageLedger[]>(`/api/v1/admin/customers/${id}/usage-ledger?limit=50`),
  adjustCredits: (id: string, credits: number, reason: string, idempotencyKey: string) =>
    financialCopilotApi<AdminCreditAdjustment>(
      `/api/v1/admin/customers/${id}/credit-adjustments`,
      json("POST", { credits, reason, idempotencyKey }),
    ),
  securityAudits: () => financialCopilotApi<AdminAudit[]>("/api/v1/admin/audits/security?limit=50"),
  billingAudits: () => financialCopilotApi<AdminAudit[]>("/api/v1/admin/audits/billing?limit=50"),
  fundPortfolioRuns: () => financialCopilotApi<{ items: FundPortfolioRun[]; totalCount: number }>("/api/v1/admin/fund-portfolio-reports/runs?page=1&pageSize=20"),
  fundPortfolioReports: () => financialCopilotApi<{ items: FundPortfolioReport[]; totalCount: number }>("/api/v1/admin/fund-portfolio-reports?page=1&pageSize=20"),
  fundPortfolioReviews: () => financialCopilotApi<{ items: FundPortfolioReview[]; totalCount: number }>("/api/v1/admin/fund-portfolio-mapping-reviews?page=1&pageSize=20&status=Pending"),
  fundPortfolioSourceStatus: (provider = "ConfiguredLocalStorage") => financialCopilotApi<{ providerName: string; available: boolean; unavailableReason: string | null }>(`/api/v1/admin/fund-portfolio-reports/source-status/${encodeURIComponent(provider)}`),
  fundPortfolioHealth: () => financialCopilotApi<{ sourceAvailable: boolean; sourceReason: string | null; totalRuns: number; queuedItems: number; retryableItems: number; pendingReviews: number; lastRunAtUtc: string | null }>("/api/v1/admin/fund-portfolio-reports/health"),
  fundPortfolioUpload: (file: File, fundName?: string) => { const form = new FormData(); form.append("file", file); if (fundName) form.append("fundName", fundName); return financialCopilotApi<{ runId: string; itemCount: number; status: string; correlationId: string }>("/api/v1/admin/fund-portfolio-reports/uploads", { method: "POST", body: form }); },
  fundPortfolioReprocess: (reportId: string) => financialCopilotApi<void>(`/api/v1/admin/fund-portfolio-reports/${reportId}/reprocess`, json("POST", { confirm: true })),
  fundPortfolioResolveReview: (review: FundPortfolioReview, approve: boolean, resolutionJson: string) => financialCopilotApi<void>(`/api/v1/admin/fund-portfolio-mapping-reviews/${review.id}/resolve`, json("POST", { expectedVersion: review.version, approve, resolutionJson, resolvedByActorId: review.resolvedByActorId ?? "console" })),
};
