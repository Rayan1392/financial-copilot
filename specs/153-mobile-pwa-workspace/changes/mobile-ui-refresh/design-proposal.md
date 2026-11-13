# Design Proposal: Mobile UI Refresh (`/m`)

Status: Gate A: Approved by the owner on 2026-11-12

## Feature Folder

`153-mobile-pwa-workspace/changes/mobile-ui-refresh`

Layers: frontend · Area: cross-cutting (mobile workspace)

## Request

The owner asked for a review of Sapio's current phone design and a refactor proposal: more attractive icons,
a friendlier and better-looking UI/UX, and text that is short enough and larger.

## Discovery (code review of `/m`, 2026-11-12, develop `e0f922b`)

The review read the code only; the app was not run on a device and no screenshots were taken.

| # | Finding | Where |
|---|---|---|
| 1 | Base text is 15px; secondary text is 12–14px (72 small-text classes in 20 mobile files). | `src/frontend/src/components/mobile/mobile-screen.tsx:42` |
| 2 | Vazirmatn and JetBrains Mono load from Google Fonts (CSS `@import`, `<link>`, and a runtime font cache in the service worker). When Google Fonts is unreachable the app falls back to Tahoma, and a first offline launch has no font. | `src/frontend/src/styles.css:1`, `src/frontend/src/routes/__root.tsx:92-96`, `src/frontend/public/sw.js:28` |
| 3 | Icons are single-colour outline lucide icons at 16–20px. The active tab differs only by colour and a 3px line. | `src/frontend/src/components/mobile/bottom-nav.tsx` |
| 4 | The tab top bar holds five items at 360px: avatar, title, credit chip, admin gear, new chat. | `src/frontend/src/components/mobile/top-app-bar.tsx:119` |
| 5 | Starter prompts are full sentences in a flat list, four visible plus «بیشتر». The short `topic` label exists and is unused on mobile. | `src/frontend/src/routes/m.index.tsx:99`, `src/frontend/src/lib/chat/starter-prompts.ts` |
| 6 | Watchlist chips on the home screen show the symbol only. | `src/frontend/src/routes/m.index.tsx:132` |
| 7 | The symbol row is written twice (market movers, watchlist); price is 14px, company name 12px. | `src/frontend/src/routes/m.market.tsx:156`, `src/frontend/src/routes/m.watchlist.tsx:97` |
| 8 | The symbol sheet is a list of sentences without icons; price is 14px; it has a «بستن» row although the sheet already has a close button. | `src/frontend/src/components/mobile/symbol-sheet.tsx` |
| 9 | A 12px disclaimer sits permanently under the composer. | `src/frontend/src/components/mobile/mobile-composer.tsx:93` |
| 10 | Empty and error states are text only; the «تلاش مجدد» block is repeated in four routes. | `m.history.tsx`, `m.market.tsx`, `m.watchlist.tsx`, `m.c.$threadId.tsx` |

## Proposed Design

### 1. Type scale (one owner)

Mobile type tokens are defined once in `src/frontend/src/styles.css` and used through named utilities;
scattered pixel sizes in mobile files are removed.

| Role | Now | Proposed |
|---|---|---|
| Body and answer prose | 15px | 17px |
| Secondary (company name, time) | 12–13px | 14px |
| Section heading | 13px | 15px, bold |
| Price and change in a row | 14px | 17px, semibold |
| Index value | 20px | 24px |
| Tab label | 12px | 13px |
| Caption floor (timestamps, legal) | 12px | 13px |

Fonts are self-hosted (Vazirmatn variable, JetBrains Mono) and precached by the service worker. This part is
app-wide: desktop keeps the same typefaces but stops calling Google Fonts.

### 2. Icons

- Phosphor Icons (`@phosphor-icons/react`): `fill` weight for the active tab, `duotone` in tiles, `regular`
  elsewhere.
- One owner file, `src/frontend/src/components/mobile/icons.ts`, maps semantic names to icons, so the set can be
  replaced in one place. Mobile files stop importing `lucide-react` directly.
- Tabs: 26px icon; the active tab is filled and sits in a soft emerald pill.
- Cards and rows: 22px icon in a 40px tinted tile (emerald for sales, gold for statements).

Alternative considered: keep lucide with the same tiles and larger sizes. No new dependency, but no filled
weight for the active tab. Rejected in favour of Phosphor because the request is for more attractive icons;
the mapping file keeps the choice reversible.

### 3. Screens

| Screen | Change |
|---|---|
| Top bar (tabs) | Avatar, title, credit chip. The admin entry moves to the account screen; «گفتگوی جدید» is hidden on `/m`. |
| Bottom nav | 26px icons, filled active state in a pill, 13px labels. Four tabs unchanged. |
| Home | Six full-width starter cards: icon tile, short topic label, and the whole question text (owner's change on 2026-11-13; the approved draft had a two-column grid without the question). The question sent is unchanged. Watchlist becomes a horizontal strip of mini cards (symbol, price, signed change). |
| Market, watchlist | One shared `SymbolRow` (64px): symbol, company name, price, change badge with arrow. |
| Symbol sheet | Large price header; analysis prompts as icon tiles; the «بستن» row is removed. |
| Conversation | User bubble on soft emerald; answer prose at 17px in `compact` density only. |
| History, account | New type scale and icon tiles; theme row becomes a switch. |
| States | Shared `EmptyState` and `ErrorState` (icon, one sentence, one button). |

### 4. Copy

| Now | Proposed |
|---|---|
| پرسش خود را بنویسید… | بپرسید… |
| جست‌وجو در عنوان گفتگوها… | جست‌وجو… |
| هنوز نمادی دنبال نمی‌کنید. نمادهای مورد علاقه را اضافه کنید تا قیمت و تغییراتشان اینجا نمایش داده شود. | هنوز نمادی ندارید |
| پوسته: تیره — تغییر به روشن | حالت تیره (switch) |
| — فقط ۱۰۰ گفتگوی اخیر نمایش داده می‌شود — | ۱۰۰ گفتگوی اخیر |
| نمایش نسخه دسکتاپ | نسخه دسکتاپ |

All copy stays subject to `src/frontend/src/lib/copy/fa-terminology.ts`.

## Scope Boundaries

- Frontend only. No API, backend, database, routing, or tab-structure change.
- Desktop screens keep their layout; the only shared effect is the font source.
- Shared answer components change only under `compact` density.

## Risks

- Larger text can overflow quarterly and balance-sheet tables on 320–360px screens.
- `npm run test:mobile-browser` (71 checks) depends on current text and selectors and must be updated with them.
- The admin gear was added to every tab on purpose on 2026-11-05; this design moves it to the account screen.
- A second icon library alongside lucide (desktop) adds bundle weight; only imported icons are bundled.

## Open Questions

- OQ-073: where the legal disclaimer is shown (see `specs/open-questions.md`).
