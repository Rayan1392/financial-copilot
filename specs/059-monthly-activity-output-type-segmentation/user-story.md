# Spec 059 — Monthly Activity Output Type Segmentation

## Status
`[ ]` Not yet implemented

## Background

The Noavaran Amin (NADPCO) monthly activity endpoint for product sales accepts an `outputTypeId` query parameter that controls which kind of period aggregation is returned:

| `outputTypeId` | Meaning | Persian label |
|---|---|---|
| 0 | Single-month period (دوره یک‌ماهه) | فروش ماهانه |
| 1 | From fiscal year start to current month (از ابتدای سال مالی تاکنون) | فروش از ابتدای سال مالی |
| 2 | Adjustments (اصلاحات) | اصلاحات |
| 3 | From fiscal year start to previous month, adjusted (از ابتدای سال مالی تا ماه گذشته اصلاح‌شده) | تا ماه گذشته (اصلاح‌شده) |
| 4 | From fiscal year start to previous month (از ابتدای سال مالی تا ماه گذشته) | تا ماه گذشته |

Currently the system only calls `outputTypeId=0` (single-month period) for product sales. All five output types contain materially different numbers: type 0 gives a monthly slice, type 1 gives a cumulative year-to-date figure, and type 4 gives fiscal-year-to-previous-month. When the AI query layer answers "آخرین فروش کگل" ("latest sales of KEGOL"), the current required behavior is a grouped sales view: latest single-month sales (type 0), same reporting month in the previous fiscal year when available (type 0 from the prior year), fiscal-year-to-date sales (type 1), and fiscal-year-to-previous-month sales (type 4).

Without storing all five types separately, the query layer cannot answer:
- Cumulative year-to-date sales without fetching type 1
- Single-month breakdown without type 0
- Adjusted or corrected figures without types 2, 3, 4

## Provider Data Semantics and Unit Policy

Noavaran Amin monthly activity rows are raw product/service line-item facts. They are not
provider-precomputed company totals until the platform aggregates them.

- `productSaleValue` and service sales values are source-unit **million Rials**.
- Each `OutputType` is a separate semantic view and must be stored as a distinct
  `MonthlyReports` row.
- Governed lookup metrics promoted from these rows must aggregate line items during
  ingestion/recalculation and normalize monetary values to the platform canonical monetary unit
  before storing the corresponding `DerivedMetrics` rows.
- Query-time symbol lookup must only read persisted facts. It must not aggregate raw monthly
  line items.
- These Noavaran conversion rules must not be applied to CyclicalWaves data. CyclicalWaves
  sales fields are provider-precomputed company metrics in Rials and are persisted as-is under
  provider-marked passthrough policies.

## Shared Monthly Sales Routing Rule

For direct symbol lookup, `فروش`, `آخرین فروش`, `فروش ماه`, `فروش ماهانه`, `فروش این ماه`,
`فروش YTD`, `متوسط فروش 12 ماهه`, and `متوسط فروش ۱۲ ماهه` are monthly-sales intents. They
resolve to `MONTHLY_SALES` and the monthly-sales snapshot renderer, never generic quarterly
`REVENUE`. `REVENUE` remains valid for explicit revenue, quarterly revenue/sales,
`درآمد فصلی`, and `فروش فصلی`.

## Bounded Context

`FinancialCopilot.Infrastructure` / `FinancialCopilot.Application` — Financial Data Ingestion

## User Stories

### Story A — Fetch and store all 5 output types per company-month

**As a** financial data engineer,  
**I want** the NADPCO monthly activity ingestion to call `outputTypeId` 0–4 for each company-month request  
**so that** all five period-aggregation views of production/sales data are available in the database for downstream query use.

**Acceptance criteria:**

