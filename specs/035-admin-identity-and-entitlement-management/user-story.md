# Admin Identity And Entitlement Management

## User Story

As an authorized platform administrator, I want a controlled backend management surface for
identity, tenancy, subscriptions, entitlements, credits, and audits so operational changes are
tenant-scoped, permission-protected, traceable, and ready for a future React Admin UI.

## Admin User Stories

### User Management

As an identity administrator, I want to find, inspect, enable, disable, unlock, and revoke
sessions for web users so access can be managed without direct database changes.

### Role And Permission Management

As a security administrator, I want to manage role definitions, inspect the stable permission
catalog, assign permissions to roles, and assign roles to users so authorization remains
explicit and auditable.

### Tenant Membership Management

As a tenant administrator, I want to inspect and change user-to-tenant memberships and default
tenant selection so web users reach only approved tenant data.

### Plan And Capability Management

As a commercial administrator, I want to inspect and manage versioned subscription plans and
their capabilities, quotas, and limits so product packaging changes are persisted Billing
policy rather than controller logic.

### Customer Subscription Management

As a commercial administrator, I want to assign an active subscription plan to an eligible
customer account with effective dates and audit evidence so direct-consumer and organization
entitlements are controlled consistently.

### Credit And Usage Operations

As a billing administrator, I want to inspect usage-ledger entries and apply justified manual
credit adjustments through Billing services so support actions remain idempotent and
financially auditable.

### Audit Visibility

As an auditor, I want to inspect security-administration and Billing-administration audit
records with correlation identifiers so sensitive operational changes can be investigated.

## Current Gap

`031-frontend-authenticated-api-bridge` defines backend-owned Identity, refresh-token sessions,
tenant memberships, role-derived permission claims, and Billing-owned `PlanCapabilities`.
`013-billing-and-credits-domain` owns customer accounts, wallet projections, immutable ledgers,
reservations, and manual credit adjustments.

The project does not yet define one controlled admin surface for operating those capabilities.
Without this feature, administrators would need direct database changes or disconnected
endpoints that risk bypassing tenant scope, audit evidence, and separation between security and
commercial policy.

## Architecture Decision

Add an API-first Admin Management Module composed of clean application-layer services and
ASP.NET Core policy-protected controllers. The module orchestrates existing Identity and Billing
boundaries; it does not duplicate their domain rules.

```text
Authenticated admin actor
-> tenant scope validation
-> explicit permission policy
-> application-layer admin command/query service
-> owning Identity or Billing boundary
-> immutable audit record with correlation id
```

The authorization layers remain separate:

```text
Authentication
-> tenant membership
-> role-derived permission claim
-> Billing-owned plan capability and quota
-> Billing credit reservation for billable execution
```

Roles are administrative groupings only. Permissions authorize admin behavior.
`PlanCapabilities` define product availability, quotas, and limits. Billing owns wallet
projection, reservations, credit adjustments, and immutable ledger records.

JWT access tokens must not carry mutable plan limits, wallet balances, credit amounts, or
subscription state. Controllers must not branch on role names or plan names.

## Admin Permission Catalog

Extend the stable permission catalog with:

| Permission code | Purpose |
| --- | --- |
| `admin.users.read` | Search and inspect web-user identity state. |
| `admin.users.manage` | Enable, disable, unlock, and revoke sessions for users. |
| `admin.roles.read` | Read roles and role assignments. |
| `admin.roles.manage` | Create, rename, disable, and assign roles. |
| `admin.permissions.read` | Inspect the stable permission catalog and role-permission mappings. |
| `admin.permissions.manage` | Assign or remove permissions from roles. |
| `admin.tenants.read` | Inspect tenants and user memberships. |
| `admin.tenants.manage` | Add, update, or remove user tenant memberships. |
| `admin.plans.read` | Inspect subscription plans and effective capability versions. |
| `admin.plans.manage` | Create plan versions and manage capability, quota, and limit policy. |
| `admin.subscriptions.read` | Inspect customer subscription assignments. |
| `admin.subscriptions.manage` | Assign, change, or end customer subscriptions. |
| `admin.usage-ledger.read` | Read tenant-scoped immutable usage-ledger entries. |
| `admin.credits.adjust` | Apply manual Billing credit adjustments with a required reason. |
| `admin.billing-audit.read` | Read Billing and financial-administration audit records. |
| `admin.security-audit.read` | Read identity, authorization, and tenant-administration audit records. |

