# CyclicalWaves Financial Metrics Integration Audit

**Date:** 2026-06-15  
**Scope:** Verify storage, AI retrieval, and screening coverage for every field returned by the CyclicalWaves per-ticker API.  
**Constraint:** Audit only — no redesign, no new entities unless a gap demands it.

---

## API Payload Reference

Endpoint: `GET /api/custom-filtering/ticker/{ticker}`

The `CyclicalWavesTickerDetailResponse` model contains 29 fields (excluding `id`, `ticker`, `enticker` which are used for linkage only):

| # | API Field | Type |
|---|---|---|
| 1 | `last_quarter_sale` | decimal? |
| 2 | `penultimate_quarter_sale` | decimal? |
| 3 | `last_year_same_quarter_sale` | decimal? |
| 4 | `average_4_quarter_sale` | decimal? |
| 5 | `last_quarter_net_profit` | decimal? |
| 6 | `penultimate_quarter_net_profit` | decimal? |
| 7 | `last_year_same_quarter_net_profit` | decimal? |
| 8 | `last_quarter_gross_profit` | decimal? |
| 9 | `penultimate_quarter_gross_profit` | decimal? |
| 10 | `last_year_same_quarter_gross_profit` | decimal? |
| 11 | `last_quarter_operating_profit` | decimal? |
| 12 | `penultimate_quarter_operating_profit` | decimal? |
| 13 | `last_year_same_quarter_operating_profit` | decimal? |
| 14 | `last_quarter_net_profit_margin` | decimal? |
| 15 | `penultimate_quarter_net_profit_margin` | decimal? |
| 16 | `last_year_same_quarter_net_profit_margin` | decimal? |
| 17 | `last_quarter_gross_profit_margin` | decimal? |
| 18 | `penultimate_quarter_gross_profit_margin` | decimal? |
| 19 | `last_year_same_quarter_gross_profit_margin` | decimal? |
| 20 | `last_quarter_operating_profit_margin` | decimal? |
| 21 | `penultimate_quarter_operating_profit_margin` | decimal? |
| 22 | `last_year_same_quarter_operating_profit_margin` | decimal? |
| 23 | `average_12_month_sale` | decimal? |
| 24 | `last_month_sale` | decimal? |
| 25 | `penultimate_month_sale` | decimal? |
| 26 | `last_year_same_month_sale` | decimal? |
| 27 | `last_month_sale_date` | string? |
| 28 | `last_quarter_date` | string? |
| 29 | `pe` | decimal? |
| 30 | `ps` | decimal? |

---

## Phase 1 – Storage Verification

### Normalizer Classes

**`CyclicalWavesFinancialStatementNormalizer`**  
Path: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesFinancialStatementNormalizer.cs`  
Produces: `NormalizedFinancialStatementRow` + `NormalizedFinancialStatementLineItemRow`  
Three periods per ticker: Q0 (latest quarter), Q1 (penultimate), Q4 (last-year same quarter).

**`CyclicalWavesMonthlyReportNormalizer`**  
Path: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesMonthlyReportNormalizer.cs`  
Produces: `NormalizedMonthlyReportRow` + `NormalizedMonthlyReportLineItemRow`  
Three periods per ticker: M0 (latest month), M1 (penultimate), M12 (last-year same month).  
Only stores `SalesAmount` (`ProductCode = "REVENUE"`).

### Field-by-Field Storage Map

