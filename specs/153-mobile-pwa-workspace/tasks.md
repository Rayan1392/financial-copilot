# Feature 153 — Tasks

**Status:** design approved; decisions D-Q1–D-Q7 and D-A1 recorded ([spec.md](spec.md) §11). **Phase 2 approved and implemented on 2026-11-03** (not committed, pushed, or deployed); per-task status and evidence are in [ImplementationReport.md](ImplementationReport.md). v1 is frontend-only. B1 and B2 are deferred. B3 (idempotent replay) is proposed and needs separate approval. v1 has no send retry (D-A1, revised).
All paths are relative to `src/frontend/` unless stated otherwise. "Tests" means Vitest/Testing Library unless Playwright is named; Playwright tests follow the existing `scripts/*.browser.test.mjs` pattern with `playwright-core`.

**Sequencing**

```
Foundation  T01 → T02 → T03
Routing     T04 → T05, T06            (T04 needs nothing else)
Mobile UI   T07 (needs T03) → T08, T09, T10 → T11, T12, T13, T14, T14b → T15, T16
PWA         T17 → T19 → T18 → T20, T21
A11y        T22 (after T08–T16), T23 (independent)
Backend     B1, B2 — deferred (D-Q2), not scheduled; B3 — proposed, needs separate approval
Docs/verify T24, T25 (last)
```

Work can run in parallel across these lanes:
- T04–T06 alongside T01–T03.
- T17/T19 alongside the UI tasks.

---

## Foundation (frontend, behavior-preserving)

### 153-T01 — Extract shared chat orchestration
- **Description:**
  - Move the duplicated logic from `routes/_app.chat.tsx` and `routes/_app.c.$threadId.tsx` into `lib/chat/`:
    - `useStartChat()`: send without a thread, pending message, navigate on success, usage invalidation;
    - `useChatThread(threadId)`: messages query, send, scanner and disclosure paging, invalidations;
    - `chatErrorMessage()`;
    - the starter-prompt list.
  - Both desktop routes then consume these modules.
  - Each hook takes a `navigateToThread(id)` callback, so desktop and mobile choose their own route.
- **Depends on:** —
- **Files:** `lib/chat/use-start-chat.ts`, `lib/chat/use-chat-thread.ts`, `lib/chat/chat-errors.ts`, `lib/chat/starter-prompts.ts`, `routes/_app.chat.tsx`, `routes/_app.c.$threadId.tsx`.
- **Acceptance:**
  - Desktop behavior is unchanged: existing tests pass, and the manual smoke of `/chat` and `/c/$id` (send, page, suggested action, error) works as before.
  - Unit tests cover `chatErrorMessage` and the hooks with a mocked server function.
  - No query keys change.

### 153-T02 — Structured chat error classification, replay characterization, draft-preserving recovery
- **Description:**
  - **Error codes.** Make `sendChatMessage` failures carry a stable code: `insufficient_credit | cooldown | idempotency_conflict | not_found | validation | network | server`.
    - There is no `timeout` code, because no client timeout is added (D-Q4).
    - Currently the only signal is a substring match on "insufficient". Spike TanStack server-function error serialization first. If a custom error class does not survive serialization, return a discriminated result from a wrapper instead.
    - Keep the existing Persian texts.
  - **Replay characterization (D-A1).** Add an API integration test against `POST /api/ai/v1/query` that sends the same key and payload twice, for both a new chat and a continuation. It asserts and **documents the actual behavior**:
    - the response content;
    - the number of persisted messages and conversations;
    - the number of charges.
  - Code inspection indicates billing is single-charge, but the AI workflow re-executes and persists again, creating a second conversation for a new chat. The test does not change backend code (v1 is frontend-only).
  - **Reliable retry is blocked** until the API provides verified idempotent replay of the original result (proposed B3). v1 ships **no** `retry()` that reuses the original key, no automatic retry, and no «تلاش مجدد» on send failures, whatever the test outcome. An unexpectedly passing test is reported to the owner; it does not enable retry by itself.
  - **Send-attempt recovery state** (`lib/chat/send-recovery.ts`, pure and unit-tested). It records whether the attempt had a known `conversationId`, the message IDs known before sending, and the draft.
    - **First message** (no `conversationId`): result `indeterminate`. The draft is preserved and the conversations query is invalidated. No resend action is exposed. The UI shows the "status unknown, check تاریخچه before sending again" message (spec AC-06a).
    - **Follow-up** (known `conversationId`): result `reconciling`. Refetch that conversation's messages and diff them against the pre-send IDs (spec AC-06b):
      - a new exchange whose user message matches the draft → `answered` (show it, clear the draft);
      - refetch succeeded and no match → `not_found` (expose «ویرایش و ارسال دوباره», which moves the draft into the composer for a new request with a new key);
      - refetch failed → `indeterminate` (expose only «بررسی دوباره», which repeats the refetch).
  - Record the limitation and the replay test outcome in the implementation report.
