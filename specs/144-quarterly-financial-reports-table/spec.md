# Feature 144 — Quarterly Financial Reports Comparison Table

## Status

The original Feature 144 table is implemented. The approved EPS and comparative-share correction is implemented in the V2 table path; see [the Qasem source investigation](eps-share-basis-investigation.md) and regression evidence. The previous cumulative-EPS subtraction policy is superseded.

The comparison chart and downloaded-image branding extensions below are implemented in the web/API path. See the C1–C6 task evidence in `tasks.md`.

## Comparison chart extension (implemented 2026-10-21)

The supplied first image communicates the chart idea, not a pixel-exact design target. In the web response, place a compact comparison chart between the existing table header/summary and the 16-row table. This lets readers see the trend before inspecting the detailed figures, while preserving the table's existing rows, order, units, and change column. The exported PNG uses the same order: header/summary, chart, table.

Plot exactly the quarters displayed by the table with the same Persian period labels. Use grouped bars for standalone quarterly sales (SourceItemId 15), gross profit (row 139 with its approved 310 fallback), and net profit (row 143 with its approved 313 fallback). Plot derived Gross Margin as a line with points on a separate percentage axis; never draw it as a bar or combine percentage and monetary values on one scale. Label the monetary axis with the table's million-rial unit. Preserve negative profit below zero and zero at the baseline. Missing or unavailable values produce no bar/point, and the margin line breaks across missing periods. The chart must not infer zero from a displayed dash or recompute financial metrics from another row. The table remains the authoritative detailed presentation, and the chart uses its underlying quarter values before display formatting.

Order the chart chronologically from left to right: the oldest displayed quarter is the leftmost group and the newest quarter is the rightmost group. The page and table retain their RTL presentation; this chart axis order applies to both the web chart and the downloaded PNG.

Use four distinct, accessible series colors, a Persian legend, readable axis labels, and exact values available through keyboard-accessible chart details or the adjacent table. On narrow screens, keep period labels legible with horizontal chart scrolling rather than compressing them beyond readability. Do not copy the first image's branding, KPI cards, watermark, or unrelated layout.

`QuarterlyFinancialTable:ShowChart` in API `appsettings.json` controls both the web chart and its inclusion in exported PNGs. Default is `true`; an environment variable `QuarterlyFinancialTable__ShowChart` in `.env` overrides the JSON setting using normal .NET configuration precedence. The frontend reads the effective non-secret value from a minimal API configuration response, including when displaying a persisted/replayed table. Disabling the setting leaves the existing table and table-only PNG. A configuration change takes effect after the API process reloads/restarts; the UI must use the current effective value consistently for screen and download.

Chart acceptance: enabled and disabled modes agree between web and PNG; bars/line reflect the exact available table quarters and values; units and independent scales are explicit; negative, zero, missing, and 1–12-quarter cases are handled without fabricated points; RTL labels and responsive/accessibility behavior are verified. No provider call, new financial calculation, database migration, or V1 change is required.

### Downloaded-image branding (2026-10-21)

The second supplied crop is a layout reference only. Add a compact Sapio brand area to the top of the Feature 144 downloaded PNG and a slim attribution footer below the table. Use the application's established name `ساپیو - دستیار هوشمند بازار`; do not reproduce StockArk wording, styling, logo, or watermark. Proposed original copy: top line `ساپیو`, supporting line `دستیار هوشمند بازار سرمایه`, and an export date in the Persian calendar (Asia/Tehran); footer line `گزارش مالی فصلی | ساپیو - دستیار هوشمند بازار` with the short context `بر پایه داده‌های گزارش‌شده`. Keep the existing company/report title and units prominent, make the brand text readable at PNG resolution, and avoid implying that Sapio authored the source financial statements. The brand area and footer appear whether the optional chart is enabled or disabled; neither is an extra table row.

## User story

As a Financial Copilot user, I want a company's quarterly income-statement table to show standalone fiscal-quarter performance, source context, and deterministic comparisons so I can inspect reported results without confusing cumulative year-to-date figures with a single quarter.

## Scope

Return the reference income-statement table for a selected company using persisted Noavaran financial statements. The table retains exactly 16 rows in the approved order. Monetary flow rows remain source-backed standalone values; Gross Margin is derived; the EPS and displayed-share rows require separate raw-observation and comparative-presentation values. The default remains five consecutive fiscal quarters newest first; a request may select 1–12 quarters and an ending fiscal period. **This correction uses only separate/non-consolidated statements** and preserves report precedence within that scope. A consolidated report must never be substituted to match an external reference.

The capability is read-only at query time. Reuse the existing provider-scoped Source Item Catalog, company resolution, statement storage, V2 capability/tool route, typed response, conversation persistence/replay, and outer Billing operation. Do not call Noavaran at query time.

### Out of scope