| API Field | DB Table | MetricCode / Column | Stored? |
|---|---|---|---|
| `last_quarter_sale` | `FinancialStatementLineItems` | `REVENUE` (Q0) | ✅ Yes |
| `penultimate_quarter_sale` | `FinancialStatementLineItems` | `REVENUE` (Q1) | ✅ Yes |
| `last_year_same_quarter_sale` | `FinancialStatementLineItems` | `REVENUE` (Q4) | ✅ Yes |
| `average_4_quarter_sale` | — | — | ❌ **Not stored** |
| `last_quarter_net_profit` | `FinancialStatementLineItems` | `NET_PROFIT` (Q0) | ✅ Yes |
| `penultimate_quarter_net_profit` | `FinancialStatementLineItems` | `NET_PROFIT` (Q1) | ✅ Yes |
| `last_year_same_quarter_net_profit` | `FinancialStatementLineItems` | `NET_PROFIT` (Q4) | ✅ Yes |
| `last_quarter_gross_profit` | `FinancialStatementLineItems` | `GROSS_PROFIT` (Q0) | ✅ Yes |
| `penultimate_quarter_gross_profit` | `FinancialStatementLineItems` | `GROSS_PROFIT` (Q1) | ✅ Yes |
| `last_year_same_quarter_gross_profit` | `FinancialStatementLineItems` | `GROSS_PROFIT` (Q4) | ✅ Yes |
| `last_quarter_operating_profit` | `FinancialStatementLineItems` | `OPERATING_PROFIT` (Q0) | ✅ Yes |
| `penultimate_quarter_operating_profit` | `FinancialStatementLineItems` | `OPERATING_PROFIT` (Q1) | ✅ Yes |
| `last_year_same_quarter_operating_profit` | `FinancialStatementLineItems` | `OPERATING_PROFIT` (Q4) | ✅ Yes |
| `last_quarter_net_profit_margin` | `FinancialStatementLineItems` | `NET_PROFIT_MARGIN` (Q0) | ✅ Yes |
| `penultimate_quarter_net_profit_margin` | `FinancialStatementLineItems` | `NET_PROFIT_MARGIN` (Q1) | ✅ Yes |
| `last_year_same_quarter_net_profit_margin` | `FinancialStatementLineItems` | `NET_PROFIT_MARGIN` (Q4) | ✅ Yes |
| `last_quarter_gross_profit_margin` | `FinancialStatementLineItems` | `GROSS_PROFIT_MARGIN` (Q0) | ✅ Yes |
| `penultimate_quarter_gross_profit_margin` | `FinancialStatementLineItems` | `GROSS_PROFIT_MARGIN` (Q1) | ✅ Yes |
| `last_year_same_quarter_gross_profit_margin` | `FinancialStatementLineItems` | `GROSS_PROFIT_MARGIN` (Q4) | ✅ Yes |
| `last_quarter_operating_profit_margin` | `FinancialStatementLineItems` | `OPERATING_PROFIT_MARGIN` (Q0) | ✅ Yes |
| `penultimate_quarter_operating_profit_margin` | `FinancialStatementLineItems` | `OPERATING_PROFIT_MARGIN` (Q1) | ✅ Yes |
| `last_year_same_quarter_operating_profit_margin` | `FinancialStatementLineItems` | `OPERATING_PROFIT_MARGIN` (Q4) | ✅ Yes |
| `average_12_month_sale` | — | — | ❌ **Not stored** |
| `last_month_sale` | `MonthlyReportLineItems` | `SalesAmount` (M0) | ✅ Yes |
| `penultimate_month_sale` | `MonthlyReportLineItems` | `SalesAmount` (M1) | ✅ Yes |
| `last_year_same_month_sale` | `MonthlyReportLineItems` | `SalesAmount` (M12) | ✅ Yes |
| `last_month_sale_date` | `MonthlyReports.VendorPeriodDate` | `DateOnly?` (M0) | ✅ **Fixed (Gap 3)** |
| `last_quarter_date` | `FinancialStatements.VendorPeriodDate` | `DateOnly?` (Q0) | ✅ **Fixed (Gap 3)** |
| `pe` | `FinancialStatementLineItems` | `PE_RATIO` (Q0, internal) | ✅ Yes (pipeline only) |
| `ps` | `FinancialStatementLineItems` | `PS_RATIO` (Q0, internal) | ✅ Yes (pipeline only) |

### Date Field Gap — Detail (Resolved)

`last_month_sale_date` and `last_quarter_date` were previously discarded by both normalizers. Period dates were estimated from the sync request timestamp using `CyclicalWavesRelativePeriodResolver` with an Iranian fiscal-year calendar approximation.

