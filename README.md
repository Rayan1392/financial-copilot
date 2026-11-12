# Financial Copilot Backend — Project Documentation

## Product Context

Financial Copilot is an AI-powered capital market assistant for the Iranian stock market. The product will support two delivery models:

1. **SaaS/API model**: external websites and financial platforms consume backend APIs and build their own UI.
2. **Owned web application model**: Financial Copilot provides its own React web app where users register, buy subscriptions, top up usage credits, and use the product directly.

The existing React + TypeScript frontend prototype built with Lovable should be preserved as the UI foundation. Backend services will be implemented separately with .NET 10, C#, PostgreSQL, EF Core, Redis, RabbitMQ, and Clean Architecture.

## Communication Rule

AGENTS (codex, claude, copilot, and other AI Agents) reply in the language of the user's message (Persian → Persian, English → English). Code, identifiers, commit messages, and new technical specifications and docs stay in English; Persian UI text, business terms, and existing Persian documentation are preserved. Full rule: "Agent Communication Language" in [`AGENTS.md`](AGENTS.md).

## Feature Development Lifecycle

New features, changes, bug fixes, and hotfixes follow [`specs/FEATURE_DEVELOPMENT_WORKFLOW.md`](specs/FEATURE_DEVELOPMENT_WORKFLOW.md): discovery and design → explicit Gate A (design approval) → detailed specs and plan → explicit Gate B (implementation-plan approval) → implementation and verification.

## Versions and Releases

Version numbers and release notes live only in [`CHANGELOG.md`](CHANGELOG.md). Releases follow
[`docs/release/release-process.md`](docs/release/release-process.md) and are run by the
[`release-manager`](.claude/agents/release-manager.md) agent. See the "Versions and Releases" section of
[`AGENTS.md`](AGENTS.md).

## Phase 1 Scope — Scanner MVP

Phase 1 focuses on the **Natural Language Financial Scanner**.

The scanner must answer questions such as:

- "Give me symbols whose latest quarter net profit grew more than 50% and P/E is below 5."
- "Which symbols had 100% growth in last month sales?"
- "Which symbols have P/S below 1?"
- "Find companies with improving production, sales, profitability, and attractive valuation ratios."

The React chat UI submits every message to `POST /api/ai/v1/query`. The backend AI Query Orchestrator detects intent and, for scanner questions, invokes an internal Scanner Tool that converts natural language into a validated financial query plan, executes it against normalized market/fundamental data, ranks the results, and returns an Explainable Answer with Data Citations and a Confidence Score.

## AI Query Examples

The same `POST /api/ai/v1/query` facade also supports deterministic direct-lookup and monthly-trend answers beyond scanner queries.

- `P/E فولاد چقدر است؟`
- `آخرین فروش کچاد چقدر بوده است؟`
- `روند فروش ماهانه کهمدا را نشان بده`
- `فروش امسال کچاد نسبت به پارسال چطور بوده؟`

Monthly production/sales trend answers are chart-ready responses built from persisted
`CompanyMonthlyActivityTrendSnapshots` data produced by spec `076-nadpco-monthly-activity-trend-snapshot`.
The AI query path does not aggregate raw Noavaran product-sales line items at response time for these trend questions.
Trend chart monetary values are returned in `میلیارد تومان`, converted in the backend from persisted `میلیون ریال` snapshot values using `value * 0.0001`.

## Core Principles

- API-first backend.
- Single public AI facade endpoint for React chat queries; tool routing stays in the backend.
- Clean Architecture.
- SOLID, testable, maintainable code.
- Domain-specific financial semantics, not generic chatbot behavior.
- Explainable output with metric values, periods, source timestamps, and confidence.
- Third-party data abstraction from day one.
- Hybrid data strategy: on-demand API calls for lightweight/fresh data; persisted normalized datasets for screening, historical analytics, textual analysis, and repeatable calculations.
- Usage metering and API key readiness from day one, even if billing is fully activated later.
- Generic Conversation and Message persistence for chat history.
- Dedicated `FinancialCopilot.Billing` bounded context for organization partners and direct consumers, using immutable usage ledger and operation-based charging.
- Provider-neutral AI model integration for configured hosted providers and local runtimes, without vendor-specific business logic.

