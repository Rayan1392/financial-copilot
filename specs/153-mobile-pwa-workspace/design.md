# Feature 153 — Mobile UX/UI design

**Status:** Design approved by the owner on 2026-11-03, with decisions D-Q1–D-Q7 and D-A1 (revised: no retry in v1) recorded in [spec.md](spec.md) §11. Implemented on 2026-11-03 (see [ImplementationReport.md](ImplementationReport.md)). Companion to [spec.md](spec.md).
**Visual direction:** this design reuses the existing SapioAI design system and adds no new palette or font.
- Tokens: `styles.css`, with dark-first `--background`, `--surface`, `--surface-2`, `--hairline`, `--emerald` (primary and up), `--rose` (down and danger), and `--gold` (warning and stale).
- Typeface: Vazirmatn. Radius: `--radius` 0.75rem. The brand mark is the emerald dot tile used in the sidebar and auth screen.
- Components: existing shadcn/Radix primitives — `drawer` (vaul) for bottom sheets, `skeleton`, `sonner` toasts, `tabs`, `progress`. The light theme follows the existing `.light` class and toggle.

Wireframes are drawn right-to-left: the right edge of each box is the start of the line.

## 1. Global mobile rules

| Topic | Rule |
|---|---|
| Viewport | `/m` head: `width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content`. Zoom is never disabled. |
| Height | The shell uses `100dvh`, with `100vh` as fallback. Each screen scrolls its own main region; the body never scrolls behind sheets. |
| Safe areas | Top bar: `padding-top: env(safe-area-inset-top)`. Bottom tab bar and docked composer: `padding-bottom: max(8px, env(safe-area-inset-bottom))`. Side padding uses `env(safe-area-inset-left/right)` in landscape. |
| Keyboard | On Chromium, `interactive-widget=resizes-content` shrinks the layout viewport. On iOS, a `visualViewport` resize listener sets `--keyboard-inset` on the shell, so the docked composer sits above the keyboard and the message list keeps its bottom anchored. The tab bar is never shown while the keyboard is open. |
| Type scale | Body 15 px/1.8 (Persian needs generous leading). Inputs ≥ 16 px. Secondary text ≥ 12 px. Section titles 13 px semibold. Screen titles 18 px bold. Index values 20 px semibold with tabular figures. No 10–11 px text. |
| Touch | Minimum target 44×44 px, 8 px between adjacent targets, and a pressed state of `bg-surface-2` with a 120 ms opacity transition. No hover-only affordances. A visible focus ring (`ring-2 ring-emerald/40`) for keyboard and switch users. |
| RTL | Content and the tab bar follow DOM order = visual right→left. Back = `ArrowRight`. Disclosure chevrons point left (`ChevronLeft`). Progress bars fill from the right. |
| Mixed scripts | Numbers use `formatNumber`/`formatPercent` (Persian digits). Signed percentages, P/E-style Latin tokens, and Latin tickers are wrapped in `<bdi>`, or `dir="ltr"` for a signed number, so `+۱٫۲۳٪` never renders as `۱٫۲۳٪+`. Tabular numerals are used in lists. |
| Direction cues | Up = emerald + ▲, down = rose + ▼, zero/unknown = muted + «–». Color is never the only signal. |
| Motion | Sheets slide 200 ms, toasts fade. `prefers-reduced-motion` uses opacity only. |
| Themes | Both themes are supported. `theme-color` meta follows the active theme. |
| Errors | Inline cards in the screen body, with `role="alert"`, a Persian message, and a primary action. They never use a modal for recoverable errors. |
| Freshness | Every market-data view shows «به‌روزرسانی {relativeTime}». Stale quotes show a gold «داده قدیمی» chip. |

## 2. Shell

```
┌─────────────────────────────────────┐  ← safe-area top
│ (avatar)  ساپیو            [✎ جدید] │  top app bar, 56px
├─────────────────────────────────────┤
│                                     │
│            screen content           │  scrolls
│                                     │
├─────────────────────────────────────┤
│  گفتگو   تاریخچه   بازار   دیده‌بان   │  bottom tab bar, 64px + safe-area
│  (rightmost = گفتگو)                 │
└─────────────────────────────────────┘
```

