# Followed Symbols Frontend Audit — 2026-07-13

## Scope

Read-only review of the frontend implementation against:

- `specs/085-followed-symbols-foundation`
- `specs/086-personalized-symbol-intelligence-feed`

No product code was changed in this audit.

## Executive summary

The frontend does contain a followed-symbols page and follow actions, so the feature is not absent. However, the visible product surfaces still make it easy to conclude that followed symbols are "not shown anywhere".

The main reasons are:

1. The only clear navigation entry is a desktop-only sidebar link to `/followed-symbols`.
2. The visible sidebar preview still shows the legacy `watchlist` feed, not the followed-symbols data.
3. There is no followed-symbols summary in the main chat area, header, or right context panel.
4. Parts of the spec-defined personalized feed card UX are incomplete in the current frontend.

## What is implemented

### Present in the frontend

- A registered authenticated route exists for `/followed-symbols` in `src/frontend/src/routeTree.gen.ts`.
- The page component exists in `src/frontend/src/routes/_app.followed-symbols.tsx`.
- Follow actions exist in:
  - AI answer table rows
  - Market movers in the context panel
  - Search results inside the followed-symbols page
- A sidebar link exists for `/followed-symbols`.

### Key evidence

- Sidebar link: `src/frontend/src/components/app/sidebar.tsx:99-105`
- Authenticated route layout: `src/frontend/src/routes/_app.tsx:7-13`
- Followed-symbols page: `src/frontend/src/routes/_app.followed-symbols.tsx:18-265`
- Follow button on AI answer rows: `src/frontend/src/components/app/message-list.tsx:228-232`
- Follow button on market movers: `src/frontend/src/components/app/context-panel.tsx:65-77`

## Why the user experience currently feels like "it is not displayed anywhere"

### 1. Discoverability depends on a desktop-only sidebar link

The only obvious navigation entry to the followed-symbols page is the sidebar link:

- `src/frontend/src/components/app/sidebar.tsx:99-105`

That sidebar is hidden below `md`:

- `src/frontend/src/components/app/sidebar.tsx:83`

So on smaller screens there is no visible route entry here. The header also does not expose any followed-symbols entry:

- `src/frontend/src/components/app/chat-header.tsx:1-13`

### 2. The sidebar preview shows legacy watchlist data, not followed-symbols data

The sidebar section labeled `دیده‌بان من` fetches `getWatchlist()`:

- `src/frontend/src/components/app/sidebar.tsx:22-46`
- `src/frontend/src/lib/market-view.functions.ts:62-66`

That calls:

- `GET /api/v1/watchlists/me`

But followed symbols use a different frontend data path:

- `src/frontend/src/lib/followed-symbols.functions.ts:110-145`

That calls:

- `GET /api/v1/followed-symbols/me`
- `POST /api/v1/followed-symbols/me/{externalCompanyId}`
- `DELETE /api/v1/followed-symbols/me/{externalCompanyId}`

So the visible sidebar preview is not backed by the followed-symbols feature. If a user expects newly followed symbols to appear in the sidebar watchlist block, that expectation is not met by the current implementation.

### 3. No followed-symbols summary is surfaced in the main chat shell

The authenticated shell is:

- left sidebar
- main content outlet
- right context panel

From:

- `src/frontend/src/routes/_app.tsx:15-24`

The right context panel shows market summary and top movers, not followed symbols:

- `src/frontend/src/components/app/context-panel.tsx:17-97`

The header also contains no followed-symbols entry:

- `src/frontend/src/components/app/chat-header.tsx:1-13`

So unless the user opens `/followed-symbols`, the feature has almost no persistent visibility.

## Spec-to-frontend gap review

## Spec 085 — Followed Symbols Foundation

### Covered

- Follow action on AI answer cards/tables: present
- Follow action on market-related UI: present in context panel
- Simple followed-symbols management view: present
- UI wording avoids portfolio semantics on the page: present

### Gaps or weak points

1. The feature is not integrated into the currently visible watchlist-style surface.
   - The sidebar still presents old watchlist data instead of followed-symbols data.
   - This is the strongest reason the feature looks missing in practice.

2. Discoverability is weak.
   - The only clear entry is a sidebar link.
   - No header-level, context-panel, or in-chat summary surfaces exist.

3. The spec wording mentions entry points from symbol pages, AI answer cards, and insight cards.
   - AI answer cards are covered.
   - Context-panel market movers are covered.
   - This audit did not find a separate generic symbol page surface in the frontend.
   - This audit also did not find a general insight-card follow action outside the followed-symbols route.

## Spec 086 — Personalized Symbol Intelligence Feed

### Covered

- Personalized feed panel exists on the followed-symbols page.
- Empty-state rendering exists.
- Filters exist for type, severity, dismissed state.
- Pagination exists.
- Seen and dismiss actions exist.
- AI explanation action exists.
- Source, period, and confidence-related data are rendered in some form.

Evidence:

- `src/frontend/src/routes/_app.followed-symbols.tsx:102-205`
- `src/frontend/src/routes/_app.followed-symbols.tsx:293-405`

### Gaps

1. `Open symbol` action is missing.
   - The spec requires card actions including open symbol.
   - The current card has buttons for seen, ask AI, open source, and dismiss.
   - No open-symbol action is rendered on the card.

2. `Open source report` is not actually wired.
   - The card renders an `ExternalLink` button:
     - `src/frontend/src/routes/_app.followed-symbols.tsx:361-369`
   - But there is no `onClick`, no link target, and no navigation handler.
   - It is only visually present.

3. Suggested next actions are not rendered.
   - The response contracts include:
     - `suggestedActions` on the insight
     - `emptyState.suggestedActions`
   - Defined in:
     - `src/frontend/src/lib/followed-symbols.functions.ts:54`
     - `src/frontend/src/lib/followed-symbols.functions.ts:73`
   - The page does not render those actions.

4. Freshness is not explicitly shown as a dedicated field.
   - The spec calls for showing source, period, freshness, and confidence on every card.
   - The card shows confidence and detected date, but not an explicit freshness label/state.

## Root-cause assessment for the reported issue

The problem is primarily a frontend integration and discoverability issue, not total absence of implementation.

The most important mismatch is:

- visible shell UI shows legacy watchlist data
- followed-symbol data lives on a separate route
- the route has weak discoverability

That makes the delivered frontend behavior diverge from the mental model a user would have after clicking "Follow symbol".

## Conclusion

If the question is "does the frontend implementation exist at all?", the answer is yes.

If the question is "does the frontend currently present followed symbols in a clear, user-visible way?", the answer is mostly no.

The current implementation is route-contained and partially integrated, while the most visible shell surface still shows legacy watchlist data instead of followed-symbols data.

## Files reviewed

- `specs/085-followed-symbols-foundation/user-story.md`
- `specs/085-followed-symbols-foundation/tasks.md`
- `specs/086-personalized-symbol-intelligence-feed/user-story.md`
- `specs/086-personalized-symbol-intelligence-feed/tasks.md`
- `src/frontend/src/routes/_app.followed-symbols.tsx`
- `src/frontend/src/components/app/sidebar.tsx`
- `src/frontend/src/components/app/context-panel.tsx`
- `src/frontend/src/components/app/message-list.tsx`
- `src/frontend/src/components/app/chat-header.tsx`
- `src/frontend/src/lib/followed-symbols.functions.ts`
- `src/frontend/src/lib/market-view.functions.ts`
