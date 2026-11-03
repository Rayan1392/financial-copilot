# Feature 153 — Mobile-first PWA workspace (`/m`)

**Status:** Phase 1 design approved by the owner on 2026-11-03, with decisions D-Q1–D-Q7 and D-A1 (§11; D-A1 revised the same day: no retry in v1, first-message and follow-up failures handled separately). **Phase 2 implemented on 2026-11-03** after explicit owner approval; not committed, pushed, or deployed. Results: [ImplementationReport.md](ImplementationReport.md).
**Bounded context:** Owned web application (presentation/delivery). No change to the AI, Billing, Conversation, or FinancialData domains is required for v1.
**Companion documents:** [discovery.md](discovery.md) (evidence), [design.md](design.md) (screens and states), [tasks.md](tasks.md) (implementation plan).

## 1. Problem statement

On a phone, SapioAI shows the desktop chat workspace with both side columns CSS-hidden (`ConversationSidebar` below 768 px, `ContextPanel` below 1024 px). Phone users cannot open chat history, see market indices or watchlist prices, see their credits, or log out. They land on a marketing-style `/chat` hero, with a composer that zooms on iOS and submits on Enter. A large share of TahlilApp traffic is mobile, and those users arrive through the TahlilApp launch flow. For them the product is effectively unusable. See [discovery.md](discovery.md).

## 2. Goals

1. Phone users reach the full **core workspace**: ask, continue, and find conversations, plus market indices, watchlist with symbol analysis, and credits/account.
2. The mobile experience is a dedicated, touch-first, Persian RTL UI at `/m`. It is not the desktop layout shrunk down.
3. Phones are routed to `/m` at entry without breaking deep links, without loops, and without removing access to the desktop experience.
4. SapioAI is installable as a PWA with a safe, network-first caching policy that never stores financial data, conversations, or credentials.
5. Reuse existing APIs, server functions, and rendering components. Do not add business logic to the frontend.

## 3. Scope

**In scope (v1):**
- Routes `/m` (new chat), `/m/c/$threadId`, `/m/history`, `/m/market`, `/m/watchlist`, `/m/account`, `/m/account/profile`.
- Shared frontend orchestration hooks extracted from the desktop pages, so desktop and mobile run the same chat, history, market, and watchlist logic.
- Device resolution and redirect (server for document requests, client for SPA navigations), a manual switch, and a remembered preference.
- Web app manifest, icons, iOS meta, service worker (static assets plus an offline fallback), update prompt, and install entry points.
- Offline and network-failure messaging. Draft-preserving recovery for failed questions, with separate handling for first-message and follow-up failures (AC-06). No retry action: reliable retry is blocked until the API provides verified idempotent replay (D-A1, proposed B-3).
- Accessibility (WCAG 2.2 AA intent), 44×44 px targets, safe areas, virtual-keyboard handling.
- A small shared fix: 16 px inputs on the auth screen, to stop iOS zoom on mobile login.

## 4. Non-goals

- Token streaming or SSE. The backend returns one complete answer, and adding streaming is a separate backend feature.
- Push notifications, background sync, or offline sending/queueing of questions.
- Offline reading of conversations, market, or watchlist data across reloads. Only in-memory data of the running session is shown.
- Mobile versions of admin pages, `/getCompanyId`, `/disclosures`, the followed-symbol **insight feed** (deferred to a later feature, D-Q7), the assisted-query dialog, and orchestration diagnostics. These stay reachable through "نسخه دسکتاپ".
- A client-side chat timeout (D-Q4), an external purchase link (D-Q6), and any backend change (D-Q2, including the proposed B-3).
- Any automatic retry or «تلاش مجدد» action for failed questions (D-A1).
- Conversation rename, pinning, or sharing. These have no API.
- In-app purchase or top-up checkout, which TahlilApp owns.
- Redesign of the desktop layout. The only desktop changes are a "نسخه موبایل" entry point, a narrow-viewport suggestion banner, and the behavior-preserving hook extraction.
- Native apps or app-store distribution.

## 5. Current-state summary

Summarized from [discovery.md](discovery.md):
- TanStack Start SSR running in a Wrangler Worker. Data flows through `/_serverFn/*` and then to the .NET API.
- The access token is kept in sessionStorage. The refresh cookie is HttpOnly, `SameSite=Strict`, path `/api/auth/v1`, on the API origin, and lasts 30 days.
- Chat is request/response (`POST /api/ai/v1/query`, idempotency key required).
- History is `GET /api/ai/v1/conversations?limit≤100`, ordered by most recent, with no search.
- Market data comes from `GET /api/v1/market/summary` (6 indices plus 3 gainers and 3 losers). The watchlist comes from `GET /api/v1/watchlists/me` plus followed-symbols CRUD and metadata search. Credits come from `GET /api/v1/usage/me`.
- No PWA assets exist.

