# Feature 153 — Implementation report

**Date:** 2026-11-03 · **Branch:** `feature/153-mobile-pwa-workspace` (from `develop` @ `7facb33`) · **State:** implemented in
the working tree; **not committed, not pushed, not deployed**. `VITE_MOBILE_AUTO_REDIRECT` defaults to `false`.

## Summary

Phones get a dedicated workspace at `/m` with four tabs (گفتگو, تاریخچه, بازار, دیده‌بان), a top bar with the account
avatar and «گفتگوی جدید», conversations, history search, market indices and movers, the watchlist with symbol
actions, and an account screen. It reuses the existing APIs; v1 has no backend code change. Device routing exists
on the server and the client but is off by default. The app is an installable PWA whose service worker caches
static assets only.

Failed sends follow the revised decision D-A1: no automatic retry and no «تلاش مجدد» anywhere.
- **First message:** indeterminate status, a تاریخچه link, and the draft back in the composer.
- **Follow-up:** the conversation is refetched and reconciled before any resend is offered.

## Tasks

| Task | Status | Notes |
|---|---|---|
| T01 shared chat orchestration | Done | `lib/chat/use-start-chat.ts`, `use-chat-thread.ts`, `starter-prompts.ts`. Desktop `/chat` and `/c/$id` use them. |
| T02 error codes, replay characterization, recovery | Done | See "T02 outcome". `sendChatMessage` returns `{ ok, result \| failure }`; `lib/chat/send-recovery.ts` is the pure state machine. |
| T03 shared read hooks | Done | `lib/workspace/queries.ts`, `watchlist-join.ts`; `listThreads({ limit })`; `resolveExternalCompanyId`. Sidebar and context panel use the hooks with unchanged keys and options. |
| T04 experience resolution (pure) | Done | `lib/experience/{path-map,preference,resolve-experience,redirect}.ts` |
| T05 server redirect adapter | Done | In the `src/server.ts` wrapper (reason documented in code), `lib/experience/server-redirect.ts`; flag in `.env.example`. |
| T06 client guard | Done | `/_app` and `/` `beforeLoad`; the unauthenticated `/_app` redirect now keeps the requested path (`/auth?redirect=`). |
| T07 mobile shell | Done | `routes/m.tsx` (auth gate, head overrides, toaster), `components/mobile/{mobile-screen,top-app-bar,bottom-nav}.tsx`, `hooks/use-keyboard-inset.ts` |
| T08 composer | Done | `components/mobile/mobile-composer.tsx` |
| T09 new chat `/m` | Done | `routes/m.index.tsx`, `components/mobile/chat-error-card.tsx`, `pending-exchange.tsx` |
| T10 conversation `/m/c/$id` | Done | `routes/m.c.$threadId.tsx`; `MessageList` gains `density="compact"` and `data-message-id`/`data-role` (additive). |
| T11 history | Done | `routes/m.history.tsx`, `lib/format/persian-search.ts`, `lib/workspace/conversation-groups.ts` |
| T12 market | Done | `routes/m.market.tsx`, `index-card.tsx`, `signed-change.tsx` |
| T13 watchlist and sheets | Done | `routes/m.watchlist.tsx`, `symbol-sheet.tsx`, `add-symbol-sheet.tsx`, `lib/chat/symbol-prompts.ts` (D-Q1), `lib/workspace/follow-actions.ts` |
| T14 account | Done | `routes/m.account.index.tsx`, `credit-summary.tsx`; text-only purchase guidance (D-Q6) |
| T14b shared profile panel | Done | `components/app/profile-panel.tsx`; `/profile` renders it unchanged; `/m/account/profile` renders it without a nested `<main>`. |
| T15 answer blocks at phone width | Done for the main blocks | First pass: wide markdown and scanner tables (text ≥ 12 px in compact density, keyboard-focusable scroll regions). Second pass: **dedicated phone layouts for the monthly sales trend chart and the quarterly report** (see "Mobile answer layouts"). Product trend, statement analysis and disclosure listings keep the desktop renderers. |
| T16 switch UI and banners | Done | Account «نمایش نسخه دسکتاپ», desktop header «نسخه موبایل», `experience-banner.tsx` (<1024 px), `desktop-suggestion.tsx` |
| T17 manifest, icons, iOS meta | Done | Icons generated from `docs/Logo.png` by `scripts/generate-pwa-icons.mjs` (**brand source needs owner confirmation**). |
| T18 service worker, offline, update, kill-switch | Done | `public/sw.js`, `offline.html`, `sw-kill.js`, `lib/pwa/{register.ts,update-toast.tsx}`, runbook `docs/deployment/pwa-service-worker.md` |
| T19 static serving | Done (Wrangler) | `public/_headers`; evidence below. Docker itself unavailable on this host; the identical `wrangler dev` command was used. |
| T20 install | Done | `lib/pwa/install.ts`, `install-card.tsx`, `ios-install-sheet.tsx`, account entry |
| T21 offline handling | Done | `hooks/use-online-status.ts`, `offline-banner.tsx`; send disabled with hint |
| T22 accessibility pass | Automated part done | axe (serious/critical) on all screens in both themes and with sheets open; text ≥ 12 px; 44 px targets; landmarks; names. Manual screen-reader script below, **not yet run**. |
| T23 auth screen | Done | 16 px inputs, `autocomplete`, `inputmode`, safe areas, closed `reason=expired` notice |
| T24 documentation | Done | README section, `specs/README.md` entry, runbook, this report. The `docs/deplyment` → `docs/deployment` rename was done after the merge, at the owner's request. |
| T25 verification | Automated runs done | Manual device matrix and production checks not run (see limitations). |
| B1, B2 | Deferred | Not implemented (D-Q2). |
| B3 | Proposed only | Not implemented. |

