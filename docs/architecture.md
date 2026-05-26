# Backend Architecture

## Solution Structure

```text
src/backend/
  FinancialCopilot.sln
  FinancialCopilot.API/
  FinancialCopilot.Application/
  FinancialCopilot.Domain/
  FinancialCopilot.Infrastructure/
  FinancialCopilot.Worker/
tests/
  FinancialCopilot.UnitTests/
  FinancialCopilot.IntegrationTests/
  FinancialCopilot.ArchitectureTests/
```

The user initially listed API, Application, and Infrastructure. Add a separate `Domain` project to keep financial concepts and business rules independent from infrastructure.

## Projects

### FinancialCopilot.API

Responsibilities:

- ASP.NET Core Web API.
- Controllers.
- Authentication and authorization.
- API key authentication for SaaS consumers.
- JWT authentication for owned web app users.
- Request/response middleware.
- Rate limiting.
- Error handling.
- API versioning.
- OpenAPI/Swagger.
- Composition root and DI wiring.

### FinancialCopilot.Application

Responsibilities:

- Use cases.
- DTOs.
- Commands/queries.
- Validators.
- Interfaces for repositories, data providers, AI services, cache, messaging, billing/metering.
- Scanner orchestration.
- Query plan generation and validation.
- Result ranking and explanation assembly.

### FinancialCopilot.Domain

Responsibilities:

- Core entities.
- Value objects.
- Enums.
- Domain services.
- Financial metric definitions.
- Period comparison semantics.
- Scanner condition model.
- Domain exceptions.
- Business invariants.

### FinancialCopilot.Infrastructure

Responsibilities:

- EF Core DbContext.
- PostgreSQL entities and mappings.
- Migrations.
- External third-party data integrations.
- Redis implementation.
- RabbitMQ publisher/consumer implementation.
- AI provider clients.
- Search provider implementation.
- Repository implementations.
- Data sync persistence.

### FinancialCopilot.Worker

Responsibilities:

- Background ingestion jobs.
- Third-party data synchronization.
- RabbitMQ consumers.
- Scheduled refresh of monthly production/sales data.
- Scheduled refresh of quarterly financial statements.
- Embedding/indexing jobs for textual analysis.
- Derived metric calculation jobs.

## Architectural Flow

```text
Frontend / External SaaS Client
  -> FinancialCopilot.API
  -> Application Use Case
  -> Domain Rules + Query Plan
  -> Infrastructure Data Providers / Repositories / Cache
  -> Scanner Result + Explanations
  -> API Response
```

## Scanner Flow

```text
Natural language query
  -> Intent detection
  -> Metric and period extraction
  -> Query plan generation
  -> Query plan validation
  -> Data availability check
  -> Query execution
  -> Ranking
  -> Explainable response
```

## Data Access Strategy

Use PostgreSQL as the source of truth for normalized and derived financial datasets required for screening. Use third-party APIs as external sources. Do not directly couple scanner use cases to third-party APIs.

Use Redis for:

- short-lived cache for API responses,
- query plan cache,
- popular scanner results,
- provider access token cache,
- rate-limit counters.

Use RabbitMQ for:

- data ingestion jobs,
- metric calculation jobs,
- textual analysis jobs,
- embedding/indexing jobs,
- retryable provider calls.

## Search Recommendation

Use PostgreSQL full-text search first for simple textual search and metadata filtering. Add Elasticsearch or OpenSearch only when one or more of the following become true:

- large volume of textual reports,
- complex Persian full-text search requirements,
- faceted search across reports, industries, symbols, and dates,
- separate ranking/search relevance requirements,
- heavy concurrent search traffic.

For Phase 1 Scanner, Elasticsearch is optional. Design an `ISearchIndex` abstraction, but start with PostgreSQL unless report search becomes a bottleneck.

## AI Recommendation

For Phase 1, AI should not directly execute SQL. It should produce a structured `ScannerQueryPlan` JSON that is validated by the backend before execution.

Use LLM for:

- natural language parsing,
- metric synonym mapping,
- ambiguity detection,
- explanation text generation.

Use deterministic code for:

- financial calculations,
- filtering,
- ranking,
- subscription and usage metering,
- permissions,
- result reproducibility.

## Security

- JWT for owned web app users.
- API keys or OAuth2 client credentials for SaaS clients.
- Tenant-aware data access.
- Per-user and per-client rate limits.
- Request logging without leaking sensitive tokens.
- Strict validation of generated scanner plans.
- No dynamic SQL from AI output.