## 6. Information architecture and navigation model

```
/m  (mobile shell: top app bar + bottom tab bar, auth-guarded)
├── گفتگو      /m              new-chat composer + starter prompts
│   └── /m/c/$threadId         conversation (pushed screen: no tab bar, docked composer)
├── تاریخچه    /m/history      searchable, date-grouped conversation list
├── بازار      /m/market       6 named indices + top gainers/losers
├── دیده‌بان    /m/watchlist    followed-symbol quotes → symbol action sheet
└── account (avatar in top app bar) /m/account   profile, credits, theme, install, switch to desktop, logout
```

**Chosen pattern: a four-tab bottom navigation bar, a top app bar with a persistent "new chat" action and an account avatar, and bottom sheets for contextual actions.**

Why this pattern:
- The four destinations are peers that users switch between often. A bottom bar keeps them visible and within thumb reach. Material and Apple HIG both recommend 3–5 bottom tabs.
- A hamburger drawer is rejected. Hiding history behind a drawer reproduces today's discoverability failure.
- Account is visited rarely, so it lives behind the avatar in the top bar. This keeps the tab bar at four wide (≥ 80 px) targets on a 320 px screen.
- "New chat" is the primary action. It is always one tap away: the **گفتگو** tab is itself the new-chat composer, and the top bar shows a "گفتگوی جدید" icon button on every screen, including inside a conversation.
- The conversation screen is pushed **over** the tabs. Hiding the tab bar there gives the answer and the docked composer the full height. The back button returns to the previous tab (history, or the composer home).
- Bottom sheets (the existing `vaul` drawer) hold contextual actions: conversation actions, symbol actions, add symbol, delete confirmation, and install instructions. They are reachable with the thumb and dismiss by swipe, the scrim, or a close button.

RTL behavior: DOM order equals visual right-to-left order, so **گفتگو** is the rightmost tab. The back arrow points right (`ArrowRight`). "Forward" chevrons point left. Swipe-to-go-back is left to the browser and OS. No custom horizontal gestures are added, to avoid conflict with system back gestures.

## 7. Device routing and user control

### 7.1 Decision model

One pure module owns the decision: `src/frontend/src/lib/experience/`. Both the server and the client adapters call the same function. There is no duplicated detection logic.

```
resolveExperience({ preference, uaMobileHint, userAgent }) → "mobile" | "desktop"
  1. explicit preference ("mobile" | "desktop") always wins
  2. Sec-CH-UA-Mobile / navigator.userAgentData.mobile === true → mobile
  3. UA phone pattern (iPhone|iPod|Android…Mobile|Mobile…Firefox|Opera Mini|Windows Phone) → mobile
  4. otherwise (desktop, iPad*, Android tablets, unknown, bots) → desktop
```
\* iPadOS reports a desktop Safari UA. It is treated as desktop by design.

### 7.2 Path mapping

A single bidirectional table is the only owner of these mappings:

| Desktop path | Mobile path | Auto-redirect phones? |
|---|---|---|
| `/chat` | `/m` | yes |
| `/c/$threadId` | `/m/c/$threadId` | yes |
| `/followed-symbols` | `/m/watchlist` | yes |
| `/profile` | `/m/account`, `/m/account/profile` | reverse only (the profile page is linked from mobile account, so auto-redirecting it would loop) |
| `/chat` (from `/m/history`, `/m/market`) | `/m/history`, `/m/market` | reverse only |
| `/`, `/auth`, `/launch*`, `/verify-*`, `/reset-password`, `/forgot-password`, `/admin*`, `/disclosures`, `/getCompanyId`, `/telegram-link`, `/top-up` | — | **never** |

The query string is preserved. The hash is preserved on client navigations. Servers never receive the hash, which is acceptable.

### 7.3 Where detection runs

1. **Server (document requests):** a request middleware or adapter in the Worker pipeline (`src/start.ts` request middleware, or the `src/server.ts` wrapper; T-05 picks one).
   - It acts only when **all** of these hold: method is `GET`, the request is a top-level navigation (`Sec-Fetch-Mode: navigate`, or `Accept` contains `text/html` when Fetch Metadata is absent), the path is in the table, the resolution is `mobile`, and the feature flag is on.
   - Response: `302` to the mapped path, with `Cache-Control: private, no-store` and `Vary: Sec-CH-UA-Mobile, User-Agent, Cookie`.
   - It never touches `/_serverFn/*`, assets, or any non-GET request.
   - This catches the TahlilApp launch (`window.location.replace("/chat")`), bookmarks, shared links, and post-login `window.location.assign(redirect)`.
