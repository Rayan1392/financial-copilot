# User Story: the provider's forward P/E and P/S on the valuation gauge cards

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-provider-forward`

## Layers / Area

Layers: backend | frontend | telegram-gateway (text only, through the shared composer)
Area: AI | FinancialData

## Priority

P2

## Complexity

S

## Depends On

- `148-cyclicalwaves-v2-independent-financial-data-provider/changes/gauge-forward-field` (the stored provider value)
- `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio` (the `forward` card member,
  the dashed needle, the Sapio estimate; released in v1.34.0)

## References

- `design-proposal.md` in this folder (Gate A approved 2026-11-28; OQ-160 to OQ-163 answered as proposed)
- `docs/providers/cyclicalwaves-v2/README.md`, `docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`
- `docs/domain-glossary.md` → Valuation Ratios
- Code: `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/`,
  `.../AI/Orchestration/ValuationGauges/ValuationGaugeTextComposer.cs`,
  `.../Infrastructure/Financial/Ingestion/CyclicalWavesV2/ValuationGaugeSnapshotReader.cs`,
  `src/frontend/src/lib/chat/valuation-gauges.ts`, `src/frontend/src/components/app/valuation-gauge-cards.tsx`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user looking at a symbol's P/E or P/S gauge,
I want the dashed forward needle to show تحلیل‌اپ's own forward ratio when it exists,
so that the card agrees with the provider's page, and I can still tell when the number is Sapio's estimate instead.

## Business Context

On 2026-11-28 تحلیل‌اپ confirmed that the gauge response carries `forward` per ratio. The owner decided that the
cards use it and that Sapio's own calculation is kept, not discarded.

## Scope

- Provider forward value on the P/E and P/S cards: web card, text answer, Telegram text.
- Source order: provider value first; Sapio's estimate only for a card without one, under its existing flag.
- A kill switch for the provider value, on by default.
- Wording by source. Documentation and glossary.

## Out Of Scope

- Any change to the Sapio estimator, its guards, its flag or its fixtures.
- History of forward values, the equilibrium card, routing, billing, the cheap/expensive label, the PNG renderer.
- A database change (the columns come from the dependency).

## Functional Requirements

1. **Source order per ratio card.** The provider value when the stored gauge has a positive one; otherwise Sapio's
   estimate when `ValuationGaugeCards:ForwardEstimate:Enabled` is on and its guards pass; otherwise no forward value.
   The estimate is not computed for a card that has a provider value, and its inputs are not read when every ratio
   card has one.
2. **Provider value.** Taken from the gauge snapshot (same freshness as the card, no extra read). Shown on a
   displayable ratio card with a rendered dial; a current value is not required. Rounded to two decimals, placed with
   the same angle and pin rule as the current needle.
3. **Not shown**, each with a reason code for telemetry: no stored value (`provider_forward_missing`); value ≤ 0
   (`provider_forward_not_positive`); a value a dial cannot place (`provider_forward_out_of_range`); card not
   displayable, including stale (`card_not_displayable`); no dial (`axis_not_renderable`); kill switch off.
4. **Payload.** `forward` gains `source`: `provider` or `sapio`. For `provider`, `methodVersion`,
   `salesGrowthFactor`, `latestSalesMonthEnd` and `latestQuarterEnd` are absent and `marginPercent` is null. A stored
   `forward` without `source` is a Sapio estimate.
5. **Nothing else changes** on the card: provider `status`, position, insights, state, credits.

## Data / Schema

None. `RatioGaugeSnapshot` gains the optional `Forward` text read from `GaugeDetails`.

## UI

| Place | Provider value | Sapio estimate (unchanged) |
|---|---|---|
| Chip, needle name, dial summary | «P/E آینده‌نگر ‹مقدار›» | «P/E آینده‌نگر (برآورد ساپیو) ‹مقدار›» |
| «جزئیات» | «نشانگر خط‌چین با حلقه P/E آینده‌نگر به محاسبهٔ تحلیل‌اپ است؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.» | today's method sentence |
| Text and Telegram | «P/E آینده‌نگر: ‹مقدار›» | «P/E آینده‌نگر (برآورد ساپیو): ‹مقدار›» |

The same wording with «P/S» on the P/S card.

## Non-Functional Requirements

- Configuration `ValuationGaugeCards:ProviderForward:Enabled`, default true. Off restores the cards as they were.
- No new query, time budget or outbound call.
- Telemetry: `valuation_gauge.forward_estimates` gains the tag `source` (`provider` or `sapio`).

## Acceptance Criteria

- [x] AC-01 A ratio card whose stored gauge has a positive forward value carries `forward` with `source: provider`,
      the value to two decimals and the angle on the card's own axis.
- [x] AC-02 A value outside the axis pins the dashed needle at the end; the solid needle is unaffected.
- [x] AC-03 A missing, zero, negative or unplaceable value shows no provider forward value, with its reason code.
- [x] AC-04 A stale or otherwise not displayable card shows no forward value.
- [x] AC-05 A card without a current value still shows the provider forward value.
- [x] AC-06 With the kill switch off, payload and text equal those of a gauge without a forward value.
- [x] AC-07 With both sources possible, the provider value is shown and the estimator inputs are not read.
- [x] AC-08 With the estimate flag on, the estimate fills in only for the card without a provider value; with the
      flag off that card has no forward value.
- [x] AC-09 The web card names the provider value without «(برآورد ساپیو)» and shows the provider sentence in
      «جزئیات»; a stored `forward` without `source` renders as the Sapio estimate.
- [x] AC-10 Text and Telegram use the wording of the table above.
- [x] AC-11 Existing estimate tests, including the 25-symbol fixtures, pass unchanged.
- [x] AC-12 Architecture tests stay green.
- [x] AC-13 `docs/providers/cyclicalwaves-v2/README.md` and `docs/domain-glossary.md` state the source order.

## Assumptions

- A1 The provider value is the dashed needle of TahlilApp's page. One sample (فولاد, 2026-11-28); to be checked
  against the 25 research symbols after the first production gauge sync.
- A2 «Keep the Sapio calculation» means: code and flag unchanged, shown only where the provider has no value (OQ-160).
- A3 Sapio's estimate was released in v1.34.0, so its flag may be on in production. There a symbol can show the
  provider value on one card and the estimate on the other; the labels tell them apart.
