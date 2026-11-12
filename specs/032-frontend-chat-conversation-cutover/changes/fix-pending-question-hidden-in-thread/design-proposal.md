# Design proposal: show the pending question in the desktop conversation page (BUG-023)

Status: Gate A: Approved by the owner on 2026-11-11

## Feature Folder   `032-frontend-chat-conversation-cutover/changes/fix-pending-question-hidden-in-thread`
## Layers / Area    frontend · Conversations
## Priority         P1
## Complexity       S
## Depends On       None
## References       `src/frontend/src/routes/_app.c.$threadId.tsx`, `src/frontend/src/routes/_app.chat.tsx`, `src/frontend/src/lib/chat/use-chat-thread.ts`, `src/frontend/src/components/app/sidebar.tsx`, `specs/bugs.md` (BUG-023)

## Problem

On desktop, after the user sends a question in a conversation page (`/c/$threadId`), only the progress
indicator («درک پرسش ← دریافت داده‌ها ← …») is shown. The question itself appears only when the answer is ready.

## Discovery

- «گفتگوی جدید» in the desktop sidebar creates an empty conversation and navigates to `/c/$threadId`
  (`sidebar.tsx`, `newChat` mutation). The first question is therefore sent from the conversation page, not
  from `/chat`.
- `useChatThread` already keeps the question being sent in `pendingMessage`, but the desktop page
  `_app.c.$threadId.tsx` never reads it. It renders only the persisted messages, which are refetched after the
  answer arrives.
- `/chat` (the starter-card page) and the mobile conversation page already show the pending question, so only
  the desktop conversation page is affected — for the first question and for every follow-up.
- A second, smaller flaw: `useChatThread` clears `pendingMessage` in `onSuccess` before the refetched messages
  arrive, so once the page shows the pending question it could briefly disappear and reappear.

## Proposed correction

No material design change: this restores the behavior `/chat` and mobile already have.

1. A shared helper in `src/frontend/src/lib/chat/` appends the pending question as a user message to the list
   of messages. `/chat` and `/c/$threadId` both use it, so the pending-message shape has one owner.
2. `/c/$threadId` passes `thread.pendingMessage` through that helper to `MessageList` and scrolls to the bottom
   when a pending question appears.
3. `useChatThread` clears `pendingMessage` only after the conversation's messages have been refetched, so the
   question never flickers out. The mobile page benefits from the same change.

Failure behavior is unchanged: the pending bubble is removed and `ChatErrorNotice` keeps the draft.

## Out of scope

- Changing the sidebar so «گفتگوی جدید» goes to `/chat` without creating a conversation.
- Streaming partial answers.

## Risks

Low. Three frontend files plus a small helper; no backend or API change. Change 3 also affects mobile and is
covered by a hook test.
