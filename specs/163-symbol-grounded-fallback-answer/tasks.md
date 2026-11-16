# Tasks: Symbol-Grounded Fallback Answer

Status: Gate B: Approved by the owner on 2026-11-16 (Gate A approved the same day)

## Feature Folder

`163-symbol-grounded-fallback-answer`

Paths: backend relative to `src/backend/`, tests relative to `tests/`. Requirements: [user-story.md](user-story.md).
Decisions D1–D9: [design-proposal.md](design-proposal.md). Task ids are stable.

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (162: released in v1.13.1).
- [x] Work happens on branch `feature/163-symbol-grounded-fallback` in worktree `D:/wt/f163`, created from `develop` after the pending 162 pricing change is committed or set aside.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-097 does not block; v1 assumes option (a)).

## Phases

| Phase | Goal | Tasks | Exit condition |
|---|---|---|---|
| 1 | Tracer: prove the assumptions | T01–T02 | Resolver and hidden-payload behaviour confirmed by tests |
| 2 | Application contracts and logic | T03–T08 | Unit tests green, no I/O |
| 3 | V2 wiring, billing, config | T09–T13 | Endpoint tests green, flag off = baseline |
| 4 | Verification and docs | T14–T18 | Definition of done |

## Implementation Tasks

### 1. Tracer

- [~] T01 Characterization tests for `ICanonicalQueryEntityResolver.ResolveExactTickerFromTextAsync` on raw messages: one symbol inside a sentence, two symbols, a symbol that is also a common word, no symbol. If one unambiguous company is not reported as `Resolved` only, stop and ask the owner (user-story Assumptions). **[~] The resolver was read, not characterized by its own tests: it returns the first ticker in the text, so the handler resolves the text again without that ticker to detect a second company (endpoint test `NoSingleCompany_LeavesTheTurnUnchanged`).**
- [x] T02 `AssistantMessagePayload`: add optional trailing members `GroundingReport` (quarterly table) and `FallbackOf` (original outcome, reason code). Round-trip test: old JSON deserializes unchanged. Check the web client and `TelegramAssistantResponseRenderer` ignore the new members (test or code reading recorded in the implementation report).

### 2. Application (`FinancialCopilot.Application/AI/SymbolGroundedFallback/`)

- [x] T03 Contracts: `SymbolGroundedFallbackOptions` (`Enabled`, `InputTokenTarget`, `MaxOutputTokens`, `ModelTimeoutSeconds`, `Validate()`), `SymbolGroundedFallbackDisposition` (`Answered`, `NotAnswerable`), `SymbolGroundedFallbackResult` (unchanged | answered text + hidden report + marker | not-answerable text + chip), diagnostics record, reason codes, execution route `symbol_grounded_fallback`, billing completion status `SymbolGroundedFallbackAnswered` (2 credits).
- [x] T04 `SymbolGroundedFallbackEligibility` (pure): FR-1 over outcome, reason code, request shape and flag. The excluded reason codes come from `ChatCapabilityAvailability` and `UnavailableTopicCatalog.TechnicalAnalysisReasonCode`; no new keyword list.
- [~] T05 Port `IGroundingIncomeStatementReader` (Application-owned): company → quarterly table or none. Adapter over the existing quarterly report use case. Projection reuses `ReportDataSetBuilder`; add an overload that takes the quarterly table without an `ActiveReport` so the rule lives in one place. **[~] No new port: the handler depends on the existing Application interface `IFinancialStatementTableQueryUseCase`; `ReportDataSetBuilder.BuildQuarterly` is the shared projection.**
- [x] T06 Prompt: extract the `<report>` rendering of `ReportDiscussionContextBuilder` into a shared renderer used by both features (no behaviour change for 162, covered by its existing tests). New system prompt per FR-4 … FR-7 with the offer list passed as data (cash flow only when enabled).
- [x] T07 Port `ISymbolGroundedFallbackModel` + `LlmSymbolGroundedFallbackModel` over `IAiModelExecutionService`: workload `ExplanationGeneration`, structured contract `SymbolGroundedFallbackReply_v1` (`disposition`, `answer`, `closingOffer`, `usedEvidenceIds`), `MaxOutputTokens`, linked timeout, no retry, typed unavailable exception carrying status and code.
- [x] T08 `SymbolGroundedFallbackHandler`: eligibility → resolve company (FR-2) → read statement (FR-3) → build prompt within budget → ensure billing capacity callback → model → grounding check on `answer` + `closingOffer` → result. Fixed-sentence closing when `closingOffer` is empty. FR-9 text and chip. Every failure returns "unchanged" with a diagnostic reason (FR-11).