**Resolution (Gap 3):** Added `DateOnly? VendorPeriodDate` to `NormalizedFinancialStatementRow` (Q0) and `NormalizedMonthlyReportRow` (M0). Both normalizers now parse the ISO 8601 date string from the vendor payload and persist it. Migration `20260615000000_AddVendorPeriodDateToStatementAndMonthlyReport` adds the nullable `date` column to both tables.

---

## Phase 2 – AI Retrieval Verification

### Metric Availability Matrix

| Metric | Stored in DB | In Semantic Catalog | Available to AI | Searchable (Lookup) | Filterable (Scanner) |
|---|---|---|---|---|---|
| `pe` → `PE_TTM` | ✅ | ✅ `PE_TTM` | ✅ | ✅ | ✅ |
| `ps` → `PS_TTM` | ✅ | ✅ `PS_TTM` | ✅ | ✅ | ✅ |
| `last_quarter_sale` → `REVENUE` Q0 | ✅ | ✅ `REVENUE` | ✅ | ✅ | ✅ |
| `last_quarter_net_profit` → `NET_PROFIT` Q0 | ✅ | ✅ `NET_PROFIT` | ✅ | ✅ | ✅ |
| `last_quarter_gross_profit` → `GROSS_PROFIT` Q0 | ✅ | ✅ `GROSS_PROFIT` | ✅ | ✅ | ✅ |
| `last_quarter_operating_profit` → `OPERATING_PROFIT` Q0 | ✅ | ✅ `OPERATING_PROFIT` | ✅ | ✅ | ✅ |
| `last_month_sale` → `MONTHLY_SALES` | ✅ | ✅ `MONTHLY_SALES` | ✅ | ✅ | ✅ |
| `last_quarter_net_profit_margin` → `NET_PROFIT_MARGIN` Q0 | ✅ | ✅ **Fixed (Gap 2)** | ✅ | ✅ | ✅ |
| `last_quarter_gross_profit_margin` → `GROSS_PROFIT_MARGIN` Q0 | ✅ | ✅ **Fixed (Gap 1)** | ✅ | ✅ | ✅ |
| `last_quarter_operating_profit_margin` → `OPERATING_PROFIT_MARGIN` Q0 | ✅ | ✅ **Fixed (Gap 1)** | ✅ | ✅ | ✅ |
| `average_4_quarter_sale` | ❌ | ❌ | ❌ | ❌ | ❌ |
| `average_12_month_sale` | ❌ | ❌ | ❌ | ❌ | ❌ |
| `last_month_sale_date` | ✅ `VendorPeriodDate` | N/A (metadata) | N/A | N/A | N/A |
| `last_quarter_date` | ✅ `VendorPeriodDate` | N/A (metadata) | N/A | N/A | N/A |

### How PE_TTM and PS_TTM Reach the AI

`pe` and `ps` from CyclicalWaves → stored as `PE_RATIO` / `PS_RATIO` line items (internal, Q0 only) → `SourceLineItemPassthroughMetricCalculator` promotes them to `DerivedMetrics.PE_TTM` / `DerivedMetrics.PS_TTM` → read by `EfCoreSymbolMetricLookupService` and `EfCoreScannerExecutionService`.

### How Margin Metrics Reach the AI (Post-Fix)

`GROSS_PROFIT_MARGIN`, `OPERATING_PROFIT_MARGIN`, and `NET_PROFIT_MARGIN` are stored in `FinancialStatementLineItems` by `CyclicalWavesFinancialStatementNormalizer`. All three are now registered in `PhaseOneFinancialSemanticCatalog` as `Define()` entries with `FiscalPeriodType.ThreeMonths` and Persian/English aliases. The alias resolver can map user terminology (e.g. "حاشیه سود عملیاتی") to the metric code, and the lookup/scanner services read the values directly from `FinancialStatementLineItems`.

