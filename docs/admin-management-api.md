# Admin Identity And Entitlement Management

The Admin Management API is a backend-only operational surface. A future React Admin UI may
consume these contracts, but no Admin UI is delivered in this feature.

## Authorization

Every route under `/api/v1/admin` requires an authenticated web-user JWT and one narrow
permission claim. SaaS `X-Api-Key` actors are rejected. Roles are administrative groupings;
policies authorize by permission code.

The stable admin permission catalog is:

```text
admin.users.read
admin.users.manage
admin.roles.read
admin.roles.manage
admin.permissions.read
admin.permissions.manage
admin.tenants.read
admin.tenants.manage
admin.plans.read
admin.plans.manage
admin.subscriptions.read
admin.subscriptions.manage
admin.usage-ledger.read
admin.credits.adjust
admin.billing-audit.read
admin.security-audit.read
```

`SuperAdmin` is the bootstrap grouping seeded with the complete admin catalog plus the existing
`data.sync.manage` and `billing.manage` compatibility permissions. Assign the first `SuperAdmin`
through a controlled deployment/bootstrap process. The API transactionally rejects disabling
the final active `SuperAdmin`, removing its role assignment or tenant membership, disabling its
role, or removing required admin permissions from its role path. Rejected attempts are audited.

## Routes

```http
GET    /api/v1/admin/users
GET    /api/v1/admin/users/{userId}
PATCH  /api/v1/admin/users/{userId}/status
POST   /api/v1/admin/users/{userId}/sessions/revoke
PUT    /api/v1/admin/users/{userId}/roles

GET    /api/v1/admin/roles
POST   /api/v1/admin/roles
PATCH  /api/v1/admin/roles/{roleId}
PUT    /api/v1/admin/roles/{roleId}/permissions
GET    /api/v1/admin/permissions

GET    /api/v1/admin/tenants
GET    /api/v1/admin/tenants/{tenantId}/members
PUT    /api/v1/admin/tenants/{tenantId}/members/{userId}
DELETE /api/v1/admin/tenants/{tenantId}/members/{userId}

GET    /api/v1/admin/plans
POST   /api/v1/admin/plans
GET    /api/v1/admin/plans/{planCode}/capabilities
PUT    /api/v1/admin/plans/{planCode}/capabilities
GET    /api/v1/admin/customers/{customerAccountId}/subscription
PUT    /api/v1/admin/customers/{customerAccountId}/subscription

GET    /api/v1/admin/customers/{customerAccountId}/usage-ledger
POST   /api/v1/admin/customers/{customerAccountId}/credit-adjustments
GET    /api/v1/admin/audits/security
GET    /api/v1/admin/audits/billing
```

List endpoints accept bounded `limit` values from `1` to `100` and return deterministic order.
Tenant identifiers are checked against the effective JWT tenant. Subscription changes require
`expectedRevision`; stale writes return `409`. Credit adjustments require a reason and
idempotency key and delegate to Billing, which appends ledger evidence and updates the wallet
projection atomically. Wallet projections are never edited directly.

Published plan codes and plan-capability policy versions are immutable. Publish a new code or
policy version instead of overwriting history.

## Audit And Errors

Identity, authorization, and tenancy mutations append redacted security-admin audit events.
Plan, capability, subscription, and credit operations append Billing-admin audit events.
Passwords, hashes, JWTs, refresh tokens, and API keys are never included. Reason text is
required for sensitive changes.

Admin authorization and application failures use RFC 7807-compatible ProblemDetails with
`traceId` and `correlationId`. Stable types include:

```text
authentication-required
permission-denied
tenant-scope-violation
validation-failed
resource-not-found
concurrency-conflict
administrator-lockout-protection
```

## Database Update

Apply the Auth and Billing migrations:

```powershell
dotnet ef database update `
  --project src/backend/FinancialCopilot.Infrastructure `
  --startup-project src/backend/FinancialCopilot.API `
  --context AuthDbContext

dotnet ef database update `
  --project src/backend/FinancialCopilot.Infrastructure `
  --startup-project src/backend/FinancialCopilot.API `
  --context BillingDbContext
```