## Recommended Documentation Reading Order

1. `docs/architecture.md`
2. `docs/domain-glossary.md`
3. `docs/data-strategy.md`
4. `docs/scanner-mvp-scope.md`
5. `docs/api-design.md`
6. `docs/billing-and-credits-domain.md`
7. `docs/codex-agent-instructions.md`
8. `docs/alert-history-explainability.md`
9. `specs/implementation-checklist.md`
10. `specs/*/user-story.md`

## Backend Development

The backend solution targets .NET 10 and is located in `src/backend/FinancialCopilot.sln`.

Run the CI-ready validation command from the repository root:

```powershell
dotnet test src/backend/FinancialCopilot.sln --configuration Release
```

Run the API locally:

```powershell
$env:TAHLILAPP_CLIENT_SECRET = "<tahlilapp-client-secret>"
dotnet run --project src/backend/FinancialCopilot.API
```

Development uses the persistent `DataProtectionKeys` directory under the API project.
The key files are ignored by Git. Set `DataProtection__KeyRingPath` to an existing,
writable directory to use a different key ring.

Run the frontend locally in a second terminal:

```powershell
cd src/frontend
npm run dev
```

The local API listens on `http://localhost:5074`. The frontend development server is commonly
available at `http://localhost:8080` in the Lovable sandbox and may also use
`http://localhost:5173` in a standard Vite session. Set the browser-visible API origin in
`src/frontend/.env`:

```text
VITE_FINANCIAL_COPILOT_API_BASE_URL="http://localhost:5074"
```

Use `src/frontend/.env.example` as the non-secret template. TanStack server functions may use
the optional server-only `FINANCIAL_COPILOT_API_BASE_URL` override. The Development API CORS
configuration explicitly permits both documented frontend origins with credentials so owned
Identity refresh cookies work without routing API requests through the frontend SSR server.

The hosted OpenAI adapter reads its API token from `OPENAI_API_KEY`. Set it in the shell before
starting the API; do not store an active token in `appsettings.json`:

```powershell
$env:OPENAI_API_KEY = "<secret>"
dotnet run --project src/backend/FinancialCopilot.API
```

In the Development environment, the initial foundation endpoints are:

- `GET /health`
- `GET /openapi/v1.json`

## Authentication Configuration

Protected AI routes accept either a configured JWT bearer token or an `X-Api-Key` credential. JWT actors must include a tenant id claim (`financial_copilot:tenant_id`) and a GUID subject. API client configuration stores SHA-256 key hashes rather than raw API keys and supplies both client and tenant ids.

JWT signing keys and API key hashes must be supplied through secrets or environment configuration; the repository settings do not contain active credentials. Authenticated AI requests are rate limited by user id or API client id using `RateLimiting:AuthenticatedActor` settings.

Feature 145 adds email verification and password recovery to owned web-app accounts. Apply the new Auth migration before deploying the matching API/frontend build. Existing users keep their stored `EmailConfirmed` value and remain eligible under the existing login policy; only new registrations require email verification. Registration no longer creates a session until the address is verified. The SMTP sender is disabled by default; configure these environment variables through the deployment secret manager:

```powershell
$env:Authentication__Email__Enabled = "true"
$env:Authentication__Email__FrontendBaseUrl = "https://app.example.com"
$env:Authentication__Email__TokenHashSecret = "<at-least-32-character-random-secret>"
$env:Authentication__Smtp__Host = "smtppro.zoho.com"
$env:Authentication__Smtp__Port = "587"
$env:Authentication__Smtp__Security = "StartTls"
$env:Authentication__Smtp__UserName = "no-reply@sapioai.ir"
$env:Authentication__Smtp__Password = "<zoho-application-specific-password>"
$env:Authentication__Smtp__SenderAddress = "no-reply@sapioai.ir"
$env:Authentication__Smtp__SenderName = "Sapio AI"
```