- **Top app bar.**
  - Start (right): the avatar initial, which opens `/m/account` with label «حساب کاربری». It falls back to a generic user icon when the profile has no display name.
  - Center: the screen title, or the brand on the گفتگو tab.
  - End (left): the «گفتگوی جدید» icon button (`SquarePen`, 44 px, label «گفتگوی جدید»).
- **Bottom tab bar.** Four tabs (icon + label, 12 px label): گفتگو (`MessageSquare`), تاریخچه (`History`), بازار (`LineChart`), دیده‌بان (`Eye`).
  - The active tab is emerald with a 3 px indicator and `aria-current="page"`.
  - Re-tapping the active tab scrolls that screen to the top.
- **Offline banner.** Gold, under the top bar: «اتصال اینترنت برقرار نیست». It has `role="status"` and does not push the tab bar.
- **Update toast.** Bottom, above the tab bar: «نسخه جدید آماده است» with the button «به‌روزرسانی».
- **Auth gate.**
  - During SSR and until client-side auth resolves, the shell renders skeleton bars only, so navigation is never flashed to signed-out users.
  - If unauthenticated, it redirects to `/auth?redirect=<current /m path + search>`.
  - Session loss mid-use (refresh failure) clears the cache and redirects the same way.

## 3. گفتگو — new chat (`/m`)

```
┌─────────────────────────────────────┐
│ (A)  ساپیو                   [✎]    │
├─────────────────────────────────────┤
│        ●  چه چیزی را بررسی کنیم؟      │  16px heading, no marketing badge
│                                     │
│  ┌───────────────────────────────┐  │
│  │ روند فروش ماهانه کگهر را نشان بده ‹│  │  starter chips: full-width rows,
│  ├───────────────────────────────┤  │  min-height 52px, max 4 visible,
│  │ گزارش فصلی فملی               ‹│  │  "بیشتر" reveals the rest
│  ├───────────────────────────────┤  │
│  │ ترکیب فروش محصولات کگهر را…    ‹│  │
│  └───────────────────────────────┘  │
│  [ نمادهای دیده‌بان من: شپنا  فولاد ]  │  optional row (if watchlist non-empty):
│                                     │  tap = symbol sheet (§6.4)
├─────────────────────────────────────┤
│ ┌─────────────────────────────┐ (↑) │  docked composer
│ │ پرسش خود را بنویسید…          │     │  16px, autosize 1–6 lines
│ └─────────────────────────────┘     │
│ صرفاً جنبه آموزشی؛ توصیه سرمایه‌گذاری نیست │  12px disclaimer
├─────────────────────────────────────┤
│  گفتگو●  تاریخچه   بازار   دیده‌بان    │
└─────────────────────────────────────┘
```

- **Starter prompts.** These are the desktop list, moved into one shared module (T-01) so both experiences use the same source. Tapping a starter submits it immediately, as desktop does.
- **Composer.**
  - On touch keyboards, Enter inserts a newline. Send is the 44 px emerald button.
  - The button is disabled when the text is empty, a request is pending, or the device is offline; when offline it explains why via an `aria-describedby` hint.
  - `enterkeyhint="send"` is set only on hardware-keyboard devices. The composer does not autofocus on load, so the keyboard does not cover the starters.
- **Submitting.**
  - The screen switches in place to the conversation layout: the user bubble, then the «در حال تحلیل...» placeholder (`aria-live="polite"`).
  - After 20 s it adds «تحلیل‌های مفصل ممکن است کمی بیشتر طول بکشد».
  - The tab bar hides while a request is pending on this screen. On success the app navigates (replace) to `/m/c/{id}`.
