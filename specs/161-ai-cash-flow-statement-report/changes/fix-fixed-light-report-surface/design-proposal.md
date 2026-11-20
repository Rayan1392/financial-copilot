# Fix: Cash Flow Report Uses a Fixed Light Surface (BUG-038)

Status: Gate A: Approved by the owner on 2026-11-21

## Layers / Area

frontend · FinancialData

## Problem

The owner reports (2026-11-21) that the Feature 161 cash flow report is shown in dark colors whatever
the site theme is, while the downloaded PNG is light. The owner asks that it behave exactly like the
quarterly income statement (Feature 144) and the balance sheet (Feature 154).

## Discovery findings

- `src/frontend/src/components/app/cash-flow-report/cash-flow-report.tsx` has 37 Tailwind `dark:`
  classes (card root, header notes, KPI strip, warnings, table, accordion, evidence) and 7 theme tokens
  (`text-muted-foreground`).
- `src/frontend/src/styles.css` defines no `@custom-variant dark`. In Tailwind v4 `dark:` then compiles
  to `@media (prefers-color-scheme: dark)`: it follows the operating system, not the site theme.
- The site theme (`components/app/theme-toggle.tsx`) only toggles `.dark` / `.light` on `<html>`. So on
  a device set to dark the card is dark in both site themes; `text-muted-foreground` follows the site
  theme, so muted text can also lose contrast on the light card in dark site theme.
- The PNG export (`cash-flow-report-image.ts`) draws a fixed light surface.
- Feature 144 (`quarterly-financial-table.tsx`) and Feature 154 (`balance-sheet-report/*`) contain no
  `dark:` classes and no theme tokens: a fixed light "paper" surface with explicit colors in both
  themes.
- This was already the approved design: Feature 161 discovery R-28 and `design-proposal-ux.md` AC-U07
  ("the report renders identically (fixed light surface) under the app's light and dark themes").
  The `dark:` classes break that criterion.
- The browser check `scripts/cash-flow-report.browser.mjs` took its "dark" screenshot with
  `emulateMedia({ colorScheme: "dark" })`, which is why the defect looked intended.

## Proposed correction

No material design change: restore AC-U07.

1. Remove every `dark:` class from `cash-flow-report.tsx`; keep the light values as they are.
2. Replace `text-muted-foreground` in the report with an explicit ink (`text-slate-600`, as Feature 154
   uses for secondary text) so no part of the card depends on the site theme.
3. Add a regression test: the rendered report contains no `dark:` class and no theme token
   (`muted-foreground`, `bg-background`, `text-foreground`, `bg-card`, `bg-muted`).
4. Make the browser check test the real theme: set the site `.dark` class with the OS preference set to
   dark, and assert the card background stays light; check text contrast ≥ 4.5:1 in both site themes
   (as `balance-sheet-report.browser.mjs` does).

## Out of scope

Other components that use `dark:` with the same media-query behavior (`monthly-activity-trend-chart.tsx`,
`monthly-trend-summary-cards.tsx`, `symbol-snapshot-card.tsx`, `components/ui/alert.tsx`,
`components/ui/chart.tsx`). Whether `dark:` should follow the site theme app-wide is a separate decision;
it will be recorded in `specs/backlog.md`, not changed here.

## Risks

- Low. Only class names in one component change; data, layout and the PNG export do not change.
- The KPI strip and warnings were tuned with dark variants; their light colors are the ones already
  seen in light mode, so no new color is introduced.
