# Feature 144 Banking — Implementation Report

Date: 2026-10-24  
Design: [APPROVED](design-review.md)  
Overall implementation: **Implemented; final Development DB and V2 host validation remain blocked by this environment.**

## Scope and implementation

The approved 20-row design is implemented in the existing quarterly financial report path. Banking selection uses `NoavaranEligibleCompanies.GroupId`; source statements continue through `EfCoreFinancialStatementTableRepository`, the existing report selection, quarter normalizer, comparable EPS/share policy, V2 response and assistant-message persistence. No ingestion worker, direct Noavaran request, JSON runtime provider, schema migration, V1 change, or Feature 143 change was added.

The omitted original rows 11, 12, 17 and 20 are absent. There are no unavailable placeholders or substitutes for them. No item 140/199/309 substitution or `143 − 278` formula is present.

## Task states

| Task | State | Evidence |
|---|---|---|
| T1 — classification and routing | COMPLETE | GroupId reader and fail-closed classification; bank/non-bank/missing/duplicate routing tests. |
| T2 — 20-row template and retrieval | COMPLETE | Stable item-ID definitions, repository catalog query extension, exact row-order/omission test. |
| T3 — standalone normalization and margins | COMPLETE | Existing adjacent YTD normalization; FY1405 Q1/Q2 deterministic calculation test; expense comparison test. |
| T4 — V2/API/render/replay | PARTIAL | Typed result, template ID and group snapshot use the existing V2/API/conversation serialization path; round-trip test passes. The V2 host integration test is blocked before request execution by missing Feature 141 DB configuration. |
| T5 — typed web table | COMPLETE | Banking adapter consumes typed results and stable IDs; actual Jalali headers, row order, state/provenance fields; Chrome web evidence passes. |
| T6 — five-series chart | UPDATED | Three amount bars (304, 306, 143) and two percentage lines. Deposit interest expense (305) remains in the table and is omitted from web and PNG charts. |
| T7 — PNG | COMPLETE | Chrome-generated 20-row PNG and web screenshot; chart labels, mobile overflow and both yellow margin rows verified. |
| T8 — persisted Bank Mellat check | PARTIAL | Supplied-reference calculations are tested. Persisted Development reports and catalog could not be read; see limitations. |
| T9 — regression gate | PARTIAL | Targeted unit/component/browser/build checks pass. V2/PostgreSQL integration execution is limited by unavailable database configuration. |

## Banking classification, rows and calculations

The centralized banking GroupId is `c96520f9-1d2e-444a-ab46-5bdffd3797d2`. After canonical company resolution, the existing quarterly repository reads eligible catalog rows that match canonical company ID, provider and external company ID. Only one matching non-null GroupId can classify a company. The bank group selects `BankingIncomeStatement`; a different known group selects `GeneralIncomeStatement`. Missing, null, duplicate or mismatched evidence returns classification unavailable.

Bank row source IDs, in order (formula rows have no source ID):

`304, 305, 306, [banking gross margin], 26, 7, 307, [fee margin], 61, 27, 40, 5, 6, 12, 142, 13, 278, 143, 160, 153`.

The two ratios use standalone values and are stored already scaled as percentages:

- Banking gross margin: `standalone 306 / standalone 304 × 100`.
- Fee profit margin: `standalone 307 / standalone 26 × 100`.

For the supplied FY1405 Q1/Q2 reference operands, unit tests verify Q2 item 306 = `572,369,843`, item 143 = `641,220,122`, item 305 = `−568,707,303`, item 307 = `58,739,197`, item 13 = `−75,750,820`, item 278 = zero, gross margin = `572,369,843 / 1,141,077,146 × 100`, and fee margin = `58,739,197 / 77,968,129 × 100`. Percentages render once at one decimal place. Item 61's source catalog title is retained as provenance; item 13 is the tax row and item 336 is not duplicated.

## Typed V2, frontend, chart and replay

`TemplateId` and `ClassificationGroupId` are properties of the existing `QuarterlyFinancialReportTableResult`. The existing API and assistant-message persistence/replay mappings serialize that result without another data path or schema. A JSON round-trip test verifies the template ID and decimal ratio survive serialization. The API response adapter passes the typed quarterly result through both new-query and persisted-message mapping.

Banking renders from typed periods and cells, not Markdown labels or display-number parsing. The table uses 20 stable rows, five chronological quarter columns, actual selected `PeriodEnd` values in Jalali, RTL order, accounting negatives and two full-row yellow margin rows. Typed availability, reason, unit, source IDs, operands and EPS evidence remain in the frontend model. Item 61 catalog-title provenance is shown with the table.

The web chart and PNG use the same typed row values: bars for 304, 306 and 143 on the monetary axis; lines for the two margin rows on the percentage axis. Item 305 remains unchanged in the table and is omitted from charts. Nulls are chart gaps and zero remains zero. The banking chart is shown even when the optional general chart setting is false. General table/chart behavior remains on the existing path.

## Acceptance criteria evidence

