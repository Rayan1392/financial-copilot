# Tasks: Mobile UI Refresh (`/m`)

Status: Gate B: Approved by the owner on 2026-11-12

## Feature Folder

`153-mobile-pwa-workspace/changes/mobile-ui-refresh`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-073 blocks only the disclaimer move, which is out of scope).

## Implementation Tasks

Work on a branch `feature/153-mobile-ui-refresh` from `develop`. Each group is a separate commit and leaves
the app working. Commands run in `src/frontend`.

State 2026-11-12: all tasks done on the branch; nothing is committed yet (see `ImplementationReport.md`).

### 0. Baseline

- [x] T00 Record the current results of `npm run lint`, `npm run test`, `npm run typecheck`, and `npm run test:mobile-browser` in `evidence/baseline.md`.
- [x] T01 Capture "before" screenshots of every `/m` screen at 360×800 in both themes into `evidence/before/`, and record the gzip size of the `/m` client chunks.

### 1. Foundations

- [x] T02 Add mobile type tokens and named text utilities to `src/styles.css` (17/15/14/13px roles, 24px index value).
- [x] T03 Add Vazirmatn variable and JetBrains Mono `woff2` files under `public/fonts/` with `@font-face` (`font-display: swap`); remove the Google Fonts `@import` in `src/styles.css` and the `preconnect` and stylesheet links in `src/routes/__root.tsx`.
- [x] T04 `public/sw.js`: add the font files to `PRECACHE_PATHS`, remove `FONT_ORIGINS` and the font cache, bump the cache version, and update the service-worker tests.
- [x] T05 Add `@phosphor-icons/react` and create `src/components/mobile/icons.ts` (semantic name → icon, weights). Add an ESLint `no-restricted-imports` rule for `lucide-react` in `src/components/mobile/**` and `src/routes/m.*`.
- [x] T06 Add shared components in `src/components/mobile/`: `icon-tile.tsx`, `symbol-row.tsx`, `empty-state.tsx`, `error-state.tsx`, with unit tests.

### 2. Shell

- [x] T07 `mobile-screen.tsx`: base text 17px.
- [x] T08 `bottom-nav.tsx`: 26px icons, filled active icon in a pill, 13px labels.
- [x] T09 `top-app-bar.tsx`: remove the admin gear from tab screens; icons from the icon module. `routes/m.index.tsx`: pass `hideNewChat`.
- [x] T10 `routes/m.account.index.tsx`: add the «پنل مدیریت» row for users who pass `canAccessAdmin`.
- [x] T11 `mobile-composer.tsx`: placeholder «بپرسید…», 17px input, 13px disclaimer and offline hint (position unchanged).

### 3. Home

- [x] T12 `lib/chat/starter-prompts.ts`: add the optional display field for the example subject; unit test that every `text` is unchanged.
- [x] T13 `routes/m.index.tsx`: six starter cards in a two-column grid (one column below 340px), accessible name = full question; remove «بیشتر».
- [x] T14 `routes/m.index.tsx`: watchlist strip of mini cards with price and signed change.

### 4. Market, watchlist, symbol sheet

- [x] T15 `routes/m.market.tsx` and `routes/m.watchlist.tsx`: render rows with `SymbolRow`; use the shared error and empty states; shorter empty-state copy.
- [x] T16 `index-card.tsx`, `signed-change.tsx`: new sizes and the change badge variant.
- [x] T17 `symbol-sheet.tsx`, `bottom-sheet.tsx`: price header, icon tiles for analysis prompts, remove the «بستن» row, 17px sheet rows. `add-symbol-sheet.tsx`, `confirm-sheet.tsx`: type scale.

### 5. Conversation and answers

- [x] T18 `pending-exchange.tsx`, `routes/m.c.$threadId.tsx`: emerald user bubble, shared error state, type scale.
- [x] T19 `components/app/message-list.tsx` and the answer blocks that read `useAnswerDensity()`: 17px prose under `compact` only.
- [x] T20 `components/mobile/answers/*`: move to the type tokens; fix any overflow found at 320px and 360px.

### 6. History and account

- [x] T21 `routes/m.history.tsx`, `conversation-row.tsx`: type scale, shared states, shorter search placeholder and limit note.
- [x] T22 `routes/m.account.index.tsx`, `routes/m.account.profile.tsx`, `credit-summary.tsx`, `install-card.tsx`, `ios-install-sheet.tsx`, `offline-banner.tsx`, `desktop-suggestion.tsx`, `chat-error-card.tsx`, `stale-chip.tsx`: icon tiles, theme switch, type scale, shorter copy.

### 7. Verification

- [x] T23 Update `scripts/mobile-workspace.browser.mjs` for the new text and selectors; add checks for AC-01, AC-02, AC-03, AC-11, and AC-15.
- [x] T24 Capture "after" screenshots into `evidence/after/` and record the new `/m` chunk size against the 25 kB budget.
- [x] T25 Write `ImplementationReport.md` in this folder: results per acceptance criterion, deviations, remaining items.

## Tests

- [x] Frontend unit tests (Vitest) for `SymbolRow`, `EmptyState`, `ErrorState`, the icon module, starter-prompt texts, bottom nav, and top bar.
- [~] Browser checks: `npm run test:mobile-browser` (94 of 96; the two failures are BUG-028), `npm run test:balance-sheet-browser` and `npm run test:symbol-snapshot-browser` pass; `npm run test:quarterly-png` times out on unmodified code too (BUG-027).
- [x] No backend tests: the change touches no backend code.

## Definition Of Done

- [~] In `src/frontend`: `npm run test` and `npm run build` have no failure outside the T00 baseline, and `npm run typecheck` adds no type error. `npm run lint` is not green before or after: it reports CRLF line endings as `prettier/prettier` errors; no other rule has a new finding.
- [~] Acceptance criteria in `user-story.md`: all checked except AC-12, which is partly verified (BUG-027).
- [x] Reference docs updated: `specs/README.md` index entry; `docs/deployment/pwa-service-worker.md` (fonts).
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md` (OQ-073); deferred work → `specs/backlog.md`; defects found → `specs/bugs.md` (BUG-026, BUG-027, BUG-028).
- [x] No commit to `develop`, push, release, or flag change without the owner's separate go-ahead.
