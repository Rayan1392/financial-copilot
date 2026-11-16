# Tasks: Report Discussion — Context-Aware Financial Report Follow-up

Status: Gate B: Approved by the owner on 2026-11-15

## Feature Folder

`162-context-aware-financial-report-follow-up`

Paths: backend relative to `src/backend/`, tests relative to `tests/`. Requirements: [user-story.md](user-story.md). Decisions D1–D9: [design-proposal.md](design-proposal.md) "Gate A Decisions". Task ids are stable so review comments can refer to them.

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done. **Decision at Gate B (2026-11-15): option (a).** 154 is `[~]` (open release gates E-5/E-7/E-1/E-2/E-8) but its implemented code is used as is; 161 is in progress locally, so the cash-flow family (T04-cash-flow, T05-cash-flow, T13 2-credit status, T15 AC-16) is deferred until Feature 161 meets its definition of done and is tracked as `[~]`.
- [x] Work happens on branch `feature/162-report-discussion` in worktree `D:/wt/f162`, created from `develop` `ff741c6`, so Feature 161's uncommitted changes are not mixed in.
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-088 answered (a) at Gate B: MVP uses `MonthlyActivityTrendResponse` only).

## Phases

| Phase | Goal | Tasks | Exit condition |
|---|---|---|---|
| 1 | Contracts and pure domain logic | T01–T07 | Unit tests green, no I/O |
| 2 | Adapters and persistence read | T08–T10 | Repository and model adapter tests green |
| 3 | V2 wiring and billing | T11–T14 | Endpoint tests green, flag off = baseline |
| 4 | Verification and docs | T15–T20 | DoD below |

## Implementation Tasks

### 1. Application contracts and logic (`FinancialCopilot.Application/AI/ReportDiscussion/`)

- [x] T01 Contracts: `ReportFamily` (`QuarterlyIncomeStatement`, `BalanceSheet`, `CashFlow`, `MonthlySales`), `ActiveReport` (source assistant message ID, family, company, scope, periods, unit, currency, provider), `ReportFact` (evidence ID, label, period, value, unit, source IDs), `ReportCalculation` (code, period, value, formula, operand evidence IDs), `ReportDiscussionDisposition` (`Answered`, `MissingInReport`, `NeedsDifferentData`, `OutsideReport`), `ReportDiscussionOptions` (Enabled=false, LookbackMessages=20, RecentMessages=6, InputTokenTarget=5000 [4000–6000 validated], RecentMessagesTokenCap=1200, MaxOutputTokens=650, ModelTimeoutSeconds=20). Options validated at startup.
- [x] T02 Payload marker: add optional `ReportDiscussionMarker? ReportDiscussion = null` (`SourceAssistantMessageId`, `ReportFamily`, `Disposition`) as the last parameter of `AssistantMessagePayload`. Old JSON deserializes unchanged (round-trip test).
- [x] T03 `ActiveReportResolver` (pure): given recent `MessageRecord`s, returns the active report per FR-2 or none. Covers: newer report replaces, normal-route answer deactivates, `OutsideReport` deactivates, `MissingInReport` keeps, non-`Answered` report outcome ignored, lookback exhaustion.
- [~] T04 Fact extractors, one per family, beside the existing report contracts (no provider logic in chat code): quarterly table rows, `BalanceSheetReport`, `CashFlowReport` (161), `MonthlyActivityTrendResponse` (latest month, previous month, same month last year, 12-month average, YTD, chart points). Stable evidence IDs; unit/scope/period carried on every fact.
- [x] T05 `ReportCalculations` (D5): margins, QoQ/YoY growth, share of revenue, current/debt/equity ratios; reuse Feature 161 `CashFlowMetricsCalculator` outputs and monthly payload percentages instead of recomputing. Missing operand → no calculation. Reuse existing quarterly margin code where it exists.
- [x] T06 `ReportDiscussionContextBuilder`: system prompt (design-proposal §B, adjusted: "use only supplied facts and calculations; never compute"), delimited JSON data block, recent messages, current question; conservative token estimator (configurable chars-per-token, calibrated later from provider usage); period trimming then `NarrowRequired` result per FR-4.
- [x] T07 `NumericGroundingValidator`: normalizes Persian/Arabic/Latin digits, `٬`/`,` separators, `٫`/`.` decimals, `%`/`درصد`; allowed set = facts (raw and display-scaled), calculations (display rounding), numbers in period labels, company identity and the question. Returns pass/fail with the offending count only.

