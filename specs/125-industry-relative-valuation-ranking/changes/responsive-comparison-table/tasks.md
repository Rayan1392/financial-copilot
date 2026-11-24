# Implement responsive industry comparison in web chat

Status: Gate B: Approved by the owner on 2026-11-24

- [x] Extract a focused comparison renderer from `markdown-message.tsx` and retain its legacy and current Markdown parsing. Reuse the same row model, order, benchmarks, and value formatting in all layouts.
- [x] Implement a six-column RTL layout for wide messages and a four-column table with full-width supporting details for narrower messages. Switch by available message width. Render symbol and benchmark cards in mobile chat and at widths too narrow for the compact table. Use the mobile chat mode on landscape phones as well.
- [x] Remove the comparison wrapper's scroll behavior and layout minimums. Let symbol names, dates, long values and action labels wrap without clipping. Keep readable labels, visible focus, appropriate mobile touch targets, and semantic table or list markup. Use the existing web theme tokens.
- [x] Add focused rendering tests for the price-evidence and legacy formats, membership order, benchmark labels, and mobile cards. Use the existing browser fixture tooling to verify the document, message and comparison have no horizontal overflow at 320, 390, 600, 768, 1024 and 1440 px, with long values, an open sidebar, browser zoom and both themes. Verify that the three follow-up actions wrap below the comparison.
- [x] Run the focused tests and frontend build. Review screenshots for desktop, tablet and mobile. Record any unrelated baseline failures and verify existing chat content remains readable.

Expected implementation surfaces are the specialized industry comparison renderer, its scoped styles, the established mobile density input and the related frontend tests. The layout work uses the existing answer payload and does not require a database migration.

## Implementation notes (2026-11-24)

- Renderer: `src/frontend/src/components/app/industry-comparison/` (`industry-comparison-model.ts` parses both Markdown formats into one row model; `industry-comparison.tsx` renders the layouts). `markdown-message.tsx` only delegates to it and lets the comparison use the full answer width.
- Layout follows the width of the answer through CSS container queries: six-column table from 40rem, four-column table with a detail line from 26rem, cards below that. The phone workspace (`AnswerDensityContext` = `compact`, `/m`, portrait and landscape) always uses cards. Because the breakpoints are in rem, text zoom moves to the narrower layout earlier.
- The hard-coded `slate-*`, `emerald-100` and `rose-100` colours that broke dark mode were replaced by theme tokens (`emerald-soft`, `rose/12`, `surface`, `surface-2`, `foreground`, `muted-foreground`). Each green/red ratio also carries a ▼/▲ marker, screen-reader text and a short legend.
- The parser no longer loops forever on an empty first cell (the old `continue` skipped the row increment).
- Browser check: `node scripts/industry-comparison.browser.mjs` (fixture `src/components/app/__tests__/industry-comparison-browser-fixture.tsx`). It covers 12 cases (1440 with and without sidebar and long values, 1024 with sidebar and with 150% text zoom, 768 with and without sidebar plus long values, 600, landscape phone 844×390, 390, 390 with text zoom, 320 with long values), each in the dark and light theme. Checks: no page or comparison overflow, no scrolling or clipped element, one visible layout (cards in the phone workspace), follow-up actions below the comparison and inside the viewport, and contrast of at least 4.5:1 for every visible text. All 24 runs pass; screenshots are in `evidence/browser/`.
- The fixture copies the assistant block markup of `message-list.tsx`, because `MessageList` itself needs the TanStack Start server runtime, which the standalone Vite fixture cannot bundle.

## Unrelated failures seen during verification

- `markdown-message.test.tsx` › "renders a quarterly financial table with 16 rows inside an RTL message" fails the same way on unchanged `HEAD` (it expects Latin digits in a period label that is now rendered in Persian digits).
- `monthly-product-trend-chart.test.tsx` › "renders a failed product trend message once in chat" fails with the previous `markdown-message.tsx` as well.
- `fa-terminology.test.ts` reports `profile-editor.tsx` and `grounded-answer-layout-browser-fixture.tsx`, files this change does not touch.
- `account-menu.test.tsx`, `in-app-messages.test.tsx` and `admin-in-app-messages.test.tsx` failed only in the full `vitest run`; they pass when run on their own.
- Vitest also collects the three `scripts/quarterly-financial-*.browser.test.mjs` files and fails them as suites.
- Full run: 631 tests, 625 passed, 6 failed (all listed above). `npm run build` succeeds.