- **Failure** (see spec AC-06a and decision D-A1). There is **no** automatic retry and **no** «تلاش مجدد» button on any send failure, because a same-key replay currently re-runs the AI and can create a duplicate conversation.
  - **First message, network or 5xx error.** The client has no conversation ID (the server creates it and returns it only on success), so it cannot check whether the request was processed.
    - The user bubble stays, marked «وضعیت نامشخص». The draft is restored to the composer.
    - The error card (`role="alert"`) says «وضعیت این درخواست مشخص نیست و ممکن است پردازش شده باشد. پیش از ارسال دوباره، تاریخچه را بررسی کنید.» Its only action is the link «تاریخچه».
    - The conversations list is invalidated, so تاریخچه shows any conversation the server created.
    - The card has no resend button. The user can still press send on the restored draft after checking تاریخچه; that is a new request.
  - «تلاش مجدد» remains only on **read** errors (loading a conversation, history, market, watchlist, account), where repeating a GET is safe.
  - **Insufficient credit.** «اعتبار کافی برای پردازش درخواست وجود ندارد» plus «مشاهده اعتبار» (→ `/m/account`). No external purchase link (D-Q6).
  - **Cooldown.** The existing cooldown message. No retry action.
  - **Long requests.** No client timeout (D-Q4). The 20 s "takes longer" hint stays, and the request runs until the server responds or the connection fails.

## 4. Conversation (`/m/c/$threadId`)

```
┌─────────────────────────────────────┐
│ [→]  روند فروش کگهر…          [✎]   │  title: 1 line, ellipsis; back = history/previous
├─────────────────────────────────────┤
│              ┌──────────────────┐   │  user bubble: right-aligned, surface, max 85%
│              │ روند فروش کگهر…   │   │
│              └──────────────────┘   │
│ ● پاسخ                              │  assistant: full-width, no bubble
│ متن پاسخ… (markdown)                 │
│ ┌───────────────────────────────┐   │  rich blocks (charts/tables) full-bleed
│ │ chart / table  ← scrolls inside │   │  inside the content column; tables scroll
│ └───────────────────────────────┘   │  horizontally in their own container
│ [پرسش پیشنهادی ۱] [پرسش پیشنهادی ۲]   │  suggested questions: wrap, 44px chips
│                                     │
├─────────────────────────────────────┤
│ ┌─────────────────────────────┐ (↑) │  docked composer (no tab bar on this screen)
│ └─────────────────────────────┘     │
└─────────────────────────────────────┘
```

- **Rendering.** The existing `MessageList` is reused with a new `density="compact"` prop: 16 px horizontal padding, `space-y-6`, no avatar column on the user side. All rich result blocks (scanner, symbol lookup, monthly trends, product comparison, PS gauge, statements, disclosures listing) and their paging handlers are reused unchanged. Admin diagnostics are hidden on mobile.
- **Scroll.**
  - On open, the view jumps to the bottom without animation.
  - When a new answer arrives, it scrolls so the **start** of the answer is visible, rather than the bottom, because long answers are read top-down.
  - A «↓ پیام‌های جدید» pill appears when the user has scrolled up.
- **Loading.** Three skeleton message rows.
- **Not found / deleted.** «این گفتگو پیدا نشد», with buttons تاریخچه and گفتگوی جدید.
- **Load error.** «بارگذاری گفتگو ناموفق بود» + «تلاش مجدد».
- **Pending** behaves as in §3, inline at the bottom. Credit and cooldown failures behave as in §3.
- **Follow-up failure, network or 5xx error** (spec AC-06b). The conversation ID is known, so the app reconciles before offering any resend.
  1. The user bubble stays with «در حال بررسی وضعیت...». The draft is kept in memory. No action is shown yet.
  2. The app refetches the conversation's messages and compares them with the message IDs it held before sending.
  3. **Answer found** (a new exchange whose user message matches the draft): the exchange replaces the pending bubble, the error is removed, and the draft is cleared.
  4. **Refetch succeeded, answer not found:** the error card shows «پاسخی برای این پرسش ثبت نشد. ممکن است درخواست قبلی هنوز در حال پردازش باشد.» and **«ویرایش و ارسال دوباره»**. That action moves the draft into the composer; sending is an explicit new request.
  5. **Refetch failed:** the card shows the indeterminate-status text and **«بررسی دوباره»**, which repeats only the refetch. No resend action is offered until a refetch succeeds.
