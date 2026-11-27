# Starter cards ask for the symbol

Status: Gate A: Approved by the owner on 2026-11-27

## Feature Folder   `153-mobile-pwa-workspace` · change `starter-cards-symbol-picker`
## Layers / Area    frontend · Conversations
## Depends On       153 change `starter-cards-cash-flow-and-industry-comparison`, 159 change `valuation-gauge-starter-card`
## References       `src/frontend/src/lib/chat/starter-prompts.ts`, `src/frontend/src/components/app/starter-card.tsx`, `src/frontend/src/components/mobile/mobile-starter-list.tsx`, `src/frontend/src/components/mobile/add-symbol-sheet.tsx`, `src/frontend/src/lib/metadata.functions.ts`

## Owner request (2026-11-26)

1. A starter card no longer names one fixed symbol.
2. The card may show a shorter phrase, but the chat receives exactly the question sent today, with the user's symbol.
3. Selecting a card asks for the symbol with type-ahead search; choosing a symbol sends the question.

Example: the card «گیج‌های ارزش‌گذاری گدنا» becomes symbol-free; the user picks فولاد and «گیج‌های ارزش‌گذاری فولاد» is sent.

## Discovery

- One list owns the cards for desktop `/chat` and mobile `/m`: `STARTER_PROMPTS` in `starter-prompts.ts`. Each card has `text` (shown and sent) and, for six cards, `forSymbol(symbol)`, which already builds the same question for another symbol.
- Three cards have no `forSymbol`: `sales-mix` (kept on کگهر by owner decision, because the answer is not reliable for every company), `product-trend` (names a company-specific product) and `ranking` (market-wide, no symbol).
- Watchlist personalization of the cards exists but is switched off (`WATCHLIST_STARTER_PROMPTS_ENABLED = false`, owner decision 2026-11-06): a symbol is not known to have the data a card asks about. This change meets the same risk for every symbol the user can pick.
- Symbol search exists: server function `searchSymbolMetadata` (`/api/ai/v1/metadata/symbols`), used with a 300 ms debounce in the mobile `AddSymbolSheet`. It returns `symbolCode`, `companyName`, `externalCompanyId`. No backend change is needed.
- UI building blocks exist: mobile `BottomSheet`; desktop `dialog.tsx`, `popover.tsx`, `command.tsx` (cmdk combobox).
- The mobile list carries the note "the cards teach how to phrase a question". A shorter symbol-free label weakens that; the design restores it with a preview of the exact question.

## UX assessment

Benefits:

- The card answers for the symbol the user cares about, so it stays useful after the first visit.
- The landing page stops promoting particular symbols.
- The watchlist becomes useful on the cards without the risk of a wrong automatic choice.

Costs, and how the design handles them:

| Cost | Handling |
|---|---|
| One tap becomes several steps. A new user with no symbol in mind loses the instant demo. | Before any typing, the picker lists the user's watchlist symbols and the card's current example symbol as «نمونه». A demo stays two taps. |
| A picked symbol may have no data for the card (the reason personalization was switched off). | OQ-150, answered: every symbol is offered and the assistant gives its normal no-data answer (phase 1). |
| A symbol-free label no longer teaches the phrasing. | The picker shows the exact question that will be sent, and the question appears in the chat as today. |
| Cards that open a picker and cards that send at once look the same. | Symbol cards show a «نماد» slot in the text; send-at-once cards keep the arrow. |

## Proposed design

### Card kinds

- **Symbol card**: `valuation-gauges`, `monthly-sales`, `industry-comparison`, `quarterly-report`, `balance-sheet`, `cash-flow`. Shows a short label with a symbol slot. Selecting it opens the symbol picker.
- **Direct card**: `ranking`. Unchanged: selecting it sends its text.
- **Fixed example card**: `sales-mix` and `product-trend`. They keep their text and send in one tap, and are marked «نمونه» (OQ-151, answered).

### Card text

The label is a short phrase with a visible slot — a dashed pill reading «نماد» inside the sentence. The slot tells the user that a choice follows and keeps the sentence shape. The sent question is unchanged.

