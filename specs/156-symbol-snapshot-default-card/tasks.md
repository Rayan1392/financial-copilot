# Tasks: Symbol Snapshot Default Card

Status: Gate B: Approved by the owner on 2026-11-09

## Feature Folder

`156-symbol-snapshot-default-card`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (none).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-006 to OQ-010 answered 2026-11-09).

## Expected Files

| Layer | New | Changed |
|---|---|---|
| Application | `AI/Orchestration/SymbolSnapshotContracts.cs`, `SymbolSnapshotIntentRules.cs`, `SymbolSnapshotUseCase.cs`, `SymbolSnapshotFollowUpSuggestionService.cs`, `SymbolSnapshotCapabilityExecutor.cs` | `ConversationalCapabilityContracts.cs`, `ChatCapabilityAvailability.cs`, `ConversationTaskStateContracts.cs`, `AiOrchestrationContracts.cs`, `SemanticCapabilityExecutionContracts.cs`, `Conversations/ConversationContracts.cs`, `FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` |
| Infrastructure | `AI/OrchestrationV2/Adapters/NoavaranEligibleSymbolResolver.cs` | `FinancialCopilotWorkflowDefinition.cs`, `FinancialCopilotWorkflowMessages.cs`, `FinancialCopilotAgentWorkflowRunner.cs`, `MessagePersistenceFunction.cs`, `ServiceCollectionExtensions.cs` |
| API / ops | none | `AiFacadeContracts.cs`, `AiFacadeController.cs`, `appsettings.json`, `docker-compose.yml` |
| Frontend | `components/app/symbol-snapshot-card.tsx` | `lib/chat.functions.ts`, `components/app/message-list.tsx` |
| Database | none | none (no migration) |

The exact file list may shift within this scope. `V1` orchestration behaviour is not changed; only exhaustive enum mappings may need a new arm.

## Implementation Tasks

### 1. Data check

- [x] 1.1 Read-only query on the Development database through the local configuration already used by the Feature 154 live runner (connection string never printed): confirm which column holds the typed ticker, list duplicate compact keys, and list eligible tickers equal to common words. Save the output under `evidence/`. Report collisions to the owner before the flag is turned on.

### 2. Contracts and detection

- [x] 2.1 Add `SymbolSnapshotResult`, section records, section status, `SymbolSnapshotOptions` (`Enabled` false, section timeout 2000 ms, turn budget 4000 ms) and the `IEligibleSymbolResolver` port.
- [x] 2.2 Add `SymbolSnapshotIntentRules` (normalization, fillers, punctuation, 40-character limit, compact key).
- [x] 2.3 Implement `NoavaranEligibleSymbolResolver` with a 10-minute cached set; a key shared by more than one company never matches; include market and group names.

### 3. Capability and routing

- [x] 3.1 Register `symbol_snapshot` (route, definition with `includeInGuidance: false`, data requirements) and add the route to `CyclicalWavesSupportedRoutes`.
- [x] 3.2 Append `DetectedIntent.SymbolSnapshot` as the last member and add its mappings.
- [x] 3.3 In `ConversationDialogueGate.PrepareAsync`, read active state first and substitute a `symbol_snapshot` interpretation for a detected ticker, skipping the hybrid interpreter. Keep all other paths unchanged.

### 4. Use case

- [x] 4.1 Expose the normalizer's comparison rule for reuse without changing its results.
- [x] 4.2 Implement `SymbolSnapshotUseCase` (sequential sections, per-section timeout, turn budget, section isolation, provider-mode check for monthly, general and banking row sets, year-over-year by fiscal year and quarter, logs and trace tags).
- [x] 4.3 Implement `SymbolSnapshotFollowUpSuggestionService` on the pattern of `QuarterlyFinancialReportFollowUpSuggestionService`.
- [x] 4.4 Implement `SymbolSnapshotCapabilityExecutor` and the Persian `TextAnswer` composition.

### 5. Workflow and contracts out

- [x] 5.1 In the V2 workflow, handle the payload, exclude this capability from the Feature 141 fallback, build suggestions in step 4, and set `workflow.intent`.
- [x] 5.2 Carry the result through the workflow messages, runner, persistence, `AiQueryResponse`, conversation contracts and both controller mappings.
- [x] 5.3 Add `SymbolSnapshot` to `appsettings.json` (off) and `SymbolSnapshot__Enabled` to `docker-compose.yml` (default false).

### 6. Frontend

- [x] 6.1 Add the types and mapping in `chat.functions.ts`, including history.
- [x] 6.2 Build `SymbolSnapshotCard`, reusing existing formatters and `FollowSymbolButton`.
- [x] 6.3 Render it from `message-list.tsx`; the card replaces the text rendering on screen.

## Tests

- [x] Unit: detection table (AC-01 to AC-03), gate guards (AC-04 to AC-06), use case sections, isolation and timeouts (AC-07 to AC-11), billing status (AC-11, AC-12), suggestion order and round-trip (AC-13), provider mode (AC-14), Telegram rendering (AC-15), `DetectedIntent` ordinals unchanged, V1 freeze tests still green (`tests/FinancialCopilot.UnitTests`).
- [x] Architecture: Application does not reference Infrastructure; the new port is implemented only in Infrastructure (`tests/FinancialCopilot.ArchitectureTests`).
- [x] Integration: resolver on the real view, and DI plus the three section reads on real SQL (`SymbolSnapshot156PostgreSqlTests`, disposable PostgreSQL). History round-trip (AC-16) is covered by a unit test on the same serializer the repository uses.
- [x] Frontend (Vitest): three rows, partial rows, banking rows, RTL numbers, accessible labels, buttons call `onSuggested`, history rendering (AC-16, AC-17).
- [x] Browser check at desktop and phone width (AC-17): `npm run test:symbol-snapshot-browser`, 12 layouts, screenshots under `evidence/browser/`.
- [~] Live Development check with the flag on locally. **Not run:** the read-only role cannot read `NoavaranEligibleCompanies`, `Markets`, `IndustryGroups`, `LatestMarketQuotes`, `MonthlyReports` or `CompanyMonthlyActivityTrendSnapshots`. Done instead: tickers and quarterly row IDs on real data (`evidence/`). Original task: live check for `اخابر`, `کگهر`, `کگل` and one bank; evidence saved under `evidence/` (AC-18).

## Definition Of Done

- [~] Solution build: 0 warnings, 0 errors. Unit: 3,146 tests, the same 29 baseline methods fail and nothing else. Architecture: 1 failure, BUG-003, in a file this feature did not change. Integration: only `SymbolSnapshot156PostgreSqlTests` was run (passes); the full suite has no baseline yet and was not run.
- [x] Frontend: typecheck stays at the 37 baseline errors. Tests: 501 pass; the 2 failing tests and 3 `*.browser.test.mjs` collection failures are in files this feature did not change. Lint: changed files have no finding except the repository-wide CRLF `prettier` noise (about 25,000 findings). Build passes into a separate output folder; `dist/client/assets` is locked on this machine (EPERM).
- [~] Acceptance criteria in `user-story.md`: 16 of 18 checked; AC-11 (one part) and AC-18 are open.
- [x] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md` (see `AGENTS.md`, "Versions and Releases").
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