- **Overflow menu.** A `⋯` button in the top bar opens a sheet with «حذف گفتگو» (confirm sheet; on success go to `/m/history`).

## 5. تاریخچه (`/m/history`)

```
┌─────────────────────────────────────┐
│ (A)  تاریخچه گفتگو             [✎]  │
├─────────────────────────────────────┤
│ ┌ 🔍 جستجو در عنوان گفتگوها…     [×] ┐ │  sticky search, 16px input
├─────────────────────────────────────┤
│ امروز                               │  group header (13px, muted)
│ ┌───────────────────────────────┐   │
│ │ روند فروش ماهانه کگهر    ۱۰:۴۲  [⋯]│   │  row ≥ 56px: title (2-line clamp),
│ ├───────────────────────────────┤   │  time (today) or date; ⋯ = 44px
│ │ گزارش فصلی فملی          ۰۹:۱۵  [⋯]│   │
│ └───────────────────────────────┘   │
│ دیروز                               │
│ ۷ روز گذشته                          │
│ قدیمی‌تر          (Jalali date per row) │
│ — فقط ۱۰۰ گفتگوی اخیر نمایش داده می‌شود — │  only when 100 returned
├─────────────────────────────────────┤
│  گفتگو   تاریخچه●  بازار   دیده‌بان    │
└─────────────────────────────────────┘
```

- **Data.** `listThreads({ limit: 100 })`, sorted by `updatedAt` from the server. Groups use the device timezone, with Jalali labels via `toLocaleDateString("fa-IR")`.
- **Search.**
  - Instant client-side filtering, with no request per keystroke.
  - Normalization: ي→ی, ك→ک, ZWNJ and extra spaces removed, Persian/Arabic digits mapped to Latin for comparison.
  - The query is kept in component state only.
- **Row actions.** Tapping a row opens `/m/c/{id}`. `⋯` opens a sheet with «حذف گفتگو», which leads to a confirm sheet («این گفتگو برای همیشه حذف می‌شود.» with the buttons «حذف» in rose and «انصراف»). Swipe gestures are deliberately not used.
- **States.**
  - Loading: 6 skeleton rows.
  - Empty: «هنوز گفتگویی ندارید» + «شروع گفتگو».
  - No search results: «نتیجه‌ای یافت نشد».
  - Error: «تاریخچه در دسترس نیست» + «تلاش مجدد».
  - Offline: cached in-memory list + banner.
- **Refresh.** Refetch on tab focus and on app resume when the data is older than 60 s. There is no pull-to-refresh: it conflicts with the browser's native gesture outside standalone mode.

## 6. بازار (`/m/market`)

```
┌─────────────────────────────────────┐
│ (A)  بازار                     [✎]  │
├─────────────────────────────────────┤
│ وضعیت شاخص‌ها     به‌روزرسانی ۲ دقیقه پیش [⟳]│
│ ┌───────────────┐┌───────────────┐  │  2-column grid of index cards
│ │ شاخص کل        ││ شاخص هم‌وزن     │  │  (1 column under 360px)
│ │ ۲٬۴۱۵٬۳۰۰      ││ ۷۸۰٬۱۲۰         │  │  value 20px tabular
│ │ ▲ +۱٫۲۴٪       ││ ▼ −۰٫۳۱٪        │  │
│ └───────────────┘└───────────────┘  │  … up to 6 named indices
│                                     │
│ بیشترین رشد                          │
│  شپنا   ۵٬۴۲۰   ▲ +۵٫۰۰٪           ‹ │  rows ≥ 52px → symbol sheet
│ بیشترین افت                          │
│  فولاد  ۸٬۱۱۰   ▼ −۴٫۸۰٪           ‹ │
├─────────────────────────────────────┤
│  گفتگو   تاریخچه   بازار●  دیده‌بان    │
└─────────────────────────────────────┘
```