- **Depends on:** T01
- **Files:**
  - Frontend: `lib/chat.functions.ts`, `integrations/financial-copilot/api-client.server.ts` (error shape only), `lib/chat/chat-errors.ts`, `lib/chat/send-recovery.ts`.
  - Tests: `tests/FinancialCopilot.IntegrationTests/` (test only; no backend code change).
- **Acceptance:**
  - Each code maps from a fixture problem response.
  - The desktop error texts are identical to today's.
  - The replay test exists and its outcome is recorded.
  - Unit tests cover every recovery transition for first-message and follow-up failures, including a failed refetch.
  - A test asserts that no recovery path calls `sendChatMessage` without a user action, and that the original idempotency key is never reused.
  - Desktop behavior is otherwise unchanged.

### 153-T03 — Shared read hooks with unchanged query keys
- **Description:**
  - Add the hooks `useConversations({ limit })`, `useDeleteConversation()`, `useMarketSummary()`, `useWatchlistWithNames()` (joins `getWatchlist` + `getFollowedSymbols` by symbol), `useUsage()` and `useOwnProfile()`.
  - Add an optional `{ limit?: number }` input to `listThreads`, validated with zod (1–100) and forwarded as `?limit=`.
  - Add the server function `resolveExternalCompanyId({ symbol })` for the existing `GET /api/v1/market/external-company-id` (404 → `null`).
  - The desktop sidebar and context panel adopt these hooks, keeping their current behavior: the sidebar still requests the default limit.
- **Depends on:** T01
- **Files:** `lib/chat.functions.ts`, `lib/market-view.functions.ts`, `lib/workspace/*.ts` (new), `components/app/sidebar.tsx`, `components/app/context-panel.tsx`.
- **Acceptance:**
  - Unit tests cover the join (missing name, case-insensitive symbol match), the limit validation, and the 404→null mapping.
  - The desktop sidebar and context panel render the same as before.

## Routing and device detection

### 153-T04 — Experience resolution module (pure)
- **Description:** Implement these as pure functions with no framework imports:
  - `resolveExperience`;
  - the bidirectional path table (spec §7.2), with `toMobilePath` and `toDesktopPath` preserving search and hash;
  - `parseExperiencePreference` and `serializeExperienceCookie`;
  - the `?experience=` override parsing;
  - `isPhoneUserAgent`.
- **Depends on:** —
- **Files:** `lib/experience/resolve-experience.ts`, `lib/experience/path-map.ts`, `lib/experience/preference.ts`, `lib/experience/__tests__/*`.
- **Acceptance:**
  - Table-driven tests cover:
    - UA samples: iPhone Safari, Android Chrome phone and tablet, iPadOS, Samsung Internet, Firefox Android, desktop Chrome, Safari and Edge, Googlebot desktop and smartphone, and empty;
    - the Client Hint `?1`/`?0`;
    - preference precedence;
    - every mapped and excluded path.
  - A property test shows `redirect(redirect(x)) == redirect(x)` (AC-22).
  - An unsafe `?experience=` value is ignored.

### 153-T05 — Server-side redirect adapter
- **Description:**
  - Add a request-level adapter, choosing between the `start.ts` request middleware and the `server.ts` wrapper (document the choice in code).
  - Conditions: GET, top-level navigation (`Sec-Fetch-Mode: navigate`, or `Accept: text/html` when Fetch Metadata is absent), a mapped path, resolution `mobile`, and `VITE_MOBILE_AUTO_REDIRECT=true`.
  - Response: 302 with `Cache-Control: private, no-store`, `Vary: Sec-CH-UA-Mobile, User-Agent, Cookie`, and the diagnostic header `X-Sapio-Experience: mobile; reason=<ch|ua>`.
  - Handle `?experience=` by setting the cookie and redirecting to the same path without the parameter.
