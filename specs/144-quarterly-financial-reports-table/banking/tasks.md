# Feature 144 banking extension — implementation tasks

Design approval: [APPROVED](design-review.md). The Product Owner later approved the two operating-income rows in this bounded implementation update. Follow the current [banking specification](spec.md) and [technical design](design.md): 22 banking rows, while the four earlier omitted rows remain excluded. Each task uses existing persisted statements and the V2 path. No JSON ingestion, direct Noavaran API call, new worker, new schema, V1 change, or modification to Feature 143 is authorized.

## T1 — Authoritative banking classification and template routing

- **Deliverable:** Add one named banking GroupId constant and a narrow classification reader over `NoavaranEligibleCompanies`; invoke it after canonical company resolution in the existing quarterly use case. Require matching canonical ID/provider/external ID and exactly one eligible group. Select stable `BankingIncomeStatement` or existing general template ID; return typed unavailable on missing/null/conflicting evidence. Preserve separate-scope enforcement. New queries reread classification; replay uses saved template ID.
- **Likely code:** `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs`; `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs` or a small adjacent classification reader; `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`.
- **Dependency:** None.
- **Acceptance criteria:** AC 1–5, 19–20.
- **Focused tests:** Bank GroupId, other non-null group, null/missing/duplicate/mismatched view row, changed group on a fresh request, consolidated request rejection, and stable old replay.
- **Complete when:** Classification uses the view, only the approved GUID selects banking, and non-bank selection is unchanged.

## T2 — Twenty-two-row bank template and persisted source retrieval

- **Deliverable:** Define the approved bank row IDs, labels, order, policies, units and source IDs in a testable immutable definition. Preserve the four previously omitted rows as excluded. Include source item 194 in the existing repository's bounded catalog/line-item selection; add the operating-income formula row with no source-item identity. Keep provider/statement/scope/source-title and selected report provenance. Item 13 is tax; 336 may be a parity check only. Item 61 retains its non-operating source qualifier.
- **Likely code:** `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` or adjacent bank definition; `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs`.
- **Dependency:** T1.
- **Acceptance criteria:** AC 4–6, 8, 21–22.
- **Focused tests:** Exact 22 IDs/order/labels; standalone FY1405 Q2 ItemId 194 and five-input operating-income reconciliation; missing/null/incompatible/zero/signed values and provenance; no item 140/199/309 substitute or `143−278` row; repository selection; general definition unchanged.
- **Complete when:** The same persisted repository returns all required approved bank observations and emits no excluded row.

## T3 — Shared quarter normalization and two bank percentages

- **Deliverable:** Route bank direct flows through existing selected-report and adjacent-YTD normalization. Add operand-driven typed ratio calculation for 306/304 and 307/26 **after** standalone amounts are formed. Keep percent values already scaled by 100, report/line IDs and operand values. Reuse current comparable EPS/share-basis policy without modification to its financial method. Make QoQ aware of both percentage rows and explicit banking expense semantics.
- **Likely code:** `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` and contracts.
- **Dependency:** T2.
- **Acceptance criteria:** AC 7–12, 18, 21.
- **Focused tests:** FY1405 Q2 item 306 `572,369,843`, item 143 `641,220,122`, gross margin `50.1605…%`, fee margin `75.3374…%`; Q1/Q3/Q4 subtraction; negative flow signs; missing/zero/incompatible denominators; zero vs null; comparable EPS/share basis and provenance; relative percent versus percentage-point QoQ.
- **Complete when:** Bank and existing general results both use deterministic typed cells, with no cumulative-ratio or double-×100 error.

## T4 — V2 response, text rendering, API and replay

- **Deliverable:** Carry template ID and bank rows through the existing `QuarterlyFinancialReportTableResult`, `FinancialStatementTableQueryUseCase.RenderQuarterly`, V2 workflow, API DTO and assistant-message persistence/replay. Render two bank percentages with Persian digits and one decimal; preserve decimals, null/zero distinction, units, source report/line IDs, EPS basis and classification snapshot. Old general-template payloads remain readable.
- **Likely code:** `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs`; `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs`; `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`; `src/backend/FinancialCopilot.API/Contracts/AiFacadeContracts.cs`; `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs`; conversation payload mappings.
- **Dependency:** T3.
- **Acceptance criteria:** AC 6, 11, 19–20, 22.
- **Focused tests:** Typed V2 response and JSON round-trip/replay for current 22-row banking output, omitted-row absence in every form, numeric precision/state/provenance retention, historical 20-row snapshot compatibility, old general replay and one-charge behavior.
- **Complete when:** A bank query and replay produce the same typed snapshot without prose-based recalculation.

## T5 — Typed banking table in the web client