| Card | Label on the card (proposed) | Question sent |
|---|---|---|
| `valuation-gauges` | گیج‌های ارزش‌گذاری ‹نماد› | گیج‌های ارزش‌گذاری {نماد} |
| `monthly-sales` | روند فروش ماهانه ‹نماد› | روند فروش ماهانه {نماد} را نشان بده |
| `industry-comparison` | مقایسه ‹نماد› با صنعت | {نماد} را با صنعت خودش مقایسه کن |
| `quarterly-report` | عملکرد مالی فصلی ‹نماد› | عملکرد مالی فصلی {نماد} را بررسی کن |
| `balance-sheet` | صورت وضعیت مالی ‹نماد› | صورت وضعیت مالی {نماد} را بررسی کن |
| `cash-flow` | جریان وجه نقد ‹نماد› | جریان وجه نقد {نماد} |

Topic labels, icons, tones, order, the featured gauge card and its detail line stay as they are. The wording was approved as proposed (OQ-149).

### Symbol picker

- Desktop: a centered dialog with a search field and a result list (cmdk combobox). Mobile: the existing full-height bottom sheet, like «افزودن نماد».
- Title: the card's topic. Under the search field, one line shows the question that will be sent; it updates with the highlighted symbol.
- Before typing: «دیده‌بان شما» (up to 8 symbols, newest first, rights issues left out) and «نمونه» (the card's current example symbol).
- While typing: debounced search (300 ms, from 1 character, 12 results), each row showing symbol and company name. States: searching, no result, failed with «تلاش مجدد».
- Choosing a row sends the question and closes the picker; there is no separate confirm button. The card costs what it costs today, and the preview line shows the question before the choice.
- Keyboard: focus starts in the search field; arrows move, Enter chooses, Escape closes and returns focus to the card. Touch targets are at least 44 px. Text is right-to-left; symbols are isolated with `bdi`.
- While a chat is starting, or the phone is offline, cards stay disabled as today.

### Code shape (frontend only)

- `StarterPrompt` separates what is shown from what is sent: a direct card keeps `text`; a symbol card has `label`, `exampleSymbol` and `forSymbol`. `forSymbol` stays the one owner of the question wording.
- A shared hook owns debounced symbol search; `AddSymbolSheet` and the new picker both use it.
- One picker content component is hosted by the desktop dialog and the mobile bottom sheet.
- The switched-off watchlist personalization (`personalizedStarterPrompts`, its flag and the «پیشنهادها بر اساس نمادهای دیده‌بان شما» line) is removed; the picker's watchlist list replaces it.

## Alternatives considered

- **Fill the composer and autocomplete there** (card writes «گیج‌های ارزش‌گذاری » into the composer, symbols are suggested as the user types). Teaches typing best and would serve every question, but it is a much larger change to both composers. Candidate for `specs/backlog.md`.
- **Card turns into a search field in place.** No overlay, but it is cramped in the two-column grid and collides with the phone keyboard.
- **Turn watchlist personalization back on.** No new UI, but it picks the symbol for the user and does nothing for users without a watchlist.

## Risks

- Extra step lowers first-use engagement. Mitigated by the «نمونه» and watchlist rows; worth comparing card use before and after release.
- No-data answers for picked symbols (OQ-150).
- Search quality for Persian input (ی/ي, ک/ك, half-space) is the backend's existing behaviour; it is checked during implementation and reported, not changed here.

## Open questions

OQ-149, OQ-150 and OQ-151 in `specs/open-questions.md` were answered at Gate A on 2026-11-27: all three as proposed.

## Draft acceptance criteria

- No symbol card on `/chat` or `/m` shows a fixed symbol; each shows its label with the «نماد» slot.
- Selecting a symbol card opens the picker; nothing is sent until a symbol is chosen. Closing the picker sends nothing.
- Choosing symbol S sends exactly `forSymbol(S)` — for the gauge card and فولاد: «گیج‌های ارزش‌گذاری فولاد».
- Before typing, the picker lists watchlist symbols (when there are any) and the example symbol.
- The picker works with keyboard only and with a screen reader, in both themes, with no horizontal overflow at 360 px.
- The ranking card still sends its text in one tap.
- The gauge card is still hidden while `valuationGaugeCards` is unavailable.

## Implementation outline (detailed plan follows Gate A)

1. Model: `StarterPrompt` kinds, labels, example symbols; remove personalization; unit tests.
2. Shared symbol-search hook; move `AddSymbolSheet` onto it.
3. Picker content component with states; tests.
4. Desktop: slot rendering in `StarterCard`, dialog host in `/chat`.
5. Mobile: slot rendering in `MobileStarterList`, bottom-sheet host in `/m`.
6. Update `starter-cards.browser.mjs`, component tests, docs; lint, type-check, tests.
