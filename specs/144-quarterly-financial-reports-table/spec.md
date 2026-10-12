# Feature 144 — Quarterly Financial Reports Comparison Table

## Status

Final Product Owner decisions recorded; design package is ready for implementation planning. This feature remains specification-only until implementation is separately authorized. No production code or schema change is part of this package update.

## User story

As a Financial Copilot user, I want a company's quarterly income-statement table to show standalone fiscal-quarter performance, source context, and deterministic comparisons so I can inspect reported results without confusing cumulative year-to-date figures with a single quarter.

## Scope

Return the reference income-statement table for a selected company using persisted Noavaran financial statements. The table has exactly 16 rows in the approved order: 15 rows sourced from statement items and Gross Margin as the sole derived financial metric. The default remains five consecutive fiscal quarters newest first; a request may select 1–12 quarters and an ending fiscal period. Separate and consolidated scopes are selected independently. If a request does not identify a scope and persisted candidates include both, return an explicit scope conflict.

The capability is read-only at query time. Reuse the existing provider-scoped Source Item Catalog, company resolution, statement storage, V2 capability/tool route, typed response, conversation persistence/replay, and outer Billing operation. Do not call Noavaran at query time.

### Out of scope

- Production implementation, database migrations, Features 141–143 changes, and all V1 changes.
- Balance-sheet/cash-flow templates, arbitrary formulas, LLM arithmetic or SQL, query-time ingestion, forecasting, valuation, investment advice, and investigation of the one-unit EPS screenshot difference.
- Alternative source mappings, duplicate catalogs, mandatory `MetricCode`, or replacement of reported line items with accounting reconstructions.

## Approved table and source mapping

Every direct mapping is scoped to provider key `NoavaranCurrentApi` (display name Noavaran Amin) and `StatementType = IncomeStatement`. Reuse the catalog identities below; SourceItemId is not globally unique. `MetricCode` may be null and is not a query prerequisite.

| Order | Display label | SourceItemId | Existing source catalog identity | Row policy |
|---:|---|---:|---|---|
| 1 | فروش | 15 | `07c03a70-a1fa-4b9b-861c-937748e2a1dc` | Cumulative flow difference |
| 2 | بهای تمام‌شده کالای فروش‌رفته | 1 | `57940eb5-27f8-42b1-8827-d64dc49eab2d` | Cumulative flow difference |
| 3 | سود (زیان) ناخالص | 139 | `917b2bbb-3859-42f0-9864-2eac3f98a3b3` | Cumulative flow difference |
| 4 | مارجین ناخالص | — | Derived; no source catalog identity | Standalone gross profit / standalone revenue × 100 |
| 5 | هزینه‌های عمومی، اداری و تشکیلاتی | 5 | `c04b4fae-69db-48d9-a63e-c96a932e7b89` | Cumulative flow difference |
| 6 | خالص سایر درآمدها (هزینه‌ها)ی عملیاتی | 39 | `17f91e30-ba52-4789-89d0-3ffd3ac61c17` | Cumulative flow difference |
| 7 | سود (زیان) عملیاتی | 140 | `f2a6e460-176f-4426-a4e7-78d4ffd86ba6` | Cumulative flow difference |
| 8 | هزینه‌های مالی | 12 | `b8342891-ad45-4c4b-a4dd-361bb5f37adb` | Cumulative flow difference |
| 9 | خالص سایر درآمدها و هزینه‌های غیرعملیاتی | 40 | `e08f0ce7-ab6a-4a24-8fab-efc3f93c140f` | Cumulative flow difference |
| 10 | سود (زیان) عملیات در حال تداوم قبل از مالیات | 142 | `1cbc0ee5-21bb-4b9d-b5ba-72c3b84731b3` | Cumulative flow difference |
| 11 | مالیات | 334 | `31f4454a-78d2-41fb-8caa-b86564423cd6` | Cumulative flow difference |
| 12 | سود (زیان) خالص عملیات در حال تداوم | 279 | `abdbd529-7e27-43cc-a955-10ae19aea4cb` | Cumulative flow difference |
| 13 | سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی | 278 | `cb6c92c3-cff6-40c1-a85a-db9fde4ba791` | Cumulative flow difference |
| 14 | سود (زیان) خالص | 143 | `bd36909d-abf8-44c2-b7db-5e53dd678b53` | Cumulative flow difference |
| 15 | سود هر سهم پس از کسر مالیات | 160 | `2146da26-b28e-4d04-b50c-f49fc9b70e61` | Cumulative EPS difference |
| 16 | تعداد سهام شرکت (هزار سهم) | 153 | `e98e6c00-7204-44b1-a279-bbbb68781d3c` | Reported value; no subtraction |

