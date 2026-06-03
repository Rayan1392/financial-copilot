# NADPCO API Provider

The `NadpcoApi` provider adds the Noavaran Amin HTTP APIs under the existing
financial-data provider abstraction. It coexists with `CyclicalWaves`, `CodalDb`, and the
configured HTTP provider and is selected by `DataSyncRequest.ProviderName = "NadpcoApi"`.

## Configuration

Use secrets or environment variables for credentials. Do not commit a live username, password, or
token.

```powershell
$env:NadpcoApi__BaseAddress = "https://data3.nadpco.com/"
$env:NadpcoApi__UserName = "<vendor-user>"
$env:NadpcoApi__Password = "<vendor-password>"
$env:NadpcoApi__TimeoutSeconds = "30"
$env:NadpcoApi__RetryCount = "2"
$env:NadpcoApi__CircuitBreakSeconds = "60"
$env:NadpcoApi__CircuitFailureThreshold = "5"
$env:NadpcoApi__BatchSize = "100"
$env:NadpcoApi__MaxReadParallelism = "4"
$env:NadpcoApi__StatementFromYear = "1400"
$env:NadpcoApi__StatementToYear = ""
$env:NadpcoApi__StatementPeriodTypeId = ""
$env:NadpcoApi__StatementIsAudited = ""
$env:NadpcoApi__StatementIsRepresented = ""
$env:NadpcoApi__StatementIsComposing = ""
$env:NadpcoApi__FundamentalIndexFromYear = "1400"
$env:NadpcoApi__FundamentalIndexToYear = ""
$env:NadpcoApi__FundamentalIndexPeriodTypeId = ""
$env:NadpcoApi__FundamentalIndexIsAudited = ""
$env:NadpcoApi__FundamentalIndexIsRepresented = ""
$env:NadpcoApi__FundamentalIndexIsComposing = ""
$env:NadpcoApi__MonthlyActivityFromDate = "1400/01/01"
$env:NadpcoApi__MonthlyActivityToDate = ""
$env:NadpcoApi__MonthlyActivityOutputType = ""
$env:NadpcoApi__OrchestrationOverlapDays = "7"
```

## Authentication Contract

The provider obtains a token with:

```http
POST /api/v2/Token
Authorization: Basic base64(username:password)
```

The implementation accepts common successful token shapes:

- `access_token` or `accessToken`
- `token`
- optional `expires_in` or `expiresIn`
- optional `expiresAt` or `expiration`

If the response does not include an explicit expiry, the provider uses
`NadpcoApi:DefaultTokenLifetimeMinutes` and refreshes early. The exact successful response contract
and lifetime still need a vendor document or controlled live smoke test before scheduled NADPCO
reads are enabled.

Data requests receive `Authorization: Bearer <token>`. A single `401` invalidates the cache,
refreshes the token, and retries once.

## Endpoints

The foundation client captures raw JSON for:

- `GET /api/v3/BaseInfo/Companies`
- `POST /api/v2/FS/BalanceSheet/Values`
- `POST /api/v2/FS/IncomeStatement/Values`
- `POST /api/v2/FS/CashFlow/Values`
- `POST /api/v2/CompanyFundamentalIndex/Values`
- `POST /api/v2/MonthlyActivity/ProductSales`
- `POST /api/v3/MonthlyActivity/ServiceSales`

Raw payloads are stored through `ProviderRawPayloads` with `ProviderName = "NadpcoApi"` and the
existing `(ProviderName, Checksum)` idempotency rule. Normalization of these payloads is handled by
the later NADPCO sync stories.

## Financial Statement Normalization

`NadpcoApi` financial-statement sync posts bounded requests to the balance-sheet,
income-statement, and cash-flow endpoints. Each request includes the current company batch and a
reviewed item allowlist; the client does not request unrestricted statement history. Optional
configuration filters (`StatementFromYear`, `StatementToYear`, `StatementPeriodTypeId`,
`StatementIsAudited`, `StatementIsRepresented`, `StatementIsComposing`) are appended as query
parameters when set.

Reviewed item mappings:

| Statement | NADPCO item IDs | Metric codes |
|---|---|---|
| Income statement | `15`, `300`, `143`, `140`, `139`, `160`, `168`, `12`, `336` | `REVENUE`, `TOTAL_REVENUE`, `NET_PROFIT`, `OPERATING_PROFIT`, `GROSS_PROFIT`, `EPS`, `EPS_CONSOLIDATED`, `FINANCE_COSTS`, `INCOME_TAX` |
| Balance sheet | `147`, `188` | `TOTAL_EQUITY`, `CAPITAL` |
| Cash flow | `1` | `OPERATING_CASH_FLOW` |