- **Deliverable:** Adapt the API/replayed typed quarterly result into a stable row-ID based `QuarterlyFinancialTableModel`, avoiding Markdown/rounded-number parsing for banking. Render the typed rows in RTL with actual Jalali period ends and five quarter columns; continue to accept older saved 20-row results. Keep the two new operating-income rows table-only with normal styling, preserve full-row yellow emphasis only on both margin rows, and preserve bank-specific QoQ expense colors. Keep legacy general Markdown parsing and non-bank presentation intact.
- **Likely code:** `src/frontend/src/components/app/markdown-message.tsx`, `quarterly-financial-table.tsx`, `quarterly-financial-table-colors.ts`, `quarterly-financial-table-header.ts`, and the frontend AI response/replay adapter.
- **Dependency:** T4.
- **Acceptance criteria:** AC 6, 11, 16–19, 22.
- **Focused tests:** Typed bank model/replay, 22-row order and omitted-row absence, old 20-row saved-result compatibility, zero/dash versus unavailable, negatives, Persian labels/dates, yellow margin rows only, bank expense-color rules, mobile width, and unchanged general table.
- **Complete when:** Both fresh and replayed banking results render from typed values and the table has no page-width overflow.

## T6 — Five-series banking web chart

- **Deliverable:** Extend the existing Recharts `ComposedChart` path with the final approved bank configuration, exactly 3 BAR + 2 LINE = 5 SERIES: signed bars 304/306/143 on the million-IRR axis; leave ItemId 305 in the table only; lines for the two unchanged typed margins on the percent axis. Use the table's five selected periods oldest to newest, Persian fiscal labels/tooltips, a five-item legend, negative bars below zero, and null gaps. Show the bank chart above its table regardless of the optional non-bank chart setting.
- **Likely code:** `src/frontend/src/components/app/quarterly-financial-chart-data.ts`, `quarterly-financial-chart.tsx`, `quarterly-financial-table.tsx`.
- **Dependency:** T5.
- **Acceptance criteria:** AC 9–11, 13–15, 19.
- **Focused tests:** Exactly three `Bar` and two `Line` series with correct axis IDs, typed source values, missing gaps, negative bars, Persian numerals, oldest-to-newest order, bank chart enabled with general chart setting off, general series unchanged.
- **Complete when:** The browser chart uses the same typed cells as the table and displays no synthetic zero or mixed-unit axis.

## T7 — Banking PNG export parity

- **Deliverable:** Extend the existing canvas exporter to render all typed banking rows and the existing bank chart from the same typed model. The two new rows are table-only. Keep chart definitions, axes and legend unchanged; preserve three signed bars, two gapped curves, Persian labels and legend, and two full-row yellow margins. Keep Sapio branding and non-bank PNG behavior.
- **Likely code:** `src/frontend/src/components/app/quarterly-financial-table-image.ts`, `quarterly-financial-chart-data.ts`, and existing browser PNG test fixture.
- **Dependency:** T5–T6.
- **Acceptance criteria:** AC 6, 11, 13–17, 19, 22.
- **Focused tests:** Canvas parity for unchanged chart series and signed bars; 22-row export with the correct new-row order and only two highlighted rows; enabled bank chart even when general setting is off; desktop/mobile browser PNG checks and general PNG regression.
- **Complete when:** Exported banking PNG agrees with web values/order/availability and fits its canvas without clipping.

## T8 — Bank Mellat financial reconciliation and persistence check