The UUIDs are existing source catalog identities from the Feature 141 snapshot, not claims that persisted production observations were inspected. For SourceItemIds 12 and 278, retain the catalog's existing `TitleFa`; do not invent original titles. Display labels are the approved table labels and may differ from source titles.

## Financial-period rules

Noavaran 3M, 6M, 9M, and 12M income-statement values are cumulative fiscal-year-to-date observations. Display standalone quarters. For all approved additive flow items (including EPS under the explicit EPS rule):

| Quarter | Calculation | Required compatible observations |
|---|---|---|
| Q1 | `YTD_3M` | 3M |
| Q2 | `YTD_6M - YTD_3M` | Adjacent 3M and 6M |
| Q3 | `YTD_9M - YTD_6M` | Adjacent 6M and 9M |
| Q4 | `YTD_12M - YTD_9M` | Adjacent 9M and 12M |

Use signed full-precision decimals; do not round intermediate values. Never bridge missing periods with a non-adjacent subtraction. Before subtraction, require the same company, canonical provider, statement type, source item, fiscal year, report scope, compatible unit and storage scale, and valid fiscal-period boundaries. Selected cumulative reports from adjacent periods may have different audit/restatement flags, report IDs, and revision lineages; preserve the selected report identities and metadata in provenance. Missing explicit Currency metadata alone does not block the confirmed Noavaran unit contract. Do not assume fiscal years end in Esfand. Do not mix separate and consolidated reports.

For each company/provider/statement type/fiscal year/duration/scope, select report priority in this order: audited and restated; audited and not restated; unaudited and restated; unaudited and not restated. `IsAudited` is audit status and persisted `IsRepresented` is restated status. On same-priority ties, use the existing deterministic announcement-date then statement-ID selection policy; if the remaining tied reports have conflicting source values, return a typed conflict.

### EPS

SourceItemId `160` is a directly reported statement row with the specifically approved cumulative subtraction policy: Q1 `EPS_YTD_3M`; Q2/Q3/Q4 use the same adjacent cumulative differences shown above. Retain both operands' report and line-item provenance. For چخزر, `YTD3M = 10,155` and `YTD6M = 24,967` produce Q1 `10,155` and Q2 `14,812`. The one-unit screenshot discrepancy is intentionally out of scope; do not investigate, adjust, warn about, or ask about it.

### Shares

SourceItemId `153` is point-in-time reported share count. Preserve the selected report's numeric value by period, with no subtraction, multiplication, capital conversion, or derivation from profit/EPS. Its unit is thousand shares. Display the accurate label `تعداد سهام شرکت (هزار سهم)`.

### Gross Margin

Gross Margin is the only derived financial metric. First normalize SourceItemIds `139` and `15` to standalone quarter values; then calculate `QuarterlyGrossProfit / QuarterlyRevenue * 100`. Return unavailable if either operand is unavailable or quarterly revenue is zero. Do not reconstruct any other reported row.

## Zero, missing, signs, and units

Numeric zero is valid data. Keep `value = 0` and `state = Available`; render a final table financial cell whose value is exactly zero as `-`. This includes SourceItemIds `12` and `278`, EPS, and share count when the selected value is zero. Missing source/report, unsupported mapping, failed normalization, or unavailable calculation remains `value = null` with a distinct typed state/reason. In particular, a missing YTD3M and a zero YTD6M make Q2 unavailable; never substitute zero for missing.

Preserve signed financial values throughout retrieval, normalization, comparison, serialization, persistence, and rendering. Accounting parentheses may be used for negative display, but arithmetic uses numeric decimals. The confirmed Noavaran unit contract is authoritative: monetary statement line items use million Iranian rials; SourceItemId 160 uses Iranian rials per share; SourceItemId 153 uses thousand shares; Gross Margin uses percent. Classify units by approved source-item identity, not Persian title substrings. Persisted `AssumedScale=MillionRials` must be present for monetary data; missing explicit Currency metadata is compatible under this provider contract. Carry units in typed metadata; keep full-precision decimals internally and round only for display. Do not perform currency conversion.

## QoQ comparison

Compare standalone quarters. For positive bases, relative change is `(current / previous - 1) * 100`. Preserve absolute change when meaningful. For zero or negative bases, sign changes, missing cells, and unavailable calculations, return a typed comparison state; do not show misleading percentage growth. Gross Margin's reference comparison is relative percent change; where a percentage-point delta is supported, represent it separately from relative change. Valid zero is not missing.

