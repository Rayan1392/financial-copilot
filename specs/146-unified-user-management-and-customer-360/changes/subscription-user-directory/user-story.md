# Feature 146 Change — Subscription User Directory

Layers: backend, frontend
Area: Administration
Priority: P2
Complexity: M
Depends On: Feature 146 base implementation — completed
References: `specs/146-unified-user-management-and-customer-360/Design.md`, `src/backend/FinancialCopilot.Application/Administration/AdminUserQueriesContracts.cs`, `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminUserQueries.cs`

## User Story

As an authorized administrator, I want to open a paginated list of users with a selected active subscription plan from «آمار کاربران», including each subscription's activation date, so I can identify current Plus or Pro subscribers without exporting or loading an unbounded user list.

## Scope

- The report covers users who have the selected plan active at the response's `asOfUtc`.
- The activation date is the Billing subscription's `EffectiveFromUtc`. If it is missing, the UI shows that the date is unknown.
- The selected plan's users are returned through a tenant-scoped, permission-protected, cursor-paginated read endpoint.
- Results contain safe user identifiers, plan code/name, current status, and activation date only.
- Existing aggregate plan counts, user-directory filters, customer management, and subscription mutation flows remain unchanged.

## Acceptance Criteria

1. An administrator with `admin.users.read` and `admin.subscriptions.read` can open the user list for a canonical plan from «آمار کاربران».
2. The list includes only tenant members whose selected plan is active at `asOfUtc`, using effective-from absent-or-at/before and effective-to absent-or-after semantics.
3. Each result displays the safe user identifier, plan, subscription status, and activation date when known.
4. The endpoint enforces tenant scope and both read permissions server-side. A caller missing either permission cannot read subscription membership data.
5. The endpoint validates the plan, page size, and cursor; it returns a stable continuation cursor and `hasMore` without requiring an unbounded total count.
6. Paging returns every matching tenant member exactly once under a stable `asOfUtc`, even when Identity fields are enriched from a separate context.
7. Aggregate plan counts and plan-user results use the same active-subscription definition and reporting instant.
8. Empty results, unavailable/permission states, and continuation pages are clear in the Persian RTL interface.
9. The existing user directory behavior and API contract remain unchanged.
