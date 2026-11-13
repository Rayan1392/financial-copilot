# User Story: Mobile UI Refresh (`/m`)

## Feature Folder

`153-mobile-pwa-workspace/changes/mobile-ui-refresh`

## Layers / Area

Layers: frontend
Area: cross-cutting

## Priority

P2

## Complexity

L

## Depends On

153-mobile-pwa-workspace

## References

- `specs/153-mobile-pwa-workspace/changes/mobile-ui-refresh/design-proposal.md` (approved at Gate A on 2026-11-12)
- `specs/153-mobile-pwa-workspace/spec.md`, `design.md`, `ImplementationReport.md`
- `src/frontend/src/routes/m.*.tsx`, `src/frontend/src/components/mobile/`
- `src/frontend/src/styles.css`, `src/frontend/src/routes/__root.tsx`, `src/frontend/public/sw.js`
- `src/frontend/src/lib/chat/starter-prompts.ts`, `src/frontend/src/lib/chat/symbol-prompts.ts`
- `src/frontend/src/components/app/answer-density.ts`, `src/frontend/src/components/app/message-list.tsx`
- `src/frontend/src/lib/copy/fa-terminology.ts`
- `src/frontend/scripts/mobile-workspace.browser.mjs`
- `specs/test-baseline.md`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user on a phone,
I want larger text, shorter labels, and clearer, more attractive icons,
so that I can read market data and answers and reach the main actions without effort.

## Business Context

The phone workspace is the main entry for retail investors. Text at 12–15px, long labels, and thin
single-colour icons make it harder to read than it should be, and the font depends on a host that may be
unreachable for users in Iran.

## Scope

- Mobile type scale as tokens, applied to every `/m` screen and to `compact`-density answers.
- Self-hosted Vazirmatn and JetBrains Mono, precached by the service worker (app-wide font source).
- Phosphor icons behind one mobile icon module.
- Redesign of the top bar, bottom nav, composer, home, market, watchlist, symbol sheet, conversation,
  history, and account screens as listed in the design proposal.
- Shared `SymbolRow`, `EmptyState`, and `ErrorState` components.
- Shorter Persian copy as listed in the design proposal.

## Out Of Scope

- API, backend, database, routing, and tab-structure changes.
- Desktop layout and desktop icons.
- New features: pull-to-refresh, haptics, animations, watchlist-personalized starters, new symbol prompts.
- The redirect flag `VITE_MOBILE_AUTO_REDIRECT` and production rollout gate D-Q5.
- Moving the legal disclaimer (blocked on OQ-073).

## Functional Requirements

1. Text sizes follow the approved scale: body and answer prose 17px, secondary 14px, section headings 15px,
   row price and change 17px, index value 24px, tab label 13px, nothing below 13px except chart axis ticks.
2. No page requests `fonts.googleapis.com` or `fonts.gstatic.com`. Fonts are served from the app origin with
   `font-display: swap` and are in the service-worker precache.
3. Files under `components/mobile/` and `routes/m.*` take icons only from `components/mobile/icons.ts`.
   A lint rule forbids importing `lucide-react` there.
4. Bottom nav: 26px icons; the active tab uses the filled icon in a soft emerald pill; `aria-current`,
   reselect-to-top, and the four destinations are unchanged.
5. Tab top bar shows the avatar, title, and credit chip. The admin entry appears on the account screen for
   users who pass `canAccessAdmin`. «گفتگوی جدید» is not shown on `/m`.
6. Home shows all six starter prompts as full-width cards. Each card shows an icon tile, the `topic` as a small
   coloured label, and the whole question text at 14px, so users learn how questions are phrased. Tapping a
   card sends exactly the existing `text`. (Changed by the owner on 2026-11-13: the first version was a
   two-column grid that showed only the topic and an example subject, which hid the question.)
7. Home shows up to eight watchlist symbols as a horizontal strip of cards with symbol, latest price, and
   signed change. Tapping a card opens the symbol sheet as today.
8. Market movers and the watchlist render rows through one `SymbolRow` component (minimum height 64px) with
   symbol, company name, price, and a change badge that carries an arrow and sign as well as colour.
9. The symbol sheet shows price (at least 22px) and change in its header and the analysis prompts as icon
   tiles. The «بستن» row is removed; the sheet's close button, swipe, scrim, and Escape still dismiss it.
10. In a conversation the user's bubble uses the soft emerald surface. Answer text is 17px in `compact`
    density; `default` density output is unchanged.
11. History, market, watchlist, and conversation use the shared `EmptyState` and `ErrorState`.
12. Copy changes follow the table in the design proposal.

## Data / Schema

None.

## API (if any)

None.

## AI Query Path (if any)

None. Questions sent by starter cards and symbol tiles are the existing strings.

## UI (if any)

Routes: `/m`, `/m/c/$threadId`, `/m/history`, `/m/market`, `/m/watchlist`, `/m/account`, `/m/account/profile`.
RTL throughout; numbers keep tabular figures and `bdi` isolation. Both themes are supported.

## Non-Functional Requirements

- Text contrast at least 4.5:1 and touch targets at least 44px in both themes.
- No horizontal page scroll at 320px and 360px width, including quarterly and balance-sheet answers.
- Font files are bounded: Vazirmatn variable and JetBrains Mono in `woff2` only; the service-worker precache
  list grows by those files and nothing else.
- Offline launch renders with Vazirmatn once the service worker is installed.
- Client JavaScript for `/m` grows by no more than 25 kB gzip.

## Acceptance Criteria

- [x] AC-01 A browser check finds no rendered text below 13px on the `/m` screens (chart axis ticks excepted) and body text at 17px.
- [x] AC-02 A browser check records no request to Google Fonts hosts on `/m` and `/chat`; font files are in the precache list.
- [x] AC-03 With the network offline after first load, `/m` renders in Vazirmatn.
- [x] AC-04 `npm run lint` fails on a `lucide-react` import in a mobile file and passes on the tree.
- [x] AC-05 Bottom nav matches requirement 4; existing nav tests pass.
- [x] AC-06 Top bar matches requirement 5 for an admin and a non-admin user.
- [x] AC-07 Each of the six starter cards sends its unchanged `text` (unit test over `STARTER_PROMPTS`).
- [x] AC-08 The watchlist strip shows price and signed change and opens the symbol sheet.
- [x] AC-09 `m.market.tsx` and `m.watchlist.tsx` both render `SymbolRow`; the change badge has a non-colour direction cue and a screen-reader label.
- [x] AC-10 The symbol sheet has no «بستن» row and still closes by button, swipe, scrim, and Escape.
- [x] AC-11 Monthly-sales, quarterly, and balance-sheet answers show no horizontal page scroll at 320px and 360px.
- [~] AC-12 Desktop checks (`test:quarterly-png`, `test:balance-sheet-browser`, `test:symbol-snapshot-browser`) pass unchanged. Balance-sheet and symbol-snapshot pass; `test:quarterly-png` times out on unmodified code too (BUG-027).
- [x] AC-13 The four screens use the shared empty and error states.
- [x] AC-14 Copy matches the approved table and the terminology test passes.
- [x] AC-15 Contrast and touch-target checks pass in light and dark themes.
- [x] AC-16 Before and after screenshots at 360×800 for every `/m` screen, both themes, are stored under `evidence/`.

## Assumptions

- Gate A approved the design with its recommendation, so Phosphor Icons is the icon set.
- The disclaimer keeps its current position, at 13px, until OQ-073 is answered.
- The example subject on a starter card is a new optional display field in `starter-prompts.ts`; desktop cards ignore it.
- Moving the admin entry off the tab bar is accepted as part of the approved design.