2. **Client (SPA navigations):** the `/_app` `beforeLoad` and the authenticated redirect in `/` call the same module with `navigator.userAgentData?.mobile`, `navigator.userAgent`, and the preference cookie. They `throw redirect()` to the mapped path. This covers in-app navigations that never reach the server.

### 7.4 Loop prevention

The redirect is **one-directional**. Automatic logic only ever maps desktop → mobile, and only when the resolution is `mobile`. `/m*` never auto-redirects to desktop. A manual switch writes the preference **before** navigating, so the next resolution agrees with the destination. A unit test checks, for every combination of preference, hint, and UA and every mapped path, that applying the redirect twice is the same as applying it once.

### 7.5 Manual switch and preference

- Mobile: **Account → «نمایش نسخه دسکتاپ»** sets `sapio_experience=desktop` and navigates to the reverse-mapped path.
- Desktop: a «نسخه موبایل» link (header) sets `sapio_experience=mobile` and navigates to the mapped `/m` path.
- One-shot override: `?experience=mobile|desktop` on any page sets the cookie, and the parameter is then removed with `history.replaceState`. This is useful for support and for TahlilApp linking.
- Cookie `sapio_experience`: values `mobile|desktop`, `Path=/`, `Max-Age=31536000`, `SameSite=Lax`, `Secure` outside localhost, **not** HttpOnly (the client guard reads it). It holds no personal data. Clearing it restores automatic detection.
- Direct navigation to `/m` always works on any device, regardless of preference. It does not change the preference.

### 7.6 Tablets, small windows, resizing, and unreliable UA

- Tablets default to desktop. At 768–1023 px the desktop `ContextPanel` is hidden. The desktop shell therefore shows a dismissible banner, «برای دسترسی کامل به تاریخچه، بازار و دیده‌بان، نسخه موبایل را باز کنید», whenever the viewport is < 1024 px. Dismissal is remembered in `localStorage` (a convenience only).
- Resizing never triggers an automatic switch. That would destroy in-progress state such as a pending question or a typed draft. Resizing only shows or hides the suggestion banner.
- On `/m` at viewport ≥ 1024 px with a fine pointer, a dismissible «نسخه دسکتاپ» suggestion is shown, with no redirect.
- The UA is used only as a fallback hint. Wrong guesses are recoverable in one tap, and the manual preference permanently overrides them.

### 7.7 SEO

- Only `/` (the login page) is public. It is **never** redirected, so crawlers see a stable URL.
- All `/m*` routes emit `<meta name="robots" content="noindex">`. Their content is behind authentication, so canonical links are not needed. No 301 is used, so nothing is permanently cached by crawlers or proxies.

### 7.8 Rollout control

- The server and client auto-redirect is behind a build-time flag, `VITE_MOBILE_AUTO_REDIRECT` (default `false`). Build-time is used because `api-client.server.ts` documents that Worker runtime env vars are unreliable.
- `/m` itself is always routable once deployed, for internal QA.
- Rollback means redeploying with the flag set to `false`. The cookie stays harmless.

## 8. API reuse map and proposed backend changes

### 8.1 Reuse (no change)

| Screen | API(s) | Existing client |
|---|---|---|
| New chat / conversation | `POST /api/ai/v1/query`, `GET /api/ai/v1/conversations/{id}/messages` | `sendChatMessage`, `getThreadMessages` |
| History | `GET /api/ai/v1/conversations?limit=100`, `DELETE /api/ai/v1/conversations/{id}` | `listThreads` (gains an optional `limit` input; frontend only), `deleteThread` |
| Market | `GET /api/v1/market/summary` | `getMarketSummary` |
| Watchlist | `GET /api/v1/watchlists/me`, `GET /api/v1/followed-symbols/me`, `POST/DELETE /api/v1/followed-symbols/me/{externalCompanyId}`, `GET /api/ai/v1/metadata/symbols`, `GET /api/v1/market/external-company-id` | `getWatchlist`, `getFollowedSymbols`, `followSymbolByExternalId`, `unfollowSymbolByExternalId`, `searchSymbolMetadata`; one **new** server function for `external-company-id` (frontend only; the endpoint exists) |
| Account | `GET /api/v1/usage/me`, `GET /api/auth/v1/profile`, `GET /api/auth/v1/account`, `POST /api/auth/v1/logout` | `getUsage`, `getOwnProfile`, `getAccountSummary`, `logout` |

### 8.2 Optional backend changes — **deferred (D-Q2)**