- Database migrations unless a concrete missing datum is proven, Features 141–143 changes, and all V1 changes.
- Balance-sheet/cash-flow templates, arbitrary formulas, LLM arithmetic or SQL, query-time ingestion, forecasting, valuation, and investment advice.
- Unapproved alternate source mappings, mandatory `MetricCode`, or replacement of reported line items with accounting reconstructions. The approved Noavaran supplemental item pairs are permitted only as fallbacks when the matching primary item has no value in that report; the primary item always wins when it has a value.

## Approved table and source mapping

Every direct mapping is scoped to provider key `NoavaranCurrentApi` (display name Noavaran Amin) and `StatementType = IncomeStatement`. Reuse the catalog identities below; SourceItemId is not globally unique. `MetricCode` may be null and is not a query prerequisite.

Retrieval includes the following supplemental Noavaran source IDs and fallback identities:

| Table row SourceItemId | Supplemental fallback SourceItemId | Table row |
|---:|---:|---|
| 139 | 310 | Gross profit |
| 140 | 311 | Operating profit |
| 142 | 312 | Continuing operations profit before tax |
| 143 | 313 | Net profit |

For rows 139, 140, 142, and 143, use the supplemental item only when the primary item is missing or has a null amount. Normalize it as the same cumulative-flow row for adjacent-period subtraction. Item 317 is not a fallback for the displayed EPS row. If the primary item has multiple observations, treat it as ambiguous and do not silently use a fallback. If both primary and supplemental items have values, the primary wins. Preserve the actual source item ID, catalog ID, and line-item ID selected in provenance.

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
| 15 | سود هر سهم پس از کسر مالیات | 160 | `2146da26-b28e-4d04-b50c-f49fc9b70e61` | Preserve source YTD EPS; display standalone/comparable EPS only with verified numerator and denominator evidence |
| 16 | تعداد سهام شرکت (هزار سهم) | 153 | `e98e6c00-7204-44b1-a279-bbbb68781d3c` | Preserve reported point-in-time observation; display a separate same-quarter comparative basis when verified |

The UUIDs are existing source catalog identities from the Feature 141 snapshot, not claims that persisted production observations were inspected. For SourceItemIds 12 and 278, retain the catalog's existing `TitleFa`; do not invent original titles. Display labels are the approved table labels and may differ from source titles.

## Financial-period rules

Noavaran 3M, 6M, 9M, and 12M income-statement values are cumulative fiscal-year-to-date observations. Display standalone quarters. For approved **additive monetary flow items only**:

| Quarter | Calculation | Required compatible observations |
|---|---|---|
| Q1 | `YTD_3M` | 3M |
| Q2 | `YTD_6M - YTD_3M` | Adjacent 3M and 6M |
| Q3 | `YTD_9M - YTD_6M` | Adjacent 6M and 9M |
| Q4 | `YTD_12M - YTD_9M` | Adjacent 9M and 12M |

Use signed full-precision decimals; do not round intermediate values. Never bridge missing periods with a non-adjacent subtraction. Before subtraction, require the same company, canonical provider, statement type, source item, fiscal year, report scope, compatible unit and storage scale, and valid fiscal-period boundaries. Selected cumulative reports from adjacent periods may have different audit/restatement flags, report IDs, and revision lineages; preserve the selected report identities and metadata in provenance. Missing explicit Currency metadata alone does not block the confirmed Noavaran unit contract. Do not assume fiscal years end in Esfand. Do not mix separate and consolidated reports.

For each company/provider/statement type/fiscal year/duration/scope, select report priority in this order: audited and restated; audited and not restated; unaudited and restated; unaudited and not restated. `IsAudited` is audit status and persisted `IsRepresented` is restated status. On same-priority ties, use the existing deterministic announcement-date then statement-ID selection policy; if the remaining tied reports have conflicting source values, return a typed conflict.

### EPS

The displayed row keeps the database label `سود هر سهم پس از کسر مالیات`; its value is **derived comparable EPS**, not a copied source EPS value. For its numerator, use the same net-profit source as row 143: prefer item 143 and use approved fallback item 313 only when 143 is missing or has a null amount. Preserve the actual selected source ID and treat 143/313 as the same numerator basis across adjacent YTD reports. Derive standalone quarterly net profit by subtracting the immediately preceding YTD amount, then calculate `standalone net profit (million IRR) × 1,000 / reported shares (thousand)` and round to the nearest whole rial, midpoint away from zero. Use the verified item 153 share count from FY `Y+1`, quarter `Q`, when available; otherwise use the verified own-period count as an explicit fallback. These are reported ending shares, **not weighted-average shares**. Require one unambiguous net-profit observation with approved catalog identity and unit in each required YTD report, separate statement scope, supported amount scale, and a positive verified share count. Items 160 and 317 are optional raw source observations only; neither is required to calculate the displayed row, and neither is subtracted or used as its fallback. Preserve raw items 143/153/160/313/317 and their report/line IDs in provenance.

