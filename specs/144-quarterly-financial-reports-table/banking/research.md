# Banking Feature 144 — architecture and financial evidence

Date: 2026-10-24 (Asia/Tehran). Investigation is read-only. Evidence: repository code/docs, the Product Owner's six-report Noavaran JSON attachment, the checked-in [bank screenshot](../Bank-sample.jpg), and the [provider catalog snapshot](../../141-schema-aware-semantic-query-engine/SemanticCatalog/source-item-snapshot.json). The attachment is **reference evidence only**; it is neither a runtime input nor an implementation fixture prerequisite. No direct Noavaran request or ingestion was made.

## Existing runtime path and database-access boundary

`POST /api/ai/v1/query` uses the V2 financial-statement-table capability. `FinancialStatementTableQueryUseCase` calls `QuarterlyFinancialReportsQueryUseCase`, then renders its typed result. The use case resolves the canonical company, forces separate scope, and reads `EfCoreFinancialStatementTableRepository`. That repository reads provider-scoped catalog entries, bounded persisted report headers and line items from PostgreSQL. `QuarterlyFinancialReportsNormalizer` selects report versions and periods, normalizes additive flows, calculates comparable EPS/share basis and gross margin, and returns `QuarterlyFinancialReportTableResult` with provenance. The V2 workflow, API response and conversation payload already carry that typed result. See [contracts](../../../src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs), [normalizer](../../../src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs), and [repository/renderer](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs). Acquisition and synchronization need no change.

