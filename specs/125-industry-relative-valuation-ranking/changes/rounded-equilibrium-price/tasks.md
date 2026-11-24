# Implement the whole-rial equilibrium price

Status: Gate B: Approved by the owner on 2026-11-24

- [x] Round the equilibrium price in `IndustryRelativeValuationPresentation.PriceEvidence` (`Math.Round(…, 0, MidpointRounding.AwayFromZero)`) and add `Presentation_ShowsTheEquilibriumPriceInWholeRials` (۷۲۳۲٫۱۷, ۱۵۰۲۱٫۵, ۷۵۶۸٫۴۲, ۱۲۰۰).
- [x] Round the parsed equilibrium amount for stored answers in `industry-comparison-model.ts` (`roundRialAmount`, half away from zero, arbitrary length via `BigInt`) and cover it in `industry-comparison.test.tsx`.
- [x] Run `IndustryRelativeValuationSemanticTests` (21 passed), the industry comparison frontend tests (8 passed), lint, the frontend build and `scripts/industry-comparison.browser.mjs`.