- **Data.** `getMarketSummary` (same query key `["market-summary"]` as desktop). Render `indices` in server order. Use the `name` field, falling back to the symbol. `null` values render «–».
- **Movers.** Show gainers and losers only when non-empty. Mover `isStale` shows the «داده قدیمی» chip. Tapping a mover opens the symbol sheet (§6.4). If the symbol is not followed, the sheet offers «افزودن به دیده‌بان», which resolves the ID with `external-company-id?symbol=` and then follows.
- **States.**
  - Loading: skeleton cards.
  - Empty indices: «داده شاخص در دسترس نیست».
  - Error: «داده بازار در دسترس نیست» + «تلاش مجدد».
  - The refresh button `⟳` (44 px) refetches and shows a spinner for at least 700 ms, as the desktop credit refresh does.
- **Not shown** (the backend always returns null for these): real money flow, trending industries, insight.

## 6.1 دیده‌بان (`/m/watchlist`)

```
┌─────────────────────────────────────┐
│ (A)  دیده‌بان                   [✎]  │
├─────────────────────────────────────┤
│ ۸ نماد · به‌روزرسانی ۱ دقیقه پیش   [+ افزودن]│
│ ┌───────────────────────────────┐   │
│ │ شپنا                 ۵٬۴۲۰      │   │  row ≥ 60px: symbol (bold) + company name;
│ │ پالایش نفت اصفهان     ▲ +۵٫۰۰٪  ‹│   │  price + signed change
│ ├───────────────────────────────┤   │
│ │ فولاد  [داده قدیمی]    ۸٬۱۱۰      │   │
│ │ فولاد مبارکه         ▼ −۴٫۸۰٪  ‹│   │
│ └───────────────────────────────┘   │
├─────────────────────────────────────┤
│  گفتگو   تاریخچه   بازار   دیده‌بان●   │
└─────────────────────────────────────┘
```

- **Data.**
  - `getWatchlist` supplies quotes (in server order).
  - `getFollowedSymbols` supplies `externalCompanyId` and `companyName`, joined by symbol. A missing join renders the row without a company name, and unfollow is then disabled (with a reason in the sheet).
  - The query keys `["watchlist"]` and `["followed-symbols"]` are shared with desktop.
- **States.**
  - Loading: skeleton rows.
  - Empty: «هنوز نمادی دنبال نمی‌کنید. نمادهای مورد علاقه را اضافه کنید تا قیمت و تغییراتشان اینجا نمایش داده شود.» + «افزودن نماد».
  - Error: «دیده‌بان در دسترس نیست» + «تلاش مجدد».

### 6.2 Add-symbol sheet

A full-height bottom sheet with a 16 px search field (autofocus is allowed here because the user asked for it). Search runs `searchSymbolMetadata({ search, limit: 12 })`, debounced 300 ms.

Rows show the symbol and company. Rows already followed show «دنبال‌شده». Tapping a row follows the symbol (optimistic disabled state), then shows the success toast «{نماد} به دیده‌بان اضافه شد» and invalidates the watchlist queries.

