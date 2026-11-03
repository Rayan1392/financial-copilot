# Manual Noavaran Financial Statement Backfill

## Problem and objective

DataAdmin operators can manually retrieve a company's monthly activity, but cannot request a specific Noavaran financial statement category for one fiscal year and reporting period. Add that operation to `/getCompanyId` and persist its response through the existing provider normalizers.

## User story

As a DataAdmin, I can select a company ticker, Jalali fiscal year, period, and statement category, then retrieve and persist that report and see the actual operation outcome.

## Functional requirements

- Keep the current monthly production and sales form and endpoint unchanged.
- Add a distinct RTL financial-statement form with ticker, fiscal year ending, period, and statement category.
- Accept fiscal years 1380–1500, and periods exactly 3, 6, 9, or 12.
- Resolve the selected ticker through the Noavaran company catalog to its external company ID.
- Retrieve only the selected category for the requested company/year/period using existing Noavaran current-API contracts.
- Normalize and persist through the existing provider normalization path; report empty data separately from successful persistence.
- Preserve source identity and existing statement variants/idempotency.
- Restrict the operation to DataAdmin and distinguish validation, missing company, unsupported type, upstream, empty, and persistence outcomes.

## Supported statement types

- Balance Sheet (`BalanceSheet`)
- Income Statement (`IncomeStatement`)
- Cash Flow Statement (`CashFlow`)
- Financial Ratios / Indicators (`FundamentalIndexes`, sourced from CompanyFundamentalIndex)

These names and endpoint families are evidenced in `NadpcoApiDataProviderClient`, its request tests, and the corresponding normalizers. The provider is configured under `NadpcoApi`; Noavaran is the operator-facing source label used by its company catalog and specs.

## Request parameters and validation

Request body: `symbol` (required), `fiscalYearEnding` (Jalali integer 1400–1500), `periodMonths` (3, 6, 9, or 12), and `statementType` (one of the four supported enum values). Reject unknown symbols and values before provider calls. Map fiscal year to the verified `fromYear` and `toYear` query parameters, and months to `perTId`, preserving configured audited / represented / composing filters.

## Expected behavior and errors

A non-empty upstream response must normalize and persist before returning success. Empty responses return an explicit empty outcome. Invalid input, unknown company, upstream HTTP/provider failures, and persistence errors remain distinguishable. The processor idempotency key includes the provider response checksum and request scope: identical responses are reported as already persisted, while changed upstream content is re-normalized through existing source identities and upsert keys.

## Acceptance criteria

1. Monthly activity backfill remains unchanged.
2. The page exposes all four Persian-labeled inputs and all four statement categories.
3. Only accepted years, periods, and category values reach acquisition.
4. Each category routes to its verified provider endpoint and exact year/period filter.
5. Data is normalized and persisted before success is returned.
6. Empty response is not reported as imported data.
7. Repeated requests do not create duplicate records.
8. Non-DataAdmin access is denied; failures are distinguishable in the UI/API.

## Out of scope

- Changing scheduled sync, provider authentication, statement variant policy, database schemas, migrations, AI routing, or existing monthly backfill behavior.
- Adding new vendor endpoints or request dimensions not present in repository contracts.

## Database Financial Statement Inventory

Add a DataAdmin-only, read-only database inventory on `/getCompanyId` at `GET /api/v1/admin/noavaran-current/financial-statements/inventory?ticker={ticker}`. It resolves the ticker through `NoavaranEligibleCompanies`, then reads only persisted records. It must not call a provider or write to the database.

The table has one row per distinct persisted report/version and displays ticker, statement category, Jalali fiscal year ending, Jalali reporting period end, duration months, audited, restated, and consolidated. Missing/invalid metadata is `نامشخص`; null must never become false. The inventory refreshes after manual backfill outcomes `Persisted` and `AlreadyExists` only when the displayed inventory is for that ticker.

### Authoritative persisted mappings

