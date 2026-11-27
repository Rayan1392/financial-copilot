# Tasks: Starter cards ask for the symbol

Status: Gate B: Approved by the owner on 2026-11-27

## Feature Folder

`153-mobile-pwa-workspace/changes/starter-cards-symbol-picker`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

All paths are under `src/frontend/`.

### 1. Card model (`src/lib/chat/starter-prompts.ts`)

- [x] 1.1 Make `StarterPrompt` a union on `kind`: a direct card (`kind: "direct"`, `text`, optional `example: true`) and a symbol card (`kind: "symbol"`, `label` as the parts before and after the slot, `exampleSymbol`, `forSymbol`). `forSymbol` stays the only owner of the question wording.
- [x] 1.2 Fill the six symbol cards with the labels and example symbols of the user story; mark `sales-mix` and `product-trend` as examples.
- [x] 1.3 Remove `personalizedStarterPrompts`, `WATCHLIST_STARTER_PROMPTS_ENABLED` and `StarterPromptSet.personalized`; `homeStarterPrompts` takes only the availability.
- [x] 1.4 Add `starterPickerSymbols(followed)`: the watchlist symbols for the picker (newest first, trimmed, unique, no rights issues, at most 8), reusing the rule the removed function had.
- [x] 1.5 Update `src/lib/chat/__tests__/starter-prompts.test.ts`: every symbol card's `forSymbol(exampleSymbol)` equals the question the card sent before this change; no symbol card label contains its example symbol; picker symbol rules.

### 2. Shared symbol search

- [x] 2.1 Extract `useSymbolSearch(query, { enabled, limit })` to `src/lib/market/use-symbol-search.ts` from `AddSymbolSheet` (300 ms debounce, from 1 character, no retry, 60 s stale time, same query key).
- [x] 2.2 Move `AddSymbolSheet` onto the hook with no behaviour change.
- [x] 2.3 Hook test with a fake server function: debounce, disabled while closed or empty, error surfaced.

### 3. Picker content (`src/components/app/starter-symbol-picker.tsx`)

- [x] 3.1 `StarterSymbolPicker({ prompt, watchlistSymbols, onPick })`: search field, preview line, «دیده‌بان شما» and «نمونه» rows before typing, result rows while typing, the three states with «تلاش مجدد». Built on `command.tsx` with its own filtering off. Rights issues filtered from results.
- [x] 3.2 `onPick(symbol)` is called once per choice; the component holds no sending logic.
- [x] 3.3 Component tests: initial rows, preview text, search results, empty, failed and retry, keyboard choice, rights issues left out.

### 4. Desktop

- [x] 4.1 `StarterCard`: render the slot pill for symbol cards and the «نمونه» mark for example cards, in both the normal and the featured card; `aria-haspopup="dialog"` on symbol cards. Slot and mark colours from existing tokens.
- [x] 4.2 `StarterCardGrid`: `onSelect(prompt)` instead of `onSelect(text)`.
- [x] 4.3 `routes/_app.chat.tsx`: direct card → `submit(prompt.text)`; symbol card → open a `Dialog` hosting the picker; on pick → `submit(prompt.forSymbol(symbol))` and close. Remove the personalization notice.
- [x] 4.4 Update `components/app/__tests__/starter-card-grid.test.tsx` and the browser fixture.

### 5. Phone

- [x] 5.1 `MobileStarterList`: same slot and «نمونه» rendering, `onSelect(prompt)`.
- [x] 5.2 `routes/m.index.tsx`: symbol card → full-height `BottomSheet` hosting the picker; pick → `submit(...)` and close.
- [x] 5.3 Update `components/mobile/__tests__/ui-refresh.test.tsx` and add a sheet test for open, pick and close.

### 6. Verification and docs

- [x] 6.1 Update `scripts/starter-cards.browser.mjs` for the new labels and the picker flow; run it in both themes, desktop and 360 px, and store screenshots in this folder's `evidence/`.
- [x] 6.2 Check the search with Persian input variants (ی/ي, ک/ك, half-space) and report the result; defects → `specs/bugs.md`.
- [x] 6.3 `specs/README.md` index entry; `specs/backlog.md` entries for the per-card eligibility filter and composer autocomplete; `CHANGELOG.md` line under `## [Unreleased]`.

## Tests

- [x] Frontend tests for UI work (`src/frontend`, Vitest): tasks 1.5, 2.3, 3.3, 4.4, 5.3.
- [x] Browser check: task 6.1.

## Definition Of Done

- [~] In `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`. Lint (touched files), build and type-check (37 errors, the baseline count) are clean. `npm run test`: 713 passed, 4 tests and 3 script files failed, none in a file this change touches and none listed in `specs/test-baseline.md` — see "Verification".
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated: `specs/README.md` index entry.
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.

## Verification (2026-11-27)

- Vitest, the four files of this change: 47 tests passed.
- Vitest, whole suite: 83 files passed, 7 failed (713 tests passed, 4 failed). The failures are in files this change does not touch and that have no uncommitted change: `fa-terminology.test.ts` (wording in `profile-editor.tsx` and `grounded-answer-layout-browser-fixture.tsx`), `markdown-message.test.tsx`, `monthly-product-trend-chart.test.tsx`, `install-card.test.tsx` (5 s timeout on a loaded machine) and the three `scripts/quarterly-financial-*.browser.test.mjs` files (esbuild `TextEncoder` invariant under jsdom). They are not in `specs/test-baseline.md`; the owner decides whether to record or fix them.
- `tsc --noEmit`: 37 errors, the baseline count; none in a touched file.
- ESLint on the touched files (with `endOfLine: auto` for the CRLF working tree): no error, no warning. `npm run build`: green.
- `node scripts/starter-cards.browser.mjs`: cards half and picker half both passed, in both themes; log and screenshots in `evidence/browser/`. The picker half runs against a fixture with a stand-in symbol search.
- Not done: a run of the real `/chat` and `/m` pages against the API (real search, real send).

## Deviations

- `availableStarterPrompts` was folded into `homeStarterPrompts(availability)`, which now returns the card list directly (task 1.3 kept both).
- The symbol-search query key gained the `limit` (task 2.1 said the same key); no other code reads that key.
- Tasks 2.3 and 5.3: the search pacing and the open, pick and close flow are tested through the picker component test, the `useStarterSelection` hook test and the phone view of the browser check, not through a separate hook test and a route-level sheet test.
- Added beyond the task list, found by the browser check: the dialog and the bottom sheet do not return focus without a trigger element, so `useStarterSelection` remembers the opening card and `BottomSheet` accepts `onCloseAutoFocus`.
- Added `--only cards|picker` to `scripts/starter-cards.browser.mjs` for a quick rerun of one half.
- Task 6.2 was done by reading the search code, not by a live search: ی/ي and ک/ك are folded on both sides; the half-space is not (→ `specs/backlog.md`).
