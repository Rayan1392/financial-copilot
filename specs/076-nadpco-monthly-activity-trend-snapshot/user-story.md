# User Story - NADPCO Monthly Activity Trend Snapshot

## Status
`[ ]` Not yet implemented

## Story

As a TahlilApp-AI user,

I want to ask trend questions about a company's monthly production and sales,

so that I can understand whether the latest monthly report is improving or weakening compared with the previous year, the trailing average, and the fiscal-year trajectory.

## Business Context

Noavaran Amin monthly activity data is already ingested from 1403 onward and contains product-level production and sales rows for each company and reporting month.

The current AI layer can answer latest/monthly sales questions and product revenue mix questions, but trend analysis still requires historical aggregation across raw monthly report rows. The LLM must not fetch multiple raw rows and calculate the chart at query time.

This story introduces a persisted derived snapshot layer for company-level production and sales trends. The snapshot is calculated deterministically during ingestion/recalculation and is optimized for AI retrieval, chart generation, and explainable trend answers.

Example user questions enabled by this story:

- روند فروش ماهانه کهمدا را نشان بده
- نمودار فروش ماهانه کچاد در سال جاری و سال قبل را بکش
- فروش کگل نسبت به میانگین ۱۲ ماهه بهتر شده؟
- تولید و فروش فولاژ نسبت به سال قبل چه تغییری کرده؟
- گزارش تولید و فروش ماهانه کسرا را با نمودار مقایسه‌ای بده

## Source Data Semantics

This feature uses only Noavaran Amin monthly activity data.

The ProductSales endpoint provides five output types:

| outputTypeId | Meaning | Use in this story |
|---:|---|---|
| 0 | Single-month period | Authoritative source for monthly sales, monthly production, monthly sales quantity, and year-over-year monthly comparison |
| 1 | From fiscal year start to current month | Authoritative source for fiscal-year-to-date totals when available |
| 2 | Adjustments | Stored for evidence, not used in the initial trend chart unless explicitly requested later |
| 3 | From fiscal year start to previous month, adjusted | Stored for evidence, not used in the initial trend chart unless explicitly requested later |
| 4 | From fiscal year start to previous month | Used for YTD-to-previous-month context when available |

Monthly trend bars must be calculated from outputTypeId = 0 only. The system must not derive the monthly bar by subtracting outputTypeId = 4 from outputTypeId = 1 unless type 0 is missing and a future explicit fallback story defines that behavior.

## Unit Policy

- `productSaleValue` from Noavaran Amin is treated as **million Rials**.
- Persisted trend snapshot monetary fields must preserve this unit unless the existing canonical `DerivedMetrics` storage policy explicitly requires conversion for metric rows.
- User-facing labels must display `واحد: میلیون ریال`.
- The chart and AI response must not mix Rials and million Rials.

## Data Aggregation Rules

For each company and single-month report period (`outputTypeId = 0`):

- `MonthlySalesAmount` = sum of all product/service sales values for the company-month.
- `MonthlyProductionQuantity` = sum of production quantity only where product units are compatible or the product/service row is aggregatable.
- `MonthlySalesQuantity` = sum of sales quantity only where product units are compatible or the product/service row is aggregatable.
- Rows with negative values such as returns or discounts must be included in monetary totals because they affect net reported sales.
- Header/section rows with zero quantity and zero value remain evidence but do not change totals.
- If multiple product units exist in the same report, total quantity fields must be marked as mixed-unit and hidden or clearly qualified in user-facing output.

For each company-month, the trend snapshot also stores:

- Same reporting month previous fiscal-year single-month sales.
- Trailing 12-month average sales calculated from the latest 12 available single-month periods up to and including the current report month.
- Month-over-month sales growth percent.
- Year-over-year sales growth percent.
- Fiscal-year-to-date sales from outputTypeId = 1 when available.
- Fiscal-year-to-previous-month sales from outputTypeId = 4 when available.
- Data completeness and missing-comparable flags.

## Persistence Requirement

The AI query path must read from the persisted trend snapshot and must not aggregate raw `MonthlyReports` / `MonthlyReportLineItems` at request time.

The persisted layer must support the chart shape shown in the product discussion:

- Bars for monthly sales of the previous fiscal year.
- Bars for monthly sales of the current fiscal year next to the same fiscal months.
- A horizontal or series line for the trailing 12-month average.

## Acceptance Criteria

### Snapshot Calculation

1. The system calculates a company-level monthly trend row after each successful Noavaran monthly activity ingestion/recalculation.
2. The calculation uses `outputTypeId = 0` for monthly bars and same-month prior-year comparison.
3. The calculation uses `outputTypeId = 1` for YTD totals and `outputTypeId = 4` for YTD previous-month totals when those output types are present.
4. Trailing 12-month average is calculated from persisted single-month company-level facts and never by the LLM.
5. Negative sales values such as returns are included in net monthly sales.
6. Mixed-unit quantity aggregation is detected and exposed as metadata.

### Snapshot Persistence

1. A dedicated company-month trend snapshot table is created.
2. The table is keyed by `ExternalCompanyId`, `ReportYear`, and `ReportMonth`.
3. The table contains source provenance, output type provenance, calculation timestamp, and completeness flags.
4. Recalculation replaces the previous snapshot for the same company/month deterministically.
5. Historical backfill can rebuild snapshots from 1403 onward.

### Chart Readiness

1. A query can retrieve all rows required for the latest annual comparison chart for one company from the snapshot table.
2. The response model contains fiscal month labels, previous-year sales, current-year sales, and the 12-month average line.
3. If the current year has only some months available, future months return `null` current-year values rather than zero.
4. Missing prior-year comparable values are explicitly flagged, not fabricated.

### AI Performance

1. The AI query provider reads at most one derived trend table plus the company/symbol resolver.
2. The AI query provider must not read all product line items for historical periods.
3. Trend response latency is bounded and suitable for chat usage.

### Explainability

Responses must include:

- Source provider: Noavaran Amin / NadpcoApi
- Latest reporting period
- Unit: million Rials
- Whether current-year, previous-year, and 12-month average values are complete
- A confidence/completeness note when comparable data is missing

## Out of Scope

- Product-level trend charts by individual product.
- Product ontology / product-name normalization beyond what existing revenue-mix specs already require.
- Forecasting future months.
- Peer/industry benchmark charts.
- Frontend implementation details beyond returning a chart-ready contract from the backend.

## Dependencies

- Spec 038 — NADPCO API Provider Foundation
- Spec 039 — NADPCO API Company Catalog Synchronization
- Spec 042 — NADPCO API Monthly Activity Synchronization
- Spec 057 — Monthly activity freshness and sales lookup
- Spec 059 — Monthly Activity Output Type Segmentation
- Spec 069 — Noavaran monthly sales composite lookup
- Spec 075 — Company Product Revenue Mix, for shared product-sales semantics only

## Priority

**High.** This is the foundation required before more advanced production/sales intelligence such as anomaly detection, revenue-quality scoring, and product-mix trend analysis. It converts raw line-item history into a stable AI-readable trend fact layer.
