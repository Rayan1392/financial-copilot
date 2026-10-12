# Implementation Report — Feature 142

## Status

**COMPLETE** — implementation and focused verification are complete. The full solution test command was run; it reports existing failures outside the changed feature, listed below. No real Noavaran request was made.

## Feature and API

- Feature: `142-manual-noavaran-financial-statement-backfill`
- Path: `specs/142-manual-noavaran-financial-statement-backfill/`
- New endpoint: `POST /api/v1/admin/noavaran-current/financial-statement-backfill/single-company-period`
- Authorization: existing `NoavaranMonthlyBackfill` policy for authenticated web users with the Noavaran backfill or `DataSyncManage` permission.

## Existing integrations discovered

The current-API client is `NadpcoApiDataProviderClient`, configured as `NoavaranCurrentApi` with base address `https://data3.nadpco.com/`. It uses the existing token/authentication handlers and retained raw-payload store.

- Balance sheet: `POST api/v2/FS/BalanceSheet/Values`
- Income statement: `POST api/v2/FS/IncomeStatement/Values`
- Cash flow: `POST api/v2/FS/CashFlow/Values`
- Financial ratios/indicators: `POST api/v2/CompanyFundamentalIndex/Values`, using the existing mapped `companyIndexIds` list.
- Statement bodies use `companyIds` and `items: []`; ratio bodies use `companyIds` and mapped `companyIndexIds`.
- Targeted query parameters are `fromYear=<year>`, `toYear=<year>`, and `perTId=<3|6|9|12>`. Existing configured audited, represented, and composing flags are retained.

## Implementation and persistence

- Ticker resolution uses `NoavaranEligibleCompanies` and its existing `TseSymbol` → `ExternalCompanyId` mapping.
- The new provider method calls only the selected category endpoint.
- `FinancialDataSyncProcessor.ProcessPayloadAsync` records the operation and invokes the registered normalizer.
- Statement payloads use `NadpcoApiFinancialStatementNormalizer`, which upserts financial statement headers, variants, source-item catalog entries, and line items.
- Ratio payloads use `NadpcoApiFundamentalIndexNormalizer`, which upserts the existing governed derived metrics.
- No database schema or migration changes were needed.
- Idempotency keys include company, category, fiscal year, period, and payload checksum. Identical responses return `AlreadyExists`; changed upstream payloads re-enter the existing normalizers and their natural-key upserts.
- Empty normalizer outcomes return `Empty`; persistence errors return `PersistenceFailed`; provider failures return HTTP 502 with `UpstreamFailed`.

## Files created or modified

- Created `specs/142-manual-noavaran-financial-statement-backfill/{spec.md,design.md,tasks.md,ImplementationReport.md}`.
- Updated `specs/README.md`.
- Created `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ManualNoavaranFinancialStatementBackfillContracts.cs`.
- Created `src/backend/FinancialCopilot.API/Controllers/NoavaranFinancialStatementBackfillController.cs`.
- Created `src/backend/FinancialCopilot.API/Contracts/FinancialStatementInventoryContracts.cs`.
- Updated `src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs`.
- Created `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/ManualNoavaranFinancialStatementBackfillService.cs`.
- Updated `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs` and `ServiceCollectionExtensions.cs`.
- Updated `src/frontend/src/components/admin/get-company-id-page.tsx`.
- Created `tests/FinancialCopilot.IntegrationTests/FinancialStatementInventoryTests.cs`.
- Updated `tests/FinancialCopilot.UnitTests/NadpcoApiProviderTests.cs` and created `tests/FinancialCopilot.IntegrationTests/NoavaranFinancialStatementBackfillEndpointTests.cs`.

## Tasks

**13 / 13 completed.** Frontend test automation is not configured in this project; the frontend production build was used for its verification.

## Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| AC-01 Existing monthly backfill | Verified | Existing single-company monthly operation integration test passed. |
| AC-02 Four Persian inputs | Verified | Added to the existing RTL page; frontend production build passed. |
| AC-03 Period validation | Verified | Provider tests cover 3/6/9/12; endpoint tests reject invalid values. |
| AC-04 Four statement categories | Verified | Frontend options and backend enum include all requested categories. |
| AC-05 Correct routing | Verified | Mocked provider tests assert all four distinct endpoint paths. |
| AC-06 Year and period mapping | Verified | Mocked tests assert exact `fromYear`, `toYear`, and `perTId`. |
| AC-07 Persistence | Verified by pipeline reuse | Request runs through `FinancialDataSyncProcessor` and existing normalizers; 21 current statement/fundamental-index normalizer tests passed. No live-provider persistence run was performed. |
| AC-08 Idempotency | Verified | Manual service test confirms stable idempotency and `AlreadyExists`; normalizer tests cover upsert behavior. |
| AC-09 Empty response | Verified by flow | Empty normalized outcomes return `Empty`, not success; mocked upstream returns empty arrays in provider tests. |
| AC-10 Distinguishable errors | Verified by implementation and validation tests | Validation 400, missing company 404, upstream 502, persistence 500, `Empty`, and `AlreadyExists` outcomes are explicit. |
| AC-11 Authorization | Verified | Five new endpoint authorization/input tests passed. |
| AC-12 Regression safety | Verified by scope and focused regression | Existing monthly direct endpoint regression passed; no scheduled or AI orchestration code was changed. |

