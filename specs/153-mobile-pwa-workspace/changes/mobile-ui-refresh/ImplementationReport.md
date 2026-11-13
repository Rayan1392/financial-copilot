# Implementation Report: Mobile UI Refresh (`/m`)

Date: 2026-11-12 · Branch: `feature/153-mobile-ui-refresh` (from `develop` `e0f922b`) · State: implemented,
uncommitted, not pushed, not released.

## What changed

| Area | Change |
|---|---|
| Type scale | `--text-m-*` tokens in `src/styles.css` (24, 20, 17, 15, 14, 13 px); every `/m` screen and the phone answer layouts use them. `cn()` in `src/lib/utils.ts` knows them as font sizes. |
| Fonts | Vazirmatn (variable) and JetBrains Mono (Latin, variable) are served from `public/fonts/`, preloaded, and precached by `public/sw.js`. The Google Fonts `@import`, links, and runtime font cache are removed; the old `sapio-fonts-v1` cache is deleted on activation. This applies to desktop too. |
| Icons | Phosphor icons through `src/components/mobile/icons.ts` (30 icons, one import path each). ESLint forbids `lucide-react` and direct Phosphor imports in mobile files. |
| Shell | Top bar: avatar, title, credit chip (44 px target); the admin entry moved to the account screen (`AdminPanelLink`); «گفتگوی جدید» is hidden on `/m`. Bottom nav: 26 px icons, filled active icon in a pill, 13 px labels. Composer: 17 px input, «بپرسید…». |
| Home | Six full-width starter cards (icon tile, coloured topic label, the whole question at 14 px, a soft tint by group); all six fit above the composer at 360×800. Watchlist strip with price and signed change. Revised on 2026-11-13 at the owner's request: the first version hid the question text. |
| Market, watchlist | Shared `SymbolRow` (64 px) with a change badge; shared `ErrorState` and `EmptyState`. |
| Sheets | Symbol sheet: 24 px price, change badge, icon tiles; the «بستن» row is removed. Sheet rows are 56 px. |
| Conversation | Emerald user bubble; prose inherits 17 px; compact density raises small answer text to 13 px. |
| History, account | New sizes, icon tiles, theme switch (`role="switch"`), shorter copy. |
| Checks | `scripts/mobile-workspace.browser.mjs` updated and extended (96 checks); new `scripts/mobile-ui-screenshots.mjs` (`npm run test:mobile-screenshots`). |

## Acceptance criteria

| AC | Result | Evidence |
|---|---|---|
| AC-01 text ≥ 13 px, body 17 px | Met | Browser checks "Type scale: text ≥ 13 px…" on eight routes and "Type scale: body 17 px…" |
| AC-02 no Google Fonts request | Met | Browser check "Fonts: no Google Fonts request on /m and /chat" |
| AC-03 font available offline | Met, with a note | Offline, the worker serves `/fonts/vazirmatn-variable.woff2` from its cache (check AC-27). An offline reload still shows `offline.html`, which uses the system font by design. |
| AC-04 icon lint rule | Met | A probe file importing `lucide-react` under `components/mobile/` fails `no-restricted-imports`; the tree has no such import |
| AC-05 bottom nav | Met | Unit tests (`presentation.test.tsx`, `ui-refresh.test.tsx`), browser check AC-01 |
| AC-06 top bar | Met | Unit tests for `TopAppBar` and `AdminPanelLink`; browser checks for a non-admin user |
| AC-07 starter questions unchanged | Met | Unit test over `STARTER_PROMPTS`; browser check "Starter card sends its unchanged question" |
| AC-08 watchlist strip | Met | Browser check "Home: six starter cards…" |
| AC-09 shared `SymbolRow` | Met | Both routes import it; unit tests; browser check AC-15/16 |
| AC-10 symbol sheet close | Met for the button and Escape | Browser check AC-15/16. Swipe and scrim come from the unchanged vaul drawer and were not re-tested. |
| AC-11 no horizontal scroll | Met | Browser checks AC-03 at 320, 360, 390 px on eight routes, plus the answer checks at three widths in both themes; `test:balance-sheet-browser` passes |
| AC-12 desktop checks unchanged | Partly | `test:balance-sheet-browser` and `test:symbol-snapshot-browser` pass. `test:quarterly-png` times out on unmodified code as well (BUG-027), so it proves nothing either way. |
| AC-13 shared states | Met | History, market, watchlist, and conversation use `ErrorState`; history, watchlist, and the not-found screen use `EmptyState` |
| AC-14 copy | Met | Copy matches the approved table; `fa-terminology.test.ts` passes |
| AC-15 contrast and targets | Met | axe (WCAG 2.x AA) passes on eight routes in both themes and with sheets open; 44 px target checks pass |
| AC-16 screenshots | Met | `evidence/before/` and `evidence/after/`, 18 files each |

## Verification results

| Check | Before | After |
|---|---|---|
| `npm run typecheck` | 37 errors | 37 errors, none in touched application files |
| Vitest | 2 failed of 509 | 2 failed of 523 (the same two: BUG-024, BUG-025); 14 new tests pass |
| `npm run build` | green | green |
| `npm run test:mobile-browser` | 78 of 91 | 94 of 96; the two failures also fail before the change (BUG-028) |
| `npm run lint` | 33,064 errors | 34,893 errors; all but 6 are `prettier/prettier` line-ending errors, as before. The 6 other errors and 12 warnings are unchanged. Touched files pass ESLint with that rule off and pass `prettier --check --end-of-line auto`. |
| Client JavaScript, gzip | 630,604 B per file | 655,676 B per file: +25.1 kB against a 25 kB budget (measured 2026-11-13, after the starter-card revision). |
| Client CSS, gzip | 22,802 B | 23,061 B |

## Deviations from the plan

- Not committed. The plan called for one commit per group; the work is left uncommitted on the branch for the
  owner's go-ahead.
- Ten near-duplicate icons were merged (for example back = the up arrow rotated) to stay inside the size budget.
- `signed-change.tsx` keeps the ▲/▼ glyphs because unit and browser checks assert them.
- `m.account.profile.tsx` wraps the shared `ProfilePanel` in `data-density="compact"` so its 11 px label meets the
  13 px floor on the phone; desktop is untouched.
- The browser check for the «داده قدیمی» chip was replaced by row and badge checks: the fixture rows share one
  trading day, so the chip is not shown under the current market-session rule.

## Remaining

- OQ-073 (disclaimer placement) is open; the disclaimer stays under the composer at 13 px.
- Real-device check on iOS Safari and Android Chrome was not done; all browser evidence is headless Chromium.
- BUG-026, BUG-027, BUG-028 are recorded in `specs/bugs.md` and are not fixed here.
- The bundle is 0.1 kB over the 25 kB budget on the per-file measure; the budget was set by the agent, and the
  owner has not ruled on it.