- **Depends on:** T04
- **Files:** `src/start.ts` or `src/server.ts`, `lib/experience/server-redirect.ts`, `.env.example` (flag), tests.
- **Acceptance:**
  - Request-fixture tests prove AC-20 and AC-21, including `/_serverFn/*`, POST, assets, `/launch`, `/auth`, `/profile`, flag off, and a desktop preference.
  - No other response is altered.

### 153-T06 — Client-side guard for SPA navigations
- **Description:**
  - In `/_app` `beforeLoad` (client only) and in the authenticated branch of `routes/index.tsx`, resolve the experience from `navigator.userAgentData?.mobile`, `navigator.userAgent` and the cookie, then `throw redirect` to the mapped path. This is flag-gated.
  - Also change `/_app`'s unauthenticated redirect to pass `redirect=<current path>`. The parameter is already sanitized by `safeInternalPath`. This fixes the loss of desktop deep links.
- **Depends on:** T04
- **Files:** `routes/_app.tsx`, `routes/index.tsx`, `lib/experience/client.ts`.
- **Acceptance:**
  - Tests: an authenticated `/` on a mobile signal lands on `/m`; a desktop preference stays on `/chat`; with the flag off nothing changes; an unauthenticated `/c/{id}` → `/auth?redirect=%2Fc%2F{id}`.

## Mobile UI

### 153-T07 — Mobile shell layout `/m`
- **Description:**
  - Create the `routes/m.tsx` layout with:
    - the client auth gate (skeleton during SSR, then `/auth?redirect=`), with a closed `reason=expired` value;
    - the top app bar (avatar → account, title, «گفتگوی جدید»);
    - the four-tab bottom navigation (`aria-current`, re-tap scrolls to top);
    - `100dvh`, safe areas, and the keyboard-inset variable;
    - route head meta: viewport with `viewport-fit=cover, interactive-widget=resizes-content`, and `robots=noindex`;
    - slots for the offline banner and the update toast;
    - per-route control of tab-bar visibility (hidden on `/m/c/$id` and while a request is pending).
  - Verify that TanStack head merging overrides the root viewport meta only on `/m`.
- **Depends on:** T03
- **Files:** `routes/m.tsx`, `components/mobile/mobile-shell.tsx`, `components/mobile/bottom-nav.tsx`, `components/mobile/top-app-bar.tsx`, `hooks/use-keyboard-inset.ts`.
- **Acceptance:** AC-01, AC-02 (control present), AC-03 (shell), AC-24. Playwright at 320/360/390 px shows no horizontal scroll. The desktop `<head>` is unchanged (snapshot test).

### 153-T08 — Mobile composer
- **Description:**
  - Create `MobileComposer`:
    - 16 px font, autosize 1–6 lines;
    - newline on Enter for touch devices (`(pointer: coarse)`), submit on Enter for hardware keyboards;
    - a 44 px send button disabled when empty, pending, or offline, with the offline hint via `aria-describedby`;
    - the disclaimer;
    - controlled value, so a failed send can restore text;
    - docked above the keyboard and safe area.
  - The desktop `PromptInput` is not changed.
- **Depends on:** T07
- **Files:** `components/mobile/mobile-composer.tsx`, tests.
- **Acceptance:** AC-05 (component tests for both pointer types), AC-09 [manual].

### 153-T09 — New-chat screen `/m`
- **Description:**
  - Build the starters (shared list, 4 visible + «بیشتر»), plus the optional watchlist symbol chips.
  - The pending view, the 20 s "takes longer" hint (no timeout; D-Q4), and replace-navigation to `/m/c/{id}` reuse `useStartChat`.
  - The error card follows the T02 first-message recovery (spec AC-06a):
    - on network/server errors: the indeterminate-status message, the «تاریخچه» link, the draft restored to the composer, and **no** resend or retry control;
    - «مشاهده اعتبار» for insufficient credit;
    - no retry action for cooldown.
- **Depends on:** T01, T02, T08
- **Files:** `routes/m.index.tsx`, `components/mobile/chat-error-card.tsx`.
- **Acceptance:** AC-02 (no empty conversation is created; assert that `createThread` is not called), AC-04, AC-06a (assert the card has no resend or retry button), AC-07.

