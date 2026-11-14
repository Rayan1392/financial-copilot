# Tasks: AI Cash Flow Statement Report (صورت جریان‌های نقدی)

Status: Gate A: Approved by the owner on 2026-11-14 — plan proposed, Gate B not approved

## Feature Folder

`161-ai-cash-flow-statement-report`

Paths: backend relative to `src/backend/`, frontend relative to `src/frontend/src/`, tests relative to `tests/`. Expected values and synthetic cases: [design-proposal-draft-plan.md §4–§6](design-proposal-draft-plan.md). Task ids are stable so review comments can refer to them.

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec (OQ-087 must be answered or its proposal (a) confirmed at Gate B).

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

- [ ] **T01 — Read-only Development evidence.** Files: `evidence/T01-queries.sql`, `evidence/T01-output.txt`, `evidence/T01-evidence.md`. Steps: (1) CashFlow catalog with presence counts by template and fiscal year; (2) sign survey per mapped item; (3) **coverage of item 128 on three-section statements and how often `128 + 121 = 122` and `128 + 121 + 123 = 124` hold** (E-03, OQ-087); (4) count `120 ≠ 0` and Codal-absent statements by fiscal year; (5) per-section closure for ≥ 20 general companies and all banks; (6) FX cases (`33 + 124 = 125` with `35 ≠ 0`; 35 absent); (7) fixtures 255 and 452: headers, flags, `JalaliFiscalYearEnd`, `AssumedScale`, all items, IS 15/143/313, GroupIds; (8) absent-vs-null check (E-09). Acceptance: every E-xx has evidence or "not closable"; the SQL contains no write statement; P items in the mapping matrix change only through an owner-reviewed update.
- [ ] **T-R00 — Routing golden baseline (flag off).** File: `FinancialCopilot.UnitTests/CashFlowReport161RoutingBaselineTests.cs`. Records today's route and request object for routing-matrix rows 1–55 and U1–U7. Acceptance: passes on unchanged code before any routing change.

### Phase 1 — Application core (`FinancialCopilot.Application/FinancialData/CashFlowReport/`)

- [ ] **T02 — Extract `FinancialStatementVersionPolicy`** (`FinancialData/Ingestion/`), used by `BalanceSheetStatementSelector` (behavior-preserving) and Feature 161. Acceptance: all `BalanceSheetReport154*` tests green unchanged; Feature 144 untouched; direct unit tests for ranking, ties and same-rank conflict.
- [ ] **T03 — `CashFlowLineRegistry`**: the mapping matrix as code — templates, sections, order, labels, roles (128 = operating `primary-total`, 122 = V3 `cross-check`), visibility, verification flags, exclusions (1, 58–63, 106–113, 126, 127), metric components. Tests `CashFlowReport161RegistryTests`: every matrix row; P items not displayable; excluded items never on value paths; architecture test: no ticker or company-id literal under `CashFlowReport/`.
- [ ] **T04 — `CashFlowPresentationPolicy`** (design §4.1): `unsupported_presentation`, `codal_totals_missing`, `operating_total_missing`, consolidated-item warning. Tests S-5, S-6, S-6b, S-6c, S-13.
- [ ] **T05 — `CashFlowPeriodSelector`**: eligibility (scope, scale, period types), fiscal slots via `FiscalCalendarPolicy`, anchor, same stage previous year, quarterly (≤ 8) and annual (≤ 5) columns, requested months + fiscal-year label, nearest periods, previous report (U3). Tests: Esfand, non-Esfand (S-14), transitional year, missing fiscal evidence, truncated window, S-12.
- [ ] **T06 — `CashFlowStatementBuilder`**: cells and states, of-which rows, residual rows, provenance, quarterly differencing of flow rows (128, 121, 122, 123, 124, 35, details) and balance selection without differencing. Tests: S-8, S-11, فلوله investing residual +131.
- [ ] **T07 — `CashFlowReconciliation`**: R1 `128 + 121 + 123 = 124`, R2, V3 `128 + 121 = 122`, C1, C2, FX-1…FX-4, statuses. Tests: S-1…S-4, S-6, S-7, S-15, fixtures A and B.
- [ ] **T08 — `CashFlowMetricsCalculator` and `CashFlowIndustryPolicy`**: mapping matrix §5. Tests: every formula and null rule (S-9, S-10); bank exclusions; FCF never uses 121; DPS never used.
- [ ] **T09 — `CashFlowWaterfallBuilder` and `CashFlowAnalysisFacts`**: steps with `from`/`to` and flows-only steps; deterministic fact sentences per topic. Tests: §6 waterfalls for A and B; R2-failed end bar = reported 125; text snapshots; verdict-word lint («قوی», «ضعیف», «مناسب», «نامناسب»).
- [ ] **T10 — Contract and `CashFlowReportQueryUseCase`**: `CashFlowReportContracts.cs` (`contractVersion "161.1"`, outcome `operating_total_missing` included), `ICashFlowStatementReader`, use case (resolve → classify → read → select → build → reconcile → metrics → waterfall → facts), telemetry tags. Tests: fake ports for every outcome; no LLM dependency.

