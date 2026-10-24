# Feature 144 banking extension — specification

Status: design approved (2026-10-24). This document extends [Feature 144](../spec.md); its common quarter, provenance, comparable EPS, authorization, and replay rules still apply. Development persistence verification remains an implementation validation, not a design approval gate.

## Scope and classification

For a uniquely resolved, eligible `NoavaranCurrentApi` company, select the banking template only when its authoritative `NoavaranEligibleCompanies.GroupId` equals `c96520f9-1d2e-444a-ab46-5bdffd3797d2`. The named GUID belongs in one classification definition. Every other eligible, known non-bank group retains the current template. A missing or contradictory eligible group yields a typed classification-unavailable result; never infer a bank from a ticker/name or silently route an unknown group as a bank. No new ingestion path, schema migration, or V1 feature is in scope.

Only persisted, separate (`IsComposing=false`) `IncomeStatement` observations from `NoavaranCurrentApi` qualify. A request for consolidated scope is invalid. The existing audit/restatement selection and adjacent cumulative-to-standalone algorithm apply before any bank-specific projection. Five consecutive fiscal quarters are the default. Source values stay signed decimals in million IRR; item 153 is thousand shares and the displayed comparable EPS is IRR/share. The supplied Noavaran JSON is reference evidence for mapping and reconciliation only; runtime reads the existing PostgreSQL repository. Financial acquisition and synchronization remain unchanged.

## Banking rows

The banking table has **exactly 20 rows**, in the order below. The `#` column retains the original request's positions for traceability; positions 11, 12, 17, and 20 are intentionally absent. The Product Owner approved omitting those four nonmandatory rows entirely: no placeholder, substitute item, or derived continuing-operations formula. The supplied six-report JSON confirms the listed source titles, signed amounts, and arithmetic for the mapped Bank Mellat rows. It is not proof that identical reports are persisted or selected in Development; the source catalog and persisted observations must be checked during implementation validation.

| # | Display title | Source/policy | Mapping state |
|---:|---|---|---|
| 1 | درآمد تسهیلات اعطایی و سپرده‌گذاری | 304, cumulative flow | Verified in reference JSON and catalog |
| 2 | هزینه سود سپرده‌ها | 305, cumulative flow | Verified in reference JSON and catalog |
| 3 | خالص درآمد تسهیلات و سپرده‌گذاری | 306, cumulative flow | Verified; 306 = 304 + 305 in all six reports |
| 4 | مارجین ناخالص عملیات بانکی | standalone 306 / standalone 304 × 100 | Approved formula |
| 5 | درآمد کارمزد | 26, cumulative flow | Verified; source title includes agency fees |
| 6 | هزینه کارمزد | 7, cumulative flow | Verified in reference JSON and catalog |
| 7 | خالص درآمد کارمزد | 307, cumulative flow | Verified; 307 = 26 + 7 in all six reports |
| 8 | حاشیه سود کارمزد | standalone 307 / standalone 26 × 100 | Approved formula |
| 9 | خالص سود (زیان) سرمایه‌گذاری‌ها | 61, cumulative flow | Verified investment profit/loss source; disclose source's non-operating qualifier |
| 10 | نتیجه معاملات ارزی | 27, cumulative flow | Verified in reference JSON and catalog |
| 13 | سایر درآمدها و هزینه‌های غیرعملیاتی | 40, cumulative flow | Verified in reference JSON and catalog |
| 14 | هزینه‌های عمومی و اداری | 5, cumulative flow | Verified; source title includes selling costs |
| 15 | هزینه مطالبات مشکوک‌الوصول | 6, cumulative flow | Verified in reference JSON and catalog |
| 16 | هزینه‌های مالی | 12, cumulative flow | Verified in reference JSON and catalog |
| 18 | سود (زیان) قبل از کسر مالیات | 142, cumulative flow | Verified in reference JSON and catalog |
| 19 | مالیات | 13, cumulative flow | `مالیات بر درآمد`; 336 is equal in all six reports, validation only |
| 21 | سود (زیان) خالص عملیات متوقف‌شده | 278, cumulative flow | Verified title; all six reference values are zero; post-tax meaning remains qualified |
| 22 | سود (زیان) خالص | 143, cumulative flow | Verified in reference JSON and catalog |
| 23 | سود هر سهم بر اساس آخرین سرمایه | existing Feature 144 comparable EPS policy | Display this explanatory label; preserve source identity and calculation provenance |
| 24 | تعداد سهام شرکت (هزار سهم) | 153, existing comparable share basis policy | Approved policy |