The normalizer writes one `FinancialStatements` row per selected source statement using the
corrected `StatementType` values (`IncomeStatement`, `BalanceSheet`, `CashFlow`). Source Gregorian
dates define `PeriodStart`/`PeriodEnd`; Jalali dates, announcement date, source symbol/title,
audited/represented/composing flags, source statement id, item unit, and the assumed
`MillionRials` scale are retained in evidence JSON.

Canonical variant selection is deterministic per provider, statement type, company, period type,
and period end: audited variants win first, non-represented variants are preferred next,
composing variants are preferred by default, then latest announcement date and highest statement id
break ties. Upserts are idempotent on `(ProviderName, ExternalStatementId, StatementType)` and line
items are idempotent on `(FinancialStatementId, MetricCode)`. Successful normalization publishes
derived-metric recalculation requests for the affected company/source metrics.

## Fundamental Index Normalization

`POST /api/v2/CompanyFundamentalIndex/Values` is captured as
`ProviderDataset.FundamentalIndexes` for `ProviderName = "NadpcoApi"`. The request body contains
bounded `companyIds` and a curated `companyIndexIds` allowlist. Optional configuration filters
(`FundamentalIndexFromYear`, `FundamentalIndexToYear`, `FundamentalIndexPeriodTypeId`,
`FundamentalIndexIsAudited`, `FundamentalIndexIsRepresented`, `FundamentalIndexIsComposing`) are
added as query parameters when set.

Curated active mappings:

| NADPCO index id | Metric code | Unit | Scale status |
|---:|---|---|---|
| `65` | `CURRENT_RATIO` | `Ratio` | sample verified as ratio-scale |
| `4069` | `NET_WORKING_CAPITAL` | `Amount` | source amount persisted as-is; vendor unit retained as evidence |
| `4071` | `CURRENT_ASSETS_TO_TOTAL_ASSETS` | `Ratio` | sample verified as ratio-scale |
| `4100` | `ASSET_TURNOVER` | `Ratio` | sample verified as ratio-scale |
| `4101` | `TANGIBLE_FIXED_ASSETS_TURNOVER` | `Ratio` | sample verified as ratio-scale |
| `4106` | `AVERAGE_COLLECTION_PERIOD` | `Days` | duration-style metric; vendor unit retained as evidence |
| `4117` | `DEBT_TO_EQUITY` | `Ratio` | sample verified as ratio-scale |
| `41105` | `COMPREHENSIVE_LIQUIDITY_INDEX` | `Ratio` | sample verified as ratio-scale |

The normalizer writes `DerivedMetricRow` observations with
`CalculationPolicyVersion = "nadpco-api-fundamental-index-source-v1"`. These rows are
vendor-precomputed source observations; they do not call `IFinancialMetricCalculator` and do not
overwrite engine-calculated metrics because the policy version is distinct. Source evidence retains
the vendor index id/title/group id/group title/unit, statement header id, company id/title,
Jalali dates, variant flags, and the Gregorian period produced from the Jalali fiscal/period dates
using .NET `PersianCalendar`.

Canonical variant selection is deterministic per company, period type, Jalali period end, and
index id: audited variants win first, non-represented variants are preferred next, composing
variants are preferred by default, then latest announcement string and highest `comBS_ID` break
ties.

Percentage-like vendor indexes, including ROE/ROA/margins/growth-style values, remain deferred
until sampled NADPCO values prove whether the API stores percent-scale or fraction-scale values.
They must be added to `NadpcoApiFundamentalIndexMap` only after that review.

## Monthly Activity Normalization

`NadpcoApi` monthly sync posts bounded company/date requests to both
`/api/v2/MonthlyActivity/ProductSales` and `/api/v3/MonthlyActivity/ServiceSales`. The request
body contains a numeric `companyIds` list plus optional Jalali date bounds
(`MonthlyActivityFromDate`, `MonthlyActivityToDate`) and optional `MonthlyActivityOutputType`.

The normalizer writes product and service activity to the existing `MonthlyReports` and
`MonthlyReportLineItems` tables with `ProviderName = "NadpcoApi"`; no schema migration is required
for the current contract. Product rows map production quantity, sales quantity, and sales value.
Service rows map sales quantity and sales value with `ProductionQuantity = null`. Zero-activity
periods are retained.

