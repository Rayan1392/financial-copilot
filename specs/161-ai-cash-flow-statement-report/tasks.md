# Tasks: AI Cash Flow Statement Report (صورت جریان‌های نقدی)

Status: Gate B: Approved by the owner on 2026-11-14

## Feature Folder

`161-ai-cash-flow-statement-report`

Paths: backend relative to `src/backend/`, frontend relative to `src/frontend/src/`, tests relative to `tests/`. Expected values and synthetic cases: [design-proposal-draft-plan.md §4–§6](design-proposal-draft-plan.md). Task ids are stable so review comments can refer to them.

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked). **Exception approved by the owner on 2026-11-14:** 117, 120, 121 are done; 154 is `[~]` (open: E-7 visual sign-off, E-5 production, E-1/E-2/E-8 evidence) and 144 has task B4 unticked. Feature 161 uses only their implemented code and rules, so work proceeds; T02 must keep every `BalanceSheetReport154*` test green.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-087 must be answered or its proposal (a) confirmed at Gate B).

## Phases

| Phase | Goal | Tasks | Exit condition |
|---|---|---|---|
| 0 | Evidence and baselines (read-only) | T01, T-R00 | Evidence reviewed by the owner; baseline test green on unchanged code |
| 1 | Application core, no I/O | T02–T10 | Unit suite green; fixtures A, B and S-1…S-15 reconcile |
| 2 | Infrastructure, API, text | T11–T13 | PostgreSQL and bounds tests green |
| 3 | Routing and conversation | T14–T18 | Routing matrix green; flag-off identical to baseline; V1 unchanged |
| 4 | Frontend | T-F01–T-F10 | Vitest, browser scripts, PNG, visual sign-off |
| 5 | Verification and documentation | T19–T22 | Definition of done |

Phases 1 and 4 can run in parallel once T10 fixes the contract (the frontend works from JSON fixtures).

## Implementation Tasks

### Phase 0 — Evidence and baselines

- [x] **T01 — Read-only Development evidence.** Files: `evidence/T01-queries.sql`, `evidence/T01-output.txt`, `evidence/T01-evidence.md`. Steps: (1) CashFlow catalog with presence counts by template and fiscal year; (2) sign survey per mapped item; (3) **coverage of item 128 on three-section statements and how often `128 + 121 = 122` and `128 + 121 + 123 = 124` hold** (E-03, OQ-087); (4) count `120 ≠ 0` and Codal-absent statements by fiscal year; (5) per-section closure for ≥ 20 general companies and all banks; (6) FX cases (`33 + 124 = 125` with `35 ≠ 0`; 35 absent); (7) fixtures 255 and 452: headers, flags, `JalaliFiscalYearEnd`, `AssumedScale`, all items, IS 15/143/313, GroupIds; (8) absent-vs-null check (E-09). Acceptance: every E-xx has evidence or "not closable"; the SQL contains no write statement; P items in the mapping matrix change only through an owner-reviewed update.
- [x] **T-R00 — Routing golden baseline (flag off).** File: `FinancialCopilot.UnitTests/CashFlowReport161RoutingBaselineTests.cs`. Records today's route and request object for routing-matrix rows 1–55 and U1–U7. Acceptance: passes on unchanged code before any routing change.

### Phase 1 — Application core (`FinancialCopilot.Application/FinancialData/CashFlowReport/`)

