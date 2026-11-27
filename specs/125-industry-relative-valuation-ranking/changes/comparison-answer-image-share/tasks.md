# Tasks: Share and copy an own-industry comparison answer as an image

Status: Gate B: Approved by the owner on 2026-11-27 («موافقم»)

## Feature Folder

`125-industry-relative-valuation-ranking/changes/comparison-answer-image-share`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (the gauge image change is released in v1.35.0; its owner check AC-11 is open and shared, see `user-story.md`, Assumptions).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-157 to OQ-159 answered).

## Implementation Tasks

All paths are under `src/frontend`. No backend, API or database work.

### 1. File name (`src/lib/chat/answer-image.ts`)

- [x] 1.1 `comparisonImageFileName()`: `مقایسه-با-صنعت-<Jalali date>.png`, built on `exportDate` like
  `gaugeImageFileName`.

### 2. Wiring (`src/components/app/message-list.tsx`)

- [x] 2.1 For an answer whose text `MarkdownMessage` renders (no typed report, card or gauge replaces it) and in which
  `parseIndustryComparison` finds the table, pass `image` to `AnswerActions`: content
  `<MarkdownMessage content={displayMessage} direction=… />`, width 800 with trade evidence and 560 without, and
  the file name. The gauge picture keeps precedence. (The density provider of the plan was replaced, see Deviation.)
- [x] 2.2 Check the picture in the browser fixture (task T3) before anything else is changed. Any change needed in
  the comparison components, `MarkdownMessage` or the frame is recorded here as a deviation.

### 3. Verification on real devices (owner)

- [ ] 3.1 iOS Safari and installed PWA, Android Chrome: share a comparison answer to Telegram; copy the image and
  paste it.
- [ ] 3.2 Desktop Chrome or Edge: «کپی تصویر», then paste in Telegram Desktop.
- [ ] 3.3 Record the results next to the gauge change's device evidence. If iOS fails, apply the agreed fallback.

## Tests

- [x] T1 Vitest, `answer-image`: `comparisonImageFileName`.
- [x] T2 Vitest, `message-list` (extend `answer-actions-image.test.tsx`): a six-column comparison answer offers the
  image action with width 800 and the default density inside the frame; a four-column one uses 560; a plain text
  answer and a read-only answer offer none; a gauge answer still offers the gauge picture.
- [x] T3 Browser check: extend `scripts/answer-image-share.browser.mjs` and its fixture with `comparison`
  (six columns), `comparison-four` and `comparison-large` (40 rows); real Chrome, viewports 390 (compact density)
  and 1280, dark and light:
  - clipboard PNG type and pixel width (AC-02, AC-04);
  - while the frame is mounted: `data-layout="wide"` (or `compact-table` for four columns) visible, no cards, row
    count, heading, group lines, disclaimer and footer present, no follow-up button, no horizontal page scroll,
    `inert` (AC-02, AC-03, AC-07);
  - picture compared with a browser screenshot of the same content at the same width (AC-03);
  - draw time of the 40-row fixture recorded (AC-06);
  - evidence PNGs saved under this change's `evidence/browser/`.
- [x] T4 Existing checks still pass: `npm run test:answer-image-browser` gauge fixtures and the Feature 125
  comparison browser check (`scripts/industry-comparison.browser.mjs`).

## Definition Of Done

- [x] In `src/frontend`: `npm run lint` clean for the touched files, `npm run test` with no failure outside
  `specs/test-baseline.md`, BUG-040 and BUG-051, `npm run build` green, and `npm run typecheck` adds no type error
  beyond the baseline.
- [~] Acceptance criteria AC-01 to AC-07 checked; AC-08 is open with the owner (section 3).
- [x] Reference docs updated: `specs/README.md` index entry for Feature 125.
- [x] A Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.

## Implementation Notes (2026-11-27)

- **Deviation (task 2.2): the comparison component changed.** The plan expected no change outside `message-list.tsx`.
  With the plan's wiring the off-screen copy rendered all three responsive layouts of the comparison (cards and both
  tables, two of them hidden by CSS), and the capture library walks every node: an 11-row answer took about 7 s, a
  20-row answer 12 s and a 40-row answer exceeded the 15-second limit («تصویر کپی نشد»). Fix: the picture frame
  provides `AnswerPictureContext` (`components/app/answer-picture.ts`, set in `AnswerSharePicture`), and
  `IndustryComparison` renders only the table that fits its content inside a picture (six columns with trade
  evidence, four without). The density provider of the plan is no longer needed. On screen nothing changes.
  Measured after the fix in headless Chrome on the development server: 5 rows under 2 s, 41 rows about 5 s,
  101 rows (the backend maximum of 100 plus the average) about 12 s.
- An answer normally has up to 11 member rows (`IndustryRelativeValuation:DefaultResultLimit` 10 plus the asked
  symbol when it is outside the top N); the maximum a user can ask for is 100.
- T3: `npm run test:comparison-image-browser` (new script entry; the same file as the gauge check with
  `--fixtures`). 12 checks pass: `evidence/browser/browser-check.log`. The four 41-row evidence PNGs are about
  1.3 MB each.
- T4: `npm run test:answer-image-browser` (12 gauge checks) and `node scripts/industry-comparison.browser.mjs` pass.
  The latter rewrote one evidence PNG of the `responsive-comparison-table` change; it was restored from git.
- Full `npx vitest run`: 13 failures in the full run. Run alone, only BUG-040 (two tests) and BUG-051
  (`fa-terminology`, three `scripts/*.browser.test.mjs` suites) fail; the others (`user-analytics-panel`,
  `account-menu`, `install-card`, `admin-in-app-messages`, `ai-conversation-logs-panel`, `mobile-answers`) pass alone
  and timed out under load in the full run. Not compared with a clean worktree in this session; BUG-040 and BUG-051
  were confirmed on clean `develop` earlier.
- `npm run typecheck`: 37 errors, the baseline count, none in the files of this change. `npm run build` green.
  ESLint is clean for the touched files (with `endOfLine: auto`, CRLF tree).
- The share sheet and Safari were not exercised by any automated check (section 3, AC-08).

## Risks And Order

Order: 1 → 2 → T1–T4 → 3. Task 2.2 and T3 come first in practice: if the off-screen `MarkdownMessage` does not
reproduce the on-screen table, work stops and the owner is asked before another approach is tried.
A commit, release, tag or production deploy is not part of this plan.