### 2. Adapters

- [x] T08 `IMessageRepository.ListRecentAsync(conversationId, tenantId, actorId, take)` with an EF query bounded by `take`, ordered by `CreatedAt` desc, tenant/actor scoped through the conversation; returns `MessageRecord` with payload. Infrastructure test for scope and bound.
- [x] T09 `ReportDiscussionModelGateway` port (Application) + adapter over `IAiModelExecutionService`: `ExplanationGeneration` workload, structured output contract `{disposition, answer, usedEvidenceIds}`, `MaxOutputTokens`, linked `CancellationTokenSource` timeout, no tools, no retry; malformed output → treated as provider failure (0 credits). Usage facts recorded through the existing accumulator under the turn's correlation ID.
- [x] T10 `ReportDiscussionHandler` (Application use case): flag/V2/pending-dialogue/active-report checks → context → model → grounding → result (`Answered` with text and marker | zero-credit deterministic Persian message | `HandBack`). Persian messages for missing-in-report, narrow, grounding failure kept in one resource class.

### 3. V2 wiring and billing

- [x] T11 `PrepareStartMessageAsync`: after ownership check and reservation, call the handler for existing conversations only; on a non-hand-back result put it on `WorkflowStartMessage` and skip `dialogueGate.PrepareAsync`; on hand-back run the gate exactly as today. Exceptions in the handler → logged, hand back (the discussion path must never break the normal route).
- [x] T12 Step 1 skips memory enrichment and step 3 returns the precomputed result as an early branch (pattern of the P/S gauge branch) with `TextAnswer`, `ReportDiscussion` marker, `Outcome`/reason code, execution route `report_discussion`; steps 4–7 unchanged.
- [~] T13 Billing (D2): before the model call, raise the hold to 2 credits for every supported report family; insufficient credit → existing insufficient-credit response, no model call. Every answered discussion uses `ReportDiscussionAnswered` (2×); zero-credit cases use existing zero statuses (`NoData`, `ProviderFailed`, `TimedOutBeforeExecution`). Hand-back: discussion cost absorbed, turn finalized by the normal route only. Operation code stays `AiQuery.Scanner`, so plan entitlements and seeds are unchanged (no migration). In Enforced usage-based mode, the measured charge replaces the fixed charge. See `changes/price-all-report-discussions-at-two-credits/`.
- [x] T14 DI and config: register in `ServiceCollectionExtensions` (V2 only); `ReportDiscussion` section in `appsettings.json` (Enabled=false) and `appsettings.Development.json` (Enabled=false; enabled locally only for verification). Telemetry tags per FR-12; no report values or answer text in logs.

### 4. Verification and docs

- [x] T15 Endpoint scenarios in `AiFacadeV2EndpointTests` style (new `ReportDiscussion162EndpointTests`) with a fake model gateway: AC-1…AC-16.
- [x] T16 Architecture test: `AI/ReportDiscussion` in Application has no Infrastructure/EF/HTTP references; V1 orchestration has no reference to it.
- [x] T17 Regression: flag off keeps existing suites green for 154 (`BalanceSheetReport154*`), 161 (`CashFlowReport161*`), monthly activity and quarterly table tests.
- [~] T18 Live local check (Development, disposable PostgreSQL, flag on locally): one conversation per family — report, then 3 discussion questions (analytical, exact, missing), one other-symbol hand-back, one unrelated hand-back; record tokens, latency, credits and screenshots in `evidence/`.
- [~] T19 Docs: `specs/README.md` index row; `docs/architecture.md` V2 section (Report Discussion branch); `docs/domain-glossary.md` "Report Discussion", "Active Report"; backlog entry for rolling summary; `CHANGELOG.md` `[Unreleased]` Persian line.
- [x] T20 Implementation report and final summary (done / `[~]` / new questions).