- **Deliverable:** Use the [reference matrix](research.md#reconciliation-matrix) for deterministic tests of the six supplied separate statements. Through authorized read-only Development access **if available**, compare eligible GroupId, persisted selected report IDs/variants, source catalog IDs/titles/units, YTD operands, standalone cells, margins, EPS basis and screenshot differences. Record any source-version difference rather than modifying financial data. If Development credentials remain unavailable, complete source-backed fixture tests and record that explicit validation limitation; the JSON is never a runtime requirement.
- **Likely code:** `tests/FinancialCopilot.UnitTests/QuarterlyFinancialReports144Tests.cs`; `tests/FinancialCopilot.IntegrationTests/QuarterlyFinancialReports144PostgreSqlTests.cs`; banking evidence note under this directory.
- **Dependency:** T2–T4; may run alongside T5–T7.
- **Acceptance criteria:** AC 1, 4–12, 20–22.
- **Focused tests:** Six report IDs, separate-only selection, exact Q2 examples, 306=304+305, 307=26+7, 13=336 parity, signed costs, zero item 278, absent four rows, and no forced screenshot matching.
- **Complete when:** Reference arithmetic is reproducible, persisted selection is verified when authorized access exists, and any remaining database-access or screenshot-version gap is documented precisely.

## T9 — End-to-end and non-bank regression gate

- **Deliverable:** Exercise a banking V2 query through the typed API, fresh web view, PNG and replay; verify the bank chart/table and billing/authorization behavior. Run focused existing general Feature 144 regressions for selection, fiscal periods, EPS/share comparisons, chart setting, table and PNG. Confirm acquisition/sync, Feature 143, V1, and schema remain untouched.
- **Likely code:** `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs`; `tests/FinancialCopilot.UnitTests/QuarterlyFinancialReports144Tests.cs`; `src/frontend/src/components/app/__tests__/quarterly-financial-*.test.tsx`; `src/frontend/scripts/quarterly-financial-table-png.browser.test.mjs`.
- **Dependency:** T1–T8.
- **Acceptance criteria:** AC 1–22.
- **Focused tests:** One bank and one non-bank end-to-end fixture, five-quarter ordering, 22 current bank rows and historical 20-row snapshot replay, unchanged chart-series count, zero/null/sign/percent semantics, replay equality, authorization and single billing finalization; existing general fixture unchanged.
- **Complete when:** Focused backend/frontend/browser suites pass and no unrelated production component was changed.

## T10 — Two operating-income rows (Product Owner approved)

- **Deliverable:** Add the banking-only direct source row `source_194` and the calculated row `banking_operating_income` immediately after it and after FX result. Reuse the persisted statement repository and standalone YTD normalization. Calculate the second row as standalone 306 + 307 + 194 + 61 + 27, preserving signs, unit and typed provenance. Do not modify General-template rows or chart series.
- **Acceptance criteria:** Current banking result has 22 rows; row 194 standalone Q2 is 614,028,450 million IRR and the calculated operating income is 1,258,004,435 million IRR for the reference reconciliation. Existing 20-row snapshots remain readable.
- **Focused tests:** Repository returns persisted item 194; exact order; normalized subtraction; valid zero, negative, null, missing predecessor and incompatible unit; source reports/line items/operand values; web/PNG parity; chart series invariance; V2 typed round-trip/replay; General-template regression.
- **Status:** Implemented. Deterministic backend tests and current frontend/browser results are recorded in `ImplementationReport.md`; Development database and full V2 host replay remain subject to the documented environment gates.

## Implementation status (2026-10-24)

| Task | Status | Evidence / limitation |
|---|---|---|
| T1 — classification and routing | COMPLETE | `NoavaranEligibleCompanies` reader checks canonical ID, provider, external ID, and exactly one non-null GroupId. Banking GroupId is centralized. Unit coverage verifies bank, non-bank, missing and duplicate evidence. |
| T2 — original 20-row template and source retrieval | COMPLETE (superseded row count) | The original approved baseline and exclusions are retained as history. The current 22-row row list and new source/formula rules are specified in T10 and `spec.md`. |
| T3 — normalization and margins | COMPLETE | Reuses adjacent YTD normalization and existing EPS/share-basis derivation. Deterministic FY1405 Q1/Q2 fixture verifies Q2 306, 143, 305, 307, 13, 278 and both ratios. Expense QoQ semantics covered for item 305. |
| T4 — V2/API/render/replay | PARTIAL | Typed template ID and classification snapshot are part of the existing quarterly result and therefore flow through the existing API and assistant-message persistence/replay. JSON round-trip is tested. V2 host integration test could not start because Feature 141 application-database settings are absent. |
| T5 — typed web table | COMPLETE | The original 20-row browser evidence is a historical baseline. The current follow-up verifies the 22-row table, omitted-row absence, RTL/mobile width and both highlighted rows. |
| T6 — five-series chart | COMPLETE | Current approved chart is exactly 3 BAR + 2 LINE = 5 SERIES: bars 304, 306, 143 and lines for the two unchanged margin formulas. Item 305 remains table-only. Follow-up browser/component checks are recorded in the implementation report. |
| T7 — PNG parity | COMPLETE | The original 20-row PNG is a historical baseline. The current 2400×4052 banking PNG includes the 22-row table, approved five-series chart and full-width yellow margin rows; evidence is saved under `evidence/`. |
| T8 — persisted Bank Mellat reconciliation | PARTIAL | Reference arithmetic is reproduced by deterministic tests. Development DB check is `NOT VERIFIED — DEVELOPMENT DB ACCESS UNAVAILABLE`: no Development connection variables or `psql` client were available; the local read-only attempt could not connect. No Production data was accessed. |
| T9 — end-to-end/regression gate | PARTIAL | 62 targeted backend tests, 17 frontend component tests, production build and Chrome PNG test pass. V2 API/replay host test is blocked by missing Feature 141 DB configuration; PostgreSQL-gated tests were skipped by their environment guard. See implementation report. |