### 153-T10 — Conversation screen `/m/c/$threadId`
- **Description:**
  - Reuse `useChatThread` and `MessageList` with a new `density="compact"` prop: padding, spacing, diagnostics hidden.
  - Scroll behavior: jump to the bottom on open, keep the start of a new answer in view, show the «↓ پیام‌های جدید» pill.
  - The back target is the previous tab, falling back to `/m/history`.
  - Add the not-found and load-error states.
  - On `network`/`server` send errors, run the T02 follow-up reconciliation before showing any resend action (spec AC-06b): show the persisted answer if found; otherwise offer «ویرایش و ارسال دوباره»; if the refetch fails, offer only «بررسی دوباره». The draft is preserved throughout.
  - Add the `⋯` menu with confirmed delete.
- **Depends on:** T01, T02, T08
- **Files:** `routes/m.c.$threadId.tsx`, `components/app/message-list.tsx` (additive prop only), `components/mobile/confirm-sheet.tsx`.
- **Acceptance:**
  - AC-04 (continuation), AC-06b (each reconciliation outcome; no resend action is rendered before a refetch succeeds), AC-08.
  - Existing `message-list` tests are unchanged and pass, and new tests cover `density`.

### 153-T11 — History screen `/m/history`
- **Description:**
  - `useConversations({ limit: 100 })`.
  - A Persian-normalizing client filter, implemented as a pure `normalizePersianSearch` with tests.
  - Date grouping with Jalali labels.
  - Rows with a `⋯` sheet and confirmed delete.
  - The 100-limit footnote.
  - Loading, empty, no-results, and error states.
  - Refetch on focus or resume after 60 s.
- **Depends on:** T03, T07
- **Files:** `routes/m.history.tsx`, `lib/format/persian-search.ts`, `components/mobile/conversation-row.tsx`.
- **Acceptance:** AC-10 – AC-13, AC-18 (history).

### 153-T12 — Market screen `/m/market`
- **Description:**
  - The index grid shows all returned indices; two columns, or one under 360 px.
  - Gainer and loser lists, a refresh button, a freshness label, and stale chips.
  - Mover tap → symbol sheet, using the T13 component.
  - States, including 403.
- **Depends on:** T03, T07
- **Files:** `routes/m.market.tsx`, `components/mobile/index-card.tsx`, `components/mobile/signed-change.tsx` (▲/▼ + `<bdi>`).
- **Acceptance:** AC-14, AC-18 (market), AC-32 (signed values).

### 153-T13 — Watchlist screen, add-symbol sheet, symbol action sheet
- **Description:**
  - **Watchlist:** `/m/watchlist` with `useWatchlistWithNames`.
  - **Add-symbol sheet:** metadata search, debounced 300 ms; follow; inline 400 message; success toast; invalidation of `["watchlist"]` and `["followed-symbols"]`.
  - **Symbol action sheet:**
    - analysis prompt templates, owned by the single shared module `lib/chat/symbol-prompts.ts` (D-Q1) and reused by the market-mover sheet and any other frontend surface, that start a new chat through `useStartChat`;
    - «پرسش دیگری…», which navigates to `/m` with the composer prefilled through router state, not the URL;
    - unfollow with a 5 s undo toast (undo calls follow again);
    - for unfollowed movers, «افزودن به دیده‌بان» through `resolveExternalCompanyId` + follow.
- **Depends on:** T03, T07, T09
- **Files:** `routes/m.watchlist.tsx`, `components/mobile/symbol-sheet.tsx`, `components/mobile/add-symbol-sheet.tsx`, `lib/chat/symbol-prompts.ts`.
- **Acceptance:** AC-15 – AC-17, AC-18 (watchlist).

### 153-T14 — Account screen `/m/account`
- **Description:**
  - Profile and email, plan, and credits, reusing `credit-progress.ts` for both finite and «نامحدود».
  - Purchase guidance as **text only** (D-Q6): «برای خرید یا تمدید اعتبار، از طریق حساب کاربری خود در تحلیل‌اپ اقدام کنید.» A test asserts that no external `tahlilapp.com` link renders on the account screen or the chat error cards.
  - A credit refresh button, a theme toggle (reusing `ThemeToggle` logic), and links to the profile and `/telegram-link`.
  - An install entry (wired in T20), the «نمایش نسخه دسکتاپ» switch (T16), and logout.