- [~] **T02 — Extract `FinancialStatementVersionPolicy`** (`FinancialData/Ingestion/`), used by `BalanceSheetStatementSelector` (behavior-preserving) and Feature 161. Acceptance: all `BalanceSheetReport154*` tests green unchanged; Feature 144 untouched; direct unit tests for ranking, ties and same-rank conflict.
- [x] **T03 — `CashFlowLineRegistry`**: the mapping matrix as code — templates, sections, order, labels, roles (122/121 derive Codal operating cash flow; 128 = V3 cross-check only), visibility, verification flags, exclusions (1, 58–63, 106–113, 126, 127), metric components. Tests `CashFlowReport161RegistryTests`: every matrix row; P items not displayable; excluded items never on value paths; architecture test: no ticker or company-id literal under `CashFlowReport/`.
- [x] **T04 — `CashFlowPresentationPolicy`** (design §4.1): `unsupported_presentation`, `codal_totals_missing`, `operating_total_missing`, consolidated-item warning. Tests S-5, S-6, S-6b, S-6c, S-13.
- [x] **T05 — `CashFlowPeriodSelector`**: eligibility (scope, scale, period types), fiscal slots via `FiscalCalendarPolicy`, anchor, default five latest independent quarters, same stage previous year for explicit YTD requests, quarterly (≤ 8) and annual (≤ 5) columns, requested months + fiscal-year label, nearest periods, previous report (U3). Tests: Esfand, non-Esfand (S-14), transitional year, missing fiscal evidence, truncated window, S-12.
- [x] **T06 — `CashFlowStatementBuilder`**: cells and states, of-which rows, residual rows, provenance, quarterly differencing of flow rows (128, 121, 122, 123, 124, 35, details) and balance selection without differencing. Tests: S-8, S-11, فلوله investing residual +131.
- [x] **T07 — `CashFlowReconciliation`**: R1 `(122 - 121) + 121 + 123 = 124`, R2, V3 cross-check `128 + 121 = 122`, C1, C2, FX-1…FX-4, statuses. Tests: S-1…S-4, S-6, S-7, S-15, fixtures A and B.
- [x] **T08 — `CashFlowMetricsCalculator` and `CashFlowIndustryPolicy`**: mapping matrix §5. Tests: every formula and null rule (S-9, S-10); bank exclusions; FCF never uses 121; DPS never used.
- [x] **T09 — `CashFlowWaterfallBuilder` and `CashFlowAnalysisFacts`**: steps with `from`/`to` and flows-only steps; deterministic fact sentences per topic. Tests: §6 waterfalls for A and B; R2-failed end bar = reported 125; text snapshots; verdict-word lint («قوی», «ضعیف», «مناسب», «نامناسب»).
- [x] **T10 — Contract and `CashFlowReportQueryUseCase`**: `CashFlowReportContracts.cs` (`contractVersion "161.1"`, outcome `operating_total_missing` included), `ICashFlowStatementReader`, use case (resolve → classify → read → select → build → reconcile → metrics → waterfall → facts), telemetry tags. Tests: fake ports for every outcome; no LLM dependency.

### Phase 2 — Infrastructure, API and text

- [~] **T11 — `EfCoreCashFlowStatementReader`** (`FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/`): implemented with SQLite reader tests. PostgreSQL test compiles but was skipped because `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` is not configured; query-count and PostgreSQL assertions remain unverified.
- [x] **T12 — Wiring, flag and carrier**: service registration, typed carrier, API mapping, and default-off option are implemented. Endpoint persistence/replay is covered by `CashFlowReport161EndpointTests` with a deterministic report-use-case fixture; PostgreSQL-backed and live-data verification remain under T11/T20.
- [~] **T13 — `CashFlowReportTextRenderer`** (UX §11): implemented and covered by report text assertions and the 3,900-character bound; A/B metric-focus snapshot coverage remains open.

### Phase 3 — Routing and conversation (`FinancialCopilot.Application/AI/Orchestration/`)

- [~] **T14 — V2 intent rules** (`FinancialStatementTableIntentRules.V2.cs` only): aliases, cues, request/follow-up parsing, company hints, and quarterly guard implemented; broad routing-matrix phrase coverage remains open.
- [~] **T15 — Interpreter, governance, precedence**: governed V2 route and active-task follow-up recognition implemented; only focused interpreter assertions have been run.
- [~] **T16 — Executor, outcomes, markers, conversation**: typed execution and outcome mapping implemented; full two-company and U2–U5 continuation coverage remains open.
- [~] **T17 — Suggestions and help**: related follow-up and help entries implemented; focused help tests pass, full suggestion matrix remains open.
- [~] **T18 — Routing regression suite**: direct routing and V1-freeze tests added and pass. Full rows 1–55/U1–U7 on both V2 paths and dependent feature suites remain open.

