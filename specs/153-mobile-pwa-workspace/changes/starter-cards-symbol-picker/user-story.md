# User Story: Starter cards ask for the symbol

## Feature Folder

`153-mobile-pwa-workspace/changes/starter-cards-symbol-picker`

## Layers / Area

Layers: frontend
Area: Conversations

## Priority

P2

## Complexity

M

## Depends On

153 change `starter-cards-cash-flow-and-industry-comparison`, 159 change `valuation-gauge-starter-card` (both released)

## References

- `specs/153-mobile-pwa-workspace/changes/starter-cards-symbol-picker/design-proposal.md` (approved at Gate A on 2026-11-27)
- `specs/open-questions.md` OQ-149, OQ-150, OQ-151 (answered 2026-11-27)
- `src/frontend/src/lib/chat/starter-prompts.ts` — the one list of starter cards
- `src/frontend/src/components/app/starter-card.tsx`, `starter-card-grid.tsx`, `src/frontend/src/routes/_app.chat.tsx` — desktop
- `src/frontend/src/components/mobile/mobile-starter-list.tsx`, `src/frontend/src/routes/m.index.tsx` — phone
- `src/frontend/src/components/mobile/add-symbol-sheet.tsx`, `src/frontend/src/lib/metadata.functions.ts` (`searchSymbolMetadata`) — existing symbol search
- `src/frontend/src/components/ui/dialog.tsx`, `command.tsx`, `src/frontend/src/components/mobile/bottom-sheet.tsx`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user on the new-chat page,
I want a starter card to ask me which symbol I mean,
so that one tap on a capability answers for my symbol instead of a fixed example.

## Business Context

The starter cards on desktop `/chat` and phone `/m` each name one fixed symbol. They demonstrate a capability once, then stop being useful, and they promote a few symbols. Asking for the symbol keeps the cards useful on every visit.

## Scope

- Six symbol cards show a symbol-free label with a «نماد» slot and open a symbol picker.
- A symbol picker with type-ahead search, watchlist symbols and an example symbol; choosing a symbol sends the card's question for it.
- `sales-mix` and `product-trend` stay fixed examples, marked «نمونه»; `ranking` is unchanged.
- The switched-off watchlist personalization of the cards is removed.

## Out Of Scope

- Limiting a card's search to symbols that have data for it (OQ-150 option b) → `specs/backlog.md`.
- Symbol autocomplete inside the chat composer → `specs/backlog.md`.
- Remembering recently picked symbols.
- Any backend, API or Telegram change; the symbol menu prompts in `symbol-prompts.ts`.

## Functional Requirements

1. Card kinds. `valuation-gauges`, `monthly-sales`, `industry-comparison`, `quarterly-report`, `balance-sheet` and `cash-flow` are symbol cards. `ranking`, `sales-mix` and `product-trend` are direct cards.
2. Symbol card label. The card shows the label below; ‹نماد› is drawn as a dashed pill inside the sentence. No symbol card shows a fixed symbol.

   | Card | Label | Question sent for symbol S | Example symbol |
   |---|---|---|---|
   | `valuation-gauges` | گیج‌های ارزش‌گذاری ‹نماد› | گیج‌های ارزش‌گذاری S | گدنا |
   | `monthly-sales` | روند فروش ماهانه ‹نماد› | روند فروش ماهانه S را نشان بده | کگهر |
   | `industry-comparison` | مقایسه ‹نماد› با صنعت | S را با صنعت خودش مقایسه کن | شاراک |
   | `quarterly-report` | عملکرد مالی فصلی ‹نماد› | عملکرد مالی فصلی S را بررسی کن | فولاد |
   | `balance-sheet` | صورت وضعیت مالی ‹نماد› | صورت وضعیت مالی S را بررسی کن | فاذر |
   | `cash-flow` | جریان وجه نقد ‹نماد› | جریان وجه نقد S | اخابر |