Authentication email is sent through Zoho Mail SMTP as `no-reply@sapioai.ir`; `appsettings.json` ships every non-secret value above, so only the password must be supplied. With email enabled, a user name without a password fails startup validation. Use port 465 with `SslOnConnect` if 587/STARTTLS is blocked, and `smtp.zoho.com` or the regional host shown in Zoho Mail settings if the organization plan or data centre differs. See `specs/145-email-verification-and-password-recovery/ZohoSmtpMigrationReport.md`.

The frontend origin must be an HTTPS origin with no path/query/fragment outside local development. Verification and recovery links use fixed frontend paths and fragments, so the token is removed from browser history before submission. Do not enable email delivery until valid SMTP and frontend-origin settings are present. The API uses a 10 requests/minute in-process public-auth IP limiter and shared-database per-email cooldowns (60 seconds, at most 5/hour); multi-instance deployments must also enforce IP limits at a shared gateway. Password reset revokes all refresh tokens and new owned-user JWTs through Identity security-stamp validation. Pre-feature JWTs without a stamp claim remain valid until their expiry; the checked-in `Authentication:OwnedIdentity:AccessTokenMinutes` value is 15 minutes. Logout revokes refresh sessions but cannot invalidate an already-issued stateless JWT before its expiry.

When deployed behind a reverse proxy, enable `ForwardedHeaders` only with explicit `KnownProxies` or non-global `KnownNetworks`; arbitrary client-supplied `X-Forwarded-For` values must never be trusted. The API validates the allow-list at startup and applies forwarded headers before request logging, authentication, and rate limiting.

## Scanner Cache Configuration

Scanner plan and deterministic result caching is configured through `ScannerCache`. The default local configuration uses distributed in-memory storage so local development does not require Redis. For shared deployment, set `ScannerCache:UseRedis` to `true` and configure `ScannerCache:RedisConfiguration`; keys remain tenant/actor scoped and are version-invalidated after data synchronization or derived-metric persistence.

## Admin Data Operations

Users with the `DataAdmin` role can enqueue ingestion work and inspect provider operations through `POST /api/v1/admin/data-sync/*`, `GET /api/v1/admin/data-sync/runs`, and `GET /api/v1/admin/provider-health`. Sync trigger endpoints publish to RabbitMQ and require `DataSyncMessaging:Enabled=true` with a configured broker in the API and Worker processes.

Generic sync requests accept an optional `providerName` when an operator needs to target a coexisting source. For example, enqueue a CodalDB companies/symbols-only sync without the full per-company fan-out:

```http
POST /api/v1/admin/data-sync/symbols
Content-Type: application/json

{
  "idempotencyKey": "manual-codaldb-symbols-2026-06-01",
  "providerName": "CodalDb"
}
```

## StockMarketDB Trading Statistics

Trading-statistics ingestion reads the separate SQL Server `StockMarketDB` source through a
read-only adapter. Authoritative source tables: instruments from `Tse.Instrument`, intraday
trades from `Tse.Trade`, daily trades from `Tse.TradeRefined` (one refined price per trading day
per instrument), intraday indices from `Tse.IndexB1LastDay`, and daily indices from
`Tse.IndexNew` scoped to the named market indices (شاخص کل, کل فرابورس, بازده نقدی و قیمت,
۵۰ شرکت فعال‌تر, قیمت هم‌وزن, کل هم‌وزن). Each dataset supports a bounded full-sync (historical
backfill) and an overlap-watermark incremental sync that share the same idempotent normalizers.
See [docs/stockmarketdb-trading-statistics-datasource.md](docs/stockmarketdb-trading-statistics-datasource.md)
for the full mapping. Keep its connection string in secrets or environment configuration:

