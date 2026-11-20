# Tasks: Feature 081 on/off switch

Status: Gate B: Approved by the owner on 2026-11-20 (Gate A approved in the same message, option B)

## Feature Folder

`081-ai-financial-statement-period-analysis-query/changes/period-analysis-feature-flag`

Paths: backend relative to `src/backend/`, tests relative to `tests/`. Requirements: [user-story.md](user-story.md).
Design: [design-proposal.md](design-proposal.md).

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [~] The features in `Depends On` are done: 081 yes. 163 is a runtime collaborator whose definition of done is not
      ticked; only its implemented trigger is used (user-story Assumptions).
- [x] Read `user-story.md`, the references it lists, and the relevant code.
- [x] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

- [x] T01 Application: `FinancialStatementPeriodAnalysisOptions` (`SectionName`, `Enabled = true`) and reason code
      `FinancialStatementPeriodAnalysisOptions.DisabledReasonCode` = `financial_statement_period_analysis_disabled`.
- [x] T02 Application: `AiQueryBillingOperationSelector.Select(..., periodAnalysisEnabled = true)` (FR-6).
- [x] T03 Infrastructure DI: bind the options and register the value; `BillingFunctions` passes the flag.
- [x] T04 V2 workflow: optional options parameter; semantic gate (FR-2), deterministic gate (FR-3), shared
      `PeriodAnalysisDisabledAsync` (FR-4), result-step label (FR-5).
- [x] T05 `appsettings.json`: `"FinancialStatementPeriodAnalysis": { "Enabled": false }` (AC-6).

## Tests

- [x] Unit: options default and selector with flag on/off (AC-4, AC-5).
- [x] Integration: endpoint tests with the flag off (AC-1, AC-2, AC-3): `PeriodAnalysisFeatureFlagEndpointTests`, 4/4 pass; with the flag forced on, the two offer cases fail (the gate is what makes them pass). `V2FinancialStatementAnalysisApiFactory` sets the flag on so 081 stays covered.

## Definition Of Done

- [x] Build green; unit and the new and touched integration tests green; no failure outside `specs/test-baseline.md`.
      2026-11-20, `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: architecture 23/23; unit 32 failed,
      all 29 methods in the baseline; integration 242 failed (no baseline). Every failing integration test was re-run
      with `FinancialStatementPeriodAnalysis__Enabled=true`: all still fail except 7 `AdminDataOperationsEndpointTests`,
      which fail with either value when their class runs alone (order-dependent, not this change).
- [x] No database migration.
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated: `specs/README.md`, `docs/architecture.md` (V2 routing note) where it lists 081.
- [x] Persian user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects → `specs/bugs.md`.

## Deviations

- The capability stays `Enabled` in the catalog (design-proposal, «Deviation»): disabling it would send these questions
  to the statement table instead of Feature 163.
- V1 is unchanged (`specs/POLICY-V1-FREEZE.md`).
- The intent label is corrected once in the result step (covers both the semantic frame and the keyword rule).
