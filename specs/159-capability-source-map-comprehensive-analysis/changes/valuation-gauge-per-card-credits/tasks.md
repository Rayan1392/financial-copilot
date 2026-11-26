# Tasks: Valuation gauge cards — one credit per card

Status: Gate B: Approved by the owner on 2026-11-26

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-per-card-credits`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked). Released in v1.29.2 with the flag on; only its staging-evidence items stay open.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

### 1. Policy (Application)

- [x] 1.1 Add `ValuationGaugeBillingPolicy` in `src/backend/FinancialCopilot.Application/AI/Orchestration/ValuationGauges/`:
  `CreditsPerCard = 1`, `AnsweredStatusPrefix = "ValuationGaugesAnswered."`, `ChargedCards(ValuationGaugeCardsResult)`
  (count of `IsDisplayable()` cards), `AnsweredStatus(int)` (1–3, else `ArgumentOutOfRangeException`),
  `TryParseAnsweredStatus`, `FixedCredits(string?)` (n × 1 or null), and `RequiredCredits(ValidatedQueryFrame)`
  (normalized requested kinds × 1).
- [x] 1.2 `SemanticBillingCompletionStatus.For(CapabilityExecutionResult)`: an `Executed`/`Partial` result with a
  `ValuationGaugeCardsResult` payload and at least one usable card returns `ValuationGaugeBillingPolicy.AnsweredStatus(n)`.
  Every other result keeps its current status.

### 2. Settlement (Infrastructure)

- [x] 2.1 `AiFacadeBillingHook.FinalizeAsync`: resolve the fixed amount from `GroundedAnalysisBillingPolicy.FixedCredits`
  or `ValuationGaugeBillingPolicy.FixedCredits`. When either applies, set `CreditsCharged` and skip the report-discussion
  adjustment and both usage-based blocks (before and after commit), as today for grounded analysis.
- [x] 2.2 **Not needed (see Deviations):** `FinancialCopilotWorkflowDefinition`, semantic branch: settle the gauge result with
  `SemanticBillingCompletionStatus.For(semantic.Execution)` instead of `For(semantic.Execution.Status)`, so the
  deferred settlement uses the same owner as the coordinator.
- [x] 2.3 Same branch, before `ExecuteWithReservationAsync`: when the frame is `valuation_gauges`, carries a symbol,
  and `msg.Reservation` is not null, raise the hold with
  `billingFunctions.EnsureReservedCapacityAsync(reservation, ValuationGaugeBillingPolicy.RequiredCredits(frame), ct)`
  and pass the raised handle on. Let `InsufficientCreditException` propagate to the existing insufficient-credit
  handling, as the grounded-analysis path does.
- [x] 2.4 `DefaultPricingPolicy`: add a comment next to the Scanner entry naming the `ValuationGaugesAnswered.*` statuses
  and their owner. The table value stays 1.

### 3. Records

- [x] 3.1 `docs/providers/cyclicalwaves-v2/README.md` «Activation»: replace the OQ-131 sentence with the per-card rule.
- [x] 3.2 `specs/open-questions.md`: OQ-131 superseded (done at Gate A); confirm the OQ-138–140 answers.
- [x] 3.3 `CHANGELOG.md` `[Unreleased]` → `### Changed`: «هر کارت گیج ارزش‌گذاری یک اعتبار است؛ «گیج‌های ارزش‌گذاری ‹نماد›» با سه کارت سه اعتبار.»
- [x] 3.4 `specs/README.md`: index entry for this change.

## Tests

- [x] T1 Unit, `ValuationGaugeBillingPolicyTests`: counts for 0–3 usable cards, Partial counted, range-only equilibrium
  counted, NoData/Stale/TemporarilyUnavailable not counted; status round trip; `FixedCredits` null for
  `Completed` and `GroundedAnalysisAnswered.*`; `RequiredCredits` for one kind, two kinds, and the empty slot (3).
- [x] T2 Unit, `SemanticBillingCompletionStatus`: gauge Executed → `.3`, gauge Partial with 2 usable → `.2`, other
  payloads → `Completed`, gauge NoData → `NoData`.
- [x] T3 Hook tests, `ValuationGaugeBillingTests` (integration, through `BillingHookHarness`, as for grounded analysis), `AiFacadeBillingHook`: `ValuationGaugesAnswered.1/2/3` charge 1/2/3 in Legacy, Shadow and Enforced, and
  the usage accumulator is not claimed (AC-08). Unlimited is charged 0 (AC-09).
- [x] T4 Integration, `ValuationGaugeCardsE2ETests`: update `AllGauges_ComeInFixedOrder_ForOneMinimumCharge` to 3
  credits and rename it; add a missing-card answer (2 credits), a range-only equilibrium (1 credit), an insufficient
  balance of 2 for all gauges (insufficient-credit outcome, snapshot reader not called, balance unchanged), and keep
  the single-card, free, retried-request and no-symbol tests (AC-01 to AC-07).
- [x] T5 Existing billing and grounded-analysis tests stay green (AC-10).

## Definition Of Done

- [x] `dotnet test` (unit, integration, architecture): no new failure against clean base 864b7344. Unit 29/4176 (the baseline list); architecture 24/24; integration 195/1267, all failing on the base too except two that pass alone and failed only under concurrent load (`ARetriedAllGaugesRequestSettlesOnceAtThreeCredits`: a read timeout makes the answer free by design; `TelegramFinancialStatement143EndToEndTests.Gateway_post_with_auto_backfill_disabled…`).
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated: `specs/README.md` index entry, `docs/providers/cyclicalwaves-v2/README.md`.
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [ ] Release, tag and deploy only after the owner's separate confirmation.

## Deviations

- **2.2 not needed.** A gauge answer with a usable card is `Executed` or `Partial`, which the coordinator never defers,
  so it always settles inside `SemanticExecutionCoordinator` with `SemanticBillingCompletionStatus.For(result)`. The
  deferred branch in the workflow only settles fallback-eligible statuses, which stay free. Changing it would alter the
  recorded status of other capabilities, so it was left as it is.
- **Hold raise is skipped when the hold already covers the cards** (`ReservedCredits < required`). A retried request
  reuses its first, already settled reservation, which cannot be upgraded (`Only a reserved usage reservation can be
  upgraded`); the first version returned 500 on a retry.
- **Replay reuses the original hold in every mode.** `AiFacadeBillingHook.GetReservationCreditsAsync` looked up the
  existing reservation only in Enforced mode, so in Legacy mode a retry of a raised hold asked for 1 credit against a
  stored 3 and got 409 «Idempotency key conflict». The lookup now comes first in every mode. For a hold that was never
  raised the amount is unchanged.
- **T3 runs as integration tests** through `BillingHookHarness` (Enforced, Shadow, Legacy factories), the same seam as
  `GroundedAnalysisBillingTests`, because the hook needs the billing database.
