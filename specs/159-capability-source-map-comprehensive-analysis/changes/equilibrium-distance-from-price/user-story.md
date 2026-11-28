# User Story: equilibrium card distances measured from today's price

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-distance-from-price`

## Layers / Area

Layers: backend | frontend | telegram-gateway (text only, through the shared composer)
Area: AI | FinancialData

## Priority

P2

## Complexity

S

## Depends On

- `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh` (released)
- `159-capability-source-map-comprehensive-analysis/changes/equilibrium-below-lower-bound-label` (released; replaced)

## References

- `design-proposal.md` in this folder (Gate A approved 2026-11-28; OQ-164 to OQ-167 answered as proposed)
- `docs/providers/cyclicalwaves-v2/README.md` (gauge card insights)
- Code: `ValuationGaugeProjector.ProjectEquilibrium`, `ValuationGaugeTextComposer`,
  `src/frontend/src/lib/chat/valuation-gauges.ts`, `src/frontend/src/components/app/valuation-gauge-cards.tsx`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a shareholder looking at a symbol's equilibrium price range,
I want to see by what percent today's price must rise or fall to reach each level of the range,
so that the figure answers the question I actually ask, without a sign I have to interpret.

## Business Context

Owner, 2026-11-28 (گدنا: price 12,800, midpoint 17,055): the released tile divided by the midpoint (−24.9%); the
shareholder's figure divides by the price (33.2%). The wording must be very short and must not read as a promise.

## Scope

- Distances from today's price to the lower bound, the midpoint and the upper bound: web card, text answer, Telegram.
- Wording with the direction as a word. A visible note that the figures are distances, not a forecast.

## Out Of Scope

- The bar, the needle, «جایگاه», the quote and source lines, the P/E and P/S cards, credits, routing.
- Recomputing answers stored before this change (they keep their released tiles, OQ-167).

## Functional Requirements

1. **Figure.** For each level L: `|L − price| ÷ price`, one decimal, half away from zero. Direction «رشد» when L is
   above the price, «افت» when below, «هم‌سطح» when the figure rounds to 0.0. No figure when the price is not positive.
2. **Wording.** «تا حد پایین ‹عدد›٪ افت», «تا میانه ‹عدد›٪ رشد», «تا حد بالا ‹عدد›٪ رشد»; no plus or minus sign.
3. **Web card.** Four tiles: «جایگاه», then the three levels in axis order, each with the level in rials under it
   («۱۷٬۰۵۵ ریال»). Two rows of two on a narrow card, one row when it is wide. «رشد» keeps the cool tone and «افت» the
   warm tone. Under the tiles: «درصدها فاصلهٔ قیمت روز تا هر سطح بازه‌اند؛ پیش‌بینی و توصیهٔ سرمایه‌گذاری نیست.»
4. **Text and Telegram.** One line: «فاصلهٔ قیمت روز: تا حد پایین …، تا میانه …، تا حد بالا …».
5. **Replaced.** The tile «تا میانهٔ بازه» (signed, divided by the midpoint) and the lower-bound tile with
   «بالای حد پایین» / «زیر حد پایین» (OQ-137), in new answers.
6. **Payload.** `insights` gains `lowerDistancePercent`, `midpointDistancePercent`, `upperDistancePercent` (signed,
   positive when the level is above the price; omitted when absent). `priceVsMidpointPercent` and
   `priceVsLowerPercent` stay for older clients.
7. **Stored answers** without the new figures render their released tiles and no note.

## Acceptance Criteria

- [x] AC-01 گدنا (12,800 against 5,537 / 17,055 / 28,573) gives 56.7% افت, 33.2% رشد, 123.2% رشد.
- [x] AC-02 Above the range every level is «افت»; below the range every level is «رشد»; at a level «هم‌سطح».
- [x] AC-03 A price that is not positive gives no distance.
- [x] AC-04 The web card shows the four tiles with the wording of requirement 2 and 3, no sign, and the note.
- [x] AC-05 The text answer shows the line of requirement 4 and none of the replaced wording.
- [x] AC-06 A card stored before the change keeps its released tiles; a result without the new figures keeps its
      released text line.
- [x] AC-07 The released payload members are still written.
- [x] AC-08 `docs/providers/cyclicalwaves-v2/README.md` describes the new figures.

## Assumptions

- A1 The owner approved the recommended wording («رشد» / «افت») together with the condition that it must not read as
  a promise. The condition is met by «تا», by «فاصلهٔ قیمت روز» in the text line and by the note under the tiles. If
  the owner reads «رشد» itself as a promise, the alternative «بالاتر / پایین‌تر از قیمت روز» is a wording-only change
  in two helpers.