## T02 outcome (replay characterization)

`tests/FinancialCopilot.IntegrationTests/AiQueryIdempotentReplayCharacterizationTests.cs` (test-only, V2 orchestration,
in-memory stores) **passes** and documents current behavior:
- **New chat:** the same key and payload sent twice is charged once (+1 ledger entry, +1 reservation), but returns a
  **different conversation ID** and creates **two conversations**.
- **Follow-up:** the same key sent twice is charged once, but persists the exchange **twice** (+4 messages) with
  different message IDs.

So reliable retry stays blocked (D-A1). v1 ships no retry; B-3 remains the proposed fix.

Running it needs `Authentication__Email__Enabled=false` in the local environment. Without it, the host fails
startup validation (email token and SMTP settings). That is a pre-existing local configuration issue: an existing
test (`V2ScannerEndpointTests.V2AiQuery_ScannerTool_ReturnsConversationId`) fails the same way. No configuration file
was changed.

## Acceptance criteria

The evidence keys:
- **U:** Vitest unit or component test.
- **B:** `scripts/mobile-workspace.browser.mjs`, run against a production build served by `wrangler dev --local`,
  with Chrome at phone sizes and a mock API.
- **M:** manual check, not yet performed.

| AC | Result | Evidence |
|---|---|---|
| AC-01 four tabs, `aria-current`, ≥ 44 px | Pass | U `presentation.test.tsx`, B |
| AC-02 «گفتگوی جدید» everywhere, no empty conversation | Pass | B; U `chat-hooks.test.tsx` (`createThread` not called) |
| AC-03 no horizontal scroll at 320/360/390 | Pass | B: 7 screens × 3 widths |
| AC-04 one send with GUID key, pending view, navigate | Pass | U, B |
| AC-05 composer 16 px, Enter rules, disabled states | Pass | U `mobile-composer.test.tsx` |
| AC-06a first-message failure | Pass | U `send-recovery`, `chat-hooks`, `chat-error-card`; B |
| AC-06b follow-up reconciliation | Pass | U (answered / not found / refetch failed / recheck only refetches); B (persisted-then-lost answer shown) |
| AC-07 credit/cooldown, no retry, no external link | Pass | U `chat-error-card`, `credit-summary`; B |
| AC-08 unknown conversation | Pass | B |
| AC-09 keyboard keeps composer visible | Not verified | M (iOS Safari 16.4+, Chrome Android) |
| AC-10/11/13 history list, normalized search, 100 note | Pass | U `persian-search`, `conversation-groups`; B |
| AC-12 confirmed delete | Pass | B |
| AC-14 all indices, glyphs, freshness | Pass | B; U `SignedChange` |
| AC-15/16 watchlist names, sheet, undo | Pass (undo toast not automated) | B; U `watchlist-join` |
| AC-17 add symbol, inline 400 | Pass | B |
| AC-18 loading/empty/error states with retry | Implemented; error states not automated | Code review only |
| AC-19 account | Pass | B; U `credit-summary` |
| AC-20/21/22 redirect rules, exclusions, no cycles | Pass | U `server-redirect.test.ts`, `experience.test.ts` (exhaustive idempotence) |
| AC-23 manual switch persists | Pass | B |
| AC-24 deep link through login | Pass | B |
| AC-25 installable manifest | Fields verified; Lighthouse not run | B (head), manifest file |
| AC-26 no API/serverFn/HTML in Cache Storage | Pass | B (59 entries, all static); U `service-worker.test.ts` |
| AC-27 offline reload → `/offline.html` → back online | Pass | B |
| AC-28 update prompt, never during a pending answer | Implemented; not automated | Code review (`useIsMutating` gate) |
| AC-29 kill-switch | Pass | U `service-worker.test.ts` |
| AC-30 axe serious/critical, contrast, ≥ 12 px | Pass, with one item to review | B, both themes, sheets open. axe could not resolve 1 contrast node on the light `/m/account`; review manually. |
| AC-31 live regions, alerts, labels | Pass | B (landmarks, names); U (roles) |
| AC-32 Persian digits, isolated signed numbers | Pass | U `SignedChange` |
| AC-33 reduced motion | Implemented; not automated | CSS in `styles.css` |
| AC-34 desktop non-regression | Pass with listed deviations | Baseline-equal Vitest results; B (desktop head, banner, no redirect) |