| AC | Result / evidence |
|---:|---|
| 1 | Implemented: centralized GroupId routing; use-case test selects banking for the approved group. Persisted view access itself remains unverified without Development DB access. |
| 2 | Implemented: known non-bank group returns the existing general template; existing general unit/component regressions pass. |
| 3 | Implemented: missing and duplicate evidence return `ClassificationUnavailable`; code also rejects null groups and mismatched canonical/provider/external identity. |
| 4 | Implemented: existing repository query remains provider- and income-statement-scoped and requests `IsComposing=false`; consolidated query is rejected. PostgreSQL execution is environment-blocked. |
| 5 | Implemented: audit/restatement, fiscal boundary, adjacent-pair, selected report and provenance logic remain shared in the existing normalizer. |
| 6 | Verified: exact 20 ordered rows asserted; browser test verifies 20 rows and excludes unsupported labels. |
| 7 | Verified: FY1405 Q2 standalone operands and values tested; existing adjacent YTD implementation is reused. |
| 8 | Implemented: existing typed missing/incompatible/conflicting states propagate per cell; no rule was relaxed. Database-level bank cases remain unverified. |
| 9 | Verified: standalone item 306/304 calculation is tested with reference operands. |
| 10 | Verified: standalone item 307/26 calculation is tested with reference operands. |
| 11 | Verified: percentage unit, serialized ratio and one-decimal single-percent rendering test pass; browser table shows one percent sign. |
| 12 | Implemented: bank rows reuse existing comparable EPS and rolling share-basis calculations; no bank EPS algorithm was introduced. Existing EPS regressions pass. |
| 13 | Updated per Product Owner: banking series has three bars and two lines; item 305 is table-only. Re-run the focused web and PNG checks for this presentation change. |
| 14 | Chart keeps zero and null distinct; negative values in charted rows remain below zero. Item 305 is no longer plotted. |
| 15 | Verified: browser test sets the optional general chart flag false and confirms the bank chart remains visible. |
| 16 | Verified: browser screenshot confirms five quarter columns, actual Jalali period ends and RTL order; mobile viewport has no document horizontal overflow. |
| 17 | Verified: browser test checks every table cell in both margin rows has the same yellow styling; PNG visual inspection confirms both rows. |
| 18 | Implemented: bank margin comparisons use percentage-point change; negative signed bank expenses compare by absolute expense movement; sign transitions remain unavailable/neutral. Item 305 QoQ is tested. |
| 19 | Partially verified: typed result JSON round-trip and existing API/conversation mappings preserve the snapshot. V2 endpoint replay test could not start because its host requires unavailable Feature 141 DB settings. |
| 20 | Implemented: classification is queried per new use case execution; persisted replay retains its stored result. No banking cache was added. Runtime restatement and group-change behavior await DB-backed integration. |
| 21 | Verified in deterministic tests: identical source fixtures produce stable calculations and row ordering; targeted general tests pass. Persisted Bank Mellat selection awaits Development access. |
| 22 | Verified: exact row IDs asserted; the four omitted rows and substitutions are absent. Reference JSON is not read by production code. |

## Changed production files

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs`
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs`
- `src/frontend/src/lib/chat.functions.ts`
- `src/frontend/src/components/app/message-list.tsx`
- `src/frontend/src/components/app/quarterly-financial-table.tsx`
- `src/frontend/src/components/app/quarterly-financial-chart-data.ts`
- `src/frontend/src/components/app/quarterly-financial-chart.tsx`
- `src/frontend/src/components/app/quarterly-financial-table-colors.ts`
- `src/frontend/src/components/app/quarterly-financial-table-image.ts`

No acquisition worker, synchronization service, database schema, V1 route or Feature 143 production file was changed.

## Tests, builds and browser evidence

- Backend targeted tests: **62 passed, 0 failed, 0 skipped** (`QuarterlyFinancialReports144` and `QuarterlyFinancialHeader144`).
- Frontend Feature 144 table/chart/PNG component tests: **17 passed, 0 failed**.
- Frontend production build: **PASS** (`npm run build`).
- Chrome banking PNG browser test: **PASS** (`npm run test:quarterly-banking-png`); 20 rows, 5 fiscal periods, both yellow margin rows, bank chart visible with general chart setting off, and no mobile page overflow.
- Generated evidence: [banking-quarterly-table.png](evidence/banking-quarterly-table.png) and [banking-quarterly-web.png](evidence/banking-quarterly-web.png). The table PNG is 2400×3836.
- PostgreSQL Feature 144 filtered tests: **4 skipped** by their environment guards.
- V2 API/replay endpoint test: **BLOCKED before request execution** because the test host's Feature 141 application-database connection, database name and read-only role are not configured.
- Full `npx tsc --noEmit`: **FAIL** on existing unrelated project/test typing errors (including route `search` requirements, test `toReversed` library support, and existing assistant test fixtures). A filtered scan reported no TypeScript diagnostics in the modified banking table/chart/chat files. Production Vite build passes.

## Development data and remaining limits

**NOT VERIFIED — DEVELOPMENT DB ACCESS UNAVAILABLE.** This session had no Development DB connection environment variables or `psql` client. The local PostgreSQL read-only check could not be run. The six supplied Bank Mellat reports remain optional reference evidence, not a runtime dependency; no claim is made that Development selected those exact report IDs or that the screenshot's report versions match.

The Chrome evidence uses a deterministic typed presentation fixture and is evidence of rendering, not evidence of production financial values. V2 API/conversation replay, PostgreSQL repository selection, persisted eligible GroupId, source-catalog inventory and actual selected Bank Mellat report IDs still need execution in an environment with the authorized Development database settings.

No Production financial data was accessed or modified.


## Product Owner presentation update (2026-10-24)

The banking chart excludes source item 305 (deposit interest expense); the table row and its values remain unchanged. The banking EPS row label now reads "سود هر سهم بر اساس آخرین سرمایه". The chart has three amount bars and two margin lines. Focused component and PNG verification cover the updated presentation.
