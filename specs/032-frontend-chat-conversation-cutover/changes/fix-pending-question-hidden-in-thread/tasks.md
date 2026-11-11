# Tasks: show the pending question in the desktop conversation page (BUG-023)

Status: Gate B: Approved by the owner on 2026-11-11

## Feature Folder

`032-frontend-chat-conversation-cutover/changes/fix-pending-question-hidden-in-thread`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] `Depends On` is None.
- [x] Read `design-proposal.md` and the referenced files.
- [x] No open question in `specs/open-questions.md` blocks this change.

## Plan Summary

All paths under `src/frontend/src/`.

| Action | File | Change |
|---|---|---|
| New | `lib/chat/pending-question.ts` | `withPendingQuestion(messages, pendingMessage)` — the one owner of the pending user-message shape. |
| New | `lib/chat/__tests__/pending-question.test.ts` | Unit tests for the helper. |
| Change | `routes/_app.c.$threadId.tsx` | Render the pending question; scroll when it appears. |
| Change | `routes/_app.chat.tsx` | Use the shared helper instead of its inline pending message. |
| Change | `lib/chat/use-chat-thread.ts` | Clear `pendingMessage` only after the messages refetch completes. |
| Change | `lib/chat/__tests__/chat-hooks.test.tsx` | Test: pending question stays until the refetched messages contain it. |

## Tasks

- [x] 1. Record BUG-023 in `specs/bugs.md` and this change folder.
- [x] 2. Add `withPendingQuestion` and its unit tests.
- [x] 3. Use the helper in `/c/$threadId` (with scroll) and `/chat`.
- [x] 4. Clear `pendingMessage` in `useChatThread` only after the messages refetch; add the hook test.
- [x] 5. Run `npm test`, `npm run typecheck`, `npm run lint` in `src/frontend`; no failure outside `specs/test-baseline.md`.

## Acceptance Criteria

- AC-1: On desktop, after «گفتگوی جدید» and send, the question bubble appears at once above the progress indicator.
- AC-2: The same holds for follow-up questions in an existing conversation.
- AC-3: When the answer arrives, the question is shown once — not duplicated, and it does not disappear in between.
- AC-4: On a failed send, the pending bubble is removed and the error notice keeps the draft (unchanged).

## Definition of Done

- [x] All tasks ticked; acceptance criteria verified.
- [~] Tests, type check, and lint have no failure outside `specs/test-baseline.md`. No new failure; 2
  pre-existing test failures are not in the baseline yet (see Implementation Notes) — owner to decide.
- [x] `specs/bugs.md` BUG-023 status updated.

## Implementation Notes (2026-11-11)

- Deviation (same scope): instead of clearing `pendingMessage` after the refetch, `useChatThread` now derives it.
  The question stays visible until the loaded conversation holds a user message that was not there when it was
  sent. Clearing it after the awaited refetch would still leave a gap: React Query delivers the new data to
  components on a later tick than the awaited promise. `onSuccess` also awaits the messages refetch, so the
  progress indicator stays until the answer is loaded.
- Verification: `src/lib/chat` + `src/routes` + `answer-presentation` tests: 71 passed. Lint on changed files:
  clean (line-ending check ignored; the working tree is CRLF via `core.autocrlf`). Type check: 37 errors, same
  as BUG-002 baseline, none in changed files.
- Full `vitest run`: 2 failures outside `specs/test-baseline.md` —
  `markdown-message.test.tsx` ("renders a quarterly financial table with 16 rows inside an RTL message") and
  `monthly-product-trend-chart.test.tsx` ("renders a failed product trend message once in chat"). Both fail
  identically with this change stashed, so they are pre-existing and unrelated. The three
  `scripts/*.browser.test.mjs` suites also fail to load under vitest (browser scripts, run separately).
- Vitest on Windows fails every suite ("failed to find the runner") when started from a lowercase `c:` path;
  run it from `C:\Source\...`.