3. Direct cards send their text in one tap, as today. `sales-mix` and `product-trend` keep their current text and show a «نمونه» mark.
4. Selecting a symbol card opens the picker and sends nothing. Closing the picker (Escape, scrim, close button, swipe on the phone) sends nothing and returns focus to the card.
5. Picker content:
   - Title: the card's topic. A search field, focused on open.
   - A preview line with the exact question that will be sent; it shows the ‹نماد› slot until a row is highlighted (desktop) and the symbol of the highlighted row after.
   - Before typing: «دیده‌بان شما» with up to 8 watchlist symbols, newest followed first, rights issues left out, shown only when there are any; and «نمونه» with the card's example symbol.
   - While typing: results of `searchSymbolMetadata`, debounced 300 ms, from 1 character, at most 12 rows, each with symbol and company name. Rights issues are left out.
   - States: «در حال جست‌وجو…», «نمادی یافت نشد», and «جست‌وجو ناموفق بود» with «تلاش مجدد».
6. Choosing a row sends exactly the card's question for that symbol and closes the picker. There is no confirm button.
7. Every symbol in the search can be chosen; a symbol without data gets the assistant's normal no-data answer (OQ-150).
8. Cards stay disabled while a chat is starting and, on the phone, while offline. The gauge card stays hidden while `valuationGaugeCards` is unavailable. The picker closes when a chat starts.
9. `personalizedStarterPrompts`, `WATCHLIST_STARTER_PROMPTS_ENABLED` and the line «پیشنهادها بر اساس نمادهای دیده‌بان شما» are removed.

## Data / Schema

None.

## API (if any)

None new. The picker calls the existing `GET /api/ai/v1/metadata/symbols?search=&limit=` through `searchSymbolMetadata`.

## AI Query Path (if any)

Unchanged. The questions sent are the phrases the cards and the symbol menu already send.

## UI (if any)

- Desktop `/chat`: `StarterCard` renders the slot and the «نمونه» mark; a dialog hosts the picker as a cmdk combobox with its own filtering switched off (the server filters).
- Phone `/m`: `MobileStarterList` renders the same; the full-height `BottomSheet` hosts the picker, like «افزودن نماد».
- Right-to-left; symbols isolated with `bdi`; both themes; touch targets at least 44 px.

## Non-Functional Requirements

- Search requests: debounced, not retried automatically, cached for 60 s per term (as `AddSymbolSheet` does). A failed search never blocks the example or watchlist rows.
- Accessibility: the card announces that it opens a dialog; the picker is operable by keyboard alone (arrows, Enter, Escape) and labelled for screen readers; the slot is read as «نماد».
- No horizontal overflow at 360 px width.

## Acceptance Criteria

- [x] AC-1: On `/chat` and `/m`, the six symbol cards show the labels of requirement 2 with the «نماد» slot and no fixed symbol.
- [x] AC-2: Selecting a symbol card opens the picker and sends nothing; closing it sends nothing and returns focus to the card.
- [x] AC-3: Choosing symbol S sends exactly the question of requirement 2 — for the gauge card and فولاد: «گیج‌های ارزش‌گذاری فولاد» — and closes the picker.
- [x] AC-4: Before typing, the picker lists the example symbol, and the watchlist symbols when the user has any (at most 8, no rights issues).
- [x] AC-5: Typing searches after the debounce and shows symbol and company name; searching, empty and failed states appear, and «تلاش مجدد» searches again.
- [x] AC-6: The preview line shows the question that will be sent.
- [x] AC-7: `ranking`, `sales-mix` and `product-trend` send their current text in one tap; the last two show «نمونه».
- [x] AC-8: The picker works with the keyboard alone on desktop.
- [x] AC-9: The gauge card is hidden while `valuationGaugeCards` is unavailable; cards are disabled while a chat is starting and while the phone is offline.
- [x] AC-10: Both themes, desktop and 360 px phone: no horizontal overflow, slot and «نمونه» mark readable (contrast at least 4.5:1 for text).
- [x] AC-11: The watchlist personalization code and its notice line are gone; «افزودن نماد» behaves as before.

## Assumptions

- The metadata search handles Persian input variants (ی/ي, ک/ك, half-space) as it does today for «افزودن نماد»; this change checks and reports it, and does not change it.
- The example symbols are the symbols the cards name today.