`NET_PROFIT_MARGIN` was previously bound to a CodalDB `DefineRatio()` path (supporting 3/6/9/12-month periods). It has been replaced with a `Define()` entry scoped to `ThreeMonths` only, consistent with the CyclicalWaves line item source.

---

## Phase 3 – Natural Language Coverage Audit

### Single Symbol Questions

| Question | Status | Reason |
|---|---|---|
| `PE غگلپا چقدر است؟` | ✅ **Supported** | `PE_TTM` in catalog → `DerivedMetrics` → lookup service |
| `PS غگلپا چقدر است؟` | ✅ **Supported** | `PS_TTM` in catalog → `DerivedMetrics` → lookup service |
| `فروش فصل آخر غگلپا چقدر بوده؟` | ✅ **Supported** | `REVENUE` (Q0) in catalog → `FinancialStatementLineItems` |
| `سود خالص فصل آخر غگلپا چقدر بوده؟` | ✅ **Supported** | `NET_PROFIT` (Q0) in catalog |
| `حاشیه سود عملیاتی غگلپا چقدر است؟` | ✅ **Supported** | `OPERATING_PROFIT_MARGIN` registered in catalog (Gap 1 fixed) |
| `حاشیه سود ناخالص غگلپا چقدر است؟` | ✅ **Supported** | `GROSS_PROFIT_MARGIN` registered in catalog (Gap 1 fixed) |
| `حاشیه سود خالص غگلپا چقدر است؟` | ✅ **Supported** | `NET_PROFIT_MARGIN` now reads CyclicalWaves line item (Gap 2 fixed) |

### Market-Wide Screening Questions

| Question | Status | Reason |
|---|---|---|
| `تمام سهم‌هایی که P/E کمتر از 5 دارند` | ✅ **Supported** | `PE_TTM` in catalog → `DerivedMetrics` → scanner AND-filter |
| `تمام سهم‌هایی که P/S کمتر از 1 دارند` | ✅ **Supported** | `PS_TTM` in catalog → `DerivedMetrics` → scanner AND-filter |
| `سهم‌هایی که حاشیه سود عملیاتی بالای 15٪ دارند` | ✅ **Supported** | `OPERATING_PROFIT_MARGIN` registered in catalog (Gap 1 fixed) |
| `سهم‌هایی که فروش فصل آخر آنها از فصل قبل بیشتر بوده است` | ⚠️ **Partially Supported** | `REVENUE_GROWTH_QOQ` defined in catalog, but QoQ derivation uses CodalDB cumulative data, not CyclicalWaves discrete quarters |
| `سهم‌هایی که سود خالص فصل آخر آنها بیش از 50٪ رشد کرده است` | ⚠️ **Partially Supported** | `NET_PROFIT_GROWTH_QOQ` defined, same CodalDB-only derivation limitation |

### Multi-Metric Screening Questions

| Question | Status | Reason |
|---|---|---|
| `سهم‌هایی با P/E کمتر از 5 و حاشیه سود عملیاتی بالای 15٪` | ✅ **Supported** | Both conditions now available: `PE_TTM` + `OPERATING_PROFIT_MARGIN` (Gap 1 fixed) |
| `سهم‌هایی با رشد فروش فصلی و رشد سود خالص فصلی` | ⚠️ **Partially Supported** | Both QoQ metrics defined but derived from CodalDB only |
| `سهم‌هایی با P/S کمتر از 1 و حاشیه سود ناخالص بالاتر از 20٪` | ✅ **Supported** | Both conditions now available: `PS_TTM` + `GROSS_PROFIT_MARGIN` (Gap 1 fixed) |

---

## Phase 4 – Gap Report

### Existing Coverage — What Works Today