1. For each company-month ingestion request, the system calls `POST api/v2/MonthlyActivity/ProductSales` five times — once per `outputTypeId` value 0 through 4.
2. Each response is normalized and stored separately with the `OutputType` value persisted as a column on `MonthlyReports`.
3. Records from different `outputTypeId` values for the same company-month do **not** overwrite each other; they coexist as separate rows distinguished by `OutputType`.
4. The unique index on `MonthlyReports` (currently `(ProviderName, ExternalReportId)`) continues to enforce row uniqueness; `ExternalReportId` must include the output type when the vendor does not provide a stable activity ID, so separate output types produce separate external IDs.
5. When the vendor returns an empty array for a given output type, no report row is created for that type (not an error).
6. The `ServiceSales` endpoint (`api/v3/MonthlyActivity/ServiceSales`) does not accept `outputTypeId`; it continues to be called once per company-month without that parameter.
7. Backfill and scheduled-sync orchestration remain unchanged in their scope/sequencing logic; they now implicitly fetch all five types because the underlying provider call fetches all five.

### Story B — Expose `OutputType` through the API response layer

**As a** data platform consumer,  
**I want** the admin data sync API to include `OutputType` in monthly report state responses  
**so that** operators can verify which output types are present and diagnose data gaps by type.

**Acceptance criteria:**

1. The `AdminMonthlyActivityBackfillProgressResponse` and any monthly-report-listing endpoints include an `outputTypeCounts` summary (how many stored rows exist per output type for the last sync window).
2. The `GetStockMarketSyncState`-equivalent endpoint for Noavaran monthly activity returns a per-output-type row count in the response.

### Story C — AI query routing by output type

**As a** user querying the AI assistant,  
**I want** the system to automatically select or combine the correct output types based on my intent  
**so that** "آخرین فروش" returns the grouped sales view, while "فروش فروردین ۱۴۰۵" returns the single-month figure (type 0).

**Acceptance criteria:**

1. A `MonthlyActivityOutputTypeResolver` service (Application layer) maps a `MonthlyActivityQueryIntent` (enum: `SingleMonth`, `YearToDate`, `Adjustment`, `YearToDateAdjusted`, `YearToDatePrevious`) to an `outputTypeId` integer.
2. When a metric query is for "latest sales" without explicit month qualification, the response composes the persisted `SingleMonth` (type 0), prior fiscal-year same-month `SingleMonth` (type 0 from the prior year), `YearToDate` (type 1), and `YearToDatePrevious` (type 4) facts when available.
   This composed monthly production/sales response is a monthly activity view, not a market quote
   view: it must omit `LATEST_PRICE` and `DAILY_CHANGE_PCT`.
3. When a metric query is specifically for "year-to-date" / "from the beginning of fiscal year", `YearToDate` (type 1) is selected.
4. When a metric query references a specific Shamsi month explicitly (e.g., "فروردین 1405"), `SingleMonth` (type 0) is selected.
5. The scanner and symbol-lookup metric resolution layer uses the resolver when retrieving `MONTHLY_SALES`, `MONTHLY_SALES_QUANTITY`, `MONTHLY_PRODUCTION_QUANTITY`, `MONTHLY_SALES_RATE` metrics.
6. Story C is a separate milestone and **must not block** Story A or Story B delivery.

## Out of Scope

- Changing the `ServiceSales` endpoint logic (it has no `outputTypeId` parameter).
- Adding new governed metrics specific to output types 2, 3, 4 — those metrics can be added in a future spec once data is confirmed available.
- Frontend UI for output-type selection in the chat interface (handled by the AI query intent layer).

## Data Model Changes Required

1. `NormalizedMonthlyReportRow` — add `int? OutputType` column.
2. `MonthlyReports` table — add `OutputType` column (nullable int).
3. `ExternalReportId` uniqueness — already includes `output-{N}` in the fallback key when no vendor activity ID is present; vendor-assigned activity IDs may differ per output type (the vendor returns different `ActivityID` values per output type for the same company-month). The normalizer must always include output type in the external report ID.
4. EF migration required.

## Dependency on Existing Specs

- Depends on spec 042 (NADPCO monthly activity sync — provider client, normalizer, persistence)
- Depends on spec 057 (freshness, backfill coordinator, scheduled sync)
- Story C depends on spec 057 Phase C (governed metrics for `MONTHLY_SALES`, etc.)

## Priority

**High.** Without output type segmentation, the AI query layer can only return a single sales number, which does not match the current expectation for "latest sales" queries. Type 0, persisted 12-month average sales, type 1, and type 4 are needed for the default latest-sales view; prior fiscal-year same-month type 0 remains required for explicit comparable-period questions.
