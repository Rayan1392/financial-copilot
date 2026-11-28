# Design Proposal: show the provider's forward P/E and P/S on the valuation gauge cards

Status: Gate A: Approved by the owner on 2026-11-28 («Gate A and Gate B را تایید میکنم»; OQ-160 to OQ-163 answered as proposed)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-provider-forward`

## Layers / Area

backend · frontend · telegram-gateway (text only) · AI / FinancialData. No database change.

## 1. Request and goal

On 2026-11-28 the owner decided: the gauge cards use the forward P/E and P/S that تحلیل‌اپ now returns, and Sapio's own
calculation is kept, not discarded. This closes the direction of OQ-136.

## 2. Discovery

- The provider value is stored per gauge in `cyclicalwaves."GaugeDetails"` (`Forward*`, nullable) by change
  `148-…/changes/gauge-forward-field`. Rows fill on the first gauge sync after that change is deployed.
- The card already has everything for a second needle (change `valuation-gauge-forward-ratio`, flag
  `ValuationGaugeCards:ForwardEstimate:Enabled`, default off): the optional `forward` member of the card payload, the
  dashed needle with a ring, the chip, the «جزئیات» sentence and the text line. All of them say «برآورد ساپیو».
- `ValuationGaugeSnapshotReader` reads the gauge row but not `Forward`; `RatioGaugeSnapshot` has no forward member.
- One sample only (فولاد: 3.82 and 0.86). Unknown: the value when the provider has no forecast, and whether it can be
  zero or negative.

## 3. Proposed behaviour

- **Source order per card.** The provider value when the stored gauge has a positive one. Otherwise Sapio's estimate,
  only when its existing flag is on and its guards pass. Otherwise no forward needle. Where the estimate flag is off, a card
  shows the provider value or nothing; where it is on (the estimate was released in v1.34.0), the estimate is shown
  only for a card without a provider value.
- **Provider value.** Read with the gauge snapshot, so it has the card's freshness; no extra query and no extra time
  budget. A value ≤ 0 is not drawn (reason code `provider_forward_not_positive`). It is rounded to two decimals and
  pinned at the axis end when outside the axis, exactly as the estimate is.
- **Sapio estimate.** Code, tests, configuration and flag stay as they are. It is not computed for a card that
  already has a provider value.
- **Payload.** `forward` gains `source`: `"provider"` or `"sapio"`. For `provider`, `methodVersion` and the
  method figures (growth factor, margin, months, quarter) are absent. A stored card whose `forward` has no `source`
  is a Sapio estimate (cards stored while the estimate flag was on).
- **Wording by source.**

  | Place | Provider value | Sapio estimate (unchanged) |
  |---|---|---|
  | Chip and needle name | «P/E آینده‌نگر ‹مقدار›» | «P/E آینده‌نگر (برآورد ساپیو) ‹مقدار›» |
  | «جزئیات» | «نشانگر خط‌چین با حلقه P/E آینده‌نگر به محاسبهٔ تحلیل‌اپ است؛ پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.» | today's method sentence |
  | Text and Telegram | «P/E آینده‌نگر: ‹مقدار›» | «P/E آینده‌نگر (برآورد ساپیو): ‹مقدار›» |

- **Kill switch.** `ValuationGaugeCards:ProviderForward:Enabled`, default **true**. Off restores today's cards.
- **Unchanged.** The cheap/expensive label (provider `status`), insights, card state, routing, billing, the
  equilibrium card.

## 4. Architecture

- `RatioGaugeSnapshot` gains `Forward` (text, nullable); `ValuationGaugeSnapshotReader` fills it.
- `ValuationGaugeProjector` attaches the provider forward to a displayable ratio card with a dial.
- `ValuationGaugeCardsUseCase` asks for the Sapio estimate only for ratio cards still without a forward value.
- `ValuationGaugeTextComposer` and the web card choose the wording from `source`.
- Telemetry: the existing `valuation_gauge.forward_estimates` counter gains a `source` tag.

## 5. Testing

- Projector: provider value inside, outside (pinned) and ≤ 0; rounding.
- Use case: provider value wins; estimate fills in only when its flag is on; kill switch off equals today's payload.
- Composer and web card: both wordings; a stored card without `source` renders as the Sapio estimate.
- Existing estimate tests (fixtures of 25 symbols) stay unchanged and green. No live provider or LLM call.

## 6. Risks

- **Meaning of the value** is assumed to be the dashed needle of TahlilApp's page. Check: after the first sync,
  compare the stored values with the 25 research symbols (read-only).
- **Two different numbers over time.** A symbol could show the provider value one day and the estimate another day
  if the estimate flag is on and the provider value disappears. The label shows which one it is.
- **Sparse values** right after deployment: no needle until the first gauge sync has run.

## 7. Open questions (owner decisions)

- **OQ-160** What "keep the Sapio calculation" means for display. Recommended: keep the code and its flag (off); when
  the flag is on, it only fills in for cards without a provider value. Alternative: keep it in code only and never
  show it.
- **OQ-161** Wording for the provider value: name «P/E آینده‌نگر» without a qualifier and the «جزئیات» sentence above.
- **OQ-162** A provider forward value ≤ 0. Recommended: not drawn. Alternative: drawn pinned at the axis start.
- **OQ-163** The kill switch defaults to on (the value appears after deployment and the first sync). Alternative:
  default off and turned on by the owner.