```powershell
$env:StockMarketDb__ConnectionString = "Server=localhost;Database=StockMarketDB;User Id=sa;Password=<secret>;TrustServerCertificate=true"
```

Apply PostgreSQL migrations, synchronize instruments first, then ingest bounded pages. During
initial warm-up, repeat the incremental instrument call until it returns fewer rows than the
configured page size before enabling time-series polling:

```http
POST /api/v1/admin/stockmarketdb/instruments/sync?fullReload=true
POST /api/v1/admin/stockmarketdb/instruments/sync
POST /api/v1/admin/stockmarketdb/intradaytrades/sync
POST /api/v1/admin/stockmarketdb/dailytrades/sync
POST /api/v1/admin/stockmarketdb/intradayindices/sync
POST /api/v1/admin/stockmarketdb/historicaldailyindices/sync
GET  /api/v1/admin/stockmarketdb/sync-state
```

After initial instruments and trade synchronization, set
`StockMarketDb:UsePersistedMarketQuotes=true` for the API and enable
`StockMarketDbPolling:Enabled=true` for the Worker. Polling defaults to one minute for trades,
five minutes for indices, hourly for daily summaries, and daily for instruments. Intraday
snapshots are retained for 30 days by default.

## NADPCO API Provider

The `NadpcoApi` HTTP provider is configured through the `NadpcoApi` section and reads credentials
from secrets or environment variables:

```powershell
$env:NadpcoApi__UserName = "<vendor-user>"
$env:NadpcoApi__Password = "<vendor-password>"
```

The provider obtains `/api/v2/Token` with Basic authentication, caches the returned token, and sends
Bearer authentication on data requests. Keep scheduled NADPCO reads disabled until the vendor's
successful token response shape and lifetime are confirmed. Details are documented in
[docs/nadpco-api-provider.md](docs/nadpco-api-provider.md).

Automatic NADPCO incremental synchronization is controlled by `NadpcoScheduledSync` and is disabled
by default. The scheduled plan includes a non-destructive `CompanyCatalog` refresh so newly listed
NADPCO companies are inserted and changed metadata is updated after the initial backfill. The
destructive company-catalog clean-slate path is maintenance-only through DataAdmin and is never run
by the scheduled worker. DataAdmin operators can inspect scheduler health/history and trigger an
on-demand scheduled workflow through the protected `nadpcoapi/scheduled-sync/*` admin endpoints.

## CyclicalWaves v2 Provider and Chat Provider Selection (Feature 148)

The CyclicalWaves v2 (TahlilApp API v2) provider is configured through the `CyclicalWavesV2` section
(contracts in [docs/providers/cyclicalwaves-v2](docs/providers/cyclicalwaves-v2/README.md)). Credentials are
required only while ingestion is enabled and come from secrets or environment variables:

```powershell
$env:CyclicalWavesV2__Identifier = "<account-identifier>"
$env:CyclicalWavesV2__Password = "<account-password>"
$env:CyclicalWavesV2__GaugeBaseUrl = "https://api.tahlilapp.com/api/" # optional independent gauge/equilibrium host
$env:CyclicalWavesV2__IngestionEnabled = "true"      # Worker and Admin sync
$env:CyclicalWavesV2__DailySyncEnabled = "true"      # daily Full run, cron CyclicalWavesV2__DailySyncCronUtc (default 30 23 * * *)
```

Data lands in schema `cyclicalwaves` only. Gauge/equilibrium reads use the separately configurable
`GaugeBaseUrl` host while sharing Feature 148 credentials and token refresh. DataAdmin operators use `/admin/data/cyclicalwaves-v2`
(API `api/v1/admin/cyclicalwaves-v2/*`): catalog and single-ticker syncs run inline; screener, income
statements, analysis and Full runs are queued for the Worker.

