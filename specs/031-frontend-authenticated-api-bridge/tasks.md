# Tasks

## Backend Identity Foundation

1. Add ASP.NET Core Identity packages and an EF Core authentication context using project-owned
   user and role entity types.
2. Add persistence for `Users`, `Roles`, `Permissions`, `UserRoles`, `RolePermissions`,
   `RefreshTokens`, `Tenants`, and `UserTenants`; retain additional standard Identity tables
   required by ASP.NET Core Identity.
3. Add EF Core configurations, indexes, constraints, and a migration. Enforce unique normalized
   username/email, unique permission code, unique role-permission assignment, unique
   user-tenant membership, and unique refresh-token hash.
4. Seed stable permission codes and baseline roles through an idempotent startup or deployment
   strategy. Do not seed production passwords.
5. Define server-side tenant membership resolution and default-tenant selection for owned
   web-app sessions.

## Access And Refresh Tokens

6. Add token options for issuer, audience, signing credentials, access-token lifetime,
   refresh-token lifetime, and clock skew; load secrets outside source control.
7. Implement a JWT access-token issuer with `sub`, `jti`, tenant, authentication-mode, role, and
   permission claims.
8. Implement cryptographically random opaque refresh tokens. Persist only SHA-256 hashes plus
   user, tenant, expiry, created-at, revoked-at, replaced-by-token-id, and replay diagnostics.
9. Rotate refresh tokens transactionally on every refresh. Reject and revoke a token family
   when a replaced token is replayed.
10. Keep existing JWT bearer validation and SaaS API-key scheme selection compatible with the
    new web-app token issuer.

## Authentication APIs

11. Implement `POST /api/auth/v1/register`.
12. Implement `POST /api/auth/v1/login`.
13. Implement `POST /api/auth/v1/refresh`.
14. Implement `POST /api/auth/v1/logout`.
15. Implement `POST /api/auth/v1/revoke`.
16. Implement `GET /api/auth/v1/me`.
17. Return stable problem-details responses for invalid credentials, lockout, disabled users,
    invalid membership, expired/revoked refresh tokens, and permission denial.

## Permission-Based Authorization

18. Define stable permission constants and a `financial_copilot:permission` claim type.
19. Add `PermissionRequirement`, `PermissionAuthorizationHandler`, and permission policy
    registration or a dynamic `IAuthorizationPolicyProvider`.
20. Map existing protected surfaces to permissions, including:
    - `ai.query`
    - `ai.scanner.execute`
    - `ai.stock-analysis.execute`
    - `financial-reports.read`
    - `watchlist.read.self`
    - `watchlist.write.self`
    - `portfolio.read.self`
    - `portfolio.write.self`
    - `ai.portfolio-analysis.execute`
    - `ai.deep-research.execute`
    - `conversation.read.self`
    - `conversation.write.self`
    - `usage.read.self`
    - `memory.manage.self`
    - `data.sync.manage`
    - `billing.manage`
21. Replace role-name checks in API policies with permission requirements. Retain roles as the
    persisted grouping mechanism and optional token claims for display/audit.
22. Keep tenant and actor validation in every protected policy.

## Plan Capabilities And AI Credit Enforcement

23. Add versioned persisted `PlanCapabilities` and a Billing EF Core migration owned by the
    Billing subscription/entitlement boundary. Keep capability rules out of Identity roles and
    controller branches.
24. Seed configurable baseline capability matrices for `Free`, `Pro`, `Plus`, and `Premium`.
    Store numeric quotas, watchlist limits, and operation limits as policy data rather than code
    constants.
25. Extend the existing operation-code-based `IEntitlementService` contract and implementation
    so scanner, stock analysis, report reads, watchlist, portfolio, and deep-research operations
    check active plan capability and requested limits before wallet-capacity validation.
26. Route every billable AI operation through the existing Billing flow:
    permission check -> plan entitlement -> pricing -> credit reservation -> execution ->
    usage finalization.
27. Return stable problem details that distinguish missing permission (`403`), unavailable plan
    capability, quota exhaustion, and insufficient AI credit capacity.
28. Keep portfolio and deep-research permissions seeded as reserved catalog entries until their
    application services are delivered. Authorization metadata must not imply feature delivery.

## Frontend Authentication Cutover

29. Replace Supabase login/sign-up calls in `src/frontend/src/routes/auth.tsx` with backend
    register/login APIs.
30. Add frontend session handling for access-token expiry, refresh rotation, logout, and
    unauthorized redirects. Prefer secure server-mediated storage; do not persist refresh
    tokens in `localStorage`.
31. Remove Supabase auth middleware and bearer attachment from the production frontend path.
32. Add a typed FinancialCopilot API client with environment-based base URL, bearer forwarding,
    problem-details parsing, and correlation-id handling.
33. Ensure later chat, usage, watchlist, and metadata server functions use this client.

## Verification And Documentation

34. Add backend unit tests for permission resolution, plan-capability resolution, Billing
    enforcement ordering, and refresh-token lifecycle rules.
35. Add backend integration tests for register/login, refresh rotation, replay rejection,
    logout, revoke, lockout, disabled user, tenant isolation, `401`/`403`, permission
    allow/deny, plan capability allow/deny, quota exhaustion, insufficient credits, no-execution
    on denial, and SaaS API-key regression.
36. Run frontend lint/build checks after the authentication cutover.
37. Document local configuration, migration commands, role/permission seed strategy,
    plan-capability policy, token lifetimes, and operational refresh-token revocation behavior.
