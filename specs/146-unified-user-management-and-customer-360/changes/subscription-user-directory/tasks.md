# Feature 146 Change — Subscription User Directory Tasks

Status: Gate B: Approved by owner on 2026-11-14

## Implementation Plan

1. [x] Review the existing Feature 146 query contracts, Billing ownership/indexes, cursor protection, and analytics UI. Confirm the selected active-plan filter can be applied before pagination with tenant membership and deterministic ordering.
2. [x] Extend `AdminUserQueriesContracts.cs` with a plan-user query/page/item contract. Bind the cursor to tenant, plan code, page size, ordering, and `asOfUtc`; cap page size consistently with the existing directory.
3. [ ] Extend `IAdminUserQueries` with a plan-scoped read operation and implement it in `EfCoreAdminUserQueries.cs`. Apply the same effective-date boundaries as `GetAnalyticsAsync`, filter by tenant and plan before keyset paging, and return `pageSize + 1`-bounded results. Batch-enrich safe Identity fields for only the selected page and enforce tenant membership without dropping rows after the page limit.
4. [ ] If the existing persisted indexes cannot support complete, bounded server-side paging, stop and present the measured query-plan findings and any required database/index change for renewed Gate B approval. Do not add a migration or fallback candidate scan under this plan.
5. [x] Add a read-only endpoint to `AdminManagementController.cs` requiring both `admin.users.read` and `admin.subscriptions.read`. Validate the canonical plan code and query bounds, and return stable validation/authorization errors.
6. [x] Extend `admin-client.ts` with typed query/page/item contracts and the endpoint call. Do not change the existing `userDirectory` query contract.
7. [x] Update `user-analytics-panel.tsx` so a plan breakdown entry opens its paginated user list. Reuse cursor pagination and Persian RTL date formatting; show unknown activation dates explicitly and handle loading, empty, permission, and network-error states.
8. [x] Add integration coverage in `Feature146PostgreSqlIntegrationTests.cs` for date boundaries, tenant isolation, stable continuation, complete page enrichment, maximum page size, and active-plan consistency with analytics. Add endpoint authorization and invalid-query coverage in `AdminManagementEndpointTests.cs`.
9. [x] Add frontend coverage for selecting a plan, displaying safe user fields and activation date, empty/error states, and next/previous cursor navigation.
10. [ ] Verify the PostgreSQL execution plan and bounded query timeout at representative tenant sizes. Run the focused backend integration tests, frontend checks, and relevant build commands; record any limitations.
11. [x] Update the change tasks with verification evidence and update the Feature 146 reference documentation only where needed to describe the new read surface.

## Expected Files

- `src/backend/FinancialCopilot.Application/Administration/AdminUserQueriesContracts.cs`
- `src/backend/FinancialCopilot.API/Controllers/AdminManagementController.cs`
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminUserQueries.cs`
- `src/frontend/src/integrations/financial-copilot/admin-client.ts`
- `src/frontend/src/components/admin/user-analytics-panel.tsx`
- `src/frontend/src/components/admin/__tests__/user-analytics-panel.test.tsx`
- `tests/FinancialCopilot.IntegrationTests/Feature146PostgreSqlIntegrationTests.cs`
- `tests/FinancialCopilot.IntegrationTests/AdminManagementEndpointTests.cs`
- Existing frontend test files for admin components/client, if present
- This change's `tasks.md` and Feature 146 reference documentation as needed

## Database Impact

No database migration is included in this plan. The implementation must prove that current Billing ownership and plan indexes support a complete tenant-scoped plan filter before pagination, with the expected bounded query time. A required index/migration is a material plan change and needs renewed Gate B approval.

## Security And Reliability

- Require `admin.users.read` and `admin.subscriptions.read` on the endpoint and omit Billing reads when unauthorized.
- Constrain all account and membership reads to the authenticated tenant.
- Return only user identity fields needed by this list; exclude credentials, provider secrets, credit balances, purchase records, and unrelated Customer 360 data.
- Validate canonical plan code, page size, and cursor. Keep ordering deterministic and cursor-protected.
- Anchor all pages to one `asOfUtc`, and use a bounded database timeout.
- Do not calculate total count unless a measured requirement and query budget are approved.

## Verification And Definition Of Done

- Backend endpoint and query tests cover permission denial, cross-tenant isolation, date boundaries, stable paging, and no missing/duplicate results.
- PostgreSQL query plans demonstrate bounded execution at representative tenant scale.
- Frontend checks confirm plan drilldown, RTL presentation, activation-date rendering, and paging/error states.
- Existing analytics and user directory checks pass without regressions.
- No schema change, historical-purchase report, or subscription-mutation behavior is added.

## Execution Notes

- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --filter "FullyQualifiedName~SubscriptionUsers_RequiresBothReadPermissions_AndValidPlan"` passed (permission denial and plan validation).
- `npm test -- --pool=threads --maxWorkers=1 src/components/admin/__tests__/user-analytics-panel.test.tsx` passed (4 tests).
- `npx eslint` passed for the changed frontend component, test, and API client.
- `git diff --check` passed.
- `npm run typecheck` remains non-zero due to repository-wide pre-existing errors listed in `specs/test-baseline.md`; a targeted diagnostic scan returned no errors for the changed analytics panel, test, or admin client.
- The PostgreSQL integration cases were skipped because the local PostgreSQL/Testcontainers environment is unavailable. The PostgreSQL join, effective-date pagination, and query plan therefore still need execution in an environment with Docker/PostgreSQL. No database migration was added.
- The implementation uses one PostgreSQL query to join the tenant membership and Identity tables before applying the plan filter, keyset cursor, ordering, and page limit. The actual planner/index behavior remains unmeasured locally.
- Production reported PostgreSQL `42P18` for the null `EffectiveFromUtc` cursor parameter. The predicate now branches on a separately typed boolean and casts the date parameter to `timestamp with time zone`; the PostgreSQL regression case includes multiple unknown activation dates so the null-date continuation path is covered when the integration environment is available.