## Implementation Notes (2026-11-15)

- T05: margins, share of revenue, YoY growth and current/debt ratios are computed in `ReportDataSetBuilder`; QoQ changes, gross margin, the equity ratio and monthly percentages are copied from the report, which already computes them deterministically. The quarterly banking template contributes its rows and QoQ changes; revenue-based margins apply to the general template only.
- T08: implemented as a role-sized port `IReportDiscussionMessageReader` (Infrastructure `ReportDiscussionMessageReader`) instead of a new `IMessageRepository` member, so existing repository fakes stay unchanged; same bound and owner scope.
- T09: `LlmReportDiscussionModel` lives in Application next to the other `IAiModelExecutionService` users (no Infrastructure adapter was needed).
- T11: state checks before the model call also skip suggestion-chip clicks and insight/alert actions (`structured_action`): they request specific data through their existing deterministic routes. This is a state check, not a keyword rule (D3).
- T15: endpoint scenarios cover AC-1, 2, 3, 4, 5, 7 (1-credit and 0-credit paths), 10, 12 and 14. AC-6, AC-9 and AC-13 are covered by unit tests. AC-8 holds by construction (the active report is read from PostgreSQL payloads, no in-memory state). AC-11: billing is idempotent through the existing reservation and ledger keys; a retried request re-runs the turn and persists it again, which is the existing behaviour of every route (not changed here). AC-15 is unit-tested only (no monthly fixture in the endpoint factory). AC-16 waits for Feature 161.
- T18: live local check not run yet (needs Development stack, disposable PostgreSQL and a real model key).
- T19: architecture, glossary, specs index and backlog updated. The CHANGELOG line is held back until the flag is enabled anywhere, because with `ReportDiscussion:Enabled=false` nothing is user-visible.

## Implementation Notes (2026-11-16)

- `[~]` T04 / T13: income statement, balance sheet and monthly sales Report Discussion use the shared two-credit `ReportDiscussionAnswered` status. The cash-flow discussion extractor remains deferred until Feature 161 meets its definition of done; when supported, it uses the same answered status. Zero-credit turns use `NoData` / `ProviderFailed`. Focused billing verification remains pending.

## Tests

- [ ] Unit: T03 resolver, T04 extractors, T05 calculations, T06 budget/trimming/narrow, T07 grounding (digits, separators, percentages, rounding, label numbers, injection text), T10 handler dispositions, T13 pricing statuses.
- [ ] Integration: T08 repository scope/bound; T15 endpoint scenarios incl. isolation, idempotency, timeout, restart (PostgreSQL suite where available).
- [ ] Architecture: T16.
- [ ] Frontend: none (no UI change).

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`.
- [ ] No database change: `dotnet ef migrations has-pending-model-changes --context ConversationDbContext` reports none.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated (T19).
- [ ] Persian user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects → `specs/bugs.md`.
- [ ] Gate B does not authorize release, tag, production deploy or enabling the flag in any shared environment.

## Risks Accepted At Gate B

- Every message in a conversation with an active report costs one discussion model call before a hand-back (≈1–3 s and absorbed tokens on hand-back turns). Mitigation: bounded input, telemetry on hand-back rate; revisit if the hand-back rate is high.
- Model disposition can be wrong: a `NeedsDifferentData` misread as `Answered` is caught when the answer cites numbers not in the report (grounding check); a pure-text wrong answer is not. Mitigation: prompt instructs `NeedsDifferentData` whenever another company, report or period is named; endpoint tests cover these cases.
