# Design: Manual Noavaran Financial Statement Backfill

## Existing architecture and reuse

The `/getCompanyId` UI is `GetCompanyIdPage` in `src/frontend/src/components/admin/get-company-id-page.tsx`; its monthly form uses the direct single-company-month route. Backend ingestion uses the configured `NadpcoApiDataProviderClient`, `FinancialDataSyncProcessor`, and category normalizers. Statements persist through `NadpcoApiFinancialStatementNormalizer` into normalized financial statement and source-item tables; ratios use the fundamental-index normalizer and existing metric persistence. `NoavaranMonthlyBackfillController` demonstrates ticker resolution and DataAdmin authorization.

## Verified Noavaran current-API contracts

Existing client code posts JSON containing `companyIds` and `items: []` to the statement endpoints. Bounded query parameters are `fromYear`, `toYear`, `perTId`, `isAudited`, `isRepresented`, and `isComposing`. Provider tests verify period type ID 6 for six months and all-items request bodies. Ratios use the existing `CompanyFundamentalIndex/Values` endpoint with `companyIds`, `companyIndexIds`, and corresponding `FundamentalIndex*` query settings. Authentication and serialization stay in the existing client. The targeted operation overrides only the verified year and period filters; variant filters retain provider configuration.

| Statement Type | Existing Noavaran Integration | Parameters | Persistence Path | Reuse Strategy |
|---|---|---|---|---|
| Balance Sheet | `POST api/v2/FS/BalanceSheet/Values` | `companyIds`, `items: []`, `fromYear`, `toYear`, `perTId`, configured variant flags | `NadpcoApiFinancialStatementNormalizer` → normalized statements and source catalog/items | Targeted provider fetch, existing normalizer |
| Income Statement | `POST api/v2/FS/IncomeStatement/Values` | Same statement contract | `NadpcoApiFinancialStatementNormalizer` → normalized statements and source catalog/items | Targeted provider fetch, existing normalizer |
| Cash Flow Statement | `POST api/v2/FS/CashFlow/Values` | Same statement contract | `NadpcoApiFinancialStatementNormalizer` → normalized statements and source catalog/items | Targeted provider fetch, existing normalizer |
| Financial Ratios / Indicators | `POST api/v2/CompanyFundamentalIndex/Values` | `companyIds`, mapped `companyIndexIds`, `fromYear`, `toYear`, `perTId`, configured variant flags | `NadpcoApiFundamentalIndexNormalizer` → existing fundamental-index/metric records | Targeted provider fetch, existing normalizer |

## API and backend changes

Add one DataAdmin-protected operation under the existing Noavaran admin route family. The request uses the ticker and requested year/period/category. Resolve ticker using `NoavaranEligibleCompanies`; reject unresolved external IDs. Validate the category enum and period/year before acquisition. The service calls the existing provider client with targeted filters, verifies a non-empty payload, and invokes the existing category normalizer before returning persisted record counts/status. It does not create a separate data schema or alter scheduler behavior.

## Data flow and duplicate handling

Ticker → Noavaran external company ID → selected provider endpoint → retained raw payload → category normalizer → existing database upserts/unique identities → result DTO. Repeated calls reuse source statement IDs/catalog keys and existing normalizer upserts. A successful HTTP response with no report returns `Empty`; persistence exceptions do not return success.

## Frontend and authorization

Add a separate form on the existing page, RTL labels, exact period/category dropdowns, input validation, request disabling, and outcome-specific states. Existing monthly form keeps its state and endpoint. Authorization follows the existing DataAdmin policy used by admin controllers.

## Logging and observability

Log request correlation, ticker/external ID, category, fiscal year, period, outcome, and persisted count. Do not log credentials or raw financial payloads. Use existing provider client error logging and avoid claiming import success before normalization completes.

## Testing strategy

Mock upstream HTTP for endpoint/body/query verification. Exercise each category, all valid periods, invalid values, company resolution, empty/error response, normalization/persistence, repeat idempotency, and DataAdmin authorization. Run frontend build, focused .NET tests, and existing monthly backfill endpoint regression tests; do not call the live provider.

## Database Financial Statement Inventory

The read path is a GET action on `NoavaranFinancialStatementBackfillController`, under the same DataAdmin authorization and authenticated-actor rate limit as manual backfill. It resolves `ticker` against the read-only `NoavaranEligibleCompanies` view, selects report metadata with `AsNoTracking`, and never injects or calls the provider client or ingestion processor.

For `FinancialStatements`, each row is already a report-level aggregate. `Id` is the stable report identity and `ExternalStatementId` plus the persisted audited/represented/composing flags form the provider version identity. The inventory projects metadata only and does not join/load `FinancialStatementLineItems`.

For financial indicators, the source storage is `DerivedMetrics`, whose row granularity is one metric. The production query expands persisted Noavaran `SourceEvidenceJson` and aggregates by `ComBSID`, stored period end/type, fiscal-year evidence, and the three source variant flags. This keeps metric rows from becoming duplicate reports and preserves versions where source evidence allows it. Because this model has no indicator report-row ID, its `ReportId` is null; `VersionId` is the source `ComBSID`.

The current financial-statement normalizer stores exact Jalali fiscal-year and variant flag metadata in `WarningsJson` evidence and actual period end as a Gregorian `DateOnly`. Convert the latter to Jalali with `PersianCalendar`. Duration comes from `PeriodType`. The audited/represented/composing flags also have first-class columns and participate in variant persistence identity, but the migration added those columns with a `false` default. For legacy rows without source evidence, the stored default cannot distinguish “false” from “unknown”; return null for display as `نامشخص`. No schema changes are needed.

Frontend inventory ticker shares the existing ticker inputs, displays all eight required columns in a horizontally scrollable RTL table, counts rows, and handles loading/error/empty responses. A successful `Persisted` or `AlreadyExists` manual result refreshes the inventory only if that ticker's inventory is already visible; `Empty` and failures do not trigger a refresh.

### Server-side pagination

The endpoint accepts `ticker` and optional 1-based `page` (default 1); page size is a server constant of 15. It returns `items`, `page`, `pageSize`, `totalCount`, and `totalPages` (`0` when no reports exist). Non-positive pages return 400. A valid page beyond the final page returns an empty item list with the same totals.

For relational deployments, one PostgreSQL CTE builds the logical report set from `FinancialStatements` and deduplicated indicator report identities from `DerivedMetrics`. Filtering uses the resolved external company ID. The endpoint runs `COUNT(*)` over this complete set, then applies deterministic ordering (fiscal year descending, period end descending, statement type, version identity, report ID, and flags) followed by `LIMIT 15 OFFSET (page-1)*15`. Indicator de-duplication is inside the CTE, before both count and page selection. The SQL projects evidence metadata without loading metric rows or all reports into application memory. The controller's non-relational branch exists for EF InMemory controller tests only; production relational databases take the CTE query path.

The frontend sends `page` for each request and uses a monotonically increasing request ID so a response from an earlier ticker/page request cannot overwrite the newest state. Ticker edits invalidate pending requests and reset the table to page 1; a successful matching manual backfill reloads page 1. Previous/Next buttons, current page/total pages, and total matching reports are shown in RTL.
