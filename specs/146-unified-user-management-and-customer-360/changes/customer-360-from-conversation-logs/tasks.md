# Tasks — Shared Customer 360 and Manual Credit Grants

Status: Gate B: Approved by owner on 2026-11-23

## Before Starting

- [x] Record explicit Gate B approval and verify the Feature 035, 146 and 164 code paths and relevant baseline checks.

## Implementation Plan

1. [x] Extend the Customer 360 contract with a nullable credit-adjustment target account ID. In `AdminManagementController`, check `AdminCreditsAdjust` alongside the existing section permissions. In `EfCoreAdminUserQueries`, reuse the existing tenant-and-user-scoped Billing account lookup and perform it for adjustment-only callers as well. Populate the new field only for an authorized caller and an existing account. Preserve the independent read gates and the query's existing unavailable response when Billing lookup fails. Update the typed client. No migration or new mutation route.
2. [x] Extract the Customer 360 dialog and its detail state into a shared component owned by the admin page. Pass a small common identity payload from Users and from the conversation log's accessible user button in `ai-conversation-logs-panel.tsx`. Keep both originating lists mounted and preserve the conversation log's turn-view action. Reset account-scoped state on target changes and ignore stale responses. Keep the existing messaging, account actions and history components connected.
3. [x] Apply the approved responsive RTL layout using existing dialog, tab and button primitives. Use labeled key/value grids, consistent section headings and readable formatting for mixed-direction values. Compare against the pre-change dialog field by field, including metadata, credit periods, purchase and credit history, permissions, and pagination.
4. [x] Share the manual credit grant form and request flow with the existing Billing entry point. Use `adminApi.adjustCredits` with a request-scoped idempotency key bound to the account and submitted values. Reuse it after an uncertain result for the same submission. Keep validation, pending state and feedback scoped to the selected account. Refresh Customer 360 and any loaded credit history after confirmed success. Distinguish a failed refresh from a failed grant and preserve existing Billing behavior.
5. [x] Run the focused tests and browser checks. Document the additive response field and permission in `docs/admin-management-api.md` and record evidence and environmental limits here. The Persian `Unreleased` entry belongs to the release manager at merge to `develop`.

## Expected Files

- `src/backend/FinancialCopilot.Application/Administration/AdminUserQueriesContracts.cs`
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminUserQueries.cs`
- `src/backend/FinancialCopilot.API/Controllers/AdminManagementController.cs`
- `src/frontend/src/integrations/financial-copilot/admin-client.ts`
- `src/frontend/src/components/admin/admin-page.tsx`, `ai-conversation-logs-panel.tsx`, the shared Customer 360 dialog and manual credit flow, and focused admin tests
- `tests/FinancialCopilot.IntegrationTests/Feature146PostgreSqlIntegrationTests.cs`, `AdminManagementEndpointTests.cs`, and `docs/admin-management-api.md`

## Verification and Definition of Done

- [x] Backend tests cover adjustment-only callers, denial without the adjustment permission, missing Billing accounts, tenant isolation, existing read permissions, and idempotent ledger/audit behavior.
- [x] Frontend tests cover opening from both lists, identity fallbacks, stale loads, permission gating, form validation, duplicate-submit protection, and post-grant refresh/error handling.
- [x] Browser verification at desktop and mobile widths covers RTL layout, focus, keyboard navigation, account sections and actions, and return to the originating row.
- [x] Run focused admin endpoint and PostgreSQL integration tests and the Release solution checks. Run frontend Vitest checks, lint, typecheck and build. Compare against `specs/test-baseline.md` and record the limits of the broader checks below.
- [x] Verify feature acceptance criteria, update task status and review the final diff. The implementation adds an optional response field, so deploy the compatible API before enabling the new action in the frontend.

## Verification Evidence

- The focused Release run passed both `Customer360_applies_section_permissions_tenant_isolation_and_active_refresh_session_rules` and `CreditAdjustment_DelegatesToBillingLedger_AndIsIdempotent` with no skips.
- The focused Vitest run passed all 12 tests in `ai-conversation-logs-panel.test.tsx`. The complete admin component run subsequently passed all 34 tests across seven files using the standard threads pool. The remaining frontend Vitest suites were outside this run.
- Headless Chrome verified both entry points at 1440 px and 390 px with synthetic Admin API responses. It checked the three-column and single-column layouts, RTL direction, scrollable content, keyboard tab navigation, the credit action, and focus restoration to the originating row.
- Headless Chrome submitted an adjustment through the UI against synthetic responses. A simulated 503 followed by an already-applied response confirmed reuse of the same key and payload, the refreshed balance, and a second read of the credit history.
- The frontend production client and SSR build passed. Type checking reported 37 errors in the 17 existing baseline files and no diagnostics in changed files. Full lint reported workspace formatting errors. The changed frontend files passed non-format lint rules. A normalized comparison found identical existing Prettier diagnostics for the admin page and log-panel tests.
- All 24 architecture tests passed. The full Release solution run was stopped after reproducing documented failures in unrelated suites, including `Feature128SemanticRoutingTests` and `ProductRevenueMixFollowUp075Tests`. See `specs/test-baseline.md` and the Feature 164 baseline notes.