## Test results

| Suite | Before (develop) | After | Notes |
|---|---|---|---|
| Vitest (`npx vitest run`) | 5 files failed, 2 tests failed, 203 passed | **5 files failed, 2 tests failed, 382 passed** | Identical pre-existing failures: `markdown-message` (quarterly header text), `monthly-product-trend-chart` (failed-trend message), and 3 `scripts/*.browser.test.mjs` that Vitest collects but cannot run. **No new failures**; 179 new tests pass (14 of them for the mobile answer layouts). |
| Browser checks (`npm run test:mobile-browser`) | — | **85/85 pass** | Production build + `wrangler dev --local`, Chrome headless, mock API. Includes the analysis conversation (sales trend + quarterly report) at 320/360/390 px in both themes. |
| .NET replay characterization (2 tests) | — | **2/2 pass** | Needs local `Authentication__Email__Enabled=false` (see above) |
| `tsc --noEmit` | fails (pre-existing) | no new errors in new or changed code | Remaining errors are pre-existing (`/disclosures` link search param, `/auth` navigations without `search`, `_app.top-up`, test fixture typings). |
| ESLint | — | new files clean | Existing files are not Prettier-clean on `develop`; they were not reformatted. |
| Full .NET test suites | not run | not run | Only the new test file was run; no backend production code changed. |

### T19 static serving evidence (`wrangler dev --config dist/server/wrangler.json --local`)

| Path | Status | Content-Type | Cache-Control |
|---|---|---|---|
| `/sw.js`, `/sw-kill.js` | 200 | `text/javascript; charset=utf-8` | `no-cache` |
| `/manifest.webmanifest` | 200 | `application/manifest+json` | `no-cache` |
| `/offline.html` | 307 → `/offline` (200, `text/html`) | — | `no-cache` |
| `/assets/*.js` | 200 | `text/javascript` | `public, max-age=31536000, immutable` |
| `/icons/*.png` | 200 | `image/png` | `public, max-age=0, must-revalidate` |
| `GET /chat` from an iPhone UA (flag off) | 200, no redirect | `text/html` | — |

The 307 for `/offline.html` comes from the asset server's HTML handling. The worker stores a non-redirected copy, so the
offline fallback works (AC-27 passes).

## Mobile answer layouts (2026-11-03, second pass)

The two densest analytical answers now have dedicated phone layouts in `/m`. They are selected through an
answer-density context that `MessageList density="compact"` provides (`components/app/answer-density.ts`). Desktop
renders exactly as before, which is verified in the browser at 1440 px and by tests.

### Monthly sales trend (`components/mobile/answers/monthly-sales-trend-mobile.tsx`)