v1 is frontend-only and uses the existing APIs. B-1 and B-2 are kept below for a later decision. Revisit B-2 if the client-side watchlist join proves ambiguous or unreliable, for example duplicate tickers or a missing followed-symbol row for a quoted symbol. T03 logs such mismatches in tests and the implementation report.

**B-1 — Keyset paging and title search for conversations.**
- *User need:* users with more than 100 conversations cannot reach older ones, and client-side search covers only the newest 100.
- *Why existing APIs fall short:* `limit` is capped at 100, and there is no cursor or `q` parameter.
- *Shape:* `GET /api/ai/v1/conversations?limit=1..100&before=<opaque cursor>&q=<≤100 chars>` → `{ items: ConversationSummaryResponse[], nextCursor: string|null }`.
  - Order: `(UpdatedAt desc, Id desc)`. `q` is a case-insensitive title match after Persian normalization (ی/ي, ک/ك, ZWNJ, digits), done in the Application layer.
  - Auth and tenant/actor scoping are unchanged. 400 for an invalid cursor, an over-long `q`, or a limit out of range.
  - **Compatibility:** the current array response must stay for existing callers. Either version the response (a new path such as `/conversations/page`) or return the envelope only when `before`/`q` is present. To be decided at approval.
- *Recommendation:* defer until usage data shows users beyond 100 conversations.

**B-2 — Add `externalCompanyId` and `companyName` to `WatchlistQuote`.**
- *User need:* show the company name and allow unfollow without a second request.
- *Why:* `GET /watchlists/me` returns only the ticker, so v1 must also call `GET /followed-symbols/me` and join by symbol.
- *Shape:* additive nullable fields. No breaking change.
- *Recommendation:* optional cleanup. v1 works without it.

### 8.3 Proposed backend change — **not in v1; requires separate owner approval**

**B-3 — Idempotent replay of the original `/query` result.**
- *User need:* a phone that loses the connection during a long question needs a safe way to recover the answer, without paying twice, re-running the AI, or creating a duplicate conversation.
- *Why existing APIs fall short:* a replay with the same idempotency key and payload is single-charge, but it re-runs the AI workflow and persists a new exchange, and for a first message it creates a second conversation ([discovery.md](discovery.md), D-A1).
- *Shape (to be designed if approved):*
  - Within the Conversations/AI facade context, keyed by actor and idempotency key, record the outcome of a completed query: conversation ID, exchange ID, and the response.
  - A replay with the same key and fingerprint returns that recorded outcome without running the workflow. A replay while the original is still running returns a distinct, documented retryable status instead of starting a second run. A different payload keeps today's 409.
  - Retention period, storage bounds, and cleanup must be defined.
  - Tests: replay returns an identical result, persists no new exchange or conversation, charges once, and handles the concurrent in-flight case.
- *Effect on the frontend:* only after B-3 is implemented **and** verified end to end may a same-key «تلاش مجدد» be added, as a separate follow-up task.
- *Recommendation:* propose for a later backend feature. It is not implemented in Feature 153 v1 without explicit approval.

## 9. PWA requirements

### 9.1 Manifest (`/manifest.webmanifest`)
`name` «ساپیو - دستیار هوشمند بازار», `short_name` «ساپیو», `description` from the existing meta, `lang: "fa"`, `dir: "rtl"`, `start_url: "/m"`, `scope: "/"`, `display: "standalone"`, `background_color` and `theme_color` equal to the dark `--background` in hex (to be confirmed against `#09090b`), and icons 192, 512 and 512-maskable PNG. No orientation lock.

`scope: "/"` is **decided (D-Q3)**, so that `/auth`, `/launch` and the desktop switch stay inside the installed app window.

### 9.2 Head tags
`<link rel="manifest">`, `apple-touch-icon` (180 px), `apple-mobile-web-app-capable=yes`, `apple-mobile-web-app-title=ساپیو`, `apple-mobile-web-app-status-bar-style=black-translucent`.

On `/m` routes only: `viewport` = `width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content`. Zoom is **not** disabled.

### 9.3 Service worker (`/sw.js`)
The service worker is hand-written and small. No new build plugin is added, because the Lovable config forbids duplicate Vite plugins. It is registered only in production builds, only on secure contexts, with scope `/`.

