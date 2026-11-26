# Tasks: Valuation gauge starter card

Status: Gate B: Approved by the owner on 2026-11-26

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-starter-card`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

### 1. Starter list (`src/frontend/src/lib/chat/starter-prompts.ts`)

- [x] 1.1 Add `"valuation"` to `StarterPromptTone`, `"valuation-gauges"` to `StarterPromptId`, and the optional fields
  `detail?: string`, `featured?: boolean` and `requires?: "valuationGaugeCards"` to `StarterPrompt`.
- [x] 1.2 Add the gauge entry first in `STARTER_PROMPTS` (FR-1), with `featured: true` and a lucide `Gauge` icon for desktop.
- [x] 1.3 `homeStarterPrompts(followed, availability)`: drop cards whose `requires` is not available. The parameter
  defaults to "nothing optional available", so a caller that does not pass it shows the current eight.

### 2. Desktop (`starter-card.tsx`, new `starter-card-grid.tsx`, `_app.chat.tsx`)

- [x] 2.1 Add the `valuation` tone to `STARTER_TONE_CLASSES`, built on `--gauge-eq-high` with a darker light-theme
  topic ink checked for 4.5:1.
- [x] 2.2 Featured variant of `StarterCard`: `md:col-span-2`; the detail line; the decorative half-dial SVG (three bands,
  pointer, `aria-hidden`); the pointer rotation on `group-hover` and `group-focus-visible` with `motion-reduce` off.
- [x] 2.3 Extract the grid into `StarterCardGrid`, which renders `prompts` with featured cards first.
  `_app.chat.tsx` uses it and passes `useChatCapabilityAvailability()` into `homeStarterPrompts`.
- [x] 2.4 Intro text: while the gauge card is in the set, the badge and the capability sentence mention valuation (FR-7).

### 3. Mobile (`m.index.tsx`, new `mobile-starter-list.tsx`, `icon-tile.tsx`)

- [x] 3.1 Add a `valuation` tone to `IconTile` and `STARTER_ICONS["valuation-gauges"] = ValuationGaugesIcon`.
- [x] 3.2 Extract the list into `MobileStarterList`. A row gets the tone-specific gradient, topic colour, and the
  detail line when present. `m.index.tsx` passes the availability.

### 4. Records

- [x] 4.1 `CHANGELOG.md` `[Unreleased]` → `### Added`: «نمونهٔ «گیج‌های ارزش‌گذاری گدنا» به پیشنهادهای صفحهٔ اول اضافه شد.»
- [x] 4.2 `specs/backlog.md`: the stale «six starter cards» check in `scripts/mobile-workspace.browser.mjs`.
- [x] 4.3 `specs/README.md`: update this change's index entry.

## Tests

- [x] T1 Vitest, `starter-prompts.test.ts`: nine cards with gauge cards available and the gauge card first; eight
  (today's list, today's order) without availability and by default; unique ids; the gauge text routes as
  «گیج‌های ارزش‌گذاری ‹نماد›» (the ZWNJ form).
- [x] T2 Vitest, `ui-refresh.test.tsx` and a new `starter-card-grid.test.tsx`:
  - the featured card has `md:col-span-2`, the detail line, an `aria-hidden` dial, and the question as its accessible
    name;
  - a click sends the exact text;
  - the mobile list puts the gauge row first with its icon;
  - the intro mentions valuation only with the card shown.
- [x] T3 Browser check `scripts/starter-cards.browser.mjs` over a fixture (`starter-cards-browser-fixture.tsx`), with
  desktop and mobile, available and unavailable, light and dark, at 1280/768/430/375/320 px:
  - no horizontal scroll and no clipped text;
  - the grid has no lone card in a row;
  - the topic, question and detail are at least 4.5:1;
  - the reduced-motion pointer stays still;
  - screenshots go to `evidence/browser/`.

## Definition Of Done

- [~] For frontend work, in `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated: `specs/README.md` index entry.
- [x] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [x] The owner has confirmed «گیج‌های ارزش‌گذاری گدنا» returns three cards in production (assumption in `user-story.md`). Confirmed by the owner on 2026-11-26.
- [ ] Release, tag and deploy only after the owner's separate confirmation. The owner granted it on 2026-11-26 («مجوز صادر میشود»).

## Deviations

- 2.1: the `valuation` tone uses a new per-theme token `--valuation` (`styles.css`: dark `oklch(0.78 0.11 232)`, light
  `oklch(0.47 0.13 240)`) instead of `--gauge-eq-high` with a light-theme override, because one token per theme keeps
  small text at 4.5:1 or more in both themes without a second class. Measured 5.30:1 (dark) and 5.79:1 (light) or better.
- `starterCoverage` (badge and sentence, FR-7) lives in `src/frontend/src/lib/chat/starter-prompts.ts` beside the list
  instead of `_app.chat.tsx`, so the route file exports only components (react-refresh rule) and the rule is tested
  without the page.

## Verification (2026-11-26)

- T1/T2: `starter-prompts.test.ts`, `ui-refresh.test.tsx`, `starter-card-grid.test.tsx` pass (31 tests).
- T3: `node scripts/starter-cards.browser.mjs` passes; log and 30 screenshots in `evidence/browser/`. It also checks
  that Tab reaches the featured card first with a 2 px ring (AC-05).
- Found by T3, pre-existing on clean `HEAD`: BUG-049, light-theme emerald text below 4.5:1 (desktop coverage badge 3.97:1,
  phone sales topics 4.07:1). The check measures and logs these elements apart; they are not part of the gauge card.
- DoD frontend item is `[~]`:
  - `npm run build` green (existing route-file and chunk-size warnings only).
  - `npm run typecheck`: 37 errors, the baseline in `specs/test-baseline.md`; none in the changed files.
  - Lint: the changed files are clean. `eslint .` over the tree reports 1,290 prettier problems, all in files this change
    does not touch; not in `specs/test-baseline.md`.
  - `npm run test` (run alone): 682 passed, 4 failed, 3 suites failed, none in or caused by the changed files:
    `markdown-message` and `monthly-product-trend-chart` (BUG-024/025/040); `fa-terminology` (flags
    `profile-editor.tsx` and `grounded-answer-layout-browser-fixture.tsx`, both unchanged); `install-card` (passes when
    run alone, load-sensitive); three `scripts/*.browser.test.mjs` suites fail at collection with the esbuild
    «TextEncoder … instanceof Uint8Array» invariant under jsdom.
