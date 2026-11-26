# User Story: Valuation gauge starter card

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-starter-card`

## Layers / Area

Layers: frontend
Area: AI

## Priority

P2

## Complexity

S

## Depends On

`159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards` (released in v1.29.2)

## References

- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-starter-card/design-proposal.md`
  (approved at Gate A on 2026-11-26)
- `specs/open-questions.md`: OQ-141, OQ-142 (answered at Gate A)
- `src/frontend/src/lib/chat/starter-prompts.ts` (the one starter list), `src/frontend/src/components/app/starter-card.tsx`,
  `src/frontend/src/routes/_app.chat.tsx`, `src/frontend/src/routes/m.index.tsx`
- `src/frontend/src/lib/chat/use-capability-availability.ts` (availability owned by the API)
- `src/frontend/src/components/mobile/icons.ts` (`ValuationGaugesIcon`), `src/frontend/src/components/mobile/icon-tile.tsx`
- Dial tokens `--gauge-*` in `src/frontend/src/styles.css`
- Precedent: `specs/153-mobile-pwa-workspace/changes/starter-cards-cash-flow-and-industry-comparison/`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a user opening a new chat,
I want an example card for the valuation gauges,
so that I learn the feature exists and how to ask for it.

## Business Context

The gauge cards (P/E, P/S, equilibrium range) are live, but nothing on the home pages points to them. The starter cards
teach users how to phrase a question. The owner chose «گیج‌های ارزش‌گذاری گدنا» as the example (2026-11-26).

## Scope

- One new starter card on desktop `/chat` and mobile `/m`, shown only while the gauge cards are available.
- A desktop layout that keeps the grid even when the card count is odd: the gauge card is wide and featured at the top.
- The desktop intro mentions valuation while the card is shown.

## Out Of Scope

- Any backend, routing, billing or gauge-card change. The card costs what the question costs (3 credits for three cards).
- Watchlist personalization (still off by owner decision, 2026-11-06).
- The stale «six starter cards» check in `scripts/mobile-workspace.browser.mjs` (→ `specs/backlog.md`).

## Functional Requirements

1. **The card.** `STARTER_PROMPTS` gets an entry with:
   - id `valuation-gauges`;
   - text «گیج‌های ارزش‌گذاری گدنا» (with the zero-width non-joiner, the routed phrase);
   - topic «ارزش‌گذاری»;
   - detail «P/E · P/S · قیمت تعادلی»;
   - tone `valuation`;
   - `forSymbol`;
   - `requires: "valuationGaugeCards"`.
2. **Availability.** `homeStarterPrompts(followed, availability)` leaves out every card whose `requires` capability is not
   available. `useChatCapabilityAvailability()` supplies the availability. While it is loading or after a failure, the
   card is hidden. Without the card the pages show the eight cards exactly as today.
3. **Order (OQ-141).** The gauge card is first in the list on both pages.
4. **Desktop.**
   - The gauge card is a featured card spanning both columns (`md:col-span-2`).
   - On the right are the topic, the question and the detail line.
   - On the left is a decorative half-dial SVG (`aria-hidden`) in the `--gauge-*` colours. Its pointer eases a few
     degrees on hover or focus and stays still under `prefers-reduced-motion`.
   - The eight other cards keep their two-column grid below it.
5. **Mobile.** The card is the first row. It has the same row design as the others, the `ValuationGaugesIcon` tile in
   the `valuation` tone, and the detail line under the question.
6. **Tone.** `valuation` uses the gauge blue (`--gauge-eq-high`) for the accent, tile and topic, in both themes. The
   small topic text has at least 4.5:1 contrast.
7. **Desktop intro (OQ-142).** While the card is shown:
   - the badge reads «پوشش فعلی: تولید، فروش، گزارش‌های مالی و ارزش‌گذاری»;
   - the capability sentence adds «گیج‌های ارزش‌گذاری».
   Otherwise the intro is unchanged.
8. **Sending.** Clicking or tapping sends the card's exact text, as every starter card does.

## Data / Schema

None.

## API (if any)

None new. The existing `GET /api/ai/v1/capabilities/availability` (`valuationGaugeCards`) is reused.

## AI Query Path (if any)

Unchanged. The card sends «گیج‌های ارزش‌گذاری گدنا», which `ValuationGaugeIntentRules` routes to `valuation_gauges`.

## UI (if any)

- Desktop `/chat`: the starter grid is extracted into `components/app/starter-card-grid.tsx`, with the featured card in
  `starter-card.tsx`.
- Mobile `/m`: the starter list is extracted into `components/mobile/mobile-starter-list.tsx`.
- RTL throughout. The dial is drawn left-to-right like the real gauges. Persian labels as in requirement 1.

## Non-Functional Requirements

- No layout shift for the eight existing cards while the availability answer is cached. On a cold load the eight
  cards move down once when the card appears, as accepted at Gate A.
- No horizontal scroll at 320 px or wider. Touch targets are at least 44 px. Visible focus.
- No new dependency.

## Acceptance Criteria

- [x] AC-01: with gauge cards available, desktop and mobile show nine cards and the gauge card is first.
- [x] AC-02: with gauge cards unavailable, or the availability request loading or failed, both pages show the current
  eight cards in the current order, and the desktop intro is unchanged.
- [x] AC-03: on desktop at 768 px and wider, the gauge card spans both columns and the eight others fill four full
  rows. No row holds a lone card.
- [x] AC-04: clicking or tapping the card sends exactly «گیج‌های ارزش‌گذاری گدنا».
- [x] AC-05: the card's accessible name is its question. The dial is hidden from assistive technology. The card has a
  visible focus ring.
- [x] AC-06: under `prefers-reduced-motion` the dial pointer does not move.
- [x] AC-07: the topic, question and detail text are at least 4.5:1 against the card, in light and dark.
- [x] AC-08: at 1280, 768, 430, 375 and 320 px there is no horizontal scroll and no clipped text. Screenshots are kept
  as evidence.
- [x] AC-09: while the card is shown, the desktop badge and intro sentence mention valuation (OQ-142).

## Assumptions

- **Before release, the owner confirms that «گیج‌های ارزش‌گذاری گدنا» returns three cards in production.** If it does
  not, the owner picks another symbol. That is a one-line change of the card text.
- The availability answer that decides the menu shortcut also decides this card (one owner, the API).