- **Depends on:** T03, T07
- **Files:** `routes/m.account.tsx`, `components/mobile/credit-summary.tsx`.
- **Acceptance:** AC-19.

### 153-T14b — Shared profile body for `/m/account/profile`
- **Description:**
  - Extract the body of `routes/_app.profile.tsx` into `components/app/profile-panel.tsx` with no logic change.
  - Render it in `/profile` (desktop) and in `routes/m.account.profile.tsx`, which has a mobile top bar and back button.
- **Depends on:** T07
- **Files:** `routes/_app.profile.tsx`, `components/app/profile-panel.tsx`, `routes/m.account.profile.tsx`.
- **Acceptance:** The desktop profile page renders and behaves identically: existing behavior, plus a snapshot. The mobile route shows the same sections at 360 px without horizontal scroll.

### 153-T15 — Rich answer blocks at phone width (audit + targeted fixes)
- **Description:**
  - Render every assistant block type from fixtures at 360 px in Playwright and screenshot it. The blocks are: scanner and symbol-lookup tables, monthly activity trend chart, product trend and comparison, PS gauge, financial statement analysis, quarterly table and chart, disclosure listing, and markdown tables.
  - Fix only page-level horizontal overflow and unreadable (< 12 px) text inside `/m` (`density="compact"` paths).
  - Record the remaining polish items in the implementation report.
- **Depends on:** T10
- **Files:** `components/app/*` (minimal, additive), `scripts/mobile-answer-blocks.browser.test.mjs`.
- **Acceptance:** AC-03 for all block fixtures. Desktop screenshots of the same fixtures are unchanged.

### 153-T16 — Experience switch UI and suggestion banners
- **Description:**
  - Mobile: «نمایش نسخه دسکتاپ» (Account), which writes the cookie and navigates to `toDesktopPath(current)`.
  - Desktop: a «نسخه موبایل» link in `ChatHeader`, plus a dismissible banner below 1024 px viewport. Dismissal is kept in `localStorage` wrapped in try/catch.
  - `/m` at ≥ 1024 px with a fine pointer shows a dismissible desktop suggestion.
  - Resizing never redirects.
- **Depends on:** T04, T07
- **Files:** `components/app/chat-header.tsx`, `components/app/experience-banner.tsx`, `components/mobile/desktop-suggestion.tsx`.
- **Acceptance:** AC-23. Resizing a Playwright window across breakpoints never changes the URL.

## PWA

### 153-T17 — Manifest, icons, iOS meta
- **Description:**
  - Add `public/manifest.webmanifest` per spec §9.1.
  - Generate icons 192, 512, 512-maskable and 180 apple-touch with a `scripts/generate-pwa-icons.mjs` script that uses the existing `sharp` devDependency on the **approved** brand source. Commit both the script and its outputs.
  - Add the root head links and meta from §9.2 (manifest, apple tags), and make `theme-color` follow the theme.
- **Depends on:** brand icon approval (dependency in spec §11).
- **Files:** `public/manifest.webmanifest`, `public/icons/*`, `scripts/generate-pwa-icons.mjs`, `routes/__root.tsx`.
- **Acceptance:** AC-25 (manifest fields). The maskable icon passes the safe-zone check.

### 153-T19 — Verify static serving in the Wrangler/Docker runtime
- **Description:**
  - Confirm in `npm run dev`, `npm run preview`, and the `docker/frontend.Dockerfile` image the following:
    - `/sw.js` is served at the root with a JS content type and `Cache-Control: no-cache`, or revalidation;
    - the manifest has `application/manifest+json`;
    - `/offline.html` is static;
    - hashed `/assets/*` are immutable.
  - If Wrangler asset headers need configuration, add the minimal config (for example a `_headers` file, if supported) and document it.
- **Depends on:** T17
- **Files:** `public/_headers` (if needed), `docs/deplyment/*` note. `docs/deplyment/` is the repository's real, misspelled directory name; see the discovery path note.
- **Acceptance:** A recorded `curl -I` evidence table for all three runtimes in the implementation report.