### Phase 2 — Infrastructure, API and text

- [ ] **T11 — `EfCoreCashFlowStatementReader`** (`FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/`): queries 1–6 of design §7; reuse `StatementMetadata.Parse`. Tests `CashFlowReport161PostgreSqlTests` (isolated like `BalanceSheetReport154PostgreSqlTests`) seeded with fixtures A, B and S-8; command-counting interceptor: ≤ 7 round trips, ≤ 12 + 2 statements.
- [ ] **T12 — Wiring, flag and carrier**: `ServiceCollectionExtensions.cs`; `FinancialStatementTableContracts.cs` (query fields, `CashFlowReport` on the result); `FinancialStatementTableServices.cs` (third branch); `API/Contracts/AiFacadeContracts.cs`, `AiFacadeController.cs`; option `CashFlowReport:Enabled` (default `false`) in `appsettings*.json`. Tests: persist/replay keeps the typed payload; flag off → branch never taken.
- [ ] **T13 — `CashFlowReportTextRenderer`** (UX §11). Tests: text snapshots for A/B report and metric focus; < 3,900 characters.

### Phase 3 — Routing and conversation (`FinancialCopilot.Application/AI/Orchestration/`)

- [ ] **T14 — V2 intent rules** (`FinancialStatementTableIntentRules.V2.cs` only): canonical aliases (whole word, «نقدینگی» guard), `CashFlowMetricCues`, `CashFlowAnalysisCues`, view/period cues, `ResolveCashFlowRequest`, `ResolveCashFlowFollowUp`, `LooksLikeCashFlowFollowUpCue`, company-hint extraction, quarterly guard. Tests: every routing-matrix phrase at rule level.
- [ ] **T15 — Interpreter, governance, precedence**: `DeterministicCapabilityInterpreter.cs`, `CapabilityInterpretationGovernance.cs`, `CapabilityRoutingPrecedence` (marker `feature161-v2-governed-report`); «روند» + cash-flow metric → table; «وضعیت تامین مالی» in an active Feature 161 task → follow-up.
- [ ] **T16 — Executor, outcomes, markers, conversation**: `SemanticCapabilityExecutors.cs` (`Feature161ReportMarker = "feature161_cash_flow_report"`, outcome mapping incl. `operating_total_missing`), `ConversationTaskStateContracts.cs`, V2 branch in `FinancialCopilotWorkflowDefinition.cs`; two-company guided response; continuation for U2–U5.
- [ ] **T17 — Suggestions and help**: `FollowUps/RelatedCapabilityFollowUpProviders.cs`, `AssistantHelp/AssistantHelpReplyComposer.cs` — «نمایش فصل‌به‌فصل», «مقایسه با سود خالص», «ترازنامه {symbol}»; help entry «صورت جریان وجوه نقد».
- [ ] **T18 — Routing regression suite**: `CashFlowReport161RoutingTests.cs`, `CashFlowReport161V1FreezeTests.cs`, `CashFlowReport161FollowUpEndpointTests.cs`. Acceptance: rows 1–55 and U1–U7 on both V2 paths with a scripted model; flag-off rows equal T-R00; V1 freeze; Feature 083/144/150/154/159 suites green.