## V2 response and provenance

Recognize the quarterly-table request through the existing V2 specialized capability/tool architecture. Resolve the company through the canonical company resolver and read persisted data only. Return a typed `QuarterlyFinancialReportTableResult` with company/ticker, provider and source, statement type/scope, fiscal-calendar evidence, requested quarter count, units/currency, freshness, selected report revisions, ordered quarter columns, and exactly 16 ordered rows.

Each row carries stable row ID, Persian label, nullable SourceItemId (null only for Gross Margin), existing source catalog identity, row class, normalization policy, unit, precision, and cells. Each cell carries decimal-or-null value, availability state and reason, source report/line-item identities, normalization operands, calculation provenance where applicable, and zero presentation hint. Comparisons carry base/target quarter, absolute and valid relative/percentage-point changes, and typed state. Keep machine values separate from renderer strings. Persist and replay the structured payload without recalculating from prose. Existing authorization applies; Billing charges once at the outer AI operation.

## Acceptance criteria

1. Quarterly-table requests route through the existing V2 capability/tool path; no new V1 behavior is added.
2. Company text resolves through canonical company resolution; unresolved or ambiguous identities produce typed outcomes.
3. Default output contains the latest five consecutive fiscal quarters newest first; a valid request may select 1–12 quarters and an ending period.
4. Every result uses `NoavaranCurrentApi`, `IncomeStatement`, and one scope. Report selection uses the four-level audit/restatement ranking independently in each scope; selected audit/restatement metadata and report IDs remain in provenance.
5. The result contains exactly 16 rows in the approved order and labels, with the 15 approved SourceItemIds and existing provider-scoped catalog identities; Gross Margin has no SourceItemId.
6. Mapping resolves through the existing provider-scoped source catalog; `MetricCode` may be null and is not required.
7. Q1 flow values pass through from compatible 3M cumulative observations; Q2/Q3/Q4 use only the adjacent differences specified above.
8. Non-adjacent periods, fiscal-year crossings, incompatible scope/provider/company/source/unit observations, unresolved conflicting top-priority reports, or missing operands never produce a fabricated quarter; state and reason are explicit. Different audit/restatement classes and report revision lineages across adjacent periods are allowed.
9. Signed decimal arithmetic preserves negatives and precision, including revenue fixture Q2 `9,388,580` from `16,754,501 - 7,365,921`.
10. EPS SourceItemId 160 follows the approved cumulative subtraction policy; fixture Q1 is `10,155` and Q2 is `14,812`, with both operands traceable and no screenshot discrepancy investigation.
11. Share count SourceItemId 153 is unchanged from its selected reported value, is never subtracted or converted, and declares thousand shares.
12. Gross Margin is the sole derived financial metric and is computed from normalized quarterly gross profit and revenue; missing inputs or zero revenue yield unavailable.
13. Valid zero remains numeric zero with `Available` state and renders as `-`, including zero values for SourceItemIds 12 and 278.
14. Missing/unavailable remains null with typed state/reason and is distinguishable from valid zero through serialization, persistence, replay, and accessible rendering; missing is never defaulted to zero.
15. Negative values remain signed through calculations and rendering; units and precision are explicit and display rounding does not alter stored numeric values.
16. QoQ uses standalone quarter values, provides exact absolute change when meaningful, only provides relative change for a valid positive base, and distinguishes relative margin change from percentage-point change.
17. Source report, source line-item, provider, period, scope, unit, revision, and normalization/calculation provenance are present for each applicable cell.
18. Persian labels, 16-row order, newest-first quarter columns, RTL layout, accounting negatives, and zero-as-dash rendering are stable.
19. Structured V2 output serializes and replays without loss of numeric zero, null state, source identity, or calculation provenance.
20. Existing authorization is enforced and the internal tool does not create a second Billing reservation/charge.
21. No query-time Noavaran call, LLM-generated arithmetic, LLM-generated SQL, or invented financial value is used.
22. Existing Features 141–143 and existing statement-table/period-analysis behavior remain unchanged.

## Readiness

`IMPLEMENTED_AND_VALIDATED_AGAINST_LOCAL_DEVELOPMENT_DATA`. Product Owner mapping, unit, report-priority, scope, and calculation decisions are resolved. The authorized local Development read-only query returned all 80 cells for the specified `چخزر` request. This does not claim production-data verification.
