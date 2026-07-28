# Authorization And Plan Entitlements

## Decision

TahlilApp-AI uses layered access control:

```text
ASP.NET Core Identity authentication
-> tenant membership
-> role-derived permission claims
-> subscription-plan capability and quota
-> Billing credit reservation for billable operations
-> application use case
```

Each layer answers a different question:

| Layer | Question | Source of truth |
| --- | --- | --- |
| Authentication | Who is the caller? | ASP.NET Core Identity and validated JWT access token |
| Tenant membership | Which tenant may the caller act within? | `UserTenants` |
| Permission authorization | May this actor attempt the capability? | `Roles`, `Permissions`, `UserRoles`, `RolePermissions`, JWT permission claims |
| Plan entitlement | Does the active subscription include the capability and requested limit? | Billing `SubscriptionPlan` plus versioned `PlanCapabilities` |
| AI credit enforcement | Can this billable execution reserve spending capacity? | Billing wallet, credit line, immutable ledger, and `UsageReservation` |

Permission handlers must not debit credits. JWT claims must not carry mutable credit balances or
be treated as the source of plan entitlement truth.

## Permission Catalog

| Permission code | Purpose |
| --- | --- |
| `ai.query` | Submit prompts through `POST /api/ai/v1/query`. |
| `ai.scanner.execute` | Execute the internal validated scanner workflow. |
| `ai.stock-analysis.execute` | Execute single-stock analysis behind the facade. |
| `financial-reports.read` | Read permitted financial-report views. |
| `watchlist.read.self` | Read the current user's watchlist. |
| `watchlist.write.self` | Modify the current user's watchlist. |
| `portfolio.read.self` | Read the current user's portfolio records. |
| `portfolio.write.self` | Modify the current user's portfolio records. |
| `ai.portfolio-analysis.execute` | Execute portfolio analysis behind the facade. |
| `ai.deep-research.execute` | Execute deep research behind the facade. |
| `conversation.read.self` | Read the current actor's conversations. |
| `conversation.write.self` | Create, continue, and delete the current actor's conversations. |
| `usage.read.self` | Read permitted usage information. |
| `memory.manage.self` | Manage the current user's optional consent-aware memory. |
| `data.sync.manage` | Operate provider ingestion. |
| `billing.manage` | Administer billing accounts. |

Reserved permission codes document future authorization boundaries. They do not make an
unimplemented application service available.

## Initial Plan Policy

The initial policy is configurable seed data. Numeric quotas and operation prices must be stored
as versioned policy data.

| Capability | Free | Pro | Plus | Premium |
| --- | --- | --- | --- | --- |
| Scanner | Low quota | Included | Included | Included |
| Single-stock analysis | Low quota | Included | Included | Included |
| Financial reports | Basic bounded access | Included | Included | Included |
| Watchlist | Small limit | Larger limit | Larger limit | Largest limit |
| Portfolio records | No | Yes | Yes | Yes |
| Portfolio AI analysis | No | Bounded quota | Larger quota | Largest quota |
| Deep research | No | No | Bounded quota | Largest quota |

Organization/API plans may define a separate matrix while reusing the same permission codes and
Billing enforcement flow.

## Existing Billing Extension Point

`FinancialCopilot.Billing` already exposes an operation-code-based `IEntitlementService` and
wallet-capacity enforcement. Extend that boundary to resolve the active `SubscriptionPlan`,
versioned `PlanCapabilities`, and requested quota before validating reservable capacity. Do not
introduce a second entitlement implementation in authentication handlers or controllers.

## Request Enforcement

For a billable AI capability:

```text
Validate JWT and tenant
-> require permission claim
-> resolve billed customer account
-> resolve active plan capability and quota
-> calculate estimated price
-> reserve available AI credits
-> execute routed workflow
-> calculate actual usage
-> commit or release reservation
-> append immutable usage ledger entry
```

Denials happen before expensive work:

| Condition | Expected result |
| --- | --- |
| Invalid or missing authentication | `401 Unauthorized` |
| Missing permission claim | `403 Forbidden` |
| Feature not included in active plan | Stable plan-entitlement problem details |
| Plan quota exhausted | Stable quota problem details |
| Insufficient wallet or approved credit-line capacity | Stable insufficient-credit problem details |

The React UI may hide controls based on `/api/auth/v1/me` and usage/plan responses for usability,
but backend enforcement remains authoritative.

## Administration Surface

Spec
[`035-admin-identity-and-entitlement-management`](../specs/035-admin-identity-and-entitlement-management/user-story.md)
defines the API-first administration surface for users, roles, permission mappings, tenant
memberships, plans, plan capabilities, subscriptions, credit adjustments, immutable
usage-ledger reads, and security/Billing audit visibility. Admin controllers apply explicit
permission policies and orchestrate the owning Identity or Billing boundary; they do not
duplicate authorization, entitlement, or accounting rules.