| Decision | Why |
|---|---|
| Full-text legend list above the plot, with a caption explaining that the number beside each year is the total of its reported months | The desktop Recharts legend packs both yearly totals and «٪ از سال قبل» into one wrapped line; at 320 px it collided with the plot. |
| Value axis sized to its longest tick, 12 px ticks, no rotated axis title (the unit is shown once as a chip) | Gives the 12 columns the remaining width without clipping. |
| Month labels at −60°, 12 px, all twelve kept | Every period stays visible; tapping shows the full name. |
| **Tap a month column** (selection computed from the tap position) or use «ماه قبل»/«ماه بعد» (44 px) | No floating tooltip: the exact month, year, values and unit appear in a detail panel **below** the chart, so a finger never covers the bars. The selected month is emphasized; the others are dimmed. |
| Detail panel: current-year and previous-year values for that month (with year), 12-month average, and the change against the same month of the previous year | Unreported months read «گزارش نشده», never zero. The average reads «–» where the series has no value. |
| PS gauge below the chart instead of overlaid; insights with signed percentages; missing-data notes; source and calculation time | Nothing from the desktop card is dropped. Source and calculation time (previously only in the PNG export) are shown too. |
| Screen-reader table with all 12 months and an `aria-live` detail panel | Exact values do not depend on the chart. |

### Quarterly report (`components/mobile/answers/quarterly-report-mobile.tsx`)

| Decision | Why |
|---|---|
| **Card per line item (default)**: label, «درصد تغییر به فصل قبل» badge (glyph ▲/▼ plus the favorable or unfavorable tone of the desktop, which inverts for expense rows), latest period value, previous period value | The desktop table at phone width used 7–9 px text. Cards keep the most important comparison readable at 16 px. |
| «همه فصل‌ها (n)» expands every period with a bar proportional to the value (negative values in rose, margins in gold) | Every value of every period stays one tap away, with the trend shape visible. |
| Short period names in cards («فصل دوم ۱۴۰۵»), with the exact end dates listed once above the cards | Avoids repeating two-line labels on every card while keeping the exact definition of each period. |
| **«جدول» view**: all line items × all periods (newest first) plus the change column, with the item column pinned; only this region scrolls sideways | For comparing many items across periods, where cards cannot preserve a useful comparison. The region is focusable and labelled. |
| Values are the desktop cell strings: parentheses for negatives, «—» unavailable (announced «ناموجود»), «-» zero, «٪» on margin rows | No value is recomputed or reformatted. |
| Design tokens for both themes; trend chart collapsed under «نمودار روند»; image download kept (44 px); EPS and provenance notes kept | |

### Verification (production build in the Worker runtime, mock API, real-shaped answers)

- **Fixtures** (`scripts/mobile-workspace/answer-fixtures.mjs`, typed by `answer-fixtures.d.mts`):
  - the monthly trend has a long company name, five unreported current-year months, an unreported previous-year
    month, three months without an average, insights and a missing-data note;
  - the quarterly report has 15 rows × 5 periods with missing cells, negative expenses, zeros, a long label, the
    gross-margin row, and the trend chart enabled.
- **Browser** at 320, 360 and 390 px in dark and light, with no page-level horizontal scroll in any view:
  - a tap selects the month under the finger;
  - the buttons step through months;
  - the detail panel never overlaps the chart;
  - 15 cards render, and expanding one and opening the chart work;
  - the table has 15 rows, its item column is pinned, and it scrolls inside its region;
  - axe finds no serious or critical issues, text is ≥ 12 px and touch targets are ≥ 44 px.
- **Desktop at 1440 px** still renders the original chart card (Recharts legend) and the fixed quarterly table.
- **Unit tests** (`components/mobile/answers/__tests__`) cover:
  - month selection, tap mapping and same-month change;
  - period ordering, cell formatting and bar scaling;
  - that every value, change and period of the desktop table appears in the phone table view, and every period in
    an expanded card.

## Deviations from the plan

- `sendChatMessage` returns a discriminated result instead of throwing, because TanStack serializes only an error's
  message. Both desktop routes were updated, and desktop texts are unchanged.
- Small desktop-visible changes:
  - After a first message, the conversations list is refreshed so the sidebar shows the new conversation.
  - Signed-out deep links into `/_app` now return to the requested page after login.
  - Wide answer tables are keyboard-focusable regions.
  - `theme-color` follows the theme.
  - The desktop header has «نسخه موبایل», and a banner suggests mobile below 1024 px.
- Browser checks are named `*.browser.mjs`, not `*.browser.test.mjs`, so Vitest does not collect them. The 3 existing
  scripts are left as they were.
- `axe-core@4.10.3` was added as an exact devDependency. `package-lock.json` is updated; `bun.lock` is **not**, since
  Docker uses `npm ci`.
