# Design proposal: valuation gauge cards — one credit per card

Status: Gate B: Approved by the owner on 2026-11-26 (Gate A on the same day; OQ-138, OQ-139 and OQ-140 answered as proposed)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-per-card-credits`

## Layers / Area

backend · Billing, AI

## Request

The owner wants each card to cost one credit (2026-11-26):

- «گیج P/E ‹نماد›»: 1 credit;
- «گیج P/S ‹نماد›»: 1 credit;
- «بازهٔ قیمت تعادلی ‹نماد›»: 1 credit;
- «گیج‌های ارزش‌گذاری ‹نماد›»: 3 credits.

This replaces owner decision OQ-131 (answered 2026-11-25): one minimum credit per usable answer, however many cards.

## Current behaviour (discovery)

- **Billing operation.** `AiQueryBillingOperationSelector` maps the `valuation_gauges` capability to `AiQuery.Scanner`. In
  `DefaultPricingPolicy`, Scanner costs 1 credit.
- **Completion status.** `SemanticBillingCompletionStatus.For` reports both `Executed` and `Partial` as `Completed`.
  `NoData`, clarification and `TemporarilyUnavailable` stay zero-charge.
- **Amount.** Cards make no model call. In the usage-based modes the amount is therefore
  `max(ceil(operation credits), MinimumCredits)`, which is 1. In Legacy mode it is the operation price, also 1.
- **Holds.** The reservation is placed before execution. `EnsureReservedCapacityAsync(handle, requiredCredits)` can raise
  the hold to a fixed amount, as change «sol-deep-analysis-web-search» does for its fixed analysis tiers.
- **Fixed-amount precedent.** `GroundedAnalysisBillingPolicy` puts the tier in the completion status
  (`GroundedAnalysisAnswered.<tier>`). `AiFacadeBillingHook.FinalizeAsync` settles it at that fixed amount and skips the
  usage-based path in every `UsageBasedCredits:Mode`. Production's mode is set outside the repository, so the new rule
  must give the same amount in Legacy, Shadow and Enforced.
- **Scope of impact.** Web chat and the Telegram assistant share this workflow. The UI shows no gauge price anywhere;
  the answer footer shows the credits actually charged.

## Proposed design

1. **One owner for the rule.** Add `ValuationGaugeBillingPolicy` (Application, next to the gauge capability):
   - `CreditsPerCard = 1`;
   - `ChargedCards(ValuationGaugeCardsResult)`, the number of usable cards;
   - `AnsweredStatus(n)`, which returns `ValuationGaugesAnswered.<n>` for n = 1–3;
   - `FixedCredits(status)`, which returns `n × CreditsPerCard`, or null for any other status.

   The price stays in code, like the operation table, so a price change is a reviewed code change.
2. **Settlement.**
   - A usable gauge answer settles with `ValuationGaugesAnswered.<n>` instead of `Completed`.
   - `AiFacadeBillingHook.FinalizeAsync` applies it the way it applies the grounded-analysis tier: a fixed amount, no
     usage-based path, the same result in every mode.
   - Answers with no usable card keep their zero-charge statuses.
3. **Hold before execution.**
   - After the reservation, the gauge route raises the hold to `requested kinds × 1` through the existing
     `EnsureReservedCapacityAsync(handle, requiredCredits)`.
   - A user without enough balance gets the existing insufficient-credit answer, and no card is read or shown.
   - Settlement then charges only the usable cards, never more than the hold.
4. **Unchanged.**
   - Unlimited plans.
   - Idempotent replay: the same key gives the same settlement.
   - Free outcomes: clarification, no data, stale only, temporarily unavailable.
   - The payload, the cards and the text.
5. **Records.**
   - OQ-131 is marked superseded by this change.
   - The provider README «Activation» paragraph is updated.
   - `CHANGELOG.md` gets a «Changed» line: «هر کارت گیج ارزش‌گذاری یک اعتبار است؛ «گیج‌های ارزش‌گذاری ‹نماد›» با سه کارت سه اعتبار.»

## Decisions for the owner

| Id | Question | Proposed |
|---|---|---|
| OQ-138 | «گیج‌های ارزش‌گذاری» where one card has no data (for example P/S missing, or the equilibrium range missing): 3 credits, or only the cards shown? | Charge only the usable cards shown (2 in that example). A card with no answer is free, as today. |
| OQ-139 | An equilibrium card with only the range (no market price; «قیمت بازار در دسترس نیست»): does it count as a card? | Yes, 1 credit, because the range is the card's main content. Alternative: free without a price. |
| OQ-140 | Balance below the requested amount (for example 2 credits for «گیج‌های ارزش‌گذاری»): refuse, or show as many cards as the balance covers? | Refuse with the existing insufficient-credit message before reading anything. Simple, predictable, no partial answers. |

## Alternatives considered

- **A separate operation `AiQuery.ValuationGauges` priced 3.** The pricing table has no quantity, so single cards and
  missing cards would be priced wrong. Rejected.
- **A completion multiplier (`CompletionMultipliers`).** It works in Legacy mode only. In the usage-based modes the
  amount comes from the token path. Rejected.

## Risks

- **Billing hook change.** A second fixed-amount status family in `AiFacadeBillingHook`. Mitigation: mirror the
  grounded-analysis path, with unit tests per mode (Legacy, Shadow, Enforced) and an integration test on the charged
  amount and the ledger entry.
- **Users see a higher price for the combined answer** (1 → 3). This is the intended product change; the CHANGELOG
  announces it.

## Outline plan (detailed at Gate B)

1. Add `ValuationGaugeBillingPolicy` with unit tests for the counts, statuses and amounts.
2. Settle the usable count in the workflow. Raise the hold to the requested count before execution.
3. Add the fixed-amount settlement in `AiFacadeBillingHook` for all three modes, with tests.
4. Integration tests:
   - single card charged 1;
   - all gauges charged 3;
   - one missing card charged 2 (per OQ-138);
   - range-only equilibrium charged per OQ-139;
   - insufficient balance per OQ-140;
   - a free no-data answer;
   - an idempotent replay.
5. Docs, OQ-131 superseded, CHANGELOG. Release only through the separately confirmed release process.
