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
GET    /api/v1/admin/users/{userId}/customer-360
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
PATCH  /api/v1/admin/plans/{planCode}/included-credits
GET    /api/v1/admin/plans/{planCode}/capabilities
PUT    /api/v1/admin/plans/{planCode}/capabilities
GET    /api/v1/admin/customers/{customerAccountId}/subscription
PUT    /api/v1/admin/customers/{customerAccountId}/subscription

GET    /api/v1/admin/customers/{customerAccountId}/usage-ledger
POST   /api/v1/admin/customers/{customerAccountId}/credit-adjustments
GET    /api/v1/admin/audits/security
GET    /api/v1/admin/audits/billing
```

The active subscription catalog is limited to `Free`, `Plus`, and `Pro`. Updating a plan's
included credits requires `admin.plans.manage`, a non-negative credit amount, a pricing-policy
version, and an audit reason. The update changes future plan policy only; it does not rewrite
usage-ledger history or directly change existing customer wallet balances.

List endpoints accept bounded `limit` values from `1` to `100` and return deterministic order.
Tenant identifiers are checked against the effective JWT tenant. Subscription changes require
`expectedRevision`; stale writes return `409`. Credit adjustments require a reason and
idempotency key and delegate to Billing, which appends ledger evidence and updates the wallet
projection atomically. Wallet projections are never edited directly.

Customer 360 requires `admin.users.read` and resolves the user in the effective tenant.
The nullable `creditAdjustmentAccountId` identifies an existing Billing account for callers with
`admin.credits.adjust`. The subscription, credit and purchase sections remain governed by their
own read permissions. The UI submits grants through the existing credit-adjustment route.

The conversation-log list and turn detail include `user.hasUserAccount`. The flag is true when
the conversation actor resolves to an Identity user with membership in the effective tenant.
Otherwise, the log retains the actor ID and the turn, with display name, email, and masked
mobile set to null. The UI offers Customer 360 for confirmed accounts.

Published plan codes and plan-capability policy versions are immutable. Included credit is the
only mutable plan field and uses the dedicated audited endpoint above; capability changes still
require a new policy version.

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

## In-App Messages

Feature 165 exposes admin send and support routes documented in
[`user-story.md`](../specs/165-admin-in-app-messages/user-story.md). Both require
`admin.users.read` plus the respective `admin.in-app-messages.send` or
`admin.in-app-messages.support` permission. The Auth migration assigns both to
`SuperAdmin`. Role administrators can delegate the permissions independently. Operators receive the updated claims when their web token is refreshed.

The recipient can view messages from «پیام‌ها» on desktop or mobile and reply to a
reply-enabled conversation. The account badge shows unread staff messages. Every message is
tenant-scoped to the recipient's membership. Membership removal deletes the history immediately.
Temporary account locks block access and preserve history. Security audit metadata has its own
retention and contains identifiers and timestamps without message content. An authorized support
operator reading message bodies creates an audit record. A staff send or reply and its audit commit together.

Apply the Feature 165 Auth migration before serving the new routes. Roll back the application
version while retaining the migration if messages have already been stored. Rolling back the
migration drops the feature's tables and is intended for verification on disposable databases.
Before opening the queue to operators, assign its owner and target response time in OQ-129.

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
