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
8. `specs/*/user-story.md`

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

In the Development environment, the initial foundation endpoints are:

- `GET /health`
- `GET /openapi/v1.json`

## Authentication Configuration

Protected AI routes accept either a configured JWT bearer token or an `X-Api-Key` credential. JWT actors must include a tenant id claim (`financial_copilot:tenant_id`) and a GUID subject. API client configuration stores SHA-256 key hashes rather than raw API keys and supplies both client and tenant ids.

JWT signing keys and API key hashes must be supplied through secrets or environment configuration; the repository settings do not contain active credentials. Authenticated AI requests are rate limited by user id or API client id using `RateLimiting:AuthenticatedActor` settings.