### 3. V2 wiring, billing, config

- [x] T09 Result-computation step in `FinancialCopilotWorkflowDefinition`: after the outcome is determined and before the fixed text is composed, call the handler once when the outcome is `Unsupported`. Answered → text, outcome `Answered`, no suggestion chips, hidden report + marker + `FallbackOf` passed to persistence. Not answerable → original text + sentence, the single chip, outcome unchanged. Unchanged → existing code path untouched. The five non-answer exits of the agent step are not edited.
- [x] T10 Report Discussion continuation: `ActiveReportResolver.TryDescribe` and `ReportDataSetBuilder.Build` read `GroundingReport` as a `QuarterlyIncomeStatement` active report. Tests: fallback answer then follow-up; `OutsideReport` deactivates it; a later shown report replaces it.
- [x] T11 Billing: derived reservation for the fallback (pattern of `TryFeature141FallbackAsync`, own idempotency suffix, usage read under the turn's correlation id); raise to 2 credits before the model call; settle `SymbolGroundedFallbackAnswered` on an answer and zero otherwise; register the status in `DefaultPricingPolicy` and wherever `ReportDiscussionAnswered` is registered. The response usage shows the fallback charge.
- [~] T12 Persistence: message persistence writes `GroundingReport`, the marker and `FallbackOf`; the Feature 155 report query reads `FallbackOf` so these turns stay visible as demand (query change only if the current SQL filters on outcome alone; otherwise note it). **[~] Payload fields are written; the Feature 155 SQL reads only `Outcome` and was not changed (backlog).**
- [x] T13 DI and config: register options, handler, reader and model in `ServiceCollectionExtensions` (V2 only); `SymbolGroundedFallback` section in `appsettings.json` and `appsettings.Development.json` (`Enabled=false`); options defaults in the C# class because production images ship without `appsettings.json`. Structured log line and activity tags per NFR.

### 4. Verification and docs

- [~] T14 Endpoint scenarios with a fake model (`SymbolGroundedFallback163EndpointTests`): AC-1 … AC-16. **[~] 11 endpoint tests cover AC-2 … AC-5, AC-7 … AC-11, AC-13; AC-12 (insufficient credit), AC-15 (Feature 141 order) and AC-16 (log content) have no endpoint test.**
- [x] T15 Architecture test (AC-17) and regression run (AC-18).
- [ ] T16 Live local check (Development, disposable PostgreSQL, flag on locally): at least 12 real questions from the Feature 155 unanswered list — general, valuation, «ارزش خرید», not answerable, plus typed replies to the closing offer with and without a symbol (OQ-097). Record disposition, tokens, latency, credits and the answer quality per question.
- [~] T17 Docs: `specs/README.md` index row; `docs/architecture.md` V2 section; `docs/domain-glossary.md` "Symbol-Grounded Fallback", "Grounding Report"; `specs/backlog.md` (balance sheet / cash flow as grounding data); `CHANGELOG.md` `[Unreleased]` Persian line. **[~] All docs updated except `CHANGELOG.md`: nothing is user-visible while the flag is off (backlog).**
- [x] T18 Implementation report and final summary (done / `[~]` / new questions). Production stays off until the owner asks to enable `SYMBOLGROUNDEDFALLBACK__ENABLED`.

## Tests

- [ ] Unit: eligibility matrix (T04), prompt content and budget (T06), model reply parsing (T07), handler paths incl. every failure (T08), resolver continuation (T10), payload round-trip (T02).
- [ ] Integration/endpoint: T14. Architecture: T15.
- [ ] No frontend work.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`.
- [ ] No database migration needed; `dotnet ef migrations has-pending-model-changes` reports none for the touched DbContext.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated (T17).
- [ ] Persian user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