| Request | Strategy | Notes |
|---|---|---|
| `GET /assets/*` (content-hashed JS/CSS/fonts) | cache-first, versioned cache `sapio-static-<build>`, max 200 entries | immutable |
| `GET /manifest.webmanifest`, icons, `/offline.html`, `favicon.ico` | precache on install | static, no user data |
| `GET` Google Fonts CSS and font files (`fonts.googleapis.com`, `fonts.gstatic.com`) | stale-while-revalidate, max 30 entries | public assets |
| Navigations (HTML documents) | **network-only**. On network failure, serve precached `/offline.html` | SSR HTML is never cached |
| `/_serverFn/*` | **network-only, never cached** | carries bearer-authenticated financial and conversation data |
| API origin (`VITE_FINANCIAL_COPILOT_API_BASE_URL`), `/api/*` | **network-only, never cached** | includes auth, refresh, and usage |
| `/launch*`, `/auth`, `/verify-*`, `/reset-password`, `/forgot-password` | network-only, no offline fallback substitution beyond the generic page | contain one-time tokens |
| Any non-GET, any response with `Cache-Control: no-store` or `private`, any other cross-origin | passthrough | — |

`activate` deletes caches whose name is not the current build's. A **kill-switch** procedure is documented: deploy a `sw.js` that calls `self.registration.unregister()` and clears its caches.

### 9.4 Install experience
- Chromium: capture `beforeinstallprompt`. Show «نصب برنامه» in Account, and a dismissible card on the `/m` home after the user's first successful answer. Never show it on first paint.
- iOS Safari, not standalone: «افزودن به صفحه اصلی» shows a sheet with Share → Add to Home Screen steps.
- Hide all install entries when running standalone (`display-mode: standalone` or `navigator.standalone`).

### 9.5 Offline and network failure
- An `online`/`offline` listener plus failed requests drive a persistent top banner: «اتصال اینترنت برقرار نیست».
- Offline, the send button is disabled with the explanation «برای ارسال پرسش به اینترنت نیاز است». The typed draft is kept.
- Market, watchlist, and history keep showing the data already in React Query memory, with an «آخرین به‌روزرسانی …» label. Nothing is persisted.
- A reload while offline shows `/offline.html`: Persian RTL, branded, with a «تلاش مجدد» button.

### 9.6 Updates
- A new `sw.js` installs and waits. The app shows a toast: «نسخه جدید آماده است» with «به‌روزرسانی».
- Tapping it posts `SKIP_WAITING`, and the page reloads on `controllerchange`. The app **never** reloads automatically while a question is pending.
- `vite:preloadError` (missing chunk after a deploy) shows the same update prompt instead of a blank error.
- `sw.js` must be served with `Cache-Control: no-cache`, verified in the Wrangler runtime (T-19).

### 9.7 Security rules (binding)
- No API response, server-function response, conversation content, token, cookie, or credential is ever written to Cache Storage, IndexedDB, or localStorage by this feature.
- Preferences in `localStorage` or cookies are limited to the experience choice, banner and install dismissals, and theme (theme already exists).
- The access token remains in sessionStorage as today. Its handling does not change.
- An automated test asserts that the service worker's fetch handler returns passthrough or network-only for `/_serverFn/`, `/api/`, and the API origin.

### 9.8 Authentication in the installed app (documented behavior)
iOS standalone apps have storage separate from Safari. A user who installs the app logs in once **inside** it (email/password or a fresh TahlilApp launch). After that, the 30-day refresh cookie keeps the session. A TahlilApp launch link opens in the browser, not in the installed app. Session persistence depends on the frontend and API origins being same-site, because the refresh cookie is `SameSite=Strict`. This must be verified for production origins (D-Q5).

## 10. Acceptance criteria

Each criterion is verifiable by an automated test (unit, component, or Playwright mobile viewport), unless it is marked **[manual]**.

**Navigation and shell**
- **AC-01** At 360×800, an authenticated user at `/m` sees a bottom bar with exactly four tabs: گفتگو, تاریخچه, بازار, دیده‌بان. The active tab has `aria-current="page"`. Each tab target is ≥ 44×44 CSS px.
- **AC-02** A «گفتگوی جدید» control is visible in the top app bar on every `/m` screen, including `/m/c/$id`. Activating it opens `/m` with an empty composer and does **not** create an empty conversation.
- **AC-03** No `/m` screen produces horizontal page scroll at 320 px width. Wide tables scroll inside their own container.

