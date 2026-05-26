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
- Public AI facade controller for `POST /api/ai/v1/query`.
- Generic Conversation and Message query endpoints.

### FinancialCopilot.Application

Responsibilities:

- Use cases.
- DTOs.
- Commands/queries.
- Validators.
- Interfaces for repositories, data providers, AI services, cache, messaging, billing/metering.
- AI Query Orchestrator, Intent Detection, and Tool Routing.
- Scanner Tool / Scanner Use Case execution behind the facade.
- Conversation and Message application services.
- Query plan generation and validation.
- Result ranking, Data Citation mapping, Confidence Score policy, and Explainable Answer assembly.
- Usage Accounting orchestration for AI query execution.

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
- Conversation, Message, and usage ledger persistence.

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
React Chat UI / External Conversational Client
  -> POST /api/ai/v1/query
  -> FinancialCopilot.API AI Facade
  -> Application: AI Query Orchestrator
  -> Intent Detection
  -> Tool Routing / Use-Case Selection
  -> Scanner Tool / Single Stock Analysis / Market Summary / Portfolio / Deep Search
  -> Data Fetching / Cached Data / Third-Party APIs
  -> Explainable Answer Generation
  -> Data Citations / Confidence Score / Usage Accounting
  -> Conversation + Message Persistence
  -> Facade Response
```

The React UI has no responsibility for selecting a tool. A message that is ultimately handled by the Scanner Tool follows the same public facade contract as any other user message.

## Scanner Tool Flow

```text
AI Query Orchestrator selects Scanner Tool
  -> Metric and period extraction
  -> Query plan generation
  -> Query plan validation
  -> Data availability check
  -> Query execution
  -> Ranking
  -> Explainable Answer with Data Citations and Confidence Score
```

Scanner parsing, execution, and ranking are Application-layer services, such as `IScannerQueryParser`, `IScannerExecutionService`, and `IScannerResultRanker`. They are not public React UI endpoints.

## Conversation Flow

```text
Authenticated actor
  -> Submit Message through POST /api/ai/v1/query
  -> Create or continue Conversation
  -> Persist user Message
  -> Execute routed use case
  -> Record Usage Accounting outcome
  -> Persist assistant Message and answer evidence
  -> Retrieve history through /api/ai/v1/conversations endpoints
```

## Data Access Strategy

Use PostgreSQL as the source of truth for normalized and derived financial datasets required for screening. Use third-party APIs as external sources. Do not directly couple scanner use cases to third-party APIs.

Use Redis for:

- short-lived cache for API responses,
- query plan cache,
- popular scanner results,
- conversation read-model cache where safe,
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

For Phase 1, the AI Query Orchestrator detects intent and routes a user Message to an Application-layer use case. When the Scanner Tool is selected, AI should not directly execute SQL. It should produce a structured `ScannerQueryPlan` JSON that is validated by the backend before execution.

Use LLM for:

- natural language parsing,
- Intent Detection and Tool Routing suggestions,
- metric synonym mapping,
- ambiguity detection,
- explanation text generation.

Use deterministic code for:

- financial calculations,
- filtering,
- ranking,
- subscription and usage metering,
- Conversation ownership and Message persistence,
- permissions,
- result reproducibility.

## Security

- JWT for owned web app users.
- API keys or OAuth2 client credentials for SaaS clients.
- Tenant-aware data access.
- Per-user and per-client rate limits.
- The React UI accesses tools only through the public AI facade.
- Request logging without leaking sensitive tokens.
- Strict validation of generated scanner plans.
- No dynamic SQL from AI output.
