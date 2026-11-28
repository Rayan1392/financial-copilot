# Tasks: Share and copy a product revenue mix answer as an image

Status: Gate B: Approved by the owner on 2026-11-28 («گیت A و B مورد تایید است. پیاده سازی را شروع کن.»)

## Feature Folder

`075-company-product-revenue-mix/changes/product-mix-answer-image-share`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner. Both gates were approved in one request,
  before the design was shown; the choices made are listed in `user-story.md`, Assumptions.
- [x] The features in `Depends On` are done (released in v1.35.0 and v1.36.0; their owner device checks are open).
- [x] Read `user-story.md` and the references it lists.

## Implementation Tasks

All paths are under `src/frontend`. No backend, API or database work.

- [x] 1. `src/lib/chat/product-revenue-mix.ts`: `parseProductRevenueMix(content)` recognises the answer by its heading
  and table header and returns the symbol.
- [x] 2. `src/lib/chat/answer-image.ts`: `productMixImageFileName(symbol)`.
- [x] 3. `src/components/app/message-list.tsx`: for a product revenue mix answer pass `image` to `AnswerActions`
  (content `MarkdownMessage`, width 640, the file name). Gauge and comparison pictures keep precedence.
- [x] 4. `src/components/app/markdown-message.tsx`: no reading-width limit inside a picture.

## Verification on real devices (owner)

- [ ] iOS Safari, Android Chrome, Telegram Desktop paste (AC-06), together with the open checks of the gauge and
  comparison pictures.

## Tests

- [x] Vitest `product-revenue-mix.test.ts`: recognition, symbol from the label, other answers ignored.
- [x] Vitest `answer-image.test.ts`: `productMixImageFileName`.
- [x] Vitest `answer-actions-image.test.tsx`: a product revenue mix answer in `MessageList` offers the picture at
  640 px with heading, period and rows, without the reading-width limit; the shared file has the expected name.
- [x] Browser check `npm run test:product-mix-image-browser` (fixtures `mix`, 8 products, and `mix-long`, 25 products
  with long names; viewports 390 and 1280; dark and light): clipboard PNG, row count, texts, table not cut off, frame
  inert and removed, picture compared with a browser screenshot. Evidence in `evidence/browser/`.
- [x] Existing checks still pass: `npm run test:answer-image-browser` (12) and
  `npm run test:comparison-image-browser` (12).

## Definition Of Done

- [~] In `src/frontend`: ESLint clean for the touched files; `npm run build` green; `npm run typecheck` 37 errors
  (the baseline count, none in the files of this change). Vitest: the 17 related files were run (166 tests; the one
  failure is BUG-040); the full `npm run test` was **not** rerun for this change.
- [~] Acceptance criteria AC-01 to AC-05 checked; AC-06 is open with the owner.
- [x] Reference docs updated: `specs/README.md` index entry.
- [x] A Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] No new open question; deferred work is the existing backlog item (pictures for other answers).

## Implementation Notes (2026-11-28)

- Browser check results: `mix` 1280x953 px, drawn in 1.2–1.8 s, at most 0.93% of pixels differ from the screenshot;
  `mix-long` 1280x3050 px, about 3 s, 1.98%. The text-dense `mix-long` fixture has its own tolerance (3%): the two
  pictures were compared by eye and have the same layout and line breaks; the difference is glyph edges.
- The browser script now reads the body rows and texts of any Markdown answer and checks that the table container
  does not overflow (`clipped`).
- The first navigation of the browser script timed out twice on this machine while the development server was
  starting (once per script start, under load from other work); a rerun passed each time.
- A commit, release, tag or production deploy is not part of this plan.
