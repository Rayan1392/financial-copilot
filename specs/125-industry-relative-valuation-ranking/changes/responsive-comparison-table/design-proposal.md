# Responsive industry comparison in web chat

Status: Approved by the owner on 2026-11-24 (Gate A)

## Current behavior

The specialized renderer in `src/frontend/src/components/app/markdown-message.tsx` puts the six-column price-evidence table in an `overflow-x-auto` wrapper and gives the table a 720 px minimum width. The older four-column version has a 460 px minimum. Both can exceed the space available inside a chat message.

## Proposed layout

| Available space | Presentation |
|---|---|
| Wide desktop message | A six-column RTL table with symbol, three ratios, rank, and trade details. Use readable spacing, aligned numeric values, and a separate summary row. The price, trading date and source, and equilibrium price can wrap inside their column. |
| Narrow tablet or desktop message | Keep four aligned table columns for the symbol and three ratios. Place the rank, trade price, trading date/source, and equilibrium price in a wrapped detail line under each symbol row, spanning the table. |
| Mobile chat or narrow message | A vertical list of compact symbol cards. The header shows the ticker and rank, followed by labeled metric values and a separate price detail area. Put the group averages in a distinct summary card. |

Use the width available to the component to switch between the desktop and tablet layouts. Use cards on the mobile chat surface and where the four-column layout cannot fit. Use the current Sapio typography, spacing, light and dark colors, and RTL conventions. Preserve all available values and the symbol order. Keep comparison color cues accompanied by a non-color distinction. Present the approved three-symbol follow-up actions below the comparison using the existing chat controls.

## Acceptance criteria

- The webpage, the answer, and the comparison have no horizontal overflow caused by the comparison. All values remain visible, including long tickers, long numbers, and dates. Content wraps vertically where necessary.
- Both the current six-column answer format and stored four-column answers render as described. Older answers without trade details render the values they contain.
- Verify desktop and tablet layout with the sidebar open and closed, mobile portrait and landscape, a viewport as narrow as 320 px, and text zoom. Check light and dark modes, RTL reading order, and keyboard access to follow-up actions.