### 153-T18 — Service worker, offline page, registration, update flow, kill-switch
- **Description:**
  - **Service worker:** hand-written `public/sw.js` implementing the spec §9.3 table, with the build ID injected (or a hash-derived cache name), versioned caches, cleanup on activate, and a 200-entry bound.
  - **Offline page:** `public/offline.html`.
  - **Registration:** in `lib/pwa/register.ts`, production plus secure context only. A `waiting` worker triggers the update toast; `SKIP_WAITING` is followed by a reload on `controllerchange`, blocked while a chat mutation is pending.
  - **Chunk errors:** `vite:preloadError` shows the same prompt.
  - **Kill-switch:** `public/sw-kill.js` template and a runbook.
- **Depends on:** T19, T07
- **Files:** `public/sw.js`, `public/offline.html`, `lib/pwa/register.ts`, `lib/pwa/update-toast.tsx`, `docs/deplyment/pwa-service-worker.md`.
- **Acceptance:**
  - AC-26, AC-27, AC-28, AC-29.
  - Unit tests of the fetch-routing function cover `/_serverFn/`, `/api/`, the API origin, navigation, `/assets/`, fonts, and non-GET requests.

### 153-T20 — Install experience
- **Description:**
  - Capture `beforeinstallprompt`.
  - Show the install entry in Account and an install card after the first successful answer. «بعداً» snoozes it for 30 days (`localStorage`).
  - Add the iOS instruction sheet, and hide everything in standalone mode.
- **Depends on:** T18, T14
- **Files:** `lib/pwa/install.ts`, `components/mobile/install-card.tsx`, `components/mobile/ios-install-sheet.tsx`.
- **Acceptance:** Component tests for each environment (Chromium prompt available, iOS Safari, standalone, Firefox Android). [manual] Install on Android Chrome and iOS Safari 16.4+.

### 153-T21 — Offline and online handling in the mobile UI
- **Description:**
  - A `useOnlineStatus()` hook drives the banner and disables send.
  - Queries keep their in-memory data and show the «آخرین به‌روزرسانی» label.
  - A failed fetch while `navigator.onLine` is true is classified as `network` (T02).
- **Depends on:** T07, T02
- **Files:** `hooks/use-online-status.ts`, `components/mobile/offline-banner.tsx`.
- **Acceptance:** Playwright `context.setOffline(true)` scenarios. The draft survives going offline and back online.

## Accessibility and i18n

### 153-T22 — Accessibility and RTL verification pass
- **Description:**
  - Run axe-core in Playwright on all `/m` screens, in both themes, at 360 px.
  - Verify 44 px targets programmatically, landmarks (`header`, `nav` labelled «ناوبری اصلی», `main`), focus order and visible focus, live regions, reduced motion, Persian `aria-label`s, `<bdi>` isolation, and minimum font sizes.
  - Write the manual TalkBack and VoiceOver script.
- **Depends on:** T08–T16
- **Files:** `scripts/mobile-a11y.browser.test.mjs`, fixes in mobile components.
- **Acceptance:** AC-30 – AC-33. The manual screen-reader script results are recorded.

### 153-T23 — Auth screen mobile fixes (shared)
- **Description:**
  - In `AuthScreen`:
    - inputs `text-base sm:text-sm` (16 px on phones);
    - `autocomplete` and `inputmode` attributes;
    - safe-area padding;
    - optional closed `reason=expired` toast.
  - No logic change.
- **Depends on:** —
- **Files:** `routes/auth.tsx`, `routes/auth-closed-registration.test.tsx` (still passes).
- **Acceptance:** No iOS focus zoom [manual]. The existing auth tests pass.

## Backend/API — **deferred (D-Q2); not part of v1**

These tasks are kept for a later decision. B2 is revisited if T03's client-side watchlist join proves ambiguous or unreliable.

### 153-B1 — Conversation keyset paging and title search
- **Description:** Implement spec §8.2 B-1 in the Application layer:
  - a cursor value object;
  - Persian normalization owned by the Conversations context;
  - a repository query with `(UpdatedAt, Id)` ordering and an index check.
  - Keep the existing array contract per the approved compatibility option.
- **Depends on:** owner decision. The frontend then switches `useConversations` to infinite paging plus server search (an additional small task).
- **Files:** `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs`, `FinancialCopilot.Application/Conversations/*`, `FinancialCopilot.Infrastructure/Conversations/Persistence/ConversationRepositories.cs`, tests.
- **Acceptance:**
  - Paging is stable under concurrent updates.
  - Search is normalization-insensitive.
  - Tenant/actor isolation holds.
  - 400 for bad input.
  - Existing callers are unaffected.
  - PostgreSQL tests pass.