- **PE_TTM and PS_TTM**: fully functional end-to-end — ingested, derived, searchable, and filterable.
- **Revenue (quarterly)**: all three CyclicalWaves periods (Q0, Q1, Q4) stored and reachable via `REVENUE` metric.
- **Net profit (quarterly)**: Q0, Q1, Q4 stored and reachable via `NET_PROFIT`.
- **Gross profit and operating profit (quarterly)**: Q0, Q1, Q4 stored and reachable via `GROSS_PROFIT` / `OPERATING_PROFIT`.
- **Monthly sales**: M0, M1, M12 stored and reachable via `MONTHLY_SALES`.
- **Net profit margin, gross profit margin, operating profit margin**: Q0, Q1, Q4 stored; all three now registered in catalog and reachable (Gaps 1 & 2 fixed).
- **Vendor period dates**: `last_quarter_date` and `last_month_sale_date` now persisted in `VendorPeriodDate` columns (Gap 3 fixed).
- **QoQ and YoY growth for revenue, net profit**: derivable from stored periods (via CodalDB path; see remaining limitation below).

### Missing Storage — Fields Received but Discarded

| Field | Impact |
|---|---|
| `average_4_quarter_sale` | 4-quarter average revenue unavailable |
| `average_12_month_sale` | 12-month average monthly sale unavailable |

### Remaining Limitations

| Area | Description |
|---|---|
| QoQ growth from CyclicalWaves discrete periods | `REVENUE_GROWTH_QOQ` and `NET_PROFIT_GROWTH_QOQ` are defined in the catalog but derived from CodalDB cumulative periods, not CyclicalWaves Q0/Q1 snapshots. CyclicalWaves provides discrete quarterly snapshots, but the QoQ derivation engine reads CodalDB data. |
| `average_4_quarter_sale` / `average_12_month_sale` | Not stored; add only if confirmed user need emerges. Would require new line item codes (`AVG_4Q_REVENUE`, `AVG_12M_SALE`) with no parent table schema change. |

### Fixed Gaps Summary

| Gap | Description | Fix |
|---|---|---|
| Gap 1 | `GROSS_PROFIT_MARGIN` and `OPERATING_PROFIT_MARGIN` not in semantic catalog | Added `Define()` entries with `ThreeMonths` period type and Persian/English aliases to `PhaseOneFinancialSemanticCatalog` |
| Gap 2 | `NET_PROFIT_MARGIN` catalog entry used CodalDB `DefineRatio()` path | Replaced with `Define()` scoped to `ThreeMonths`, reading CyclicalWaves line item |
| Gap 3 | `last_quarter_date` and `last_month_sale_date` discarded | Added `LastQuarterDate`/`LastMonthSaleDate` to payload model; added `DateOnly? VendorPeriodDate` to both row entities; normalizers now parse and persist the vendor date; migration `20260615000000_AddVendorPeriodDateToStatementAndMonthlyReport` added |

---

## Key Files

| File | Role |
|---|---|
| `src/.../Providers/CyclicalWaves/CyclicalWavesPayloadModels.cs` | JSON payload contract — all 30 fields including date fields |
| `src/.../Ingestion/CyclicalWaves/CyclicalWavesFinancialStatementNormalizer.cs` | Maps quarterly fields → `FinancialStatementLineItems`; populates `VendorPeriodDate` for Q0 |
| `src/.../Ingestion/CyclicalWaves/CyclicalWavesMonthlyReportNormalizer.cs` | Maps monthly sale fields → `MonthlyReportLineItems`; populates `VendorPeriodDate` for M0 |
| `src/.../Ingestion/CyclicalWaves/CyclicalWavesRelativePeriodResolver.cs` | Estimates period dates from request timestamp (still used for Q1/Q4/M1/M12) |
| `src/.../Ingestion/Persistence/FinancialIngestionRows.cs` | Entity definitions; `VendorPeriodDate` added to statement and monthly report rows |
| `src/.../Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs` | Master catalog — now includes all three margin metrics |
| `src/.../Application/Scanner/LlmScannerQueryParser.cs` | LLM scanner parser; metric resolution via alias catalog |
| `src/.../Financial/Scanner/EfCoreScannerExecutionService.cs` | AND-filter execution against `DerivedMetrics` |
| `specs/020-cyclicalwaves-data-provider/database-structure.md` | Authoritative design spec for CyclicalWaves mapping |