## Build and test results

- `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore` — passed before the final idempotency refinement; focused tests after that refinement rebuilt the affected backend and tests successfully.
- `npm run build` — passed. Vite emitted its existing chunk-size and unused-import warnings.
- Focused provider dispatch/period tests — **8 passed**.
- Existing Noavaran statement/fundamental-index normalizer tests — **21 passed**.
- Manual service repeated-payload idempotency test — **1 passed**.
- New authorization and invalid-scope integration tests — **5 passed**.
- Existing monthly single-company integration regression — **1 passed**.
- Full `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore --verbosity quiet` — **not clean**: Architecture 12/12 passed; Unit 2,081 passed and 18 failed; Integration 445 passed, 62 failed, 44 skipped. Failures span unrelated Feature 128/141 routing and authorization, semantic catalog, Telegram, market insight, and existing admin-test rate limiting. The new focused tests pass independently; full-suite failures were not caused by this feature's files.

## Remaining operational verification

A live Noavaran request and database import were not run because this change was verified with mocked upstream responses and existing normalizer tests. In deployment, verify one authorized DataAdmin request for each statement category and confirm the returned `ProcessedRecords` and persisted rows. The existing full-suite failures should be triaged separately.

## Database Financial Statement Inventory

### 1. Feature path

`specs/142-manual-noavaran-financial-statement-backfill/` (same Feature 142; no additional feature number).

### 2. Inventory API endpoint

`GET /api/v1/admin/noavaran-current/financial-statements/inventory?ticker={ticker}`. It is protected by the existing `NoavaranMonthlyBackfill` policy and authenticated-actor rate limit.

### 3. Database tables and mappings inspected

- `NoavaranEligibleCompanies` view and `Companies` mapping for ticker / external company ID resolution.
- `FinancialStatements` and its EF configuration/migrations for report identity, provider, type, period, evidence, and variant flags.
- `FinancialStatementLineItems`, `FinancialStatementSourceItems`, and metric mappings to confirm that they store statement values/catalog rows rather than separate reports.
- `DerivedMetrics` and its normalizer/source-evidence mapping for Noavaran fundamental indicators.
- No migration was added.

### 4. Tables actually queried

The endpoint queries `NoavaranEligibleCompanies` to resolve ticker, then `FinancialStatements` and `DerivedMetrics`, all with `AsNoTracking()`. It does not query/load line items, call Noavaran, trigger ingestion, or write data.

### 5. Report identity and deduplication

Each `FinancialStatements` row becomes one result, identified by stable `Id` and provider `ExternalStatementId`; different statement types and separate variant rows are preserved. Line items are not joined, so they cannot multiply report rows. `DerivedMetrics` rows are grouped by retained Noavaran `ComBSID`, persisted period end/type, fiscal-year evidence, and audited/represented/composing evidence; metric rows from one report yield one indicator row. Rows without retained source report identity are excluded rather than incorrectly merged. Indicator `ReportId` is null because this persistence model has no report row ID.

### 6. Metadata mappings

- Ticker/external ID: `NoavaranEligibleCompanies.TseSymbol` / `.ExternalCompanyId`.
- Statement category: `FinancialStatements.StatementType`; indicator category comes from the Noavaran source evidence on `DerivedMetrics`.
- Fiscal year ending: `JalaliFiscalYearEnd` retained in statement `WarningsJson` or indicator `SourceEvidenceJson`.
- Actual period end: persisted `DateOnly PeriodEnd`, converted to `yyyy/MM/dd` with `PersianCalendar`.
- Duration: `PeriodType` mapped to 3, 6, 9, or 12 months.
- Audited/restated/consolidated: source `IsAudited` / `IsRepresented` / `IsComposing`, with the latter mirrored in statement variant columns.

### 7. Unsupported or unknown metadata

Noavaran statement and indicator evidence can supply the requested dates and flags. When evidence is absent or malformed, fiscal year and status flags return null and the UI shows `نامشخص`. This matters for legacy statement rows because the migration that added the first-class variant booleans supplied `false` defaults; those defaults alone cannot establish that the source status was false. An indicator report has no persisted report-row GUID, so it returns a null `ReportId` and retained `ComBSID` as its version key. Indicator metric rows with no retained source report ID are omitted rather than merged under an ambiguous identity.