**Chat**
- **AC-04** Submitting from `/m` sends `POST /query` once with a GUID idempotency key, shows the user bubble and «در حال تحلیل...» immediately, and navigates to `/m/c/{conversationId}` on success.
- **AC-05** The composer font-size is ≥ 16 px. On touch devices Enter inserts a newline and only the send button submits. The send button is disabled while empty, pending, or offline.
- **AC-06** On a network or 5xx failure, the message text is **always preserved** and never lost. No automatic retry and no «تلاش مجدد» action is offered, because a same-key replay currently re-runs the AI and can duplicate the answer or the conversation (D-A1). The two failure cases are handled differently:
  - **AC-06a — First message (no `conversationId` yet).** The server generates the conversation ID and returns it only in the successful response, so the client cannot identify or refetch the conversation the failed request may have created.
    - The draft stays in the composer. No resend or retry control is shown on the error card.
    - The card shows an indeterminate-status message: «وضعیت این درخواست مشخص نیست و ممکن است پردازش شده باشد. پیش از ارسال دوباره، تاریخچه را بررسی کنید.» with a link to تاریخچه.
    - The conversations list query is invalidated, so تاریخچه shows any conversation the server created.
    - Submitting again is only possible by the user pressing send on the preserved draft, which is a new request with a new key.
  - **AC-06b — Follow-up (known `conversationId`).** Before offering any resend action, the app refetches that conversation's messages and reconciles them with the message IDs it held before sending.
    - If a new exchange whose user message matches the draft is present, it is shown, the error card is removed, and the draft is cleared.
    - If the refetch succeeds and no such exchange exists, the error card offers «ویرایش و ارسال دوباره». It moves the draft into the composer; sending is an explicit new request with a new key. The card notes that the earlier request may still complete.
    - If the refetch fails, no resend action is offered. The card shows the indeterminate-status message and «بررسی دوباره», which repeats only the refetch.
  - A test asserts that no failure path re-sends the original idempotency key or issues a `/query` call without a user action.
- **AC-07** Insufficient credit and cooldown errors show their existing Persian messages and **no** retry action.
  - Insufficient credit links to `/m/account`. There the user sees their plan, credits and purchase guidance in text: «برای خرید یا تمدید اعتبار، از طریق حساب کاربری خود در تحلیل‌اپ اقدام کنید.»
  - No external purchase link is rendered until the official TahlilApp destination is confirmed (D-Q6). A test asserts that no `tahlilapp.com` link appears on the account or error surfaces.
- **AC-08** `/m/c/{unknownId}` shows «این گفتگو پیدا نشد» with links to تاریخچه and گفتگوی جدید.
- **AC-09** With the virtual keyboard open, the composer stays visible above the keyboard and the last message stays scrollable into view **[manual: iOS Safari 16.4+, Chrome Android]**.

**History**
- **AC-10** `/m/history` requests `limit=100` and lists conversations by most recent first, grouped into امروز / دیروز / ۷ روز گذشته / قدیمی‌تر.
- **AC-11** Search filters titles client-side. It is insensitive to ی/ي, ک/ك, ZWNJ, and Persian/Arabic/Latin digits. It shows «نتیجه‌ای یافت نشد» when nothing matches.
- **AC-12** Deleting requires confirmation in a bottom sheet, removes the item on 204, and shows an error on failure without removing it.
- **AC-13** When exactly 100 items are returned, the list ends with «فقط ۱۰۰ گفتگوی اخیر نمایش داده می‌شود».

**Market and watchlist**
- **AC-14** `/m/market` shows **all** indices returned by `/market/summary` (up to 6), with value, signed change %, a ▲/▼ glyph in addition to color, and a relative «به‌روزرسانی» time. It shows gainers and losers when they are non-empty. Null fields render «–».
- **AC-15** `/m/watchlist` shows every followed symbol with price, change, company name (from the followed-symbols join), and a «داده قدیمی» chip when `isStale`.
- **AC-16** Tapping a symbol opens a sheet with analysis prompts. Choosing one starts a new conversation with that prompt (as AC-04). «حذف از دیده‌بان» unfollows the symbol and offers «بازگردانی» (undo) for 5 s.
- **AC-17** «افزودن نماد» searches metadata (debounced, at least 1 character), follows the selected symbol, and shows the API validation message on 400 (for example, the plan limit).
- **AC-18** Loading, empty, and error states from [design.md](design.md) render for market, watchlist, and history, each with a retry action.

**Account**
- **AC-19** `/m/account` shows the display name (when present), plan name, and credits (finite «X از Y» with a progress bar, or «نامحدود» with expiry). It also offers a theme toggle, the desktop switch, and logout. Logout clears the query cache and lands on `/auth`.

**Routing**
- **AC-20** With the flag on and no preference, a `GET` navigation to `/chat`, `/c/{id}?x=1`, or `/followed-symbols` with an iPhone UA or `Sec-CH-UA-Mobile: ?1` returns 302 to the mapped `/m` path, preserving the query, with `Vary` and `no-store`. The same request with a desktop or iPad UA, a `desktop` preference, a non-GET method, a `/_serverFn/` path, or the flag off is not redirected.
- **AC-21** `/`, `/auth`, `/launch`, `/profile`, `/telegram-link`, `/admin*` and the other excluded paths are never redirected.
- **AC-22** Redirect is idempotent: no combination of inputs produces a desktop↔mobile cycle (property test).
- **AC-23** Choosing «نمایش نسخه دسکتاپ» on `/m/c/{id}` lands on `/c/{id}` and is not redirected back, including after a reload. Choosing «نسخه موبایل» on desktop lands on the mapped `/m` path.
- **AC-24** An unauthenticated user opening `/m/c/{id}` reaches `/auth?redirect=%2Fm%2Fc%2F{id}` and returns to that path after login.

