# Tasks: Fix — Cash Flow Chart Legend Text Contrast (BUG-039)

Status: Gate A and Gate B: Approved by the owner on 2026-11-21 («BUG-039 را رفع کن»)

## Feature Folder

`161-ai-cash-flow-statement-report/changes/fix-chart-legend-contrast`

## Implementation Tasks

- [x] 1. `cash-flow-report.tsx`: Legend `formatter` renders the label in `#334155` (`CashFlowLegendLabel`); swatch keeps the series color. Same ink as the PNG export legend (`cash-flow-report-image.ts`).
- [x] 2. Vitest: the legend label is rendered in `#334155`, not the series color.
- [x] 3. Browser check: remove the `.recharts-legend-wrapper` exclusion; all report text ≥ 4.5:1 in both site themes. Done: 50 text nodes (46 + 4 legend labels), min 4.76:1 in both themes; before the fix the legend measured 2.15:1.
- [x] 4. `specs/bugs.md` BUG-039 status; CHANGELOG `[Unreleased]` entry.

## Definition of Done

- [x] Cash-flow Vitest (11/11) and `npm run test:cash-flow-browser` pass; ESLint clean on touched files; no new type errors or warnings.
