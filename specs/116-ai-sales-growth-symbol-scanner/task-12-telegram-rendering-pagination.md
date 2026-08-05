# Task 12 — Telegram Rendering and Pagination

Implemented Feature 116 Telegram rendering using the existing Feature 089 response and callback conventions.

## Delivered

- Sales-growth scanner responses render compact rows with symbol, current sales, selected baseline sales, growth percentage, and optional multiple.
- The footer exposes target period, comparison period/window, coverage, freshness source, latest observation, status, missing-data warnings, and page position.
- Values are taken directly from the governed scanner table cells and localized with the existing Persian digit/MarkdownV2 pipeline, keeping Telegram values aligned with the web contract.
- Opaque, expiring callback state is reused for `sgp1` next/previous callbacks; callbacks remain actor/chat/thread scoped and idempotency-safe.
- Pagination replays the original query with the same scanner page semantics and explicitly discloses evidence refresh in the footer.
- Long output continues through the existing bounded splitter without changing row order; a conversation route is included as the web-table deep link when available.

## Verification

- Telegram-focused unit tests passed: 58 tests.
- Feature 116 Telegram renderer test passed.