- Danger text on mobile surfaces uses a darker rose in the light theme (`.mobile-danger-text`), and the confirm
  button uses dark text, both for contrast.

## Remaining limitations and open items

1. **Production rollout gate (D-Q5) is open.** Keep `VITE_MOBILE_AUTO_REDIRECT=false`. Before production login
   validation or enabling redirects:
   - read the live frontend and API origins from the deployment configuration;
   - verify that the refresh cookie is set and sent on those origins.
2. **Manual checks not performed:**
   - the device matrix (iPhone SE/14/15, Android Chrome, Samsung Internet, iPad, desktop at 1440/800);
   - AC-09 keyboard behavior;
   - installing on Android Chrome and iOS Safari;
   - Lighthouse installability;
   - TalkBack and VoiceOver (script below).
3. **Brand icon:** generated from `docs/Logo.png`. Its suitability is unconfirmed; regenerate with
   `npm run pwa:icons` after any change.
4. **Other answer blocks keep desktop renderers on phones.** Product trend, statement analysis and disclosure
   listings were not redesigned. Polish item: the monospace «دنبال کردن نماد» button in scanner rows wraps
   awkwardly at 360 px.
   The optional quarterly trend chart (`QuarterlyFinancialChart`, shown when `/api/ui/v1/config` enables it or for
   banks) keeps its light-only colors and scrolls sideways inside its region. On phones it is collapsed under
   «نمودار روند».
5. **Same-key retry stays unavailable** until B-3 is approved, built and verified. A user who resends after a
   first-message failure without checking تاریخچه can still create a duplicate conversation; the message mitigates
   this but cannot prevent it.
6. **Docker image not run** (Docker is broken on this host). The same `wrangler dev` command was verified directly.
7. **Not automated:** AC-18 error states, AC-28 update prompt, AC-33 reduced motion, and the 5 s undo toast.
8. **History search** covers the newest 100 conversations only (B-1 deferred).
9. The deployment docs folder is now `docs/deployment/` (renamed after the merge, at the owner's request).

## Manual screen-reader script (T22, to run)

1. On `/m` with VoiceOver (iOS) or TalkBack (Android), swipe through the page and confirm this order: the account
   button «حساب کاربری», the title, «گفتگوی جدید», the starters, the composer «پرسش خود را بنویسید», «ارسال», then
   the tab bar «ناوبری اصلی» with the current tab announced as selected.
2. Send a question. Confirm «در حال تحلیل...» is announced without moving focus.
3. Turn on airplane mode just after sending, to make the send fail. Confirm the error card is announced as an alert, and that
   «تاریخچه» is reachable.
4. In بازار, confirm each index reads its name, its value, and the direction («افزایش» or «کاهش») with the signed
   percentage in the right order.
5. Open a watchlist symbol. Confirm the sheet title is announced, focus is trapped in the sheet, and closing it
   returns focus to the row.

## Changed files

The full list is in `git status`.
- **Backend:** one test file (`AiQueryIdempotentReplayCharacterizationTests.cs`) and no production code.
- **Frontend, new:**
  - routes `m*.tsx`;
  - `components/mobile/*`, `components/app/{experience-banner,profile-panel}.tsx`;
  - `lib/{chat,experience,pwa,workspace}/*`, `lib/{dismissal,session-end}.ts`, `lib/format/persian-search.ts`;
  - `hooks/use-{keyboard-inset,media-query,online-status}.ts`;
  - `public/{sw.js,sw-kill.js,offline.html,manifest.webmanifest,_headers,icons/*}`;
  - `scripts/{generate-pwa-icons.mjs,mobile-workspace.browser.mjs,mobile-workspace/mock-api.mjs}`;
  - the tests.
- **Frontend, changed:**
  - routes `__root`, `_app`, `_app.chat`, `_app.c.$threadId`, `_app.profile`, `auth`, `index`, and `routeTree.gen.ts`;
  - components `chat-header`, `context-panel`, `header-account`, `markdown-message`, `message-list`, `sidebar`,
    `theme-toggle`;
  - `lib/chat.functions.ts`, `lib/market-view.functions.ts`, `integrations/financial-copilot/api-client.server.ts`;
  - `server.ts`, `styles.css`, `vite.config.ts`, `.env.example`, `package.json`, `package-lock.json`.
- **Docs:** `README.md`, `specs/README.md`, `docs/deployment/pwa-service-worker.md`, and this folder.