The frontend currently parses rendered Markdown into a table model, then reparses display strings for chart values. Recharts supports `ComposedChart` with bars, lines and distinct monetary/percentage axes; PNG has its own canvas drawing routine. At investigation time, the existing general chart had 3 bars + 1 line, and the initial Banking proposal called for 4 + 2; that Banking proposal is historical and superseded by the final Product Owner decision. **Current approved Banking configuration: 3 BAR + 2 LINE = 5 SERIES**—items 304, 306 and 143 as bars; the two margin formulas as lines; item 305 remains table-only. See [specification](spec.md#chart-and-presentation), [table/parser](../../../src/frontend/src/components/app/quarterly-financial-table.tsx), [chart](../../../src/frontend/src/components/app/quarterly-financial-chart.tsx), and [PNG exporter](../../../src/frontend/src/components/app/quarterly-financial-table-image.ts). No Feature 144 Telegram chart renderer was found.

`CompanyResolverService.ResolveAsync` loads `Companies` once and returns `ResolvedCompany` without GroupId. Both `NormalizedCompanyRow` and `NoavaranEligibleCompanyRow` expose nullable GroupId. The view is `SELECT * FROM Companies` restricted to `NoavaranCurrentApi`, `PrecedencyRight=0`, and three eligible markets. One projected view lookup after canonical resolution proves both eligibility and group without changing the general resolver. See [resolver](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs), [view row](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs), and [view migration](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/20260610100455_AddMonthlyActivityBackfillAndLineItemDetails.cs).

An authorized read-only Development query was attempted using `default_transaction_read_only=on`, but `localhost:5432` required a password and no Development connection credential was available to this session. Thus this report does **not** claim that Bank Mellat's six reports or GroupId were independently checked in the persisted database. The user's assertion that ingestion already stores the reports is the expected architecture, not an observed database result. No production data was accessed.

## Six supplied reference reports

All six JSON objects identify `وبملت`, `com_ID=452`, `isComposing=false`, `isRepresented=false`. They contain 62–65 item lines each. The source unit field on inspected lines is `N/A`; the established Feature 144 Noavaran monetary scale policy supplies million IRR, subject to persisted `AssumedScale` validation.

| FY / YTD | Statement ID | Jalali period end | Audited | Scope | Role |
|---|---:|---|---|---|---|
| 1404 3M | 525663 | 1404/03/31 | No | Separate | predecessor for Q2 |
| 1404 6M | 531302 | 1404/06/31 | Yes | Separate | Q2 current / Q3 predecessor |
| 1404 9M | 532744 | 1404/09/30 | No | Separate | Q3 current / Q4 predecessor |
| 1404 12M | 548144 | 1404/12/29 | Yes | Separate | Q4 current |
| 1405 3M | 548128 | 1405/03/31 | No | Separate | Q1 / Q2 predecessor |
| 1405 6M | 554251 | 1405/06/31 | No | Separate | Q2 current |

The JSON verifies these identities and values **for the supplied response**, not that the repository's audit/restatement ranking would select exactly these revisions from all persisted candidates. The screenshot contains no report IDs or scope metadata. Its FY1404 quarters and FY1405 Q2 appear to differ from some reference values; possible report-version, restatement, basis or screenshot-source differences remain unproven. Do not edit source data to match the image.

## Source semantics and identities

Across **all six supplied reports**, exact identities hold: `306 = 304 + 305`, `307 = 26 + 7`, `140 = 199 + 200`, `143 = 142 + 13`, `13 = 336`, and `27 = 309`. This validates 306 and 307 as net rows and shows why 199 is **total income**, not net operating income. It also supports choosing item 13 (`مالیات بر درآمد`) for the tax row, while keeping 336 as a reconciliation check rather than summing/duplicating tax. Item 61's title is investment profit/loss/income **non-operating**, which supports the requested investment row if that qualifier remains in provenance. Item 278 is zero in all six reports; its title says discontinued operations but does not independently prove a general continuing-operations formula when discontinued profit is nonzero. Item 309 is `جمع سایر درآمدهای عملیاتی` yet equals foreign-exchange result 27 in this sample; it cannot safely stand for the broader other-operating-income-and-expense row. No line titled depreciation was present.

The provider catalog snapshot agrees with the supplied JSON titles for the relevant IDs. Item 26 includes agency-fee income; item 5 includes selling costs. Bank-specific 304–307 titles carry `(بانک مرکزی)`. These labels should be retained as source provenance even when the approved display title is shorter.

## Product Owner row decision

At the earlier approval stage, the Product Owner approved a 20-row Banking template. Subsequent approval added the ItemId 194 and derived operating-income rows, producing the current 22-row table. Original positions 11 (other operating income/expense), 12 (net operating income), 17 (depreciation), and 20 (continuing-operations net profit) remain **omitted**, with no unavailable placeholders. Item 140 does not replace row 12, and `143 − 278` is not calculated for row 20. The four rows remain in the evidence matrix below only to document why no source was selected; they are not output rows or implementation tasks.

## Reconciliation matrix

All numeric values below come from the **reference JSON**, not a database query. For direct flows FY1405 Q2 uses report `554251` minus `548128`; FY1404 Q2/Q3/Q4 use `531302−525663`, `532744−531302`, and `548144−532744`. `—` in the screenshot column means no value was transcribed with sufficient certainty. `Omitted` means no output row. Screenshot percentages are visually approximate.

| # | Display row | Item/formula | 1405 3M YTD | 1405 6M YTD | 1405 Q2 standalone | Screenshot Q2 | Difference / qualification |
|---:|---|---|---:|---:|---:|---|---|
| 1 | درآمد تسهیلات اعطایی و سپرده‌گذاری | 304 | 1,009,684,387 | 2,150,761,533 | 1,141,077,146 | — | Title has `(بانک مرکزی)` |
| 2 | هزینه سود سپرده‌ها | 305 | −651,453,193 | −1,220,160,496 | −568,707,303 | — | Signed expense |
| 3 | خالص درآمد تسهیلات و سپرده‌گذاری | 306 | 358,231,194 | 930,601,037 | 572,369,843 | — | 304 + 305 in each report |
| 4 | مارجین ناخالص عملیات بانکی | Q2 306 / Q2 304 × 100 | n/a | n/a | 50.1605% | ≈50.3% | Rounded source result 50.2%; screenshot/version differs slightly |
| 5 | درآمد کارمزد | 26 | 186,404,253 | 264,372,382 | 77,968,129 | — | Includes agency fees |
| 6 | هزینه کارمزد | 7 | −15,582,207 | −34,811,139 | −19,228,932 | — | Signed expense |
| 7 | خالص درآمد کارمزد | 307 | 170,822,046 | 229,561,243 | 58,739,197 | — | 26 + 7 in each report |
| 8 | حاشیه سود کارمزد | Q2 307 / Q2 26 × 100 | n/a | n/a | 75.3374% | ≈75.3% | One-decimal display 75.3% |
| 9 | خالص سود (زیان) سرمایه‌گذاری‌ها | 61 | 0 | 9,955,258 | 9,955,258 | — | Source qualifies this as non-operating |
| 10 | نتیجه معاملات ارزی | 27 | −720,353 | 2,191,334 | 2,911,687 | — | 309 duplicates 27 here; do not add both |
| 11 | سایر درآمدها و هزینه‌های عملیاتی | unmapped | n/a | n/a | Omitted | — | 309 is too narrow and equals FX in all six |
| 12 | خالص درآمدهای عملیاتی | 199? | 790,354,935 | 2,057,633,044 | Omitted | — | 199 Δ=1,267,278,109 is **total**; 140 Δ=795,997,097 is operating profit; neither is an approved match |
| 13 | سایر درآمدها و هزینه‌های غیرعملیاتی | 40 | 4,287,806 | 8,611,704 | 4,323,898 | — | Source is net |
| 14 | هزینه‌های عمومی و اداری | 5 | −273,294,909 | −707,453,017 | −434,158,108 | — | Source also says selling |
| 15 | هزینه مطالبات مشکوک‌الوصول | 6 | −25,538,169 | −43,432,141 | −17,893,972 | — | Signed expense |
| 16 | هزینه‌های مالی | 12 | −84,491,880 | −177,797,191 | −93,305,311 | — | Signed expense |
| 17 | هزینه استهلاک | unmapped | n/a | n/a | Omitted | — | No corresponding source line in six reports |
| 18 | سود (زیان) قبل از کسر مالیات | 142 | 395,735,576 | 1,112,706,518 | 716,970,942 | — | Source ordinary activities before tax |
| 19 | مالیات | 13 | −11,677,566 | −87,428,386 | −75,750,820 | — | 336 is identical in all six, crosscheck only |
| 20 | سود (زیان) خالص عملیات در حال تداوم | 143? − 278? | 384,058,010 / 0 | 1,025,278,132 / 0 | Omitted | — | 143 is total net profit; 278 discontinued; Product Owner rejected this formula |
| 21 | سود (زیان) خالص عملیات متوقف‌شده | 278 | 0 | 0 | 0 | — | Valid zero; post-tax semantics not independently proven |
| 22 | سود (زیان) خالص | 143 | 384,058,010 | 1,025,278,132 | 641,220,122 | — | Matches request arithmetic |
| 23 | سود هر سهم بر اساس آخرین سرمایه | 143/313 + 153 policy | 384,058,010 / 3,382,000,000 | 1,025,278,132 / 3,382,000,000 | Uses the latest selected quarter share basis | — | Do not subtract EPS or shares |
| 24 | تعداد سهام شرکت (هزار سهم) | 153 | 3,382,000,000 | 3,382,000,000 | Point-in-time 3,382,000,000 | — | Comparable display basis may use FY1406 Q2 if present |

The reference 1404 standalone values most relevant to the screenshot are:

| Item / percent | 1404 Q2 | 1404 Q3 | 1404 Q4 | 1405 Q1 | 1405 Q2 |
|---|---:|---:|---:|---:|---:|
| 304 income | 1,053,921,801 | 733,061,352 | 799,427,173 | 1,009,684,387 | 1,141,077,146 |
| 305 expense | −423,287,032 | −477,014,801 | −519,227,838 | −651,453,193 | −568,707,303 |
| 306 net | 630,634,769 | 256,046,551 | 280,199,335 | 358,231,194 | 572,369,843 |
| Gross margin | 59.8% | 34.9% | 35.1% | 35.5% | 50.2% |
| 26 fee income | 57,832,139 | 52,970,039 | 74,468,106 | 186,404,253 | 77,968,129 |
| 307 net fee | 46,054,687 | 40,147,411 | 63,881,587 | 170,822,046 | 58,739,197 |
| Fee margin | 79.6% | 75.8% | 85.8% | 91.6% | 75.3% |
| 143 net profit | 700,389,141 | 48,064,933 | 692,720,233 | 384,058,010 | 641,220,122 |

The screenshot visually appears to show FY1404 Q2 gross margin around 46.1%, materially different from the 59.8% produced by the supplied 525663/531302 pair. That is a **version/basis discrepancy**, not a reason to alter the YTD subtraction or use consolidated data. The screenshot does not identify its selected report revisions, so an exact difference or cause remains unverified. FY1405 Q2's screenshot fee margin approximately agrees (75.3%); gross margin appears near 50.3% against the reference JSON's 50.2% one-decimal result. Verify against persisted selected report IDs before claiming exact screenshot parity.
