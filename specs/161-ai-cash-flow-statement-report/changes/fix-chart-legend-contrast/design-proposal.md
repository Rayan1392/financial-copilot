# Fix: Cash Flow Chart Legend Text Contrast (BUG-039)

Status: Gate A: Approved by the owner on 2026-11-21 («BUG-039 را رفع کن», approving the fix proposed in `specs/bugs.md`)

## Layers / Area

frontend · FinancialData

## Problem

The trend chart in `cash-flow-report.tsx` uses the Recharts `Legend`, which colors each label with its
series color. «سرمایه‌گذاری» is amber `#f59e0b` on white: 2.15:1, below WCAG 1.4.3 (4.5:1), in both
site themes. Found by the BUG-038 browser contrast check, which excluded `.recharts-legend-wrapper`.

## Correction

No material design change. Legend labels use the report's secondary ink `#334155` (slate-700, the
tick-label color of `balance-sheet-funding-chart.tsx`); the series color stays on the legend swatch, so
the mapping between label and series is unchanged. The browser check no longer excludes the legend.

## Risks

Low: one chart's legend text color. Series colors, data and the PNG export are unchanged.
