# Tasks: Report Discussion — Context-Aware Financial Report Follow-up

Status: Gate A: Approved by the owner on 2026-11-15 — plan proposed, Gate B not approved

## Feature Folder

`162-context-aware-financial-report-follow-up`

Paths: backend relative to `src/backend/`, tests relative to `tests/`. Requirements: [user-story.md](user-story.md). Decisions D1–D9: [design-proposal.md](design-proposal.md) "Gate A Decisions". Task ids are stable so review comments can refer to them.

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done. Today: 154 is `[~]` (open release gates E-5/E-7/E-1/E-2/E-8) and 161 is in progress locally (uncommitted). **Gate B must decide:** (a) proceed with 154's implemented code and run cash-flow tasks (T07, T15-cash-flow cases) only after 161's definition of done; or (b) owner exception to build against 161's current local code.
- [ ] Work happens on a separate branch/worktree from `develop`, so Feature 161's uncommitted changes are not mixed in (if (b) is chosen, branch from the 161 work instead and say so here).
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec (OQ-088 is non-blocking: MVP uses `MonthlyActivityTrendResponse` only).

## Phases

| Phase | Goal | Tasks | Exit condition |
|---|---|---|---|
| 1 | Contracts and pure domain logic | T01–T07 | Unit tests green, no I/O |
| 2 | Adapters and persistence read | T08–T10 | Repository and model adapter tests green |
| 3 | V2 wiring and billing | T11–T14 | Endpoint tests green, flag off = baseline |
| 4 | Verification and docs | T15–T20 | DoD below |

## Implementation Tasks

### 1. Application contracts and logic (`FinancialCopilot.Application/AI/ReportDiscussion/`)

- [ ] T01 Contracts: `ReportFamily` (`QuarterlyIncomeStatement`, `BalanceSheet`, `CashFlow`, `MonthlySales`), `ActiveReport` (source assistant message ID, family, company, scope, periods, unit, currency, provider), `ReportFact` (evidence ID, label, period, value, unit, source IDs), `ReportCalculation` (code, period, value, formula, operand evidence IDs), `ReportDiscussionDisposition` (`Answered`, `MissingInReport`, `NeedsDifferentData`, `OutsideReport`), `ReportDiscussionOptions` (Enabled=false, LookbackMessages=20, RecentMessages=6, InputTokenTarget=5000 [4000–6000 validated], RecentMessagesTokenCap=1200, MaxOutputTokens=650, ModelTimeoutSeconds=20). Options validated at startup.
- [ ] T02 Payload marker: add optional `ReportDiscussionMarker? ReportDiscussion = null` (`SourceAssistantMessageId`, `ReportFamily`, `Disposition`) as the last parameter of `AssistantMessagePayload`. Old JSON deserializes unchanged (round-trip test).
- [ ] T03 `ActiveReportResolver` (pure): given recent `MessageRecord`s, returns the active report per FR-2 or none. Covers: newer report replaces, normal-route answer deactivates, `OutsideReport` deactivates, `MissingInReport` keeps, non-`Answered` report outcome ignored, lookback exhaustion.
- [ ] T04 Fact extractors, one per family, beside the existing report contracts (no provider logic in chat code): quarterly table rows, `BalanceSheetReport`, `CashFlowReport` (161), `MonthlyActivityTrendResponse` (latest month, previous month, same month last year, 12-month average, YTD, chart points). Stable evidence IDs; unit/scope/period carried on every fact.
- [ ] T05 `ReportCalculations` (D5): margins, QoQ/YoY growth, share of revenue, current/debt/equity ratios; reuse Feature 161 `CashFlowMetricsCalculator` outputs and monthly payload percentages instead of recomputing. Missing operand → no calculation. Reuse existing quarterly margin code where it exists.
- [ ] T06 `ReportDiscussionContextBuilder`: system prompt (design-proposal §B, adjusted: "use only supplied facts and calculations; never compute"), delimited JSON data block, recent messages, current question; conservative token estimator (configurable chars-per-token, calibrated later from provider usage); period trimming then `NarrowRequired` result per FR-4.
- [ ] T07 `NumericGroundingValidator`: normalizes Persian/Arabic/Latin digits, `٬`/`,` separators, `٫`/`.` decimals, `%`/`درصد`; allowed set = facts (raw and display-scaled), calculations (display rounding), numbers in period labels, company identity and the question. Returns pass/fail with the offending count only.