### Phase 4 — Frontend (`components/app/cash-flow-report/`)

- [~] **T-F01** — Typed contract and A/B/quarterly/failed fixtures implemented; the UI fixtures currently cover only available/failed outcomes rather than every outcome.
- [x] **T-F02** — `cash-flow-report-model.ts` formatting, labels, and reason helpers implemented.
- [~] **T-F03** — Header, status, warnings, and KPI rendering implemented; verification popover and all state-fixture coverage remain open.
- [x] **T-F04** — Wide responsive table renders period columns, cells, and server-computed change percentages.
- [~] **T-F05** — Compact accordion renders at 320/360/390 px; dedicated quarter-per-metric cards remain open.
- [x] **T-F06** — Proportional waterfall bars use the backend `from`/`to` values, with the flows-only toggle. Bank FX specific UI assertions remain part of the open coverage work.
- [x] **T-F07** — Trend chart is implemented from the typed report values.
- [~] **T-F08** — Shared-brand PNG export works on desktop and mobile. Export currently includes the table but omits the waterfall and KPI sections.
- [x] **T-F09** — Browser script, screenshots, dark-theme render, desktop/mobile PNG exports, and visual evidence recorded; the fixture limitation is documented.
- [x] **T-F10** — Message-list typed payload branch and compact rendering are implemented; 9 message-list tests pass.

### Phase 5 — Verification and documentation

- [x] **T19 — V2 endpoint integration** `CashFlowReport161EndpointTests.cs`: typed response, persistence/replay, exact two-credit billing, zero-cost NoData, configured provider name, and different-company isolation are covered through the V2 endpoint and the real statement-table service with a deterministic report-use-case fixture. Real persisted-data behavior remains under T11/T20.
- [~] **T20 — Live Development verification (read-only)**: Feature 161 is enabled in the local Development API and startup completed successfully after migrations. The six-company sample checks and evidence file remain open; no second billed query was issued.
- [~] **T21 — Documentation**: glossary, financial statement model, and `specs/README.md` updated. CHANGELOG is deferred until this uncommitted feature lands on `develop`, per `release-manager.md`.
- [x] **T22 — Implementation report** `ImplementationReport.md`: verified vs remaining, deviations, evidence links.

## Tests

- [x] Unit — accounting and period suites: fixtures A/B, 122 − 121, item 128 cross-check, reconciliation and metric rules; targeted `CashFlowReport161` tests pass (207 total including routing/help tests).
- [x] Unit — periods: Esfand, non-Esfand, transitional; quarterly differencing, annual, requested and nearest periods covered by the targeted period suite.
- [~] Unit — routing: focused rule/interpreter and flag-off/V1 checks pass; every routing-matrix row and U1–U7 are not yet covered.
- [~] Unit — text renderer assertions and architecture checks exist; full A/B metric-focus snapshot coverage remains open.
- [~] Integration — endpoint payload/replay/billing for the cash-flow report is covered with a deterministic use-case fixture; the PostgreSQL test is skipped without its isolated connection string, live data remains open, and follow-ups U1–U7 remain open.
- [~] Frontend (Vitest): 4 report tests and 9 message-list tests pass; browser at all six required widths, PNG desktop/mobile, and light/dark screenshots pass. Every outcome, bank layout, and export content assertions remain open.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [x] For database work: not applicable (no migration was added).
- [ ] For frontend work, in `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Both supplied datasets produce the [§6 values](design-proposal-draft-plan.md#6-expected-results-for-the-supplied-datasets) in unit tests and, after T20, in Development.
- [ ] Routing matrix green with the flag on; flag-off baseline identical; V1 unchanged.
- [x] Visual sign-off recorded for responsive layout, RTL, theme screenshot, and PNG download; financial data validation is still open.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated: `specs/README.md` index entry and affected domain/model documentation.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md` (see `AGENTS.md`, "Versions and Releases").
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
