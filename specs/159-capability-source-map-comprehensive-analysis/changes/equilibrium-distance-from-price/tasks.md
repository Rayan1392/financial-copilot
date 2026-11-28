# Tasks: equilibrium card distances measured from today's price

Status: Gate B: Approved by the owner on 2026-11-28 («موافقم نباید معنای وعده بده با گیت A و B موافقم»; Gate A in the same message)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-distance-from-price`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner. The plan approved with it is sections 3 to 5
  of `design-proposal.md`; the owner approved both gates in one message after that proposal was presented.
- [x] OQ-164 to OQ-167 answered as proposed on 2026-11-28.

## Implementation Tasks

- [x] `ValuationGaugeInsights`: `LowerDistancePercent`, `MidpointDistancePercent`, `UpperDistancePercent` (omitted
  from JSON when null).
- [x] `ValuationGaugeProjector.ProjectEquilibrium`: the three distances, `(level − price) ÷ price`, only for a
  positive price. The released figures are still written.
- [x] `ValuationGaugeTextComposer`: `LevelDistance` and the line «فاصلهٔ قیمت روز: …»; the released line only for a
  result without distances.
- [x] `valuation-gauges.ts`: the insight members, `levelDistance`, `LEVEL_DISTANCE_NOTE`.
- [x] `valuation-gauge-cards.tsx`: the four tiles, the two-by-two layout for four tiles, the note; the released tiles
  for a card without distances.
- [x] Tests: `ValuationGaugeCardsTests` (گدنا, the four positions of the price, price zero, wording, released
  fallback; three released text tests rewritten for the new wording), `valuation-gauge-cards.test.tsx` (four cases),
  `valuation-gauges-level-distance.test.ts`.
- [x] Records: provider README, `specs/README.md`, `specs/open-questions.md` (OQ-164 to OQ-167, note on OQ-137),
  CHANGELOG `[Unreleased]`.

## Owner steps (not done by the agent)

- [ ] Look at the card on a phone and on a desktop: the four tiles were verified by tests only, not in a browser.
- [ ] Commit, release and deployment.

## Verification results (2026-11-28, local)

- Backend build: 0 warnings, 0 errors. Gauge unit tests (ValuationGauge, ForwardRatio): 235 passed.
- Frontend: gauge card and gauge helper tests 69 passed; ESLint clean on the changed files; `tsc --noEmit` 37 errors,
  the baseline number, none in the changed files.
- Not run: the gauge card E2E classes (BUG-053), a browser or real-device check, the answer-picture render of the
  equilibrium card with four tiles. No live provider or LLM call.

## Deviations

- The proposal put the explanatory sentence in «جزئیات». The equilibrium card has no «جزئیات» section, so the
  sentence is a small line under the tiles, always visible, and shorter: «درصدها فاصلهٔ قیمت روز تا هر سطح بازه‌اند؛
  پیش‌بینی و توصیهٔ سرمایه‌گذاری نیست.» This also serves the owner's condition that the figures must not read as a
  promise.
- The text line opens with «فاصلهٔ قیمت روز: » for the same reason.
