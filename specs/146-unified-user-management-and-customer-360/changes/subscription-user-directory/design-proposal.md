# Feature 146 Change — Subscription User Directory

Status: Gate A: Approved by owner on 2026-11-14

## Discovery

The Admin panel already has an «آمار کاربران» tab with an aggregate subscription-plan breakdown and a user directory with cursor pagination. The directory currently filters by identity/provider attributes only. Each authorized directory row can include a current subscription summary with its effective-from timestamp, but the directory request cannot filter by plan. The aggregate analytics response does not identify individual users.

Feature 146 intentionally excluded plan/subscription filters from its MVP directory. Its design records that combining the Identity and Billing contexts without a bounded query and index contract could produce incomplete pages or an unbounded candidate scan. This change therefore needs a distinct bounded read path; adding a client-side filter or filtering after an existing page is not sufficient.

Billing stores the current account plan and its effective-from/effective-to dates. These dates represent subscription effectiveness, not necessarily a payment or purchase timestamp. The existing record is not a complete history of prior plans.

## User outcome

An administrator can choose a subscription plan from the subscription breakdown in «آمار کاربران» and browse the users whose subscription matches that plan, including the activation/effective-from date. Large results are returned in bounded pages.

## Proposed scope

- Make each plan count in the analytics breakdown a way to open that plan's user list.
- Add a permission-protected, tenant-scoped, read-only endpoint for users matching a canonical plan code.
- Use cursor pagination with stable ordering and a bounded page size. The response need not calculate a total count; it returns whether another page exists and a continuation cursor.
- Show the user's available safe identifier (email and/or display name), plan name/code, activation date, and current subscription status.
- Preserve the existing analytics permission behavior: subscription data is available only with `admin.subscriptions.read`. Requests without that permission must not query or reveal Billing subscription data.
- Define the cohort as subscriptions active at the response's `asOfUtc`: effective-from is absent or no later than that time, and effective-to is absent or later than that time. The displayed date is the Billing effective-from timestamp, localized by the UI.
- Keep the existing all-user directory's Identity/provider filters and cursor contract unchanged.

## Proposed architecture

Add a separate plan-scoped read model under the existing Administration query boundary rather than adding a post-page filter to `GET /users/directory`. Anchor filtering and keyset pagination on tenant-scoped Billing subscription rows, using the canonical plan code and effective dates. Enrich only the bounded page of matching account owners with safe Identity fields and verify tenant membership before returning them. If enrichment or membership checks cannot preserve a complete bounded page under the current ownership indexes, use a measured database-side query/index path before shipping; do not silently scan all users or return short pages while claiming there are no more results.

The analytics plan counts and the new list must use the same `asOfUtc` active-subscription definition. The cursor must be bound to tenant, plan, page size, ordering, and `asOfUtc` so paging does not drift between requests.

The frontend reuses the existing RTL admin shell, typed admin client, permission-aware behavior, and cursor-pagination patterns. The plan list should expose a clear empty state and a readable unavailable/permission state.

## Security and reliability

- Require authenticated admin access, `admin.users.read`, and `admin.subscriptions.read` for the detail list; enforce tenant ownership in the backend.
- Return only safe user profile fields and subscription summary data needed for this view. Do not include credentials, secrets, credit balances, or unrelated purchase history.
- Bound page size, validate plan code and cursor, order deterministically, and use a bounded database timeout consistent with the existing directory.
- Avoid a total-count query unless measurement shows it is needed and remains within the query budget.
- A missing effective-from date is displayed as unknown; it is not replaced with account creation or payment time.

## Acceptance criteria

1. An administrator with both required read permissions can open the user list for a selected canonical plan from «آمار کاربران».
2. Results contain only tenant members with that plan active at the response's `asOfUtc` and include the subscription effective-from date when known.
3. The list uses stable, bounded cursor pagination and does not omit matching users due to cross-context page enrichment.
4. The plan totals and detail list use a consistent active-subscription definition.
5. Missing permission, invalid plan/cursor, empty results, and later pages are represented clearly and safely.
6. Existing user directory filters and current user-management behavior remain intact.

## Open decision

The request says users who “bought” Plus or Pro. The current Billing account state can reliably identify the effective current plan and its activation date, but it does not by itself prove a paid purchase or provide complete plan-change history. This proposal assumes the desired list is **users with the plan active now**, not everyone who ever purchased or previously held that plan. Confirm this cohort before Gate A; a historical paid-purchase report would need a separate source-of-truth and scope.

## Trade-offs and alternatives

- **Recommended: plan-scoped, Billing-anchored cursor endpoint.** It directly matches the selected plan and can use Billing plan/date indexes; it requires careful bounded Identity enrichment and a verified tenant-membership path.
- **Filter the existing Identity directory by plan.** Rejected for this proposal because Feature 146 already documents the risk of incomplete or expensive pages across the separate Identity and Billing models.
- **Load all matching users in the browser and paginate locally.** Rejected because it exposes an unbounded result and does not scale.

## Verification strategy

Add contract and integration coverage for plan selection, effective-date boundaries, tenant isolation, authorization, stable cursor continuation, maximum page size, and complete results where some Billing owners are not tenant members. Verify the actual PostgreSQL query plan and timeout against representative tenant sizes before release. Add frontend coverage for selecting a plan, rendering activation dates, empty/error states, and next/previous page behavior.