### Phase 4 — Frontend (`components/app/cash-flow-report/`)

- [ ] **T-F01** — `cash-flow-report-types.ts` and JSON fixtures A, B, quarterly, failed, each outcome. Test: fixture round-trip matches `161.1`.
- [ ] **T-F02** — `cash-flow-report-model.ts`: formatters, view models, labels, reason texts (incl. `operating_total_missing`). AC-U02, AC-U03.
- [ ] **T-F03** — `cash-flow-report.tsx`: header, verification badge and popover (R1, R2, V3, residuals), focus answer, warnings, KPI strip. AC-U06.
- [ ] **T-F04** — Wide statement table (default, quarterly, annual). UX §6.
- [ ] **T-F05** — Compact accordion and mobile quarterly cards. AC-U01 (320/360/390 px).
- [ ] **T-F06** — Waterfall chart and flows-only toggle. AC-U04.
- [ ] **T-F07** — Trend chart. UX §5.3.
- [ ] **T-F08** — PNG export `cash-flow-report-image.ts` on the shared brand helper; Feature 144/154 PNG tests stay green.
- [ ] **T-F09** — Browser script `scripts/cash-flow-report.browser.mjs`, screenshots, `evidence/visual-signoff.md`. AC-U07, AC-U08.
- [ ] **T-F10** — `message-list.tsx` branch, `chat.functions.ts` types, `/m` compact; existing message-list tests green.

### Phase 5 — Verification and documentation

- [ ] **T19 — V2 endpoint integration** `CashFlowReport161EndpointTests.cs`: typed response; persist/replay; one charge for a report, NoData free; CyclicalWaves mode → `provider_unavailable`; V1 and Feature 141 untouched.
- [ ] **T20 — Live Development verification (read-only)**: flag on in Development; fixtures 255 and 452 plus one non-Esfand issuer, one issuer with FX, one more bank; `evidence/live-development-verification.md`. Only this task may claim live validation.
- [ ] **T21 — Documentation**: `docs/domain-glossary.md` (جریان خالص نقد عملیاتی، جریان نقد آزاد، مبنای ارائه کدال، اثر تغییرات نرخ ارز), `docs/financial-statement-model.md` (CashFlow presentations; operating = item 128 rule), `specs/README.md` status; CHANGELOG `[Unreleased]` through the release-manager agent.
- [ ] **T22 — Implementation report** `ImplementationReport.md`: verified vs remaining, deviations, evidence links.

## Tests

- [ ] Unit — accounting: fixtures A and B end to end; OCF = item 128; R1/R2/V3/C1/C2; FX-1…FX-4; residuals; FCF eligibility; zero vs not reported; duplicates; presentation selection; S-1…S-15.
- [ ] Unit — periods: Esfand, non-Esfand, transitional; quarterly differencing S-8; annual; requested and nearest periods.
- [ ] Unit — routing: rule and interpreter level for every routing-matrix row; flag off = baseline; V1 freeze.
- [ ] Unit — text renderer snapshots; architecture test for ticker/company-id literals.
- [ ] Integration — PostgreSQL reader, bounds, statement selection, scope filtering, IS companion alignment; endpoint payload, replay, billing, Feature 117 codes, follow-ups U1–U7.
- [ ] Frontend (Vitest): view models, layouts, states, bank differences, chart/table consistency from the same typed numbers; browser at 320/360/390/768/1280/1440 px; PNG desktop/mobile; light and dark app theme.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [ ] For database work: not applicable (no migration); `dotnet ef migrations has-pending-model-changes` unchanged for every DbContext.
- [ ] For frontend work, in `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Both supplied datasets produce the [§6 values](design-proposal-draft-plan.md#6-expected-results-for-the-supplied-datasets) in unit tests and, after T20, in Development.
- [ ] Routing matrix green with the flag on; flag-off baseline identical; V1 unchanged.
- [ ] Visual sign-off recorded.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md` (see `AGENTS.md`, "Versions and Releases").
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
