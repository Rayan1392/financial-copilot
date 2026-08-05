# Task 11 — Web Conversation Rendering

Implemented the governed sales-growth scanner table rendering contract in the web conversation UI.

## Delivered

- Dynamic sales-growth baseline identifiers receive stable Persian column titles.
- Sales-growth tables render target period, baseline period/window, coverage, selection status, freshness source, and latest observation time in a compact RTL status panel.
- Partial and unavailable data remain visible, including empty-result tables with missing-data warnings.
- Numeric fallback values use the existing Persian digit and percent/number formatters.
- Pagination resubmits the original user query and the current table page size, preserving the scanner interpretation and result ordering contract.
- Conversation reloads retain the serialized table metadata and values because rendering is entirely client-side and does not call a provider.
- Existing generic scanner-table behavior and requested-column limits remain unchanged.

## Verification

- `npm run build` — passed.
- `npm test -- --run src/components/app/__tests__/message-list.test.tsx` — passed (4 tests).
