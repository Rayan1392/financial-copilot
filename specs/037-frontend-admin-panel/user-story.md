# Frontend Administration Panel

## User Story

As an authorized platform administrator, I want a protected web administration panel for user
management and related operational controls so I can manage access, entitlements, credits, and
audit evidence without direct database edits or manual API calls.

## Current Gap

`035-admin-identity-and-entitlement-management` delivered the permission-protected,
tenant-scoped Admin Management API. The React frontend has no admin route, navigation entry,
admin API integration layer, or management screens. Administrators currently need Postman,
`curl`, or direct operational assistance to use the implemented backend.

The backend remains the source of truth. The frontend must consume the existing
`/api/v1/admin/*` contracts and must not duplicate authorization, tenant-scope, lockout,
subscription, or Billing rules.

## Admin User Stories

### Admin Entry And Authorization

As an administrator, I want the admin entry point and each management action to appear only
when my JWT permissions allow them so the interface reflects the capabilities available to my
account.

### User Management

As a user administrator, I want to search users, inspect their profile and role state, enable
or disable accounts, unlock accounts, and revoke sessions so access issues can be handled from
the web application.

### Role, Permission, And Tenant Management

As a security administrator, I want to inspect roles and permissions, assign roles to users,
manage role permissions, and update tenant memberships so authorization changes remain
explicit and auditable.

### Subscription And Credit Management

As a billing administrator, I want to inspect customer subscriptions and usage-ledger entries,
change subscriptions, and apply justified idempotent credit adjustments so support operations
use Billing-owned workflows rather than direct wallet edits.

### Audit Visibility

As an auditor, I want to inspect security and Billing administration audit records with
correlation identifiers so sensitive changes can be investigated from the admin panel.

## Scope

- Add a protected frontend administration area using the existing owned-web authentication
  session.
- Add permission-aware navigation and route guards.
- Add typed frontend integrations for the implemented Admin Management API.
- Deliver user management as the first complete workflow.
- Deliver role, permission, tenant-membership, subscription, credit-adjustment, usage-ledger,
  and audit screens as permission-gated modules.
- Preserve server-side validation and surface backend ProblemDetails clearly.
- Support Persian RTL presentation consistently with the existing application.

## Acceptance Criteria

1. The frontend exposes a protected admin route reachable only by authenticated web users with
   at least one supported admin read permission.
2. Admin navigation, modules, and mutation controls are driven by permission claims from
   backend-owned Identity. The frontend does not authorize by hardcoded role names.
3. A user-management screen supports bounded email search, user listing, detail inspection,
   enabled/disabled state changes, unlock operations, session revocation, and role assignment.
4. Role and permission screens support role listing, role creation and update, permission
   catalog reads, and audited role-permission assignment when the actor has the required
   permissions.
5. Tenant screens support tenant listing, member listing, membership updates, default-tenant
   selection, and membership removal when authorized.
6. Billing screens support plan reads, plan publication, capability reads and publication,
   customer subscription reads and updates, usage-ledger reads, and credit adjustments when
   authorized.
7. Credit adjustments require amount, reason, and a generated or user-visible idempotency key.
   The UI never exposes direct wallet-balance editing.
8. Sensitive mutations require confirmation and collect a reason whenever the backend
   contract requires one.
9. Security and Billing audit screens show bounded results, timestamps, actor, action, target,
   correlation id, and redacted before/after evidence without exposing secrets.
10. Backend ProblemDetails are rendered with actionable messages and correlation ids.
    `administrator-lockout-protection`, `permission-denied`, `tenant-scope-violation`,
    `concurrency-conflict`, and `validation-failed` remain distinguishable.
11. Loading, empty, success, permission-denied, validation, stale-write, and network-error
    states are visible for each module.
12. The panel is RTL-compatible and uses localized Persian labels where the surrounding
    application is Persian.
13. The frontend never stores passwords, JWTs, refresh tokens, API keys, or credential hashes
    in admin form state, logs, URLs, or browser persistence beyond the existing auth session
    design.
14. Frontend route, integration, component, and build checks pass. Backend Admin API behavior
    remains covered by the existing `035` integration tests.

## Architecture Decision

Add a TanStack Router admin area and a typed frontend integration layer over the existing API:

```text
owned-web JWT session
-> permission-aware admin route guard
-> admin page module
-> typed frontend admin API client
-> /api/v1/admin/* backend endpoint
-> existing server-side permission, tenant, audit, and Billing rules
```

UI permission checks improve navigation and usability only. Every request still relies on
backend policy enforcement. A hidden or disabled button is not a security boundary.

## Suggested Route Shape

```text
/admin
/admin/users
/admin/users/$userId
/admin/roles
/admin/tenants
/admin/billing
/admin/audits/security
/admin/audits/billing
```

## Delivery Priority

Implement the panel incrementally:

1. Admin shell, route protection, permission-aware navigation, and typed API integration.
2. User search, detail, status, session revocation, and role assignment.
3. Roles, permissions, and tenant memberships.
4. Plans, subscriptions, usage ledger, and credit adjustments.
5. Security and Billing audit views.

## Non-Functional Requirements

- Reuse the existing FinancialCopilot API URL builder, access-token refresh behavior, and
  ProblemDetails mapping.
- Keep list requests bounded and deterministic. Do not fetch unbounded administrative data.
- Avoid optimistic local mutation for sensitive operations; render the confirmed server
  response after each successful mutation.
- Generate idempotency keys for retry-sensitive Billing operations and preserve them during a
  retry.
- Keep components split by admin module and avoid one oversized administration page.
- Provide accessible labels, keyboard navigation, and confirmation dialogs.
- Keep secrets and credential material out of UI state, telemetry, logs, and error rendering.

## Out Of Scope

- Changing backend Admin Management API contracts unless a verified UI-blocking gap is found.
- Direct database administration.
- Direct wallet-projection editing or immutable ledger editing.
- Password reset, MFA enrollment, social login, and external identity-provider administration.
- Payment gateway, invoicing, and settlement-provider administration.
- Data-ingestion operations UI for `/api/v1/admin/data-sync/*`; define a separate story if
  required.
- Replacing server-side permission, tenant-scope, audit, concurrency, or lockout enforcement.

