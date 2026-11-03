# Tasks

## Admin Shell And Access Control

1. Add a TanStack Router admin layout and child routes for users, roles, tenants, Billing, and
   audits.
2. Add a permission-aware admin navigation entry that is hidden when the authenticated user
   has no supported admin read permission.
3. Add route guards that require authentication and the minimum read permission for each admin
   module. Treat guards as UX controls only; preserve backend authorization on every request.
4. Add an admin layout compatible with the existing RTL application and responsive desktop
   management workflows.

## Typed Admin API Integration

5. Add typed frontend DTOs and API wrappers for the existing `/api/v1/admin/*` contracts
   documented in `docs/admin-management-api.md`.
6. Reuse the existing `financialCopilotApi` client so access-token refresh, credentials,
   API-base resolution, and correlation headers remain centralized.
7. Add reusable bounded-list query handling with loading, empty, error, refresh, and retry
   states.
8. Extend frontend ProblemDetails rendering to display stable admin error types and correlation
   ids without exposing sensitive values.

## User Management

9. Add `/admin/users` with bounded email search and deterministic user listing.
10. Add `/admin/users/$userId` with profile, enabled/disabled, lockout, roles, and tenant
    membership summary.
11. Add confirmed user-status actions for enable, disable, and unlock with required reason
    capture where applicable.
12. Add confirmed refresh-session revocation with result feedback.
13. Add user-role assignment using the server-provided role catalog.
14. Hide or disable controls independently according to `admin.users.manage`,
    `admin.roles.read`, and `admin.roles.manage`.

## Roles, Permissions, And Tenants

15. Add role listing, creation, rename, and enable/disable controls.
16. Add permission-catalog reads and role-permission assignment controls using stable
    server-provided permission codes.
17. Add tenant listing and bounded member listing.
18. Add tenant-membership update, default-tenant selection, and confirmed removal controls.
19. Render `administrator-lockout-protection` responses prominently and preserve the submitted
    form values for correction.

## Billing Administration

20. Add plan listing and plan-capability inspection.
21. Add plan publication and append-only capability publication forms that follow backend
    immutability rules.
22. Restrict the plan table to the canonical `Free`, `Plus`, and `Pro` catalog and add an
    `admin.plans.manage`-gated included-credit edit action that requires a pricing-policy version
    and audit reason, refreshes from the confirmed server response, and never edits wallets.
23. Add customer-account lookup entry points needed to inspect subscriptions and usage ledger.
24. Add subscription detail and update forms with effective dates and `expectedRevision`.
25. Handle `concurrency-conflict` by refreshing the server state before a retry.
26. Add bounded usage-ledger reads with operation, status, correlation, and date context.
27. Add a confirmed credit-adjustment form requiring amount, reason, and idempotency key.
28. Preserve a generated credit-adjustment idempotency key across retries. Never add direct
    wallet-balance editing.

## Audit Views

29. Add bounded security-audit and Billing-audit list screens.
30. Display timestamp, actor, permission, action, target, reason, correlation id, and redacted
    before/after evidence.
31. Ensure audit rendering never interpolates raw HTML and never persists audit payloads to
    browser storage.

## Localization And UX

32. Add Persian admin labels and RTL table/form layouts consistent with the existing owned-web
    application.
33. Add explicit loading, empty, success, permission-denied, validation, stale-write, and
    network-error states for each module.
34. Add accessible labels, keyboard-focus behavior, and confirmation dialogs for destructive
    or financially sensitive actions.

## Verification

35. Add frontend tests for permission-aware admin navigation and route guards.
36. Add frontend tests for user search, user detail, status update, session revocation, and
    role assignment request mapping.
37. Add frontend tests for role-permission, tenant-membership, subscription concurrency, credit
    idempotency, and audit rendering behavior.
38. Verify that missing permissions hide mutation controls while direct backend calls still
    return `403`.
39. Verify that no admin UI path exposes or persists passwords, refresh tokens, JWTs, API keys,
    credential hashes, or direct wallet mutation.
40. Run targeted formatter and lint checks plus `npm run build`.

## Documentation

41. Document the admin route map, permission-to-screen matrix, local administrator bootstrap
    prerequisite, and supported operational workflows.
42. Document that backend policies remain authoritative and UI permission checks are usability
    controls only.
43. Record any verified backend contract gaps as separate follow-up tasks rather than
    bypassing server rules in the frontend.

## Implementation Status

Completed on 2026-06-02. The React administration panel consumes the completed backend Admin
Management API from `035`, applies permission-aware navigation and controls, preserves backend
policy enforcement, and documents the remaining customer-account search contract gap.

Addendum 2026-10-07: the plan table lists only `Free`, `Plus`, and `Pro` and offers an
`admin.plans.manage`-gated included-credit edit (credits, pricing-policy version, reason) backed by
the audited `PATCH` endpoint. The free-form plan-publication button was removed. No automated
frontend test covers this action yet (the admin UI has no existing test harness).
