# Tasks: Local company data for every grounded analysis, and no "too large" refusal

Status: Gate B: Approved by the owner on 2026-11-22

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/local-company-data-context`

Paths: backend relative to `src/backend/`, tests relative to `tests/`.

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done. **Exception approved by the owner on 2026-11-22:** 154 and 161 are `[~]`. This change only calls their implemented query use cases (`IBalanceSheetReportQueryUseCase`, `ICashFlowReportQueryUseCase`) and changes none of their code; every `BalanceSheetReport154*` and cash-flow test must stay green.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-121…OQ-124 answered 2026-11-22).
- [x] Branch `change/162-local-company-data-context` from `develop`.

## Implementation Tasks

### 0. Evidence (read-only)

- [x] T01 Confirm BUG-042's cause (A2): the production log line of the فاراک turn (`ReportDiscussionDiagnostics`: profile, `EstimatedInputTokens`, `FactCount`) or a Development run with فاراک's statement and a fake model. Record numbers in `specs/bugs.md`. If the cause is not the input budget, stop and ask the owner.
- [x] T02 Baseline: run `ReportDiscussion162Tests`, `SymbolGroundedFallback163Tests`, `GroundedAnalysis*Tests` and the 162/163 architecture and endpoint tests on unchanged code; note results.

### 1. Options and budget (Application, no I/O)

- [x] T03 `GroundedAnalysisOptions`: Deep `InputTokenTarget` 40,000, Simple 24,000; validation 4,000–120,000; new `MaxInputTokens` (default 120,000, ≥ every profile target); new `DataPack` section (`Enabled` true, `SectionTimeoutSeconds` 3, `TotalTimeoutSeconds` 8, validated). Update `appsettings.json`; `docker/appsettings.Production.json` / `.env.example` only if a non-default value is needed (code defaults are the production values, BUG-041).
- [x] T04 Extend `GroundedAnalysisProductionConfigurationTests` (AC-9).

### 2. Section projections (Application, `AI/ReportDiscussion/ReportDataSetBuilder`)

- [x] T05 Overloads that project from a `DiscussableReport` (not only an `ActiveReport`) for balance sheet and monthly trend, as `BuildQuarterly` already does.
- [x] T06 New cash-flow projection from `CashFlowReportResult`: rows × available columns as facts (unit from the statement), KPIs with `available` state as calculations with their formula; unverified cells keep the report's state text in the label. No new calculation.
- [x] T07 Unit tests for T05–T06 (values copied, missing cells skipped, units).

### 3. Data pack (Application, `AI/GroundedAnalysis/`)

- [x] T08 `CompanyAnalysisDataPack` record (sections in order, `shown` flag, `NotAvailable` list with reason codes) and `CompanyAnalysisDataPackLoader` over the four existing use-case interfaces (refinement R1) plus `CashFlowReportOptions`: sequential reads, per-section and overall timeouts with linked cancellation, symbol check (canonical), exceptions caught per section (request cancellation rethrown). FR-1, FR-2, FR-4.
- [x] T09 Evidence-id renumbering across sections into one F and one C counter (refinement R2), operand ids remapped.
- [x] T10 Unit tests: all available; cash-flow flag off; no monthly data; one section throws; one times out; other-company result rejected; request cancellation propagates (AC-1, AC-2, AC-3).

### 4. Prompt and budget (Application)

- [x] T11 `RenderCompanyData(pack)` in `ReportDiscussionContextBuilder`: `<company_data>` with one `<section>` each, period keys `P0…` (FR-10), `not_available` list. The legacy `RenderReport` stays for the legacy path.
- [x] T12 Prompt rules in `GroundedAnalysis`: the `<company_data>` no-search rule (FR-6) added to both web and no-web variants; `WebSearchUseRule` and `WebRules` otherwise unchanged (G2); analysis tails and `NoWebRules` refer to `<company_data>`. Update `GroundedAnalysisPromptTests` (AC-6).
- [x] T13 `CompanyDataPromptBudget` (pure): the degradation order of FR-8 against `InputTokenTarget`, the hard ceiling `MaxInputTokens` (FR-9); returns the kept pack, history and the step reached.
- [x] T14 Unit tests: each degradation step; فاراک-sized synthetic statement fits at Simple and Deep (AC-8); above the ceiling → null.
- [x] T15 `NumericGroundingValidator`: accept a pack (union of sections) through an overload; existing signature kept. Tests (AC-7).

### 5. Wiring the two use cases

- [x] T16 `ReportDiscussionHandler.AnalyzeAsync`: after `Answered`, load the pack (shown section = active report's data), build with the budget, ground on the union; `NarrowRequired` only above the ceiling; optional loader parameter (null or `DataPack:Enabled=false` → shown report only, FR-13). Diagnostics gain sections sent/omitted and degradation step.
- [x] T17 `SymbolGroundedFallbackHandler.RunConfirmedAsync` and `SymbolGroundedFallbackPromptBuilder.BuildAnalysis`: same pack and budget (shown section = the statement already read); `over_input_budget` only above the ceiling. Offer step unchanged (AC-5).
- [x] T18 `GroundedAnalysisRun.SectionsUsed` (optional, stored in the existing payload JSON).
- [x] T19 DI: register `CompanyAnalysisDataPackLoader` scoped in `ServiceCollectionExtensions`; pass to both handlers.
- [x] T20 Telemetry: `GroundedAnalysisTelemetry` logs sections, omissions with reasons, per-section latency, degradation step and codal.ir citation count (hosts of verified sources), never values (FR-12, AC-11).
- [x] T21 Unit tests in `ReportDiscussion162Tests` / `SymbolGroundedFallback163Tests`: hand-back reads no section (AC-4); pack sent for 162 and 163; rollback flag (AC-10); billing status unchanged.

### 6. Integration, architecture, docs

- [x] T22 Endpoint tests (`GroundedAnalysis162EndpointTests`, `SymbolGroundedFallback163EndpointTests`) with a deterministic fake model: the captured prompt holds the expected sections; fixed tier settled.
- [x] T23 Architecture: `AI/GroundedAnalysis` and `AI/ReportDiscussion` still reference no Infrastructure/EF/HTTP (existing tests); add a check that the loader uses only Application interfaces.
- [x] T24 Docs: `docs/grounded-analysis-operations.md` (data pack, new settings, rollback, telemetry fields); `specs/bugs.md` BUG-042 status; `specs/README.md` change entry; CHANGELOG `[Unreleased]` Persian lines.
- [x] T25 Hand the owner the live checks of AC-12 (no live OpenAI calls by the agent) and the A1 server check (`CashFlowReport__Enabled` in the server `.env`).

## Tests

- [x] Unit: T04, T07, T10, T12, T14, T15, T21 (`tests/FinancialCopilot.UnitTests`).
- [x] Integration: T22 (`tests/FinancialCopilot.IntegrationTests`).
- [x] Architecture: T23 (`tests/FinancialCopilot.ArchitectureTests`).
- [x] Frontend: not applicable (no UI change).

## Definition Of Done

- [~] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
  - 2026-11-22: build with 0 warnings. Architecture 24/24.
  - Unit: every failure is in the baseline. The two prompt tests this change broke are fixed.
  - Integration: compared with a full run on a clean `HEAD` 6393c6fd (the baseline has no integration list).
    - HEAD: 243 failed. Working tree: 248 failed.
    - The 5 extra failures are `IndustryComparisonRoutingEndpointTests` (industry comparison is now answered). They come
      from the owner's uncommitted feature 125 work in the same tree, not from this change.
    - `TahlilAppTopUpPostgreSqlTests` concurrency failed only in the working tree, and one Telegram 143 test failed only
      on HEAD. Both look flaky; this change does not touch either area.
    - BUG-043 records the 44 pre-existing 154/161 routing failures.
- [x] For database work: not applicable (no migration).
- [x] For frontend work: not applicable.
- [ ] Acceptance criteria in `user-story.md` all checked (AC-12 by the owner).
- [x] Reference docs updated: `specs/README.md`, `docs/grounded-analysis-operations.md`.
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