### Shares

SourceItemId `153` is an immutable point-in-time reported share count in thousand shares. Never subtract, overwrite, or convert its raw observation to monetary capital. A separate display/comparison basis follows the approved rolling rule: for historical FY `Y` quarter `Q`, use the verified share count from the same quarter in FY `Y+1` when available; if no matching next-year quarter exists, use the verified own-period count as an explicit fallback. If a matching report exists but its share evidence is missing, zero, duplicate, mismatched, or unsupported, mark the comparison basis unavailable rather than silently using another period. Keep the original database label `تعداد سهام شرکت (هزار سهم)` and record reference period/report ID plus raw and displayed values in typed provenance. Recompute when the matching report arrives or is restated. Do not use one latest share count for every historical period.

### Gross Margin

Gross Margin remains derived from SourceItemIds `139` and `15`: `QuarterlyGrossProfit / QuarterlyRevenue * 100`. Return unavailable if either operand is unavailable or quarterly revenue is zero. The corrected EPS row may also be a derived **comparative presentation** value once its financial methodology is approved; do not reconstruct any other monetary source row.

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
4. Every corrected result uses `NoavaranCurrentApi`, `IncomeStatement`, and **separate scope only**. Report selection keeps the four-level audit/restatement ranking within that scope; selected metadata and report IDs remain in provenance.
5. The result contains exactly 16 rows in the approved order and labels, with the 15 approved SourceItemIds and existing provider-scoped catalog identities; Gross Margin has no SourceItemId.
6. Mapping resolves through the existing provider-scoped source catalog; `MetricCode` may be null and is not required.
7. Q1 flow values pass through from compatible 3M cumulative observations; Q2/Q3/Q4 use only the adjacent differences specified above.
8. Non-adjacent periods, fiscal-year crossings, incompatible scope/provider/company/source/unit observations, unresolved conflicting top-priority reports, or missing operands never produce a fabricated quarter; state and reason are explicit. Different audit/restatement classes and report revision lineages across adjacent periods are allowed.
9. Signed decimal arithmetic preserves negatives and precision, including revenue fixture Q2 `9,388,580` from `16,754,501 - 7,365,921`.
10. Source EPS items 160 and 317 are optional immutable observations and are not calculation inputs. Comparable EPS uses standalone row-143 net profit (item 143 or fallback 313) and the verified following-year same-quarter share basis, or explicit own-period fallback, with nearest-whole-rial midpoint-away-from-zero output rounding. The displayed row retains the original database label and typed provenance distinguishes the derived value from reported EPS; invalid numerator or share evidence yields unavailable.
11. Raw share count item 153 is immutable in thousand shares. The displayed share basis is separately typed and, when verified, references the same quarter of the following fiscal year; absence or invalidity has an explicit state/reason.
12. Gross Margin is derived from normalized quarterly gross profit and revenue; missing inputs or zero revenue yield unavailable. Comparable EPS is a separate derived value under the approved evidence policy and does not rename the source row.
13. Valid zero remains numeric zero with `Available` state and renders as `-`, including zero values for SourceItemIds 12 and 278.
14. Missing/unavailable remains null with typed state/reason and is distinguishable from valid zero through serialization, persistence, replay, and accessible rendering; missing is never defaulted to zero.
15. Negative values remain signed through calculations and rendering; units and precision are explicit and display rounding does not alter stored numeric values.
16. QoQ uses standalone quarter values, provides exact absolute change when meaningful, only provides relative change for a valid positive base, and distinguishes relative margin change from percentage-point change.
17. Source report, source line-item, provider, period, scope, unit, revision, and normalization/calculation provenance are present for each applicable cell. EPS/share cells additionally preserve raw 143/153/160/313/317 values where present, selected numerator source, standalone numerator, share denominator and its period-end type, units, output rounding, and reference quarter/report or explicit own-period fallback.
18. Persian labels, 16-row order, newest-first quarter columns, RTL layout, accounting negatives, and zero-as-dash rendering are stable.
19. Structured V2 output serializes and replays without loss of numeric zero, null state, source identity, or calculation provenance.
20. Existing authorization is enforced and the internal tool does not create a second Billing reservation/charge.
21. No query-time Noavaran call, LLM-generated arithmetic, LLM-generated SQL, or invented financial value is used.
22. Existing Features 141–143 and existing statement-table/period-analysis behavior remain unchanged.

## Readiness

The original Feature 144 table was implemented and validated against local Development data. The EPS/share-basis correction is implemented in code and covered by deterministic Feature 144, PostgreSQL, V2 persistence/replay, frontend, and PNG checks as recorded in eps-share-basis-investigation.md. Qasem arithmetic is validated against the selected Development statements plus one read-only live Noavaran FY1405 6M response; Production financial records were not inspected or changed. The metric is comparable EPS using reported period-end shares, not accounting EPS using weighted-average shares.