Existing `data.sync.manage` and `billing.manage` remain valid compatibility permissions for
their current protected surfaces. New endpoints use the narrowest explicit admin permission.

## Managed Boundaries

| Area | Owning boundary | Admin module responsibility |
| --- | --- | --- |
| Users and refresh-token revocation | Identity | Validate admin intent and invoke Identity application services. |
| Roles, permissions, and assignments | Identity authorization | Preserve stable permission identifiers and issue auditable changes. |
| Tenant memberships | Tenant context | Enforce tenant scope and maintain server-owned memberships. |
| Plans and `PlanCapabilities` | Billing subscriptions and entitlements | Create versioned policy changes without mutating historical interpretation. |
| Customer subscriptions | Billing subscriptions | Assign effective plan versions to eligible customer accounts. |
| Manual credit adjustments | Billing ledger | Invoke idempotent adjustment service; never mutate wallet projection directly. |
| Usage ledger and Billing audit | Billing ledger | Expose tenant-scoped read models over immutable records. |
| Security admin audit | Admin audit store | Append immutable records for sensitive administrative actions. |

## Audit Requirements

Every sensitive mutation records:

| Field | Requirement |
| --- | --- |
| `auditId` | Stable identifier for the audit event. |
| `occurredAt` | Server-generated timestamp. |
| `tenantId` | Effective tenant scope, where applicable. |
| `actorId` | Authenticated administrator actor. |
| `actorType` | Web user or approved operational actor type. |
| `permissionCode` | Permission policy authorizing the action. |
| `actionCode` | Stable administrative operation code. |
| `targetType` and `targetId` | Changed resource identity. |
| `reason` | Required for privilege changes, membership removal, user disablement, subscription changes, plan publication, and credit adjustments. |
| `correlationId` | Request correlation identifier returned in errors and logs. |
| `before` and `after` | Redacted structured change summary appropriate to the operation. |
| `idempotencyKey` | Required for financial adjustments and retry-sensitive mutations. |

Audit records must not persist passwords, raw refresh tokens, JWTs, API keys, or other secrets.

## Administrator Lockout Protection

The module must prevent destructive changes that would leave the platform without an active
`SuperAdmin` administration path.

- Do not delete, disable, or remove the final active `SuperAdmin`.
- Do not remove the final active `SuperAdmin` role assignment.
- Do not remove required administration permissions from the final effective `SuperAdmin`
  role path.
- Evaluate the invariant transactionally at write time, not only in the UI.
- Return stable ProblemDetails when a requested operation would violate the invariant.

`SuperAdmin` remains a protected bootstrap grouping. Controllers still authorize using
permission policies rather than checking the role name.

## Suggested Backend API Surface

The final route shape may be refined during implementation, but the backend should expose
versioned, tenant-scoped APIs similar to:

