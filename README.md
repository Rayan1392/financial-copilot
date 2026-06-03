# Financial Copilot Backend — Project Documentation

## Product Context

Financial Copilot is an AI-powered capital market assistant for the Iranian stock market. The product will support two delivery models:

1. **SaaS/API model**: external websites and financial platforms consume backend APIs and build their own UI.
2. **Owned web application model**: Financial Copilot provides its own React web app where users register, buy subscriptions, top up usage credits, and use the product directly.

The existing React + TypeScript frontend prototype built with Lovable should be preserved as the UI foundation. Backend services will be implemented separately with .NET 10, C#, PostgreSQL, EF Core, Redis, RabbitMQ, and Clean Architecture.

## Communication Rule

Codex must always respond in English, even when a request is written in Persian. Persian end-user prompts or localized UI examples may be retained where they are part of product requirements.

## Phase 1 Scope — Scanner MVP

Phase 1 focuses on the **Natural Language Financial Scanner**.

The scanner must answer questions such as:

- "Give me symbols whose latest quarter net profit grew more than 50% and P/E is below 5."
- "Which symbols had 100% growth in last month sales?"
- "Which symbols have P/S below 1?"
- "Find companies with improving production, sales, profitability, and attractive valuation ratios."

The React chat UI submits every message to `POST /api/ai/v1/query`. The backend AI Query Orchestrator detects intent and, for scanner questions, invokes an internal Scanner Tool that converts natural language into a validated financial query plan, executes it against normalized market/fundamental data, ranks the results, and returns an Explainable Answer with Data Citations and a Confidence Score.

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
8. `specs/implementation-checklist.md`
9. `specs/*/user-story.md`

## Backend Development

The backend solution targets .NET 10 and is located in `src/backend/FinancialCopilot.sln`.

Run the CI-ready validation command from the repository root:

```powershell
dotnet test src/backend/FinancialCopilot.sln --configuration Release
```

Run the API locally:

```powershell
dotnet run --project src/backend/FinancialCopilot.API
```

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
read-only adapter. Keep its connection string in secrets or environment configuration:

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

## Market View Configuration

Actor-scoped watchlists and `GET /api/v1/market/summary` read normalized StockMarketDB
projections. Configure quote staleness, the short-lived summary cache, top-mover count, and the
fallback watchlist limit through `MarketViews`. Subscription plans with a `Watchlist.Symbols`
capability override the fallback limit.

## Admin Management API

Apply the Auth and Billing migrations before using the permission-protected Admin Management
API. The route catalog, permission codes, `SuperAdmin` bootstrap rule, audit policy, and update
commands are documented in [docs/admin-management-api.md](docs/admin-management-api.md).