| Display value | Source table and fields | Rule |
|---|---|---|
| Company/ticker | `NoavaranEligibleCompanies.TseSymbol`, `.ExternalCompanyId` | Resolve ticker first; filter all report rows by the resolved external company ID. |
| Balance sheet, income statement, cash flow | `FinancialStatements.StatementType` | One row per `FinancialStatements.Id`; the database row is already one report, independent of its line items. |
| Report/version identity | `FinancialStatements.Id`, `ExternalStatementId`, `IsAudited`, `IsRepresented`, `IsComposing` | Preserve each persisted row and its source identity/variant. |
| Fiscal year ending | `FinancialStatements.WarningsJson[].JalaliFiscalYearEnd` | Parse the retained Noavaran Jalali year; absent or invalid evidence is unknown. |
| Reporting period end | `FinancialStatements.PeriodEnd` (`DateOnly`) | Convert the authoritative Gregorian date to Jalali with `PersianCalendar`; do not calculate from fiscal year. |
| Duration months | `FinancialStatements.PeriodType` | Map the four normalized periods (3/6/9/12 months); unknown enum values remain unknown. |
| Audited/restated/consolidated | `FinancialStatements.WarningsJson[].IsAudited`, `.IsRepresented`, `.IsComposing`; mirrored in first-class variant columns | Map respectively to audited, restated, and consolidated. Return unknown when source evidence lacks a flag: the variant columns were added with a `false` migration default, which cannot prove old rows were actually false. |
| Financial indicators | `DerivedMetrics.SourceEvidenceJson`, `PeriodEnd`, `PeriodType` | Group metric rows only when retained source report ID, period end, and all three variant flags agree. `DerivedMetrics` has no report row ID; do not invent one. |

`FinancialStatementLineItems` and the financial-statement source catalog are inspected for persistence structure but are not queried by the inventory. They contain values/catalog metadata, not report rows. `DerivedMetrics` stores one row per metric and is used only for the Noavaran fundamental-index category. Historical rows with missing/malformed source evidence or missing source report ID are excluded from indicator grouping rather than being merged or assigned fabricated report metadata.

The inventory response includes stable `ReportId` and `VersionId` for statement rows. Indicator rows have a null `ReportId` and use retained source `ComBSID` as `VersionId`; multiple metric values from the same report are aggregated into one display row.

### Inventory acceptance criteria

13. `/getCompanyId` provides a database-only inventory action with loading, error, and empty states.
14. Inventory uses persisted data only and requires the existing DataAdmin policy.
15. All four categories are discoverable from their actual persistence paths.
16. All eight requested columns are present with Persian labels and RTL layout.
17. Period-end dates use Jalali conversion from persisted dates; fiscal year and period end remain separate.
18. Statuses reflect persisted flags and preserve unknown values.
19. Unknown values render as `نامشخص`.
20. Line items do not appear as separate reports.
21. Distinct statement IDs and persisted variants remain separate.
22. Empty inventory and failed queries have explicit UI states.
23. Successful persisted/idempotent manual backfill refreshes an already displayed matching inventory.
24. Monthly backfill and manual statement ingestion continue to work.

### Inventory pagination acceptance criteria

25. Inventory supports a 1-based `page` query parameter (default 1) and always returns `pageSize: 15`.
26. `items` contains at most 15 rows; the response also returns `totalCount` and `totalPages` across statements and deduplicated indicators.
27. Filtering by the resolved external company ID, report-level indicator aggregation, deterministic ordering, count, and page selection execute in PostgreSQL before rows are returned.
28. A non-positive page is rejected; an out-of-range positive page returns no items with unchanged accurate totals; an empty ticker inventory returns zero pages.
29. The RTL table shows matching-report total and page navigation, disables unavailable directions, and resets to page 1 when ticker selection changes or successful same-ticker ingestion refreshes.
30. Concurrent inventory requests cannot replace a newer ticker/page result with an older response.
