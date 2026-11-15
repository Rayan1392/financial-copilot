# Implementation Plan: Cash Flow Statement UI Redesign

Status: Gate B: Approved by the owner on 2026-11-15

## Scope

Frontend-only redesign under Feature 161. The explicit user approval covers the design and this bounded implementation plan. No API contract, backend accounting rule, database migration, routing, billing, or production deployment changes.

## Files and implementation tasks

- [x] **UI01 — Replace the KPI grid and consolidate notices**
  - In `src/frontend/src/components/app/cash-flow-report/cash-flow-report.tsx`, select five shared headline metrics for the latest report column and render a flat, borderless typographic summary.
  - Render secondary indicators as a compact collapsed text list, latest period only.
  - Group exact duplicate warnings/notices by stable warning identity and scope; preserve distinct period messages and severity.
  - Keep failed reconciliation visible and expose multiple warnings through one accessible disclosure.
- [x] **UI02 — Redesign the statement for desktop and mobile**
  - Remove the fixed minimum table width, horizontal scroll wrapper, and separate comparison column.
  - Use a fixed-layout desktop table for description plus all five chronological quarters. Place latest amount change and any meaningful backend percentage in the latest-period cell.
  - Use responsive, border-light styling with accounting parentheses, compact row labels, emphasized subtotals/closing cash, and no per-cell boxes.
  - Provide a mobile accordion layout with each row's five period/value pairs stacked newest first; keep the operating section initially open.
- [x] **UI03 — Align exported report**
  - Update `src/frontend/src/components/app/cash-flow-report/cash-flow-report-image.ts` to show the same five-value summary, deduplicated warnings, and comparison-in-latest-cell table without KPI cards or a separate delta column.
- [x] **UI04 — Update focused UI coverage**
  - Update `src/frontend/src/components/app/__tests__/cash-flow-report.test.tsx` and fixtures only where needed to cover: five headline values, absence of KPI cards, deduplicated warnings, desktop embedded comparison, and mobile stacked periods.
- [x] **UI05 — Run and review approved checks**
  - Run the focused Vitest file and ESLint for touched frontend files.
  - Run the browser fixture at 320, 360, 390, 768, 1024, 1280 and 1440 CSS pixels; confirm no horizontal overflow and inspect RTL light/dark screenshots and PNG output.
  - Update the change proposal/task evidence and Feature 161 implementation report. Do not claim browser sign-off until observed.

## Dependencies and order

1. UI01 and UI02 can be implemented together in the report component.
2. UI03 follows the chosen presentation model but remains canvas-only.
3. UI04 validates summary, notices and both responsive renderings.
4. UI05 records actual results after the changes.

## Risks and controls

- The `changes` contract provides the latest pair only. Older quarters will show their reported amounts without invented deltas.
- Do not infer favorability from cash-flow sign or use percent for an invalid baseline; preserve backend state/reason.
- Avoid duplicating warnings that differ in severity, period or message; aggregate only exact matches and retain their period scope.
- Mobile stacked rows increase vertical length; collapsible sections limit the initial viewport while preserving all five periods.

## Out of scope

Backend changes, calculation changes, schema migrations, endpoint/prompt changes, live AI queries, changelog, commit, push, release, and deployment.