There are exactly two **bank-specific** calculated percentage rows, #4 and #8. Both use normalized standalone values, preserve decimal precision, reject missing/incompatible operands and zero denominators, and store the result already scaled by 100. Rendering uses Persian digits and one decimal place without a second multiplication. The supplied six-report JSON independently confirms FY1405 Q2 item 306 as `930,601,037 − 358,231,194 = 572,369,843` million IRR and item 143 as `1,025,278,132 − 384,058,010 = 641,220,122` million IRR. These are reference calculations, not runtime data.

## Chart and presentation

Place the banking chart above the table in web and PNG, even if the optional non-bank chart setting is off. Use the same five period identities and typed cells as the table. The chart has exactly three **bars** (304, 306, 143; million IRR) and two **lines** (rows #4 and #8; percent) with separate monetary and percent axes. Deposit interest expense (305) remains unchanged in the table and is omitted from the chart. Negative values fall below zero. Missing values are gaps, not zeros. Axis labels, fiscal-quarter labels, legend, and tooltips use Persian digits. Table and chart show the same amount precision and availability semantics; chart calculations never reparse rounded display strings.

The table retains rightmost `شرح`, oldest quarter next to it, newest quarter to the left, and leftmost QoQ column. It uses actual Jalali period ends, a teal header, pale label cells, accounting parentheses, and no page-width overflow. Both calculated margin rows use the existing full-row yellow emphasis and stronger type. QoQ uses standalone values and typed statuses. Expense favorability is defined per banking row, not from sign alone; derived percentage rows keep percentage units. Preserve non-bank colors and behavior.

## Acceptance criteria

1. A uniquely resolved, eligible company with the approved GroupId selects the banking template from persisted catalog evidence.
2. Other eligible companies with a known different GroupId retain the exact current Feature 144 template and chart behavior.
3. Missing, duplicate, or conflicting eligibility/group evidence returns a typed unavailable classification and never guesses banking membership.
4. Only persisted `NoavaranCurrentApi` separate income statements are selected; consolidated requests and mixed-scope pairs are rejected.
5. Existing audit/restatement priority, period boundaries, source IDs, and selected report provenance are preserved.
6. The banking table contains exactly the 20 approved rows in the order above; original positions 11, 12, 17, and 20 are absent, with no unavailable placeholders.
7. Each approved direct monetary item uses adjacent compatible YTD subtraction for Q2–Q4, signed full-precision values, and pass-through 3M for Q1.
8. Missing predecessor, conflicting revision, incompatible scope/unit/scale, or absent source line yields a typed unavailable cell without affecting independent valid cells.
9. Banking gross margin equals standalone 306 / standalone 304 × 100, with null for missing/incompatible/zero denominator.
10. Fee margin equals standalone 307 / standalone 26 × 100, with the same safeguards.
11. The two margin cells serialize as already-scaled `percent` values and render in Persian digits with one decimal, exactly once.
12. Comparable EPS and share count follow the current Feature 144 143/313 numerator and newest-selected-quarter item-153 basis policy; every quarter shares that denominator and raw observations remain distinct.
13. The chart uses exactly three signed monetary bar series (304, 306, 143) and two percentage line series on distinct axes, in the same chronological five-quarter order as the table. Item 305 remains in the table and is excluded from web and PNG charts.
14. Missing chart values are gaps, zero remains a valid zero, and negative values in charted rows remain below the zero line in web and PNG.
15. The banking chart is shown above the table in web and PNG; the existing non-bank chart setting and series stay unchanged.
16. RTL table columns, PeriodType quarter headings, actual Jalali ends, Persian digits, accounting negatives, and no horizontal page overflow match Feature 144 presentation.
17. Both calculated rows have full-row yellow emphasis in web and PNG, including their QoQ cell.
18. QoQ changes use standalone values, keep percentage/percentage-point kinds distinct, and use banking row semantics for expense color; ambiguous sign transitions are neutral/unavailable.
19. Typed V2 response, API serialization, conversation persistence/replay, web, and PNG preserve template ID, decimals, null/zero distinction, units, reasons, and provenance.
20. Classification changes and report restatements take effect on a new query; replay retains the original typed snapshot. Cache keys/versioning cannot serve a stale template after group change.
21. The output is deterministic for identical persisted inputs and report-selection policy, with focused Bank Mellat and non-bank regression fixtures.
22. Items 140, 199, and 309 are not substituted for omitted rows; no `143 − 278` continuing-operations formula is added. Reference JSON is never a runtime dependency.
