# Tasks

## Admin Module Foundation

1. Add an API-first Admin Management application boundary that orchestrates Identity, tenant,
   and Billing services without duplicating their domain rules.
2. Define command/query DTOs and clean application-layer services for user, role, permission,
   membership, plan, subscription, credit-adjustment, usage-ledger, and audit operations.
3. Add explicit tenant-scope validation for every admin command and query. Define the narrow
   set of approved platform-level operations separately from tenant-scoped operations.
4. Add stable RFC 7807-compatible ProblemDetails mappings with correlation ids for
   authentication failure, permission denial, tenant violation, validation failure,
   concurrency conflict, lockout protection, missing resource, and idempotency conflict.

## Admin Permission Policies

5. Extend the stable permission catalog with:
   - `admin.users.read`
   - `admin.users.manage`
   - `admin.roles.read`
   - `admin.roles.manage`
   - `admin.permissions.read`
   - `admin.permissions.manage`
   - `admin.tenants.read`
   - `admin.tenants.manage`
   - `admin.plans.read`
   - `admin.plans.manage`
   - `admin.subscriptions.read`
   - `admin.subscriptions.manage`
   - `admin.usage-ledger.read`
   - `admin.credits.adjust`
   - `admin.billing-audit.read`
   - `admin.security-audit.read`
6. Register ASP.NET Core permission policies for each admin capability and apply the narrowest
   applicable policy to each HTTP endpoint and application service.
7. Preserve existing `data.sync.manage` and `billing.manage` policies for compatibility while
   moving new administration surfaces to granular permissions.
8. Ensure controllers and application services do not branch on hardcoded role names or plan
   names.
9. Deny SaaS API-key access to admin endpoints unless a separately approved
   operational-client policy explicitly allows a bounded capability.

## Security Administration Audit

10. Add immutable security-admin audit persistence with server timestamp, actor, actor type,
    tenant scope, permission, stable action code, target type/id, redacted before/after summary,
    reason where required, correlation id, and idempotency key where applicable.
11. Require audit reasons for privilege changes, user disablement, tenant-membership removal,
    subscription changes, plan publication, and credit adjustments.
12. Redact passwords, raw refresh tokens, JWTs, API keys, signing secrets, and other sensitive
    values from audit payloads, logs, and API responses.
13. Add bounded, tenant-scoped security-audit query services with pagination, filters, and
    deterministic ordering.

## User And Session Management

14. Add tenant-scoped user search and detail reads with filters for enabled, disabled,
    locked-out, role, and tenant-membership state.
15. Add commands to enable, disable, and unlock a web user through Identity application
    services with audit evidence.
16. Add an admin command to revoke a user's active refresh-token sessions without exposing raw
    token values.
17. Keep password hashes, security stamps, refresh-token hashes, and credential internals out
    of admin response DTOs.

## Roles And Permissions

18. Add role list, detail, create, rename, enable/disable, and assignment application services.
19. Add permission-catalog read services and role-permission mapping reads.
20. Add audited commands to assign and remove stable permissions on roles.
21. Add audited commands to assign and remove roles on users.
22. Re-evaluate effective permissions on the next access-token refresh after role or
    role-permission changes. Define an operational option to revoke sessions immediately for
    high-risk privilege removal.

## Tenant Membership

23. Add tenant list, detail, and member query services with bounded pagination and filtering.
24. Add audited commands to add, update, set default, and remove user-to-tenant memberships.
25. Validate membership changes server-side and preserve tenant isolation. Never trust a
    browser-supplied tenant id without validating administrator scope.

## SuperAdmin Lockout Protection

26. Define the required effective administration permissions for an active `SuperAdmin`
    bootstrap path.
27. Add transactional invariant checks that reject deleting or disabling the final active
    `SuperAdmin`, removing the final assignment, or removing required permissions from the final
    effective administration path.
28. Add stable lockout-protection ProblemDetails and append an audit event for rejected
    lockout-risk attempts.
29. Add concurrency tests proving simultaneous privilege changes cannot bypass the final-admin
    invariant.

## Subscription Plans And Capabilities

30. Add Billing-owned plan query services for plan definitions, effective versions, included
    credits, pricing-policy version, and versioned `PlanCapabilities`.
31. Add Billing-owned commands to create and publish plan versions. Preserve historical policy
    interpretation rather than mutating versions referenced by prior usage records.
