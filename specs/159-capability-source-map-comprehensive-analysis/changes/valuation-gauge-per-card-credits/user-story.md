# User Story: Valuation gauge cards — one credit per card

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-per-card-credits`

## Layers / Area

Layers: backend
Area: Billing, AI

## Priority

P1

## Complexity

M

## Depends On

`159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards` (released in v1.29.2)

## References

- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-per-card-credits/design-proposal.md`
  (approved at Gate A on 2026-11-26)
- `specs/open-questions.md`: OQ-131 (superseded), OQ-138, OQ-139, OQ-140 (answered at Gate A)
- `docs/providers/cyclicalwaves-v2/README.md`, section «Activation»
- Fixed-amount precedent: `src/backend/FinancialCopilot.Application/AI/GroundedAnalysis/GroundedAnalysisPricing.cs`
  (`GroundedAnalysisBillingPolicy`) and its settlement in
  `src/backend/FinancialCopilot.Infrastructure/Billing/AiFacadeBillingHook.cs` (`FinalizeAsync`)
- Gauge capability: `src/backend/FinancialCopilot.Application/AI/Orchestration/ValuationGauges/` and
  `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/ValuationGaugeContracts.cs`
- Settlement status: `SemanticBillingCompletionStatus` in
  `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutionContracts.cs`
- V2 workflow: `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  (semantic capability branch)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As the product owner,
I want each valuation gauge card to cost one credit,
so that the price of a gauge answer matches what the user receives: one card, one credit; three cards, three credits.

## Business Context

Today every usable gauge answer costs one credit, however many cards it shows (OQ-131). The combined answer
«گیج‌های ارزش‌گذاری ‹نماد›» shows three cards for the price of one. The owner set the price per card on 2026-11-26.
Web chat and the Telegram assistant share the V2 workflow, so both channels change together.

## Scope

- One owner for the rule: `ValuationGaugeBillingPolicy` in the Application layer.
- Settlement of a usable gauge answer at a fixed amount, the number of usable cards × 1, in every
  `UsageBasedCredits:Mode` (Legacy, Shadow, Enforced).
- A hold raised to the number of requested cards × 1 before any gauge data is read.
- Tests, the provider README, the `CHANGELOG.md` line, and OQ-131 marked superseded.

## Out Of Scope

- Any change to the cards, the payload, the text answer or the menu shortcut.
- Showing the price in the UI before the question is sent (→ `specs/backlog.md` if the owner wants it).
- Prices of other capabilities, including the older Feature 115/148 gauge intercepts (`PsGaugeIntentRules`,
  `CyclicalWavesV2GaugeIntentRules`), which keep their current single «Completed» charge.
- A configurable price. The price stays in code, like the operation table.

## Functional Requirements

1. **Counting.** A usable card is one with state `Available` or `Partial` (`ValuationGaugeCard.IsDisplayable()`).
   A range-only equilibrium card (no market price) is usable and costs 1 credit (OQ-139). A card that is no data,
   stale or temporarily unavailable is free (OQ-138).
2. **Settlement status.** A gauge answer with n usable cards (n = 1–3) settles with the completion status
   `ValuationGaugesAnswered.<n>`. `SemanticBillingCompletionStatus.For(CapabilityExecutionResult)` returns it for
   an `Executed` or `Partial` result whose payload is `ValuationGaugeCardsResult`. All other gauge outcomes keep
   their current statuses: `NoData`, `ClarificationRequired`, `ProviderFailed` and `ValidationFailed`, all free.
3. **Amount.** `AiFacadeBillingHook.FinalizeAsync` charges `ValuationGaugeBillingPolicy.FixedCredits(status)`
   (n × 1) for those statuses. It skips the usage-based path and the report-discussion adjustment, exactly as it
   does for `GroundedAnalysisAnswered.*`. The amount is the same in Legacy, Shadow and Enforced mode.
4. **Hold before execution.** In the V2 semantic branch, before the gauge capability runs, the turn's reservation
   is raised to `requested kinds × 1` with `EnsureReservedCapacityAsync(handle, requiredCredits)`. An empty kinds
   slot means all three kinds, so the hold is 3. This happens only when the frame carries a symbol. A frame
   without a symbol keeps today's free clarification.
5. **Insufficient balance (OQ-140).** If the balance does not cover the hold, the existing insufficient-credit
   outcome is returned. No gauge data is read, no card is shown, and nothing is charged. The hold is released by
   the existing reservation upgrader.
6. **Unchanged.** Unlimited plans are not held or charged. An idempotent replay with the same key returns the
   original settlement. A retried request settles once.

## Data / Schema

None. No migration. The completion status is a string column that already stores `GroundedAnalysisAnswered.*`.

## API (if any)

No contract change. `usage.creditsCharged` in the existing AI query response carries the new amount.

## AI Query Path (if any)

V2 only (`specs/POLICY-V1-FREEZE.md`). Capability `valuation_gauges`. The route, the tools and the outcomes for
missing data are unchanged. Only the billing hold and the settlement change.

## UI (if any)

None. The answer footer already shows the credits actually charged.

## Non-Functional Requirements

- No extra outbound call. The hold raise is one existing database operation, before the snapshot read.
- Logging: the existing settlement log carries the completion status. No new log lines.
- No secrets and no configuration change.

## Acceptance Criteria

- [x] AC-01: «گیج P/E ‹نماد›», «گیج P/S ‹نماد›» and «بازهٔ قیمت تعادلی ‹نماد›» with a usable card each charge
  1 credit (`usage.creditsCharged = 1`, ledger status `ValuationGaugesAnswered.1`).
- [x] AC-02: «گیج‌های ارزش‌گذاری ‹نماد›» with three usable cards charges 3 credits (`ValuationGaugesAnswered.3`).
- [x] AC-03: the combined answer with one card unavailable charges 2 credits (OQ-138).
- [x] AC-04: a range-only equilibrium card charges 1 credit (OQ-139).
- [x] AC-05: with a balance of 2 credits, «گیج‌های ارزش‌گذاری ‹نماد›» returns the existing insufficient-credit
  outcome. The gauge snapshot reader is not called, no card is shown, and the balance stays 2 (OQ-140).
- [x] AC-06: an answer with no usable card, a clarification (no symbol) and a provider timeout charge 0.
- [x] AC-07: a retried request with the same idempotency key settles once with the same amount.
- [x] AC-08: the amounts in AC-01 to AC-03 are identical in Legacy, Shadow and Enforced mode (hook unit tests).
- [x] AC-09: an Unlimited plan is not held or charged for any gauge answer. Covered by the shared fixed-amount path: the hold raise returns early for Unlimited, and `GroundedAnalysisEnforcedBillingTests.UnlimitedAccount_IsChargedNothing…` settles a fixed status at zero.
- [x] AC-10: the Feature 115/148 gauge intercepts and every other capability are charged as before (existing
  tests stay green).

## Assumptions

- The owner's answers at Gate A on 2026-11-26: OQ-138, OQ-139 and OQ-140 as proposed.
- `MaximumCreditsPerQuestion` in Enforced mode is at least 3. If it is lower, the hold raise still covers 3,
  because `EnsureReservedCapacityAsync(handle, requiredCredits)` takes the larger value.
