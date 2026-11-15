# User Story: Cash Flow Statement UI Redesign

Status: Gate A: Approved by the owner on 2026-11-15

Layers: `frontend`

## Story

As an investor reading a quarterly cash flow statement, I want a compact report that emphasizes the statement and its five comparable independent quarters, so I can understand cash movements without navigating repeated warning text, metric tiles, or a horizontally scrolling table.

## Scope

- Remove the rounded KPI-card grid and show only the five primary latest-quarter values in a flat summary.
- Deduplicate repeated warnings and prevent notices from being repeated in multiple report regions.
- Fit the five-period statement at desktop widths without a separate comparison column; place the latest quarter change inside its period cell.
- At mobile widths, render period values vertically with collapsible sections and no horizontal scroll.
- Preserve amounts, labels, units, status and comparison values supplied by the existing API. React performs formatting only.
- Apply the same hierarchy to PNG export.

## Acceptance criteria

- No square/rounded KPI tiles, shadows, or card background per value.
- The summary shows operating, investing, financing, net change and closing cash for the latest independent quarter only.
- Each repeated warning is shown once per distinct message and scope; notices are not duplicated.
- Desktop displays five independent quarter values and the latest quarter delta without horizontal table scrolling.
- Mobile shows all five periods as vertical label/value pairs and has no horizontal page or table scrolling.
- Latest comparison remains independent-quarter versus independent-quarter. Amount deltas and valid backend percentages are displayed without computing values in the browser.
- Negative amounts use accounting parentheses; zero and unavailable remain distinct; RTL and keyboard interaction remain intact.
- PNG export follows the same summary/table hierarchy without KPI tiles.
