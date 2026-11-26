# Tasks: «زیر حد پایین» for a price below the equilibrium range (OQ-137)

Status: Gate A and Gate B: Approved by the owner on 2026-11-26 («OQ-137 را به «زیر حد پایین» تغییر بده», choosing the
second option recorded in OQ-137)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-below-lower-bound-label`

## Change

The equilibrium card's third tile and the text answer (web and Telegram) showed «بالای حد پایین ‎−۵٫۰٪» when the market
price was below the lower bound. Now:

- a negative figure that does not round to zero at one decimal reads «زیر حد پایین ۵٫۰٪» (absolute value, no sign);
- zero or a positive figure keeps the released «بالای حد پایین ‎+۸٫۴٪»;
- the tile's note «نسبت به ‹حد پایین›», the other tiles, the payload and billing are unchanged.

## Tasks

- [x] 1. Frontend rule `lowerBoundComparison` in `src/frontend/src/lib/chat/valuation-gauges.ts`; the card's tile uses it
  (`LowerBoundTile` in `valuation-gauge-cards.tsx`).
- [x] 2. Backend rule `LowerBoundComparison` in `ValuationGaugeTextComposer` (the same rule, for the text answer and
  Telegram).
- [x] 3. Tests: `valuation-gauges-lower-bound.test.ts` (below, above, values that round to zero); a below-range card case in
  `valuation-gauge-cards.test.tsx`; two cases in `ValuationGaugeCardsTests` (below, at the bound).
- [x] 4. `specs/open-questions.md` OQ-137 answered; CHANGELOG `[Unreleased]`; `specs/README.md` index entry.

## Verification (2026-11-26)

- Backend: `dotnet test tests/FinancialCopilot.UnitTests -c Release --filter "FullyQualifiedName~ValuationGauge"`,
  126 passed (124 before, plus the 2 new cases).
- Frontend: `valuation-gauges-lower-bound.test.ts` and `valuation-gauge-cards.test.tsx` 42 passed; changed-file lint
  clean; `npm run typecheck` 37 errors (the baseline), none in these files; `npm run build` green.

## Definition Of Done

- [x] Build green; tests for the changed rule green; typecheck no new error.
- [x] Records updated.
- [ ] Release, tag and deploy only after the owner's separate confirmation.
