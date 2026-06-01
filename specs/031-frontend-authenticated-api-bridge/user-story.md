# Backend Identity Authentication And Permission Authorization

## User Story

As a FinancialCopilot web-app user, I want to register, sign in, refresh my session, and access
only the capabilities granted to my account so the frontend can use backend-owned Microsoft
authentication and authorization instead of Supabase.

## Backend User Stories

### Identity Registration And Login

As a web-app user, I want to register and sign in with FinancialCopilot credentials so my
account is managed by ASP.NET Core Identity instead of a frontend-specific provider.

### JWT Access And Refresh Sessions

As a signed-in user, I want short-lived access tokens and renewable sessions so the UI can stay
signed in without storing my password or relying on long-lived bearer tokens.

### Permission-Based Authorization

As a platform owner, I want roles to grant persisted permissions that become authorization
claims so protected backend capabilities are controlled by explicit permissions rather than
hardcoded role-name checks.

### Tenant-Scoped Web Sessions

As a platform owner, I want each web session tied to a server-resolved tenant membership so a
browser cannot cross tenant boundaries by supplying identifiers.

### Frontend Authentication Cutover

As a web-app user, I want the existing sign-in screen to use FinancialCopilot authentication so
all subsequent frontend API calls use the same backend-owned identity and authorization model.

## Current Gap

The frontend currently signs users in through Supabase. The .NET API can validate JWT bearer
tokens and API keys, but it does not own web-user registration, password validation, refresh
tokens, persisted roles, persisted permissions, or role-to-permission assignments.

The existing `002-auth-and-tenancy` implementation remains the actor-context foundation. This
story completes the owned web-app authentication path without removing SaaS API-key support.

## Architecture Decision

Use ASP.NET Core Identity and ASP.NET Core authorization primitives:

```text
Authentication
-> ASP.NET Core Identity
-> short-lived JWT access token
-> rotating opaque refresh token

Authorization
-> permission-based policies
-> Role -> Permissions -> Claims
```

Roles organize administration. Permissions authorize behavior. Controllers and application
boundaries require permissions rather than branching on hardcoded role names. JWT access tokens
carry a bounded permission-claim snapshot issued from persisted role assignments.

## Authorization Layers

Product access is decided by three independent layers:

```text
Authenticated actor and tenant membership
-> role-derived permission claim
-> active subscription-plan capability and quota
-> Billing entitlement check and credit reservation for billable work
-> execute operation
```

- **Permissions** answer whether the actor may attempt an operation.
- **Plan capabilities** answer whether the customer's active subscription includes the product
  feature and requested limit.
- **AI credits** answer whether sufficient spending capacity can be reserved for this specific
  billable execution.

Do not encode plan names or credit balances as JWT permissions. Do not debit credits inside an
authorization handler. Permission handlers are security checks; the existing
`FinancialCopilot.Billing` entitlement, reservation, pricing, ledger, and wallet services remain
the accounting source of truth.

## Product Permission Catalog

| Permission code | Capability | Typical owner |
| --- | --- | --- |
| `ai.query` | Submit a message through the single AI facade. | Web user, API client |
| `ai.scanner.execute` | Execute validated natural-language scanner plans behind the facade. | Web user, API client |
| `ai.stock-analysis.execute` | Request single-stock analysis behind the facade. | Web user, API client |
| `financial-reports.read` | Read permitted normalized company and financial-report views. | Web user, API client |
| `watchlist.read.self` | Read the current user's authoritative watchlist and enriched quotes. | Web user |
| `watchlist.write.self` | Add, remove, or reorder the current user's watchlist symbols. | Web user |
| `portfolio.read.self` | Read the current user's portfolio records. | Web user |
| `portfolio.write.self` | Maintain the current user's portfolio records. | Web user |
| `ai.portfolio-analysis.execute` | Request portfolio analysis behind the AI facade. | Web user |
| `ai.deep-research.execute` | Request asynchronous or long-running deep research. | Approved web user, approved API client |
| `conversation.read.self` | Read the current actor's conversation history. | Web user, API client |
| `conversation.write.self` | Create, continue, and delete the current actor's conversations. | Web user, API client |
| `usage.read.self` | Read the current billed-account usage summary where permitted. | Web user, API client |
| `memory.manage.self` | Inspect, grant, revoke, and delete the current user's optional memory. | Web user |
| `data.sync.manage` | Trigger and inspect provider ingestion operations. | Data administrator |
| `billing.manage` | Inspect and administer customer billing operations. | Billing administrator |

Permission codes are stable identifiers, not proof that every future capability is already
implemented. Portfolio analysis and deep research remain unavailable until their application
services are delivered, even when the catalog reserves their authorization codes.

## Plan-Based Capability Rules

Plan capability rules belong to the Billing bounded context's subscription and entitlement
model. They are versioned persisted policy, not controller `if` statements and not hardcoded
Identity role assignments.

Initial configurable seed matrix:

