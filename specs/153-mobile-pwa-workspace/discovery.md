# Feature 153 — Discovery: current mobile experience and reusable capabilities

**Status:** Phase 1 discovery (read-only). No application code, API, migration, or deployment was changed.
**Inspected at:** `develop` @ `7facb33`, 2026-11-02.

## Evidence sources

1. **Owner screenshots.** The owner supplied two mobile screenshots with the original Feature 153 request as evidence of the current phone experience. The screenshot-confirmed observations below are recorded as the owner confirmed them for the original request. The agent session that wrote this discovery did not render the images itself.
2. **Repository findings.** Everything else was verified by reading the code and configuration at the commit stated above. These findings are marked **(repo)**. A repository finding explains the screenshots; it is not itself screenshot evidence.

### Screenshot observations

**Screenshot-confirmed** (limited to the screenshots' visible content):

| # | Observation | Repository explanation (repo) |
|---|---|---|
| S1 | The phone shows the **promotional / chat-entry screen**. | The authenticated `/chat` empty state is a marketing-style hero («دستیار هوشمند بازار سرمایه», phase badge, six starter cards) above the prompt input (`routes/_app.chat.tsx`). |
| S2 | **No chat-history navigation** is visible. | `ConversationSidebar` (new chat, recent conversations) is `hidden md:flex`, so it is not rendered below 768 px. |
| S3 | **No market-index panel** is visible. | `ContextPanel` (indices and watchlist) is `hidden lg:flex`, so it is not rendered below 1024 px. |
| S4 | **No watchlist panel** is visible. | Watchlist quotes exist only in `ContextPanel`. The header eye icon links to the management page `/followed-symbols`, not to quotes. |

**Not verified by the screenshots** (outside their visible content; supported only by code reading or not established):

| Item | Status |
|---|---|
| The exact route, URL, device, browser, and viewport width in the screenshots | Unverified. S1 is consistent with authenticated `/chat`, but the route is not visible. |
| Whether history, indices, or the watchlist are reachable through any interaction not shown (scrolling, menus) | Unverified by screenshot. Code reading finds no mobile path to history, index data, or watchlist quotes (repo). |
| Hidden logout and credit meter, ≈32 px header icons, iOS focus zoom on the composer, `100vh` instability, 10–11 px labels | Code findings only (repo); see "Other mobile defects" below. |
| How failures, loading, or long requests look on the phone | Unverified; not shown. |

## Feature number

The highest existing feature folder is `specs/152-tahlilapp-unlimited-subscription`. No branch, spec folder, or doc reference uses `153`. The `specs/reviews/*` folders use review ranges, not feature IDs. **Next available ID: 153.**

## Frontend architecture (verified)

| Area | Finding | Path |
|---|---|---|
| Framework | TanStack Start (React 19, TanStack Router file routes, React Query), SSR, built on `@lovable.dev/vite-tanstack-config`. It is **not** a plain Vite SPA. | `src/frontend/package.json`, `src/frontend/vite.config.ts` |
| Runtime | The build targets Cloudflare Workers. Production runs `wrangler dev --local` inside the frontend Docker image on port 80. Every document request goes through the Worker server entry. | `docker/frontend.Dockerfile`, `src/frontend/wrangler.jsonc`, `src/frontend/src/server.ts` |
| Request pipeline | `src/server.ts` wraps the TanStack server entry. `src/start.ts` registers request middleware (CSRF for server functions, error page) and function middleware (bearer attachment). **A server-side device decision can be placed in this pipeline.** | `src/frontend/src/start.ts`, `src/frontend/src/server.ts` |
| Data access | Components call TanStack **server functions** (`useServerFn`). The browser POSTs/GETs `/_serverFn/*` on the frontend origin, with the bearer token attached by `attachFinancialCopilotAuth`. The Worker then forwards to the .NET API (`financialCopilotServerApi`). | `src/frontend/src/integrations/financial-copilot/api-client.server.ts`, `auth-attacher.ts` |
| Routing | `__root.tsx` sets `lang="fa" dir="rtl"`, class `dark`, the Vazirmatn font, `theme-color #09090b` and viewport `width=device-width, initial-scale=1`. `/_app` is the pathless authenticated layout that owns `/chat`, `/c/$threadId`, `/followed-symbols`, `/disclosures`, `/profile`, `/telegram-link`, `/top-up` and `/getCompanyId`. | `src/frontend/src/routes/` |
| Auth guard | `/_app` `beforeLoad` runs only on the client (`typeof window === "undefined"` returns early). Unauthenticated users go to `/auth` **without** the requested path, so desktop deep links are lost at login today. `/auth?redirect=` already exists and is protected by `safeInternalPath`. | `routes/_app.tsx`, `routes/auth.tsx`, `lib/safe-redirect.ts` |
| Entry `/` | Unauthenticated users see `AuthScreen` (email/password login plus a link to register through TahlilApp). Authenticated users are redirected client-side to `/chat`. | `routes/index.tsx`, `routes/auth.tsx` |
| Auth tokens | Access token: memory plus **sessionStorage** (`financial_copilot_access_token`), 15 minutes. Refresh token: HttpOnly cookie `financial_copilot_refresh`, `SameSite=Strict`, `Path=/api/auth/v1`, 30 days, on the **API origin**. TahlilApp launch: `/launch?code=` → `POST /api/auth/v1/tahlilapp/launch` → `window.location.replace("/chat")`. | `integrations/financial-copilot/auth.ts`, `AuthController.cs:307`, `routes/launch.tsx` |
| Existing mobile hook | `useIsMobile()` (viewport < 768 px) exists but is only used by the shadcn sidebar primitive. | `src/frontend/src/hooks/use-mobile.tsx` |
| Design system | Tailwind v4 tokens in `styles.css`: dark-first OKLCH palette, with `--emerald` (positive/primary), `--rose` (negative), `--gold` (warning), `--surface`, `--surface-2`, `--hairline` and radius 0.75rem. Font: Vazirmatn. shadcn/Radix primitives include `sheet`, `drawer` (vaul), `dialog`, `tabs`, `sonner` and `skeleton`. Persian digit helpers live in `lib/format/persian.ts`. | `src/frontend/src/styles.css`, `src/frontend/src/components/ui/*` |
| PWA | **None.** `public/` contains only `favicon.ico`. There is no manifest, service worker, or apple-touch-icon. | `src/frontend/public/` |
| Tests | Vitest + Testing Library (jsdom). Browser checks run as `playwright-core` scripts (`scripts/*.browser.test.mjs`). | `src/frontend/vitest.config.ts`, `src/frontend/scripts/` |

## Why the product workspace is unusable on phones (verified)

The authenticated layout `/_app` is a three-column desktop shell:

| Column | Content | Visibility |
|---|---|---|
| `ConversationSidebar` | New chat, recent conversations, delete, AI credit meter, Telegram link, admin, logout | `hidden … md:flex`, so **hidden below 768 px** |
| main | `ChatHeader` + page | always |
| `ContextPanel` | Market indices (first 2 only), watchlist quotes, "add symbol" | `hidden … lg:flex`, so **hidden below 1024 px** |

On a phone the user therefore has **no access to chat history, logout, credits, market indices, or watchlist quotes** (repo). The screenshots confirm the visible part of this: no history navigation, index panel, or watchlist panel (S2–S4). The header contains only small (≈32 px) icon links: home (`/chat`), the watchlist *management* page (`/followed-symbols`), disclosures, and the theme toggle (repo). The `/chat` empty state is a marketing-style hero ("دستیار هوشمند بازار سرمایه" with a phase badge and six starter cards) (repo), which matches the promotional/chat-entry screen in the screenshots (S1).

Other mobile defects found:

- `PromptInput` uses `text-sm` (14 px), so iOS Safari zooms on focus. Enter submits, which is wrong on touch keyboards. It has fixed `p-6` padding and no safe-area handling.
- The layout uses `h-screen` (100vh), which is unstable on mobile browsers when the URL bar or virtual keyboard changes.
- Many labels are 10–11 px (`text-[10px]`, `text-[11px]`).
- Desktop deletes a conversation immediately with no confirmation. The delete button is hover-revealed, so it cannot be discovered on touch.
- A request failure clears the composer. The user must retype, and every resend uses a new idempotency key.

## Capabilities and APIs (verified)

All routes are on the .NET API. They require JWT or API key and are rate-limited by `AuthenticatedActor` (60 requests per 60 s per actor, `appsettings.json`).

### AI chat

- `POST /api/ai/v1/query` (`AiFacadeController.Query`). Body: `message`, `conversationId?`, `idempotencyKey` (non-empty GUID, required), `scannerPage`, `scannerPageSize` (1–100), `disclosurePage`, `suggestedActionId?`, `context?`. The response is a **single complete JSON** answer (`AiQueryHttpResponse`). There is **no streaming** (no SSE or chunked endpoint exists). Errors: 400 validation, 404 unknown conversation, 409 idempotency conflict (same key, different payload), plus billing/cooldown problems surfaced as problem details (`SUBSCRIPTION_COOLDOWN_ACTIVE`, "insufficient" credit).
- Frontend: `sendChatMessage` (`lib/chat.functions.ts`). The "streaming" UI is a pending placeholder ("در حال تحلیل...") inside `MessageList`.
- Idempotent replay (assumption A1). A read-only code inspection on 2026-11-03 found that `/query` is **not** end-to-end idempotent. Billing is idempotent. The answer and the conversation are not.
  - `AiFacadeBillingHook.TryReserveAsync` reuses the existing reservation for the same key. `FinalizeAsync` returns the existing ledger entry (`ReplayFinalizationAsync`), so a replay **should not double-charge**.
  - Neither orchestration path short-circuits a replay. V1 is `AiQueryOrchestrationService.ExecuteAsync`. V2, the active mode, is `FinancialCopilotWorkflowDefinition` / `FinancialCopilotAgentWorkflowRunner`. Both run the full AI workflow again and persist a **new exchange** (`MessagePersistenceFunction` → `PersistExchangeAsync`).
  - For a first message (no `conversationId`), both paths use `request.ConversationId ?? Guid.NewGuid()`, so a replay creates a **second conversation**.
  - Existing tests (`AiFacadeV2EndpointTests` "…ReplaysIt_AndChargesOnlyOnce") cover single charging, not response replay.
  - The server returns the new conversation ID only in a successful response. After a failed first message, the client therefore cannot identify the conversation the request may have created.
  - **Conclusion:** same-key retry would not return the original answer, and could duplicate the answer or the conversation. Reliable retry is blocked until the API provides verified idempotent replay (proposed backend task B-3, spec §8.3). v1 offers no retry. See spec AC-06a/b and §11 D-A1.
- Maximum `/query` duration (Q4 evidence, read-only):
  - **Nothing in the request path bounds the total duration.**
  - `AiOrchestration:AgentTimeoutSeconds` (60, `docker/appsettings.Production.json`) is declared in `AiOrchestrationOptions` but is not read anywhere in the code.
  - Each AI model-provider call has `TimeoutSeconds: 120` (`AiModelProviders`), and a workflow may make several calls.
  - No ASP.NET request timeout is configured in `Program.cs`. The Worker → API `fetch` in `api-client.server.ts` has no timeout, and neither does the browser → `/_serverFn` call.
  - Observed p95/p99 durations are not available from the repository.
- `GET /api/ai/v1/capabilities/guidance?language=fa` returns capability starter prompts. The client function `getCapabilityStarterPrompts` exists, but no route uses it. The desktop `/chat` page hardcodes six starter prompts.

### Conversation history

- `GET /api/ai/v1/conversations?limit=1..100` (default 20). Sorted by `UpdatedAt` descending. **No cursor, offset, or search.** Fields: `conversationId`, `title`, `startedAt`, `updatedAt` (`ConversationRepositories.ListByActorAsync`). The frontend `listThreads` sends no `limit`, so it shows **20 conversations at most**.
- `POST /api/ai/v1/conversations` creates an empty conversation (used by the desktop "new chat" button). `GET /api/ai/v1/conversations/{id}` reads one. `GET /api/ai/v1/conversations/{id}/messages` returns **all** messages, unpaginated. `DELETE /api/ai/v1/conversations/{id}` → 204/404.
- Renaming conversations is not supported.

### Market overview

- `GET /api/v1/market/summary` (`MarketSummaryService`, 30 s in-memory cache). Returns up to **six governed named indices** (`StockMarketNamedIndices`: name, value, changePercent, asOf, sourceKind), top 3 gainers and losers (`symbol`, `name`, `latestPrice`, `changePercent`, `asOf`, `isStale`), and `asOf`. `realMoneyFlow`, `trendingIndustries` and `insight` are **always null**. Desktop displays only the first 2 indices.
- `GET /api/v1/market/external-company-id?symbol=` resolves a ticker to `externalCompanyId` (404 when unknown).
- `GET /api/v1/market-pulse/latest|history` and `GET /api/v1/insights/market` exist, but no current user UI uses them. **They are not reused in v1.**

### Watchlist and symbols

- `GET /api/v1/watchlists/me` returns quotes (`symbol`, `latestPrice`, `changePercent`, `asOf`, `sourceKind`, `isStale`) for **followed symbols**, falling back to the legacy watchlist table. It carries **no `externalCompanyId` or company name**.
- `GET/PUT /api/v1/followed-symbols/me`, `POST|DELETE /api/v1/followed-symbols/me/{externalCompanyId}` manage the list (`externalCompanyId`, `symbol`, `companyName`, `followedAtUtc`, `source`). A validation error returns 400 with a message.
- `GET /api/ai/v1/metadata/symbols?search=&limit=` searches symbols (used by `/followed-symbols` with limit 12).
- `GET /api/v1/insights/followed-symbols/me` plus `seen`/`dismiss` provide the followed-symbol event feed (desktop `/followed-symbols` tab).
- **There is no dedicated "symbol analysis" API or page.** Analysis is produced by asking the AI through `/api/ai/v1/query`. Answers can render `FollowSymbolButton`.

### Account, subscription, credits

- `GET /api/v1/usage/me` returns `balance`, `reservedCredits`, `availableSpendingCapacity`, `planCode`, `planName`, `planIncludedCredits`, `creditQuotaMode` (`Finite|Unlimited`) and `subscriptionExpiresAtUtc`. Desktop renders this in the sidebar (`credit-progress.ts`, «نامحدود» for Max).
- `GET /api/auth/v1/profile` (display name and masked fields), `GET /api/auth/v1/account` (email and state), `POST /api/auth/v1/logout`.
- Purchases and top-ups happen on TahlilApp. SapioAI only shows top-up status (`/top-up`, `GET /api/v1/billing/top-ups/{invoiceId}`). No purchase link is shown in the app today.

## Deployment origins (Q5 evidence, documented, not verified)

| Source | Frontend origin(s) | API origin | Same-site? |
|---|---|---|---|
| `docs/deplyment/deploy-develop-to-vps.md` (lines 55, 96, 98) | `https://sapioai.ir`, `https://sapio.ir` | `https://api.sapioai.ir` | `sapioai.ir` ↔ `api.sapioai.ir`: **yes**. `sapio.ir` ↔ `api.sapioai.ir`: **no** (different registrable domain) |
| `docs/deplyment/docker-compose.md` (lines 30, 95–96), example values | `https://tse.avidaweb.com` | `https://tseai.avidaweb.com` | yes |
| `docker-compose.yml` lines 87–88 | `FRONTEND_PUBLIC_ORIGIN` (required env, no default); alternate defaults to `https://sapio.ir` | build arg `VITE_FINANCIAL_COPILOT_API_BASE_URL` | depends on deployed values |

The refresh cookie is `SameSite=Strict` on the API origin. A frontend served from a cross-site origin (for example `sapio.ir` calling `api.sapioai.ir`) would not send it on the credentialed `fetch`, so session refresh would fail there. This affects desktop today as well as the installed PWA. This is a **production rollout gate** (owner decision D-Q5). Before production login validation or enabling automatic redirects: (1) read the live frontend and API origins from the deployment configuration (production `.env` and build arguments), and (2) verify on those origins that the refresh cookie is set and sent, so the session refreshes. The gate does not block local UI implementation.

## Documentation path note

`docs/deplyment/` is **the repository's actual directory name** (misspelled in the repo, containing `deploy-develop-to-vps.md` and `docker-compose.md`). There is no `docs/deployment/`, so the path in these documents is correct as written; it is not a typo in Feature 153. No other Markdown file links to the folder. Renaming it to `docs/deployment/` would be cheap, but it is a repository change outside Feature 153's documentation scope. It is listed as an optional housekeeping item (tasks.md T24) for the owner to approve or skip.

## Reuse verdict

| Need | Reuse as-is | Gap |
|---|---|---|
| Ask / continue chat | `/query`, `sendChatMessage`, `MessageList` | No streaming (accepted; see spec non-goals). Error classification relies on message substrings (frontend fix only). No idempotent replay of the original result, so no safe retry (proposed B-3). |
| History list | `GET /conversations?limit=100` | No search or paging beyond 100. v1 uses client-side title filtering. Server search/paging is optional (B-1). |
| Delete conversation | `DELETE /conversations/{id}` | none |
| Market indices / movers | `GET /market/summary` | none (desktop just shows 2 of 6) |
| Watchlist quotes | `GET /watchlists/me` | Lacks `externalCompanyId`/name. v1 joins with `GET /followed-symbols/me` client-side. Optional additive field (B-2). |
| Add / remove symbol | metadata search + followed-symbols POST/DELETE | none |
| Follow from a market mover | `GET /market/external-company-id` then POST follow | none |
| Credits / plan | `GET /usage/me` | none |
| Profile / logout | profile, account, logout | none |

**No backend change is required for the mobile v1.** Two optional backend improvements (B-1, B-2, deferred) are listed in `spec.md` §8.2. Reliable retry of failed questions needs a backend change, proposed separately as B-3 (`spec.md` §8.3); it is not part of v1.