### 8. Frontend changes

Added a database inventory section to `/getCompanyId`, reusing/synchronizing the existing ticker inputs. It shows loading/error/empty states, a row count, all eight required columns, Persian labels and boolean values, RTL direction, and horizontal scrolling. Persisted and idempotent successful manual backfill results refresh a visible inventory for the same ticker; empty and failed results do not.

### 9. Tasks completed

Repository/schema discovery, specification/design/task updates, typed API response, read-only query, four-category aggregation, UI, same-ticker refresh, and inventory tests are complete.

### 10. Acceptance criteria verified

AC-13 through AC-24 are implemented. Inventory query tests verify all four categories, Jalali period end, fiscal-year separation, 3/6/9/12 month mappings, audited/restated/consolidated and unknown states, distinct versions, multiple line items per report, indicator aggregation, ticker isolation by resolved ID, empty and unknown ticker behavior, DataAdmin authorization, and unchanged persistence during inventory reads. Existing monthly and manual-backfill regressions remain in focused tests.

### 11. Tests and build results

- API project Release build — passed, 0 warnings / 0 errors.
- Frontend `npm run build` — passed; Vite emitted existing bundle-size and external unused-import warnings.
- Inventory and manual-backfill endpoint focused integration tests — **9 passed**.
- Existing single-company monthly backfill regression — **1 passed**.
- Provider/backfill/normalizer focused unit tests — passed (62 tests).
- The prior full-solution test run is recorded above; its unrelated failures were not rerun for this incremental inventory change.

### 12. Remaining blockers

No implementation blocker remains. Live provider/database verification was not performed; tests use the in-memory database and do not contact Noavaran. Codebase-memory MCP was unavailable during this extension (`Transport closed`), so discovery used narrowly targeted repository reads and searches after that failure.

## Server-side Inventory Pagination Extension

### Endpoint contract

`GET /api/v1/admin/noavaran-current/financial-statements/inventory?ticker={ticker}&page={page}`. `ticker` remains required; `page` is a 1-based integer defaulting to 1. Page sizes are fixed server-side at 15. Response fields are `ticker`, `items`, `page`, `pageSize`, `totalCount`, and `totalPages`. Non-positive pages return 400. Positive out-of-range pages return empty items with accurate totals; zero reports returns `totalPages: 0`.

### Query strategy and count

For relational deployments, PostgreSQL builds one logical inventory in a CTE. Statement rows come directly from `FinancialStatements`; indicator source evidence is expanded from `DerivedMetrics` and deduplicated by `ComBSID`, period, fiscal-year evidence, and variant flags before entering the combined set. The resolved external company ID filters both sources. A `COUNT(*)` over this full deduplicated set produces `totalCount`; the page query applies stable ordering by fiscal year descending, period end descending, statement type, version/report identity, then flags, followed by `LIMIT 15 OFFSET (page - 1) * 15`. No metric rows or full relational report set are materialized in the API process. The EF InMemory branch only supports existing controller tests because that provider cannot execute the PostgreSQL CTE.

### Frontend behavior

The inventory request includes the requested page. The UI displays total matching reports, current page/total pages, and RTL Previous/Next controls; boundaries are disabled. Editing a ticker invalidates in-flight responses and resets inventory state/page to 1. Requests have a monotonically increasing ID, so an older response cannot replace a newer page or ticker. A successful same-ticker manual backfill refreshes page 1.

### Verification

- API build to project-local `obj/BuildCheck` — passed, 0 warnings / 0 errors. The normal `bin` output was locked by the already-running API process, so verification used a separate output folder without interrupting it.
- Frontend `npm run build` — passed. Existing chunk-size and external unused-import warnings remain.
- Inventory and manual backfill endpoint tests — **11 passed**, including 15/16-row paging, subsequent-page uniqueness/order, out-of-range metadata, indicator aggregation, distinct versions, invalid page, empty inventory, and authorization.
- Existing monthly single-company backfill regression — **1 passed**.
- A broader `AdminDataOperationsEndpointTests` run was attempted; 6 of 77 tests failed because unrelated endpoints returned empty bodies and one request hit the existing rate limit. The targeted monthly regression and pagination/backfill endpoint tests passed independently.
- PostgreSQL execution of the CTE could not be run because Docker is unavailable. The relational query compiles; deployment remains the place to confirm it against the configured PostgreSQL version and persisted evidence data.

### Pagination task completion

All pagination-specific tasks and acceptance criteria (AC-25 through AC-30) are implemented. No new feature directory or database migration was added.