Chat answers from one provider, selected by `FinancialData:ChatProvider` (`NoavaranAmin` default, or
`CyclicalWaves`); there is no fallback between providers. Legacy CyclicalWaves-derived `DerivedMetrics` rows,
including rows that mix a CyclicalWaves and a NoavaranAmin operand, are always excluded from NoavaranAmin lookups,
scans, professional scans and financial-metric alerts; there is no setting to include them. The legacy `back1.cyclicalwaves.com` integration is
retired: `CyclicalWavesLegacy:AllowCalls=false` blocks every legacy request before transmission and its
credentials are optional.

Feature 159 adds one exception per capability: `FinancialData:CapabilitySources` maps a capability route to another
provider (only `comprehensive_analysis` is allowed). With `FINANCIAL_DATA_SOURCE_COMPREHENSIVE_ANALYSIS=CyclicalWaves`
the NoavaranAmin deployment answers comprehensive analysis from the v2 analyses with the fixed TahlilApp link
`COMPREHENSIVE_ANALYSIS_TAHLILAPP_URL`. Empty (default) keeps C2. Any other key, an unknown provider, or a map used
with `ChatProvider=CyclicalWaves` stops start-up. See `docs/adr/0001-per-capability-chat-data-source.md`.
For local runs `appsettings.Development.json` maps `comprehensive_analysis` to `CyclicalWaves` (production runs the
`Production` environment and never reads it); integration test hosts reset the map in `AuthenticationApiFactory`.

## Market View Configuration

Actor-scoped watchlists and `GET /api/v1/market/summary` read normalized StockMarketDB
projections. Configure quote staleness, the short-lived summary cache, top-mover count, and the
fallback watchlist limit through `MarketViews`. Subscription plans with a `Watchlist.Symbols`
capability override the fallback limit.

## Admin Management API

Apply the Auth and Billing migrations before using the permission-protected Admin Management
API. The route catalog, permission codes, `SuperAdmin` bootstrap rule, audit policy, and update
commands are documented in [docs/admin-management-api.md](docs/admin-management-api.md).

## Telegram Notification Orchestration

Features 090–096 publish durable notification intents; Feature 097 owns preference evaluation,
deduplication, digest scheduling, Telegram delivery attempts, retry/dead-letter handling, and
Feature 099 outcome handoff. Apply the Financial ingestion and Billing migrations, configure the
bot token through secrets, and enable the dispatcher only after both schemas are current. See
[docs/notification-orchestration.md](docs/notification-orchestration.md).

## Telegram Billing Purchases

Feature 098 adds Telegram-visible Billing products, checkout intents, manual receipt review,
exactly-once credit/subscription fulfillment, reconciliation, and Telegram status notifications.
Apply the Billing migration before enabling `/plans`, `/buy`, or receipt review. See
[docs/telegram-billing-purchases.md](docs/telegram-billing-purchases.md).

## Mobile Workspace and PWA (Feature 153)

Phones get a dedicated workspace at `/m` (chat, history, market, watchlist, account) built on the
existing APIs; no backend change is required. Automatic phone redirects from `/chat`, `/c/<id>` and
`/followed-symbols` are behind the build-time flag `VITE_MOBILE_AUTO_REDIRECT` (default `false`).
Keep it off in production until the live frontend/API origins and refresh-cookie behavior are
verified (spec D-Q5). `/m` works with the flag off, and users switch manually from the account
screen or the desktop header.

The app is installable (manifest scope `/`, start URL `/m`). The service worker in
`src/frontend/public/sw.js` caches static assets only and never caches HTML, server-function or API
responses. Update, header verification and kill-switch steps are in
[docs/deployment/pwa-service-worker.md](docs/deployment/pwa-service-worker.md). Specs, decisions and the
implementation report: [specs/153-mobile-pwa-workspace/](specs/153-mobile-pwa-workspace/).
