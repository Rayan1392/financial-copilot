# Tasks: Share and copy a valuation gauge answer as an image

Status: Gate B: Approved by the owner on 2026-11-27

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-156 answered).

## Implementation Tasks

All paths are under `src/frontend`. No backend, API or database work.

### 1. Capture adapter (`src/lib/chat/answer-image.ts`)

- [x] 1.1 Add the dependency `modern-screenshot` (pinned `^4.7.0`) to `package.json` and the lock file.
- [x] 1.2 `renderNodeToPngBlob(node)`: dynamic `import("modern-screenshot")`, `domToBlob` at scale 2, awaits
  `document.fonts.ready` first, rejects after 15 s, rejects an empty or non-PNG blob. No other module imports the library.
- [x] 1.3 Capability checks: `canCopyImage()` (`ClipboardItem` and `navigator.clipboard.write`) and
  `canShareFile(file)` (`navigator.canShare({ files })`).
- [x] 1.4 `gaugeImageFileName(symbol)` built on `safeFileSymbol` and `exportDate` from `components/app/png-brand.ts`.

### 2. Off-screen frame (`src/components/app/answer-share-frame.tsx`, `src/styles.css`)

- [x] 2.1 `AnswerShareFrame`: portal to `document.body`; fixed, off-screen, `aria-hidden`, `inert`,
  `data-share-frame`; `width` prop; page background and padding; children; footer from the signature constants.
- [x] 2.2 Export Sapio's name line from `lib/chat/answer-text.ts` so the text signature and the footer share one source.
- [x] 2.3 `styles.css`: inside `[data-share-frame]` turn off animations and transitions and hide `[data-share-hide]`.

### 3. Image lifecycle (`src/lib/chat/use-answer-image.tsx`)

- [x] 3.1 Hook `useAnswerImage({ content, width })` returning `frame` (element to render, or null), `warmUp()` and
  `getBlob()`. `getBlob` mounts the frame, waits for it to lay out, calls the adapter, unmounts the frame (also on
  failure) and returns one shared promise per answer.
- [x] 3.2 Cache the blob; drop it when the theme class on `<html>` changes, on failure, and on unmount.

### 4. Actions (`src/components/app/answer-actions.tsx`)

- [x] 4.1 Optional `image` prop (`content`, `width`, `fileName`). Without it, rendering and behavior are unchanged.
- [x] 4.2 «کپی تصویر»: `navigator.clipboard.write([new ClipboardItem({ "image/png": getBlob() })])` called directly
  in the click handler (promise form, required by Safari). States and labels of FR-3.
- [x] 4.3 «اشتراک‌گذاری» with an image: draw, build the `File`, share files + title + text when `canShareFile`;
  otherwise today's text share. `NotAllowedError` → keep the blob, label «ارسال تصویر». `AbortError` → silent.
  Drawing failure → text share.
- [x] 4.4 `warmUp()` on pointer-enter, focus and touch-start of the two image actions.

### 5. Wiring (`src/components/app/message-list.tsx`, `valuation-gauge-cards.tsx`)

- [x] 5.1 For a gauge answer pass `image`: `<ValuationGaugeCards data={valuationGauges} />`, width 800 for several
  cards and 480 for one, and the file name.
- [x] 5.2 OQ-156: mark the «راهنمای خواندن گیج و جزئیات عددی» block with `data-share-hide`. No other change to the cards.

### 6. Verification on real devices (owner)

- [ ] 6.1 iOS Safari and installed PWA: share a three-card answer to Telegram; copy the image and paste it.
- [ ] 6.2 Android Chrome: the same.
- [ ] 6.3 Desktop Chrome or Edge: «کپی تصویر», then paste in Telegram Desktop.
- [ ] 6.4 Record the results in `evidence/devices.md`. If iOS fails, apply the fallback in the user story's
  assumptions and report it.

## Tests

- [x] T1 Vitest, `answer-actions` (capture module mocked):
  - no `image` prop: the existing tests pass unchanged;
  - «کپی تصویر» visible only with clipboard image support; writes an `image/png` item; success and failure labels;
  - share with files when `canShare` accepts them; text share when it does not or is absent;
  - `NotAllowedError` → «ارسال تصویر», second tap shares without a second draw;
  - drawing failure → «تصویر کپی نشد» / text share;
  - one draw for two actions; a new draw after a theme change.
- [x] T2 Vitest, `answer-image`: timeout, empty blob, file name, capability checks.
- [x] T3 Vitest, `message-list`: a gauge answer offers the image action; a text answer and a read-only answer do not.
- [x] T4 Browser script `scripts/answer-image-share.browser.mjs` with a fixture (`npm run test:answer-image-browser`),
  real Chrome, clipboard permission granted, viewports 390 and 1280, dark and light, fixtures `single`, `all`,
  `forward`:
  - click «کپی تصویر», read the clipboard back, check type and pixel size (AC-02);
  - while the frame is mounted: card count, no visible tablist, no horizontal page scroll, `inert` (AC-02, AC-09);
  - compare the clipboard PNG with a browser screenshot of the cards at the same width, within a small pixel
    tolerance (AC-03);
  - frame removed afterwards; evidence PNGs saved under `evidence/browser/`.
- [x] T5 Existing `npm run test:valuation-gauges-browser` still passes (the cards are unchanged on screen).

## Definition Of Done

- [~] In `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no
  type error beyond `specs/test-baseline.md`.
- [x] `npm run build` shows the capture library in a separate lazily loaded chunk (AC-10).
- [ ] Acceptance criteria AC-01 to AC-10 checked; AC-11 checked by the owner or reported as open (open: section 6).
- [x] Reference docs updated: `specs/README.md` index entry for Feature 159.
- [x] A Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md` (image sharing for other answers,
  «دانلود تصویر»); defects found → `specs/bugs.md`.

## Risks And Order

Tasks run in the order 1 → 5, then T1–T5, then 6. Task 1 and T4 repeat the spike inside the repository first; if the
library cannot reproduce the cards there, work stops and the owner is asked before any alternative is tried.
A release, tag or production deploy is not part of this plan.

## Implementation Notes (2026-11-27)

- T4 fixture: it renders the cards and `AnswerActions` directly, not `MessageList`, because the message list needs the
  app's server runtime in a plain Vite page. The message-list wiring is covered by T3.
- T4 comparison: the reference screenshot is taken at the same viewport as the picture, because the cards' padding
  follows the viewport breakpoint (`p-4 sm:p-5`). A phone picture is therefore about 8 CSS px shorter than a desktop
  one; widths and cards are identical (→ `specs/backlog.md`).
- Full `npm run test`: the only failures are pre-existing on `develop` (BUG-040, BUG-051), confirmed on a clean
  worktree of `c52739d9`. `npm run lint` is not green on `develop` (about 1,200 formatting errors with CRLF ignored,
  backlog); the files of this change lint clean. `npm run typecheck`: 37 errors, exactly the baseline.
- AC-10: the capture library is the 26 kB chunk loaded by a dynamic import from the message-list chunk.
- Section 6 (real devices) and AC-11 are open with the owner. The share sheet and Safari were not exercised by any
  automated check.
