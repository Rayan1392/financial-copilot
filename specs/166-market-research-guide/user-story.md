# User Story: Market research guide

## Feature Folder

`166-market-research-guide`

## Layers / Area

- Layers: frontend
- Area: Conversations · FinancialData · Notifications · Telegram

## Priority / Complexity / Depends On

- Priority: P1
- Complexity: M
- Depends On: None. Reuses the desktop and mobile workspaces, the existing new-chat prefill, and the shipped watchlist, account inbox, and Telegram connection screens.

## References

- [Approved design](design-proposal.md)
- [Approved standalone preview](../../docs/help/sapio-feature-guide-fa.html)
- `src/frontend/src/routes/releases.tsx`, `src/frontend/src/lib/chat/new-chat-intent.ts`, and `src/frontend/src/lib/experience/client.ts`

## User Story

As a prospective or signed-in Sapio user, I want to see examples of real market research answers and workflows so I can choose a useful question or find the right part of Sapio.

## Acceptance Criteria

- **AC-01 — Access.** `/market-guide` opens for guests and signed-in users. On the public home page (`/`), the «راهنمای تحلیل و رصد بازار» link appears directly below the product introduction, outside the sign-in form and before it in reading and keyboard order. The guide is also discoverable from desktop workspace navigation and the mobile account menu.
- **AC-02 — Content.** The Persian RTL guide uses the approved design, five topic groups, and 15 selectable examples. It shows 14 supplied captures, distinguishes answer images from product screens, and shows a Telegram connection example with instructions. The guide uses the existing Sapio mark, typography, and theme.
- **AC-03 — Images and interaction.** Inline previews use local optimized assets. The keyboard-accessible image viewer loads the full-size capture when opened, supports fit and original-size browsing, and returns focus on close. Topics, examples, and actions stay usable at 320 px and in both themes.
- **AC-04 — Questions.** A chat example can be copied or opened as an editable draft in the appropriate Sapio composer. A guest can sign in and return to the selected example. The guide never submits a question. No prompt text is carried in a URL.
- **AC-05 — Workflows.** Watchlist, events, Telegram, and support examples can open their existing destination. The events example targets the existing events tab, including when opened from a phone.
- **AC-06 — Boundaries.** Loading and browsing the guide uses static content and images. No financial API call, data migration, new AI route, or Telegram bot change is required. New public assets contain no account identifiers or other private information.