```http
GET    /api/v1/admin/users
GET    /api/v1/admin/users/{userId}
PATCH  /api/v1/admin/users/{userId}/status
POST   /api/v1/admin/users/{userId}/sessions/revoke

GET    /api/v1/admin/roles
POST   /api/v1/admin/roles
PATCH  /api/v1/admin/roles/{roleId}
PUT    /api/v1/admin/roles/{roleId}/permissions
PUT    /api/v1/admin/users/{userId}/roles

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

Mutating operations use request DTOs, optimistic concurrency where appropriate, and
idempotency keys for retry-sensitive writes. Read endpoints support bounded pagination and
filtering. The API is designed for a future React Admin UI, but no UI is delivered here.

## Acceptance Criteria

1. The backend exposes API-ready admin application services for users, roles, permission
   mappings, user-role assignments, tenant memberships, plans, plan capabilities, customer
   subscriptions, credit adjustments, usage-ledger reads, and audit reads.
2. Every admin endpoint requires an explicit stable permission policy. Controllers contain no
   hardcoded role-name or plan-name authorization branches.
3. Admin queries and mutations are tenant-scoped unless an explicitly approved platform-level
   operation requires broader scope.
4. User management supports search, detail reads, enabled/disabled state changes, lockout
   release, and refresh-session revocation without exposing credential secrets.
5. Role management supports role lifecycle, user-role assignment, and role-permission
   assignment while permissions remain stable behavior identifiers.
6. The permission catalog is visible to authorized administrators and cannot be silently
   replaced with arbitrary frontend-defined values.
7. Tenant membership changes are server-validated, audited, and cannot grant cross-tenant
   access through a browser-supplied tenant identifier.
8. Plan and `PlanCapabilities` management publishes versioned Billing-owned policy. Historical
   usage records retain the policy version used when they were created.
9. Subscription assignment supports direct-consumer and SaaS organization customer accounts
   according to Billing policy and records effective dates plus audit evidence.
10. Manual credit adjustments use Billing services, require an audit reason and idempotency
    key, append immutable financial evidence, and update wallet projection through Billing
    persistence only.
11. Wallet projection is never treated as the source of accounting truth and is never directly
    edited by the admin module.
12. Usage-ledger and Billing-audit reads expose immutable tenant-scoped evidence with bounded
    pagination and filtering.
13. Security-sensitive mutations append immutable redacted audit records with actor, tenant,
    action, target, reason where required, timestamp, permission, and correlation id.
14. The backend transactionally rejects deletion, disabling, role removal, or permission
    changes that would remove the final active `SuperAdmin` administration path.
15. Stable RFC 7807-compatible ProblemDetails distinguish authentication failure, missing
    permission, tenant-scope violation, validation failure, concurrency conflict, lockout
    protection, missing subscription capability, and idempotency conflict. Responses include a
    correlation id.
16. Existing SaaS API-key support and tenant isolation continue to work. API-key access to
    admin operations is denied unless a separately approved operational-client policy is
    explicitly configured.
17. The Admin API contract is suitable for a future React Admin UI without requiring that UI
    in this feature.

## Non-Functional Requirements

- Apply ASP.NET Core policy-based authorization at HTTP and application-service boundaries.
- Keep Identity, authorization, Billing entitlement, and credit enforcement as separate
  layers with explicit dependency direction.
- Use clean query and command services; controllers map HTTP contracts and do not own domain
  decisions.
- Use PostgreSQL transactions for lockout-protection invariants and authoritative mutations.
- Use immutable, append-only audit evidence for security and financial administration.
- Redact secrets and sensitive values from responses, logs, ProblemDetails, and audit payloads.
- Require bounded pagination, indexed filters, and deterministic ordering for admin list and
  audit endpoints.
- Use optimistic concurrency tokens for mutable admin resources where concurrent edits matter.
- Require idempotency for credit adjustments and other retry-sensitive financial mutations.
- Emit structured logs and correlation ids suitable for operational tracing.

## Out Of Scope

- React Admin UI implementation.
- End-user self-service profile UI.
- Social login, external identity providers, MFA, passwordless login, and password-reset UX.
- Payment gateway adapters, automated invoicing, and settlement-provider integrations.
- Editing immutable usage-ledger or financial-ledger history.
- Direct wallet-balance mutation.
- Replacing SaaS API-key authentication or adding delegated OAuth administration.
- Promoting reserved portfolio-analysis or deep-research services into delivered product scope.