### 153-B2 — Additive `externalCompanyId` / `companyName` on `WatchlistQuote`
- **Description:** Populate the fields from the followed-symbol rows in `WatchlistService.BuildViewAsync`. They are nullable for legacy watchlist rows.
- **Depends on:** owner decision. Afterwards T03 can drop the client-side join.
- **Files:** `FinancialCopilot.Application/FinancialData/MarketViews/MarketViewContracts.cs`, `FinancialCopilot.Infrastructure/Financial/MarketViews/MarketViewServices.cs`, tests.
- **Acceptance:** The contract stays backward compatible. Tests cover followed and legacy paths.

## Backend/API — **proposed; not in v1; requires separate owner approval**

### 153-B3 — Idempotent replay of the original `/query` result
- **Description:** Implement spec §8.3 B-3, only if the owner approves it as separate work:
  - record the completed outcome (conversation ID, exchange ID, response) per actor and idempotency key;
  - return it on a same-key, same-fingerprint replay without running the AI workflow;
  - return a documented retryable status while the original request is still running;
  - define retention and cleanup.
- **Depends on:** owner approval; the T02 replay characterization test as the baseline.
- **Files (indicative, to be confirmed in its own design):** `AiFacadeController.cs`, the V1 and V2 orchestration paths (`AiQueryOrchestrationService`, `FinancialCopilotAgentWorkflowRunner`), `AiFacadeBillingHook.cs`, persistence and migration, tests.
- **Acceptance:**
  - The T02 test, inverted, passes: a replay returns the identical result, persists no new exchange or conversation, and charges once.
  - Concurrent in-flight replay is covered.
  - Existing callers are unaffected.
- **Follow-up (frontend, separate task):** only after B3 is verified end to end, add a same-key «تلاش مجدد» to the mobile error cards.

## Documentation and verification

### 153-T24 — Documentation
- **Description:**
  - **README:** the mobile route, the `VITE_MOBILE_AUTO_REDIRECT` flag, and the PWA notes.
  - **`specs/README.md`:** the index entry.
  - **`docs/deplyment`:** the service-worker runbook (update, kill-switch, header verification).
  - **`ImplementationReport.md`:** evidence for each AC, test results, the device matrix, and known gaps.
- **Depends on:** all implemented tasks.
- **Files:** `README.md`, `specs/README.md`, `docs/deplyment/pwa-service-worker.md`, `specs/153-mobile-pwa-workspace/ImplementationReport.md`.
- **Optional housekeeping (owner to approve or skip):** rename `docs/deplyment/` to `docs/deployment/`. No other Markdown file links to it today. If approved, update the Feature 153 references in the same change.
- **Acceptance:** Every AC has linked evidence or an explicit "not verified" note. The report includes the T02 replay outcome and the D-Q4/D-Q5 follow-ups.

### 153-T25 — Verification run and device matrix
- **Description:**
  - Run `npm run lint`, `npm test`, all new Playwright scripts, and `dotnet test` (only if B1/B2 are approved).
  - Manual matrix: iPhone SE (320 px, iOS 16.4+), iPhone 14/15 Safari, Android Chrome 360×800, Samsung Internet, iPad (desktop default + switch), and desktop Chrome at 1440 and 800 px.
  - Local UI verification (dev server, local builds, Playwright mobile viewports) is **not** gated by D-Q5 and runs normally.
  - **Production rollout gate (D-Q5).** Production login validation, in the browser and in the installed PWA, runs **only after** both checks pass:
    1. the live frontend and API origins are read from the deployment configuration, and their same-site relationship is recorded;
    2. the `financial_copilot_refresh` cookie is set at login and sent on refresh on those origins, so the session survives token expiry and an app relaunch.
  - Until then, record these checks as blocked, and do not test against assumed origins.
- **Depends on:** T01–T23
- **Acceptance:**
  - Results are recorded in the ImplementationReport. Failures are reported, not hidden.
  - `VITE_MOBILE_AUTO_REDIRECT` stays `false`.
  - No deployment is performed.
  - Enabling redirects in production is a separate owner decision after the D-Q5 gate passes.
