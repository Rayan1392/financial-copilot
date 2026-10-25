# Feature 146 — Implementation Report

## Decision

**FEATURE_146_IMPLEMENTED_WITH_BLOCKERS**

The approved API, persistence, Billing read, and React Admin changes are implemented. The new PostgreSQL integration tests compile but could not execute because the Testcontainers PostgreSQL fixture cannot reach a Docker engine in this environment. PostgreSQL pagination/query-plan, concurrency, authorization/source-read, history, migration-backfill, and refresh-session evidence remains a release blocker. No production or staging migration was applied.

## Task completion

| Task | Result | Evidence / outstanding work |
|---|---|---|
| 146-T01 | Implemented | New directory request excludes `planCode`, `subscriptionStatus`, and `creditState`; Customer 360 and history routes use existing Feature 035 policies. The optional-section matrix is implemented in the controller and query service. PostgreSQL authorization assertions are pending. |
| 146-T02 | Implemented | Auth operational metadata, registration/provisioning/login writes, optimistic concurrency, indexes, and additive migration generated. Native Identity and TahlilApp launch integration tests pass. PostgreSQL historical backfill and concurrency assertions are pending. |
| 146-T03 | Implemented; runtime verification pending | One tenant-scoped Identity/provider query applies allow-listed filters before keyset paging and `Take(pageSize + 1)`; Billing remains a batch summary for selected page IDs. PostgreSQL pagination, statement-timeout, no-N+1, and query-plan tests are pending. |
| 146-T04 | Implemented; runtime verification pending | Customer 360, per-section backend permission checks, tenant membership, masking, and Active Refresh Sessions calculation are implemented. PostgreSQL authorization, tenant-isolation, and refresh-session tests are pending. |
| 146-T05 | Implemented; runtime verification pending | Tenant-resolved, keyset-paged Billing credit and purchase history reads are implemented. Reads do not mutate wallet or checkout state; purchase transaction detail is constrained to the resolved account. PostgreSQL history tests are pending. |
| 146-T06 | Implemented | Existing Admin client/panel now provides server-paged directory filters, Customer 360 sections/states, and history navigation. Focused ESLint and Vite production build pass. No frontend behavior test suite was added. |
| 146-T07 | Partially complete | Focused neighboring regression tests pass and six PostgreSQL tests were added. All six were skipped because Docker is unavailable; query plans at representative tenant size therefore remain unverified. |

## Acceptance criteria verification

“Implemented; not verified” means the code is present and builds, but the corresponding PostgreSQL or behavior evidence did not run. No criterion is marked test-verified based on a skipped test.

| AC | Outcome | Evidence / limitation |
|---|---|---|
| AC-01 | Implemented; not verified | Directory query starts from tenant membership and filters every page in SQL. PostgreSQL tenant-isolation test was skipped. |
| AC-02 | Implemented; not verified | Page size is bounded to 1–100, default 50; cursor is protected and keyset-bound. PostgreSQL stable-page test was skipped. |
| AC-03 | Implemented; not verified | Query supports GUID, normalized-email prefix, and provider-scoped TahlilApp external-ID prefix; profile name, national ID, and mobile are not search inputs. PostgreSQL filter test was skipped. |
| AC-04 | Code verified | Request contract and validation contain only Identity/provider filters. Billing directory filters are absent. |
| AC-05 | Code verified | Directory returns page rows without aggregate user/link counts; no ambiguous count is exposed. |
| AC-06 | Partial | Native and TahlilApp acquisition metadata are exercised by passing registration/launch tests. A TahlilApp account acquiring native credentials was not separately exercised. |
| AC-07 | Implemented; not verified | Migration backfills `unknown` and null timestamps. The PostgreSQL backfill test was skipped. |
| AC-08 | Partial | TahlilApp launch regression tests pass for repeated and concurrent provisioning of the same provider identity. Customer 360 read-only behavior is implemented; its PostgreSQL test was skipped. |
| AC-09 | Partial | Query uses user+tenant, non-revoked, unexpired rows at `asOfUtc`; refresh rotation/replay regression tests pass on the existing SQLite test host. The PostgreSQL revoked/expired/rotated/replayed and duplicate-hash assertions were skipped. |
| AC-10 | Implemented; not verified | Provider links are projected with status/timestamps and masked identifiers; Telegram username is display metadata. The PostgreSQL Customer 360 test was skipped. |
| AC-11 | Implemented; not verified | Subscription, plan capabilities, and TahlilApp sync state come from their owning stores with source/as-of fields. PostgreSQL source/freshness assertions were skipped. |
| AC-12 | Implemented; not verified | Credit summary uses wallet projection, reservation, approved limit, and active period data; no heterogeneous-ledger balance sum is used. PostgreSQL Billing summary test was skipped. |
| AC-13 | Implemented; not verified | Credit history pages read immutable ledger/period rows. The PostgreSQL history/non-mutation test was skipped. |
| AC-14 | Implemented; not verified | Purchase count requires a fulfilled checkout and same-account financial transaction; no external entitlement revenue is synthesized. PostgreSQL account-isolation/history test was skipped. |
| AC-15 | Partial | Customer 360 DTO does not include raw profile values, credentials, token values, or conversation bodies. Existing TahlilApp encrypted-profile and PII-log tests pass; response-level PostgreSQL assertions were skipped. |
| AC-16 | Regression verified | Admin management suite passes 8/8, including user disable/audit, subscription stale-revision, role lifecycle, and idempotent credit adjustment. Owned Identity suite passes 16/16. |
| AC-17 | Implemented; not verified | Base `admin.users.read` and independent optional-section checks are enforced in the backend; history routes retain section policies. PostgreSQL tests for `not_authorized` with no Billing query and authorized single-section reads were skipped. |
| AC-18 | Regression verified | Existing `/api/v1/admin/users` action and DTO remain unchanged; Admin management integration suite passes 8/8. |
| AC-19 | Implemented; query-plan verification pending | Identity page is one tenant-scoped SQL query with `Take(pageSize + 1)`, 5-second PostgreSQL statement timeout, and no application candidate loop. Pagination/no-N+1 test and representative `EXPLAIN` plans were not run because PostgreSQL was unavailable. |
| AC-20 | Implemented; operational gates remain | UI/API show recorded sync state and source freshness; implementation does not make live TahlilApp calls for Customer 360. Feature 139 staging contract/key-ring gates remain external release requirements. |
| AC-21 | Partial | Focused suites pass 40 tests. Six new PostgreSQL tests covering migration, concurrency, paging, authorization, refresh sessions, Billing failures, and histories were skipped; the full matrix in AC-21 is therefore not verified. |