32. Add audited commands to manage capability availability, quotas, watchlist limits, portfolio
    limits, AI operation limits, and other typed limits as versioned policy data.
33. Keep exactly `Free`, `Plus`, and `Pro` as the canonical configurable seed policy. Remove
    `Premium` from the active catalog and migrate any existing `Premium` subscription assignment
    to `Pro` before deleting the obsolete plan and its capabilities.
34. Add an audited included-credit update command for canonical plans. Validate a non-negative
    amount and a pricing-policy version, preserve historical usage-ledger entries, and do not
    change existing wallet balances as a side effect.

## Customer Subscriptions

35. Add tenant-scoped subscription-assignment query services for individual and organization
    `CustomerAccount` records.
36. Add audited commands to assign, change, schedule, and end customer subscriptions with
    effective dates and optimistic concurrency.
37. Validate subscription changes through Billing services and keep subscription state out of
    JWT claims.

## Credits, Usage Ledger, And Billing Audit

38. Reuse the Billing manual-adjustment service for admin credit changes. Require tenant scope,
    customer account, amount, reason, correlation id, and idempotency key.
39. Ensure credit adjustments append immutable financial evidence and update wallet projection
    through Billing persistence only. Do not expose direct wallet-balance mutation.
40. Add tenant-scoped immutable usage-ledger query services with pagination and filters for
    customer account, operation, actor/API client, completion status, correlation id, and date
    range.
41. Add tenant-scoped Billing-audit query services for reservations, usage finalization,
    financial adjustments, refunds, and subscription changes.

## Suggested Admin API Surface

42. Add versioned admin controllers for user and session administration.
43. Add versioned admin controllers for role, permission, and tenant-membership administration.
44. Add versioned admin controllers for plan, capability, and subscription administration.
45. Add versioned admin controllers for credit adjustments, usage-ledger reads, Billing audits,
    and security audits.
46. Keep controllers thin: validate HTTP contracts, pass correlation and idempotency metadata,
    invoke application services, and map typed results to responses or ProblemDetails.

## Verification

47. Add unit tests for each permission policy, tenant-scope validator, audit-reason requirement,
    secret redaction rule, plan-version publication rule, and subscription validation rule.
48. Add unit and concurrency tests for final-`SuperAdmin` lockout protection.
49. Add integration tests for user status changes, session revocation, role lifecycle,
    user-role assignment, role-permission assignment, permission-catalog reads, tenant
    membership changes, and cross-tenant rejection.
50. Add Billing integration tests for plan publication, plan-capability updates, subscription
    assignment, idempotent credit adjustment, immutable usage-ledger visibility, Billing audit
    visibility, and wallet projection rebuilding.
51. Add integration tests proving every admin endpoint rejects missing authentication, missing
    permission, unauthorized tenant scope, and unapproved SaaS API keys with stable
    ProblemDetails and correlation ids.
52. Add architecture tests preventing admin controllers from directly mutating Identity
    persistence, wallet projections, usage ledgers, or plan-capability rows.

## Documentation

53. Document admin permission codes, baseline role-to-permission seed strategy, `SuperAdmin`
    bootstrap and recovery procedure, and policy rollout process.
54. Document Admin API contracts, pagination/filter conventions, ProblemDetails types,
    correlation-id propagation, optimistic concurrency, and idempotency requirements.
55. Document audit retention, redaction, reason requirements, and the operational process for
    investigating security and Billing administration changes.
56. Document that React Admin UI implementation remains a later feature consuming these
    backend contracts.

## Implementation Status

Completed on 2026-06-02. The delivered backend-only module exposes narrow permission-protected
Admin APIs, serializable final-`SuperAdmin` lockout protection, immutable security and Billing
administration audit stores, append-only plan-capability publication, subscription revision
checks, and Billing-owned idempotent credit adjustment. React Admin UI remains out of scope.

Addendum 2026-10-07: the plan catalog is restricted to `Free`, `Plus`, and `Pro`
(`RestrictCanonicalSubscriptionPlans` migration retires `Premium`, remaps its accounts to `Pro`,
and aborts if checkout intents still reference it). `PATCH /api/v1/admin/plans/{planCode}/included-credits`
is audited and covered by `AdminManagementEndpointTests`.