**PWA**
- **AC-25** Lighthouse (or Chrome DevTools) "installable" checks pass for production builds. The manifest has `lang=fa`, `dir=rtl`, `start_url=/m`, and 192/512/maskable icons.
- **AC-26** After using the app, Cache Storage contains no entry whose URL matches `/_serverFn/`, `/api/`, or the API origin, and contains no HTML document other than `/offline.html` (automated Playwright check).
- **AC-27** Reloading while offline shows `/offline.html`. Coming back online and pressing «تلاش مجدد» loads the app.
- **AC-28** After deploying a new build, an open app shows «نسخه جدید آماده است». It reloads only after the user confirms, and never while a question is pending.
- **AC-29** The kill-switch `sw.js` unregisters the worker and deletes its caches (scripted test).

**Accessibility and i18n**
- **AC-30** No axe-core violations of serious or critical impact on each `/m` screen in light and dark themes. Text contrast is ≥ 4.5:1, and secondary text is ≥ 12 px.
- **AC-31** Pending answers are announced through `aria-live="polite"`. Errors use `role="alert"`. All icon-only buttons have Persian `aria-label`s.
- **AC-32** Numbers use Persian digits through the existing formatters. Signed percentages and Latin tokens (P/E, tickers in Latin) are isolated (`<bdi>`/`dir="ltr"`) so the sign and order render correctly in RTL.
- **AC-33** `prefers-reduced-motion` disables sheet and toast animations beyond opacity.

**Non-regression**
- **AC-34** Existing desktop tests pass unchanged, and desktop `/chat` and `/c/$id` behave as before. Deliberate exceptions: error classification uses structured codes (same texts), and the new header link and narrow-viewport banner are added.

## 11. Risks, dependencies, assumptions, decisions

**Risks**
- R1: rich answer blocks (scanner tables, quarterly tables, Recharts charts, PS gauge) were designed for desktop width. Some may be cramped at 360 px. T-15 audits them and fixes blocking overflow only.
- R2: long AI requests and mobile backgrounding. iOS may suspend the page during a long `/query`, and the fetch then fails although the server may complete the request. Same-key retry is **not** a safe recovery path today (D-A1). Recovery is: preserve the draft; for a follow-up, refetch and reconcile the known conversation before offering a resend; for a first message, show an indeterminate status and point to تاریخچه (AC-06a/b). A user who resends after a first-message failure without checking تاریخچه can still create a duplicate conversation; the message mitigates this but cannot prevent it until B-3. Nothing bounds the total `/query` duration (discovery Q4 evidence). No client timeout is added (D-Q4).
- R3: a service worker defect can affect desktop too, because scope is `/`. Mitigations: network-only HTML, production-only registration, the kill-switch, and staged rollout.
- R4: `wrangler dev --local` in production may not set the correct headers for `sw.js` or the manifest. Verified in T-19 before enabling.
- R5: rate limit is 60 requests/min per actor. Mobile mounts about 5 queries and refetches on resume. This is bounded by a 60 s `staleTime`. Low risk.
- R6: UA and Client Hint misclassification. Mitigated by the manual switch, the preference, and the banners.

**Dependencies**
- Brand icon source (square, high resolution). `docs/Logo.png` exists, but its suitability for a maskable icon is unconfirmed.
- Production rollout gate D-Q5: verified live frontend and API origins, and verified same-site refresh-cookie behavior. This gates production login validation and enabling automatic redirects. It does not gate local UI implementation.
- Reliable retry depends on proposed backend task B-3 (§8.3), which is not in v1.

**Assumptions**
- A1 (**evidence against it; see D-A1**): `/query` replays with the same idempotency key and payload return the original result. Billing replay is single-charge. Code inspection shows the AI workflow re-executes and persists a new exchange, plus a new conversation for a first message.
- A2: Conversation titles are server-generated and short enough for a two-line clamp.
- A3: Supported browsers: iOS Safari 16.4+, Chrome Android (latest 2), Samsung Internet (latest), Firefox Android (latest; no install prompt). Minimum width is 320 px.