Jalali `(year, month)` values are converted to Gregorian period windows through the shared
`JalaliDateResolver`, which uses .NET `PersianCalendar`. Product/service title, unit, rate, output
type/title, category, publication dates, industry context, instrument code, and any natural-key
fallback notes are preserved in `WarningsJson` evidence. When the vendor omits a product/service
id, the line-item `ProductCode` is a deterministic natural key derived from source fields; it is
explicitly marked in evidence as not being a fabricated vendor id.

Successful ingestion publishes the existing `MonthlyProductionSales` recalculation request, so
`MONTHLY_SALES`, `MONTHLY_SALES_GROWTH_YOY`, `MONTHLY_SALES_GROWTH_MOM`, and `TTM_SALES` continue
to use the deterministic metric engine without query-time remote calls.

## Orchestration And Operations

DataAdmin users can run bounded NADPCO refreshes through:

- `POST /api/v1/admin/nadpcoapi/full-sync`
- `POST /api/v1/admin/nadpcoapi/incremental-sync`
- `GET /api/v1/admin/nadpcoapi/sync-state`
- `GET /api/v1/admin/provider-health`

The orchestrator is provider-specific, but it only publishes normal `DataSyncRequest`s with
`ProviderName = "NadpcoApi"`. Raw payload storage, normalization, derived-metric recalculation,
scanner-cache invalidation, and sync-run telemetry remain in the existing provider-neutral data
sync processor.

Activation order:

1. Configure credentials through secrets or environment variables.
2. Apply the `FinancialIngestionDbContext` migrations, including `NadpcoApiSyncStates`.
3. Verify `GET /api/v1/admin/provider-health`.
4. Run `POST /api/v1/admin/data-sync/symbols` with `providerName = "NadpcoApi"` or
   `POST /api/v1/admin/nadpcoapi/full-sync` to refresh the company catalog.
5. After companies exist locally, run full sync again to enqueue bounded per-company statements,
   fundamental indexes, and monthly activity.
6. Enable a scheduler to call incremental sync only after a successful full backfill.

NADPCO currently does not expose a reliable modified-since cursor for the covered endpoints.
Incremental orchestration therefore records `LastSuccessfulSyncAt` and `LastOverlapFrom`, then
re-enqueues bounded company-scoped requests over the configured overlap window
(`NadpcoApi:OrchestrationOverlapDays`, default `7`). The actual historical range sent to remote
statement/index/monthly endpoints remains controlled by the provider options (`StatementFromYear`,
`FundamentalIndexFromYear`, `MonthlyActivityFromDate`, etc.) so the service never sends an
unbounded history request.

Failure recovery:

- Per-company enqueue failures are isolated and reported in the sync response.
- Progress advances only when all requested company batches are enqueued successfully.
- Failed batches can be retried with `POST /api/v1/admin/data-sync/financial-statements`,
  `.../fundamental-indexes`, or `.../monthly-reports` using the failed company id as
  `externalReference` and `providerName = "NadpcoApi"` where the endpoint does not already force it.
- Provider credentials are never logged; diagnostics contain dataset/company ids and bounded error
  messages only.

## Company Catalog Normalization

`GET /api/v3/BaseInfo/Companies` normalizes into provider-scoped `Companies`, `Symbols`,
`Industries`, `IndustryGroups`, and `Markets` rows with `ProviderName = "NadpcoApi"`.

Supported mappings:

- `coID` -> `ExternalCompanyId` / `ExternalSymbolId`
- `coTitle`, `coTitleEnglish` -> company names
- `coSymbol`, `coSymbolEnglish` -> symbol metadata
- `tseCode` -> `InstrumentCode`
- `tseCIsinCode`, `tseSIsinCode` -> company/share ISINs
- `industryID/title`, `floorID/title`, `marketID/title` -> provider-scoped dimensions

Canonical `SymbolCode` resolution for NADPCO follows the story-specific priority:
`tseCode` first, then ISIN, then exchange/vendor symbol fallback. This differs from the CodalDB
default resolver mode, which remains ISIN-first for CyclicalWaves alignment.

Fields such as listing dates, IPO date, registration data, fund type, precedence-right state,
exchange state, national id, Pinglish symbol, and market board currently have no normalized columns.
They remain available in the raw payload audit record and are logged as deferred catalog attributes.