## Files changed

- `src/backend/FinancialCopilot.Application/Administration/AdminUserQueriesContracts.cs`
- `src/backend/FinancialCopilot.API/Controllers/AdminManagementController.cs`
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminUserQueries.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/AuthDbContext.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/AuthPersistenceModels.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TahlilApp/TahlilAppAccountProvisioner.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/Migrations/20261025124646_Feature146UserOperationalMetadata.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/Migrations/20261025124646_Feature146UserOperationalMetadata.Designer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/Migrations/AuthDbContextModelSnapshot.cs`
- `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`
- `src/frontend/src/integrations/financial-copilot/admin-client.ts`
- `src/frontend/src/components/admin/admin-page.tsx`
- `tests/FinancialCopilot.IntegrationTests/OwnedIdentityEndpointTests.cs`
- `tests/FinancialCopilot.IntegrationTests/PostgreSqlIntegrationFixture.cs`
- `tests/FinancialCopilot.IntegrationTests/TahlilAppLaunchIntegrationTests.cs`
- `tests/FinancialCopilot.IntegrationTests/Feature146PostgreSqlIntegrationTests.cs`

The pre-existing unrelated working-tree edits in `.dockerignore`, `.gitignore`, `src/frontend/vite.config.ts`, `tools/TahlilApp.MockServer/`, and `tools/local-e2e/` were preserved and not changed for Feature 146.

## Migrations

- Generated Auth migration: `20261025124646_Feature146UserOperationalMetadata`.
- Adds one-to-one operational metadata, unknown-source backfill, and supporting tenant/status/source/date/provider indexes.
- No Billing migration was needed or generated.
- Migration model check reports no pending model changes.
- Migration was not applied to any database.

## Test and build results

- Focused integration filter: **40 passed, 0 failed, 6 skipped**. Passed suites: `OwnedIdentityEndpointTests` (16), `TahlilAppLaunchIntegrationTests` (16), and `AdminManagementEndpointTests` (8). The six skipped cases are all Feature 146 PostgreSQL tests.
- PostgreSQL skip cause: Testcontainers cannot connect to the Docker Desktop Linux engine named pipe. Local PostgreSQL services have no explicitly configured test connection; no unknown database was used.
- Frontend ESLint on both changed TypeScript files: **0 errors, 0 warnings**.
- Vite production client and SSR build: **passed**. Vite emitted the repository’s existing large-chunk advisory for `message-list`; no Feature 146 build error occurred.
- `tsc --noEmit`: reports existing errors in unrelated route/test files; no diagnostics referenced `admin-page.tsx` or `admin-client.ts`.
- Auth migration model check: **no changes pending**.
- `git diff --check`: **passed** (Git reports configured Windows LF-to-CRLF normalization warnings).

## Unresolved defects and release gates

- No failing Feature 146 assertion or missing code path is confirmed. The main unresolved implementation risk is unverified Npgsql translation/query-plan cost for the supported combined filters and keyset cursors.
- Run the six Feature 146 tests against isolated PostgreSQL, review `EXPLAIN` plans at representative tenant size, and resolve any provider translation or performance failure before release. This is the blocker for final implementation readiness.
- Review and apply the additive Auth migration through the normal release process; it was not applied here.
- Complete applicable Feature 139 staging contract v2, API credential, and persistent Data Protection key-ring backup/restore gates.
- Complete applicable Feature 145 SMTP staging gates when the release includes those flows.
- Resolve the repository’s unrelated TypeScript diagnostics before relying on a full-project `tsc --noEmit` gate; the two Feature 146 files had no reported diagnostics.

No deployment, commit, or push was performed.