| Capability | Free | Pro | Plus | Premium |
| --- | --- | --- | --- | --- |
| Scanner execution | Included with low quota | Included | Included | Included |
| Single-stock analysis | Included with low quota | Included | Included | Included |
| Financial-report reads | Basic bounded access | Included | Included | Included |
| Watchlist read/write | Small symbol limit | Larger symbol limit | Larger symbol limit | Largest symbol limit |
| Portfolio records | Not included | Included | Included | Included |
| Portfolio AI analysis | Not included | Included with quota | Included with larger quota | Included with largest quota |
| Deep research | Not included | Not included | Included with quota | Included with largest quota |

Exact numeric quotas, credit prices, and limits are configuration seeded under a versioned plan
policy. They are not domain constants. An organization/API plan may use a separate capability
matrix while reusing the same permission identifiers and Billing enforcement flow.

## Required Persistence Model

| Table | Purpose |
| --- | --- |
| `Users` | ASP.NET Core Identity web users, password hashes, security stamp, lockout state, and enabled state. |
| `Roles` | ASP.NET Core Identity roles such as `User`, `DataAdmin`, and `BillingAdmin`. |
| `Permissions` | Stable capability codes such as `ai.query`, `usage.read.self`, `data.sync.manage`, and `billing.manage`. |
| `UserRoles` | ASP.NET Core Identity user-to-role assignments. |
| `RolePermissions` | Role-to-permission assignments used to issue permission claims. |
| `RefreshTokens` | Hashed opaque refresh tokens with user, expiry, creation, rotation, revocation, and replacement metadata. |
| `Tenants` | Tenant records used by the existing actor context. |
| `UserTenants` | Explicit user-to-tenant memberships, including active/default membership where required by the frontend session. |
| `PlanCapabilities` | Versioned subscription-plan feature availability and quota/limit configuration persisted by the Billing bounded context and consumed by Billing entitlements. |

Identity may retain additional standard ASP.NET Core Identity tables where required by the
framework, including user claims, role claims, external logins, and token-provider storage.

## Public API Scope

```http
POST /api/auth/v1/register
POST /api/auth/v1/login
POST /api/auth/v1/refresh
POST /api/auth/v1/logout
POST /api/auth/v1/revoke
GET  /api/auth/v1/me
```

## Acceptance Criteria

1. Web users register and sign in through backend endpoints implemented with ASP.NET Core
   Identity services and password hashing.
2. Successful login returns a short-lived JWT access token and an opaque refresh token.
3. JWT validation checks signature, issuer, audience, lifetime, subject, token id, and required
   FinancialCopilot actor claims.
4. Refresh tokens are random, stored only as hashes, rotated on use, revocable, time-bounded,
   and protected against replay of replaced tokens.
   A successful refresh re-evaluates current tenant membership, roles, and permissions before
   issuing the next access token.
5. Logout revokes the current refresh-token session. Revoke supports explicit session
   invalidation without storing raw refresh tokens.
6. `Users`, `Roles`, `Permissions`, `UserRoles`, `RolePermissions`, `RefreshTokens`, `Tenants`,
   and `UserTenants` are persisted through authentication EF Core migrations.
7. The issued access token includes `sub`, `jti`, `financial_copilot:tenant_id`,
   `financial_copilot:authentication_mode=WebAppUser`, role claims for display/audit, and
   permission claims for authorization.
8. Permission policies use ASP.NET Core `IAuthorizationRequirement`,
   `AuthorizationHandler<TRequirement>`, and a policy provider or explicit policy registration.
9. Existing `AiFacade`, `DataAdmin`, and `BillingAdmin` authorization is expressed through
   permission requirements while preserving `401` versus `403` semantics.
10. Tenant membership is resolved server-side. The browser cannot gain access by submitting an
    arbitrary tenant id.
11. Disabled, locked-out, revoked, expired, and unauthorized users are rejected consistently.
12. Existing SaaS `X-Api-Key` authentication remains supported and is not coupled to Identity
    password flows.
13. The frontend replaces Supabase authentication with backend login, registration, refresh,
    logout, and authenticated-session handling.
14. Integration tests cover login, refresh rotation, replay rejection, logout, revoke,
    lockout/disabled users, permission allow/deny, tenant isolation, and API-key regression.
15. Scanner, stock analysis, report reads, watchlist, portfolio, and deep-research operations
    have stable permission codes and versioned Billing-owned plan-capability rules persisted
    through Billing EF Core migrations.
16. Billable AI operations require both permission and plan entitlement before the existing
    Billing reservation flow authorizes execution.
17. Insufficient permission returns `403`; missing plan capability and insufficient AI credits
    return stable product/billing problem details without executing expensive work.

## Out Of Scope

- Social login, external OpenID Connect providers, and passwordless login.
- Email confirmation, password reset, MFA, and device-management UI unless separately promoted.
- OAuth2/OIDC authorization-server implementation for third-party delegated access.
- Removing SaaS API-key authentication.