### 2. Adapters

- [ ] T08 `IMessageRepository.ListRecentAsync(conversationId, tenantId, actorId, take)` with an EF query bounded by `take`, ordered by `CreatedAt` desc, tenant/actor scoped through the conversation; returns `MessageRecord` with payload. Infrastructure test for scope and bound.
- [ ] T09 `ReportDiscussionModelGateway` port (Application) + adapter over `IAiModelExecutionService`: `ExplanationGeneration` workload, structured output contract `{disposition, answer, usedEvidenceIds}`, `MaxOutputTokens`, linked `CancellationTokenSource` timeout, no tools, no retry; malformed output → treated as provider failure (0 credits). Usage facts recorded through the existing accumulator under the turn's correlation ID.
- [ ] T10 `ReportDiscussionHandler` (Application use case): flag/V2/pending-dialogue/active-report checks → context → model → grounding → result (`Answered` with text and marker | zero-credit deterministic Persian message | `HandBack`). Persian messages for missing-in-report, narrow, grounding failure kept in one resource class.

### 3. V2 wiring and billing

- [ ] T11 `PrepareStartMessageAsync`: after ownership check and reservation, call the handler for existing conversations only; on a non-hand-back result put it on `WorkflowStartMessage` and skip `dialogueGate.PrepareAsync`; on hand-back run the gate exactly as today. Exceptions in the handler → logged, hand back (the discussion path must never break the normal route).
- [ ] T12 Step 1 skips memory enrichment and step 3 returns the precomputed result as an early branch (pattern of the P/S gauge branch) with `TextAnswer`, `ReportDiscussion` marker, `Outcome`/reason code, execution route `report_discussion`; steps 4–7 unchanged.
- [ ] T13 Billing (D2): before the model call, raise the hold to 2 credits for the cash-flow family (reuse `EnsureReservedCapacityAsync`/hook; add a targeted-capacity overload only if the existing call cannot express 2 credits); insufficient credit → existing insufficient-credit response, no model call. Finalization statuses: `ReportDiscussionAnswered` (1×), `ReportDiscussionAnsweredCashFlow` (multiplier 2.0 in `DefaultPricingPolicy.CompletionMultipliers`), zero-credit cases use existing zero statuses (`NoData`, `ProviderFailed`, `TimedOutBeforeExecution`). Hand-back: discussion cost absorbed, turn finalized by the normal route only. Operation code stays `AiQuery.Scanner`, so plan entitlements and seeds are unchanged (no migration). Note in code comment: if usage-based billing moves to `Enforced` for this operation, it replaces fixed credits (existing behavior).
- [ ] T14 DI and config: register in `ServiceCollectionExtensions` (V2 only); `ReportDiscussion` section in `appsettings.json` (Enabled=false) and `appsettings.Development.json` (Enabled=false; enabled locally only for verification). Telemetry tags per FR-12; no report values or answer text in logs.

### 4. Verification and docs

- [ ] T15 Endpoint scenarios in `AiFacadeV2EndpointTests` style (new `ReportDiscussion162EndpointTests`) with a fake model gateway: AC-1…AC-16.
- [ ] T16 Architecture test: `AI/ReportDiscussion` in Application has no Infrastructure/EF/HTTP references; V1 orchestration has no reference to it.
- [ ] T17 Regression: flag off keeps existing suites green for 154 (`BalanceSheetReport154*`), 161 (`CashFlowReport161*`), monthly activity and quarterly table tests.
- [ ] T18 Live local check (Development, disposable PostgreSQL, flag on locally): one conversation per family — report, then 3 discussion questions (analytical, exact, missing), one other-symbol hand-back, one unrelated hand-back; record tokens, latency, credits and screenshots in `evidence/`.
- [ ] T19 Docs: `specs/README.md` index row; `docs/architecture.md` V2 section (Report Discussion branch); `docs/domain-glossary.md` "Report Discussion", "Active Report"; backlog entry for rolling summary; `CHANGELOG.md` `[Unreleased]` Persian line.
- [ ] T20 Implementation report and final summary (done / `[~]` / new questions).

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