On 400, the API message is shown inline (for example the plan's symbol limit). Other states: «نمادی یافت نشد» and «جستجو ناموفق بود» + retry.

### 6.3 Symbol action sheet

```
┌─────────────────────────────────────┐
│ ─── (grabber)                       │
│ شپنا · پالایش نفت اصفهان              │
│ ۵٬۴۲۰  ▲ +۵٫۰۰٪  · ۱ دقیقه پیش        │
├─────────────────────────────────────┤
│ تحلیل با دستیار                      │
│  روند فروش ماهانه شپنا              ‹ │
│  گزارش فصلی شپنا                   ‹ │
│  تحلیل تکنیکال شپنا                 ‹ │
│  پرسش دیگری درباره شپنا…            ‹ │  → /m with composer prefilled "شپنا "
├─────────────────────────────────────┤
│  حذف از دیده‌بان   (rose)              │  or «افزودن به دیده‌بان» for movers
│  بستن                               │
└─────────────────────────────────────┘
```

### 6.4 Analysis prompt templates (decided: D-Q1)

These templates live in the single shared frontend module `src/frontend/src/lib/chat/symbol-prompts.ts`. Every frontend surface that offers symbol prompts reuses it. They are phrased like existing working prompts (`/chat` starters and AGENTS.md examples):
- `روند فروش ماهانه {symbol} را نشان بده`
- `گزارش فصلی {symbol}`
- `تحلیل تکنیکال {symbol}`

Choosing one submits it as a **new** conversation (the flow in §3).

## 7. Account (`/m/account`)

```
┌─────────────────────────────────────┐
│ [→]  حساب کاربری                     │
├─────────────────────────────────────┤
│ (A) علی رضایی                         │  display name (profile), email (account)
│     user@example.com                │
├─────────────────────────────────────┤
│ اشتراک: حرفه‌ای            [⟳]        │  planName
│ اعتبار هوش مصنوعی  ۱٬۲۰۰ از ۲٬۶۰۰       │  finite: progress (fills from right)
│ ████████░░░░░░                      │  Unlimited: «نامحدود» · «تا ۱۴۰۵/۰۹/۳۰»
│ برای خرید یا تمدید اعتبار، از طریق حساب    │  purchase guidance: text only,
│ کاربری خود در تحلیل‌اپ اقدام کنید.          │  no external link (D-Q6)
├─────────────────────────────────────┤
│ پروفایل و امنیت حساب               ‹ │  → /profile (desktop page, see note)
│ اتصال تلگرام                       ‹ │  → /telegram-link
│ پوسته: تیره / روشن               [◐] │
│ نصب برنامه                         ‹ │  only when installable & not standalone
│ نمایش نسخه دسکتاپ                  ‹ │
├─────────────────────────────────────┤
│ خروج از حساب   (rose)                │
└─────────────────────────────────────┘
```

- **Data and logic reuse.** Data comes from `getUsage`, `getOwnProfile` and `getAccountSummary`. The finite/unlimited logic is reused from `lib/credit-progress.ts` (`computeCreditProgress`, `creditQuotaLabel`).
- **Credit tone colors** stay emerald, gold and rose, with a text label as well.
- **Profile and Telegram.**
  - «پروفایل و امنیت حساب» opens `/m/account/profile`. That route renders the existing profile page body, extracted into a shared component (task T-14b). No profile logic is duplicated.
  - «اتصال تلگرام» opens the existing `/telegram-link` page under the desktop shell. That page is single-column, and at 360 px it lacks only the hidden side columns. Neither `/profile` nor `/telegram-link` is auto-redirected (spec §7.2), so these links never loop.
- **States.** Usage loading uses a skeleton. On usage error, «اعتبار در دسترس نیست» + retry. When the profile has no display name, only the email is shown.

## 8. Unauthenticated and auth states

- **`/auth` (existing `AuthScreen`).** Already single-column. Mobile fixes, shared with desktop:
  - inputs `text-base` (16 px) to stop iOS zoom;
  - `autocomplete="email"` / `"current-password"`;
  - `inputmode="email"`;
  - safe-area padding.
  - The «ثبت‌نام از طریق تحلیل‌اپ» link stays.
- **Redirect.** After login, `window.location.assign(redirect)` returns the user to the original `/m…` path.
- **`/launch`.** Unchanged. Its final `/chat` load is redirected server-side to `/m` on phones.
- **Session expired.** The shell redirects to `/auth?redirect=…`. A toast «نشست شما به پایان رسید؛ دوباره وارد شوید» is shown on the auth screen when the redirect carries `reason=expired`; this is a closed value.

## 9. PWA-specific surfaces

- **Install card** on `/m`, shown after the first successful answer and only when installable:
  - text «ساپیو را روی صفحه اصلی نصب کنید تا سریع‌تر به آن دسترسی داشته باشید.»;
  - actions «نصب» and «بعداً»;
  - «بعداً» hides it for 30 days.
- **iOS instructions sheet.** Step 1: tap Share (icon shown). Step 2: «افزودن به صفحه اصلی». Step 3: «افزودن».
- **`/offline.html`.** A static Persian RTL page: the brand tile, the heading «اتصال اینترنت برقرار نیست», the text «پس از اتصال دوباره تلاش کنید.», and the button «تلاش مجدد» (`location.reload()`). It uses inline CSS with the same dark tokens and the system font stack, because Vazirmatn may not be cached.
- **Desktop suggestion banner** (desktop shell, viewport < 1024 px): «برای دسترسی کامل به تاریخچه، بازار و دیده‌بان، نسخه موبایل را باز کنید.» with «باز کردن» and `×`.

## 10. Primary user journeys

1. **TahlilApp user on a phone.** TahlilApp → `/launch?code` → session → `/chat` (server 302) → `/m` → taps a starter → answer at `/m/c/{id}`.
2. **Returning user finds an old analysis.** Opens the installed app (`/m`) → تاریخچه → types «کگهر» → taps the result → reads it → asks a follow-up.
3. **Market check.** بازار → reviews 6 indices → taps a top gainer → «افزودن به دیده‌بان» → toast.
4. **Watchlist analysis.** دیده‌بان → taps شپنا → «گزارش فصلی شپنا» → new conversation with the answer.
5. **Low credit.** Asks a question → insufficient-credit error card → «مشاهده اعتبار» → Account shows 0 from 260, a progress bar, and the TahlilApp purchase guidance text (no link until the destination is confirmed).
6. **Prefers desktop on a phone.** Account → «نمایش نسخه دسکتاپ» → `/chat`, remembered across reloads → desktop header «نسخه موبایل» to return.
7. **Flaky network, follow-up.** Asks a follow-up in a conversation → the connection drops → the app refetches and reconciles the conversation.
   - If the answer arrived server-side, it is shown.
   - If not, the error card offers «ویرایش و ارسال دوباره» with the text intact. The user resends deliberately.
   - If the refetch also fails, only «بررسی دوباره» is offered until the connection returns.
8. **Flaky network, first message.** Asks a first question → the connection drops → the error card says the status is unknown and links to تاریخچه; the draft stays in the composer → the user opens تاریخچه, finds the new conversation with its answer, or returns and sends the draft again.
9. **Expired session deep link.** Opens a shared `/m/c/{id}` → `/auth?redirect=/m/c/{id}` → login → back to the conversation.

## 11. State matrix

| Screen | Loading | Empty | Error | Offline | Permission / auth |
|---|---|---|---|---|---|
| `/m` | — | starters | send: indeterminate-status card + تاریخچه link, draft kept, no retry (§3) | send disabled + banner | gate → `/auth` |
| `/m/c/$id` | skeleton | — (a new thread shows the pending bubble) | load error + retry; not found; send: reconcile, then resend or «بررسی دوباره» (§4) | in-memory messages + banner; send disabled | gate; 404 if another user's |
| `/m/history` | skeleton rows | «هنوز گفتگویی ندارید» | «تاریخچه در دسترس نیست» + retry | in-memory list | gate |
| `/m/market` | skeleton cards | «داده شاخص در دسترس نیست» | «داده بازار در دسترس نیست» + retry | in-memory + age label | gate; 403 shows «دسترسی به داده بازار برای اشتراک شما فعال نیست» |
| `/m/watchlist` | skeleton rows | onboarding + «افزودن نماد» | «دیده‌بان در دسترس نیست» + retry | in-memory | gate; 403 handled the same way |
| `/m/account` | skeleton | — | per-section error + retry | in-memory | gate |

The 403 text applies because `MarketSummaryRead` and `WatchlistReadSelf` are policies; this is defensive handling.
