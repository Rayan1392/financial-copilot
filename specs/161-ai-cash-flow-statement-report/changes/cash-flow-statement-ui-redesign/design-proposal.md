# Cash Flow Statement UI Redesign

Status: Gate A: Approved by the owner on 2026-11-15

## Problem

The current report turns every KPI for every quarter into a separate rounded card. This creates a long block of boxes before the statement. Warnings repeated across periods are rendered line by line, so the same sentence appears many times. The wide table adds a separate comparison column to five period columns and applies a fixed minimum width, forcing horizontal scrolling in the chat column.

The owner requests a redesign after reviewing the report and says the KPI boxes and horizontal table scrolling are unacceptable. The quarterly income statement and balance sheet screenshots are the visual references.

## Discovery findings

- `cash-flow-report.tsx` currently maps the entire `kpis` array into a `grid-cols-2`/`grid-cols-3`/`grid-cols-6` set of bordered rounded tiles. Since the API has KPI values for multiple quarters, the grid grows into many rows of cards.
- The same component renders every warning entry directly and separately renders notices in the footer. Repeated warning entries therefore occupy repeated lines.
- The wide table is wrapped in `overflow-x-auto`, has a `min-w-[900px]`, and adds a separate change column after the five periods. That combination causes horizontal table scrolling in a narrower chat column.
- `CashFlowMetricsCalculator` supplies shared headline metrics such as `operating_cash_flow`, `investing_cash_flow`, `financing_cash_flow`, `net_change`, and `closing_cash` per report period. The UI can select the latest period's values without doing arithmetic.
- The currently approved Feature 161 UX proposal specifies KPI cards and allows table scrolling in some cases. This request explicitly replaces those visual choices; it does not change the accounting contract or reporting scope.

## Proposed design

Keep the existing accounting values, contract, period selection, and report sections. Redesign presentation only; do not add financial calculations to React.

### 1. Header and notices

- Retain one compact teal title band with company, report period, unit, verification state, and image action.
- Combine repeated warnings with the same code, severity, message, and period scope. Show each distinct warning once; when it applies to several quarters, list those quarters in that one warning.
- Keep warnings collapsed behind one short summary when there are multiple items; failed reconciliation remains a visible error summary. Do not repeat warning text in both the summary and footer.

### 2. Summary without cards

- Remove the grid of rounded KPI cards.
- Show only five cross-template headline values for the latest independent quarter: operating, investing, financing, net change in cash, and closing cash.
- Render them as a flat typographic strip separated by whitespace or thin dividers, with no card backgrounds, borders, shadows, or individual boxes. On narrow screens it wraps to two clean rows without card styling.
- Do not render every derived KPI for every period above the table. Keep secondary indicators in a compact, plain-text section after the statement; omit unsupported metrics as today.

### 3. Chart

- Place the quarterly trend chart after the summary and before the statement, matching the supplied income statement hierarchy.
- Keep the chart collapsible on narrow screens. Chart values and available periods remain those supplied by the API.

### 4. Statement and quarter comparison

- Remove the extra comparison column and fixed table minimum width.
- At wide widths, show all five independent quarters in chronological order plus the description column. Use a fixed-layout, width-constrained table with wrapped descriptions, compact Persian-number typography, horizontal rules, and no vertical cell boxes.
- Put the latest quarter-over-quarter comparison directly under the amount in the latest-quarter cell: signed amount change first, then percentage when the backend supplies a meaningful percentage. Label the line «تغییر با فصل قبل». This keeps five periods visible without adding a seventh column.
- At narrow widths, show each row as its label followed by its five quarter/value pairs stacked vertically, newest first. Keep sections collapsible and expand only operating activities initially. This preserves all periods without horizontal page or table scrolling.
- Use a subtle teal section label, a restrained tint for statement subtotals and closing cash, and accounting parentheses for outflows. Do not fill every negative cell pink; sign and parentheses carry the accounting meaning. Color must not imply that an inflow is automatically favorable or an outflow unfavorable.
- Quarter comparisons always compare independent quarters. Never compare a cumulative/YTD value with an independent quarter. React formats the backend's amount and percentage and does not calculate either.

### 5. Export and theme

- Make the PNG follow the same order and hierarchy: header, concise summary, chart/flow, statement, secondary indicators, distinct warnings, footer.
- Keep the report surface readable in light and dark app themes. Preserve RTL, Persian digits, zero versus unavailable values, accessible table/row labels, and keyboard operation.

## Text wireframe

```text
┌ teal header: title · company · period · unit · verification · image ┐
  operating       investing       financing       net cash       ending cash
  476,771,599     (386,693,808)   (152,999,407)    (62,921,616)   325,830,147
  [one concise warning summary, only when needed]
  quarterly trend chart
  ┌ description ┬ Q2 1404 ┬ Q3 1404 ┬ Q4 1404 ┬ Q1 1405 ┬ Q2 1405 ┐
  │ operating…  │ amount  │ amount  │ amount  │ amount  │ amount  │
  │             │         │         │         │         │ Δ amount · Δ% │
  └─────────────┴─────────┴─────────┴─────────┴─────────┴─────────┘
  secondary indicators (plain rows, collapsed by default)
```

The frame above is a hierarchy sketch, not a final visual mockup. The latest-quarter sample values come from the user's pasted فولاد report; the UI must use the actual typed API values rather than hard-coded sample text.

## Scope and implementation boundary

- Frontend presentation only: cash-flow report component, responsive statement layout, warning presentation, image export, and focused UI fixtures.
- No API contract or financial formula change, no database migration, no billing or routing change.
- Use the existing Feature 154 balance-sheet palette and Feature 144 quarterly statement conventions where they do not conflict with the owner's request to remove cards and horizontal scrolling.

## Alternatives considered

1. **Keep five periods and add a comparison column.** Rejected because it is the current width pressure and causes scrolling.
2. **Keep KPI cards but reduce their count.** Rejected because the owner explicitly rejects the square KPI boxes; a flat summary line is clearer.
3. **Hide older quarters on narrow screens.** Rejected because it silently removes useful report data. The proposed stacked row layout preserves every quarter.

## Acceptance criteria for Gate A review

- No rounded KPI cards or per-metric boxed tiles.
- Repeated warnings appear once per distinct warning/scope, not once per repeated API entry.
- No horizontal page or table scrolling at 320, 360, 390, 768, 1024, 1280, and 1440 CSS pixels.
- Wide layout shows all five independent quarters, with latest quarter-over-quarter amount and server-provided meaningful percentage inline in the latest cell.
- Narrow layout preserves every period in stacked label/value rows with collapsible statement sections.
- Cash-flow accounting signs, units, zero/unavailable distinction, latest-versus-prior independent-quarter rule, and RTL remain correct.
- PNG export follows the same information hierarchy and has no KPI-card grid.

## Risks and dependencies

- A five-quarter inline table must use carefully constrained widths and type sizes; browser visual review is needed at the acceptance widths.
- Narrow stacked rows are taller. Collapsible sections and showing only a concise latest-period summary mitigate the initial page length without discarding values.
- The API returns one latest-period change per row. The design therefore shows the comparison only beside the latest quarter; it does not invent deltas for older quarter pairs.
- The metric identifiers needed by the flat headline strip are already produced by `CashFlowMetricsCalculator`; UI should select the latest-period values by metric and column id.

## Approval sequence

Gate A approval of this design is required before finalizing the implementation tasks. A separate Gate B approval of the implementation plan is required before changing production UI code.
