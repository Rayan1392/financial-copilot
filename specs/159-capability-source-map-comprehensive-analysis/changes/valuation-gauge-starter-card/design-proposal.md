# Design proposal: valuation gauge starter card

Status: Proposed — not approved

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-starter-card`

## Layers / Area

frontend · AI

## Request

The owner (2026-11-26): «گیج‌های ارزش‌گذاری گدنا» را به‌عنوان نمونه در کادرهای پیشنهاد صفحهٔ اول نشان بده (موبایل
هم). در وب دسکتاپ، چون تعداد کادرها فرد می‌شود، یک طراحی خوب بزن.

## Current behaviour (discovery)

- **One list.** `src/frontend/src/lib/chat/starter-prompts.ts` (`STARTER_PROMPTS`) holds the starter cards for
  desktop `/chat` and mobile `/m`. It has eight cards: five in the sales tone (emerald) and three in the statement
  tone (gold).
- **Desktop.** `src/frontend/src/routes/_app.chat.tsx` uses `grid-cols-1 md:grid-cols-2`, so the eight cards make
  four rows. A ninth card would sit alone in a fifth row.
- **Desktop intro.** The intro text above the cards lists the capabilities, and the badge says «پوشش فعلی: تولید،
  فروش و گزارش‌های مالی». Neither mentions valuation.
- **Mobile.** `src/frontend/src/routes/m.index.tsx` shows a one-column list. `STARTER_ICONS` maps each id to a
  Phosphor icon, and `ValuationGaugesIcon` (`Gauge`) already exists in `components/mobile/icons.ts`.
- **Availability.** `useChatCapabilityAvailability()` asks the API whether gauge cards are offered (the flag, the
  source mapping and V2 routing). The symbol-menu shortcut already depends on it. While the answer is loading or
  after a failure, the shortcut stays hidden.
- **The question.** `ValuationGaugeIntentRules` routes «گیج‌های ارزش‌گذاری ‹نماد›» to the three cards. Tests cover
  شاراک but not گدنا.

## Proposed design

1. **The card.** Add the id `valuation-gauges` with these fields:
   - text «گیج‌های ارزش‌گذاری گدنا» (with the zero-width non-joiner, as the routing rules expect);
   - topic «ارزش‌گذاری»;
   - a `forSymbol` that builds the same question for another symbol;
   - a new tone, `valuation`.
2. **Shown only when it works.** The card appears only when `useChatCapabilityAvailability().valuationGaugeCards`
   is true. This is the same rule as the menu shortcut, so turning the flag off never leaves a card that would get a
   wrong answer. When the card is hidden, the page looks exactly as it does today.
3. **Desktop: one wide featured card for the odd count.**
   - The gauge card spans both columns (`md:col-span-2`) at the top of the grid, and the eight cards keep their four
     even rows below it.
   - Inside, on the right: the topic and the question as on the other cards, plus a third line «P/E · P/S · قیمت
     تعادلی» that says what the answer contains.
   - On the left: a small decorative half-dial drawn in SVG, with green, neutral and red bands, the same dial tokens
     (`--gauge-*`) as the real cards, and a pointer.
   - On hover the pointer eases a few degrees, and stays still under `prefers-reduced-motion`.
   - The dial is `aria-hidden`. The button's accessible name is the question.
   - Colour: the new `valuation` tone uses the dial's own colours (the `--gauge-eq-*` blue for the accent), so the
     card reads as part of the gauge family and differs from the sales and statement cards.
4. **Mobile.**
   - The card is first in the list, uses the same row design as the others, and has the `ValuationGaugesIcon` tile
     in the new tone.
   - The row also gets the short line «P/E · P/S · قیمت تعادلی».
   - The list has one column, so the odd count needs no special layout.
5. **Desktop intro.** Add «گیج‌های ارزش‌گذاری» to the capability sentence, and change the badge to «پوشش فعلی:
   تولید، فروش، گزارش‌های مالی و ارزش‌گذاری». The intro text changes only when the card is shown, so it never
   promises a hidden capability.
6. **Tests.**
   - Vitest: the list (nine cards when the feature is available, eight when it is not, unique ids); the featured
     card spans both columns; the mobile order and icon.
   - The browser check for the gauge cards gets the home page at 1280, 768, 430 and 375 px, in light and dark, and
     checks that no element is clipped and the grid has no orphan card.
7. **Records.** Add a `CHANGELOG.md` line: «نمونهٔ «گیج‌های ارزش‌گذاری گدنا» به پیشنهادهای صفحهٔ اول اضافه شد.»

## Sketch

```
Desktop (≥ 768px)
┌──────────────────────────────────────────────────────────────┐
│  ╭───╮   ارزش‌گذاری                                        ▌ │
│ ( ╱  )   گیج‌های ارزش‌گذاری گدنا                              │
│  ▔▔▔▔    P/E · P/S · قیمت تعادلی                         ←   │
└──────────────────────────────────────────────────────────────┘
┌─────────────────────────────┐ ┌─────────────────────────────┐
│ فروش ماهانه                 │ │ ترکیب فروش                  │
└─────────────────────────────┘ └─────────────────────────────┘
          … four even rows of the eight existing cards …

Mobile
┌──────────────────────────────────┐
│ [◔] ارزش‌گذاری                   │
│     گیج‌های ارزش‌گذاری گدنا       │
│     P/E · P/S · قیمت تعادلی      │
├──────────────────────────────────┤
│ [◑] ترکیب فروش …                 │
```

## Decisions for the owner

| Id | Question | Proposed |
|---|---|---|
| OQ-141 | Where does the featured card go on desktop, and where is the new item in the mobile list? | At the top, the first thing users see, because it is the newest capability. Alternative: at the bottom, so the cards appearing after the availability answer never move the existing cards. |
| OQ-142 | Should the desktop intro badge and sentence mention valuation? | Yes, only while the card is shown (section 5). Alternative: leave the intro as it is. |

## Risks

- **Data for گدنا.** The example must answer well. Before Gate B I will check that production returns three usable
  cards for «گیج‌های ارزش‌گذاری گدنا», or ask the owner to try it, since the feature is live. If a card is missing,
  the owner picks another symbol.
- **Price.** With change `valuation-gauge-per-card-credits`, this card costs 3 credits. That is the owner's
  decision, and the card itself shows no price.
- **Layout shift.** The availability answer is cached for five minutes. On a cold load the card can appear after
  the others. The top position moves the eight cards down once; see OQ-141.

## Alternatives considered

- **Three columns (3 × 3).** The cards would be about 240 px wide inside `max-w-3xl`, and the Persian questions would
  wrap to three lines. Rejected.
- **The ninth card centred alone in the last row.** It looks like a leftover, not a choice. Rejected.
- **Dropping a card to keep an even count.** Each card stands for one capability (the rule in
  `starter-prompts.ts`). Rejected.