**Owner decisions (recorded 2026-11-03)**
- **D-Q1 — Symbol-analysis prompts.** v1 keeps the Persian templates in one shared frontend module, `src/frontend/src/lib/chat/symbol-prompts.ts`, reused wherever the frontend needs them (watchlist sheet, market-mover sheet, any later surface). Backend ownership will be reconsidered only if prompts need central management or must be shared with other clients.
- **D-Q2 — Optional backend tasks.** B-1 and B-2 are **deferred**. v1 uses the existing APIs. B-2 will be revisited if client-side watchlist joining proves ambiguous or unreliable.
- **D-Q3 — PWA scope.** `scope: "/"`, so authentication, launch, and the desktop switch stay inside the installed app.
- **D-Q4 — Chat timeout.** No arbitrary client-side timeout in v1. First confirm the backend's maximum query duration and the behavior of the existing request path.
  - Current evidence: nothing bounds total `/query` duration. `AgentTimeoutSeconds` is unused, and model calls time out at 120 s each ([discovery.md](discovery.md)).
  - A timeout is proposed later only if measured durations show it is needed. Follow-up: collect p95/p99 `/query` duration from telemetry, and decide whether the unused `AgentTimeoutSeconds` should be enforced server-side. That would be a backend decision outside Feature 153.
- **D-Q5 — Production origins (production rollout gate).** Do not assume or invent them. Two checks must both pass before production login validation (browser or installed PWA) and before enabling `VITE_MOBILE_AUTO_REDIRECT`:
  1. **Origins.** Read the live frontend and API origins from the deployment configuration (production `.env`, `FRONTEND_PUBLIC_ORIGIN`, the `VITE_FINANCIAL_COPILOT_API_BASE_URL` build argument) and record whether each frontend origin is same-site with the API origin.
  2. **Refresh cookie.** On those origins, verify that `financial_copilot_refresh` (`SameSite=Strict`, `Path=/api/auth/v1`) is set at login and sent on the refresh call, so the session survives an access-token expiry and an app relaunch.
  - Documented, unverified candidates and their same-site analysis are in [discovery.md](discovery.md#deployment-origins-q5-evidence-documented-not-verified). Notably, `sapio.ir` → `api.sapioai.ir` would be cross-site.
  - This gate does **not** block local UI implementation or local testing.
- **D-Q6 — Insufficient credit.** Show the user's account and credit information plus clear text guidance on purchasing through TahlilApp (AC-07). An external purchase link is added only after the official destination is confirmed.
  - The existing registration link on the auth screen (`https://tahlilapp.com/sapio`) is not treated as a confirmed purchase destination.
- **D-Q7 — Disclosures and followed-symbol insight feed.** Mobile versions are deferred to a later feature. Both stay reachable through the desktop experience («نمایش نسخه دسکتاپ»).
- **D-A1 — Retry (revised 2026-11-03).** Reliable retry is **blocked** until the API provides verified idempotent replay of the original result. Code inspection shows today's replay re-runs the AI and can create a duplicate conversation.
  - v1 offers no automatic retry and no «تلاش مجدد» action. First-message and follow-up failures follow AC-06a and AC-06b.
  - T02 adds a test that documents the current replay behavior. A passing result is reported to the owner; it does not by itself enable a retry action.
  - The backend change that would unblock retry is the separately proposed B-3 (§8.3). It is not implemented in v1 without approval.
- **Routing default.** Automatic redirects stay disabled by default (`VITE_MOBILE_AUTO_REDIRECT=false`). They are not enabled in production until both D-Q5 checks pass.
- **Caching.** Static assets only. Financial data, conversations, credentials, tokens, and API or server-function responses are never cached (§9.7).

## 12. Approval status

| Item | Status |
|---|---|
| Information architecture, navigation pattern, screen designs, touch and accessibility requirements | **Approved** |
| `/m` route, phone detection, manual switch, remembered preference | **Approved**; automatic redirects off by default and not enabled in production until the D-Q5 gate passes |
| PWA approach, scope `/`, service-worker security rules | **Approved** |
| Frontend-only v1; B-1 and B-2 | **Approved / deferred** |
| B-3 idempotent replay (§8.3) | **Proposed**; not in v1; needs separate approval |
| Open questions Q1–Q7 | **Decided** (D-Q1–D-Q7) |
| Failure recovery (AC-06a/b, D-A1 revised) | **Decided**: no retry in v1 |
| D-Q5 production rollout gate (origins + refresh cookie) | **Open**; blocks production login validation and redirect enablement only |
| Screenshot observations | **Recorded** from the original request ([discovery.md](discovery.md#screenshot-observations)) |
| Task plan ([tasks.md](tasks.md)) | **Approved and implemented** (2026-11-03); see [ImplementationReport.md](ImplementationReport.md) |
