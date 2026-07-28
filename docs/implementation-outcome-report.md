# Full Implementation Outcome Report

## Purpose

This report describes the product and backend platform that will exist after all tasks in specifications `001` through `019` are implemented. It describes the intended completed system, not the current implementation status.

## Product Outcome

FinancialCopilot will evolve from an AI chat product into an AI-native Financial Intelligence Platform for the Iranian capital market, serving:

- The owned React web/mobile chat product.
- SaaS organization partners such as TahlilAPP.
- Future enterprise integrations through secured APIs.

The product UI will interact with a clean AI facade. It will not decide whether a request requires a scanner, market summary, single-stock analysis, portfolio capability, financial comparison, or future research tool. Those decisions will be made by backend orchestration.

The intelligence platform foundation will additionally provide governed financial semantics, reproducible future derived features, internal AI evaluation, correlated AI operations telemetry, and a consent-aware future memory strategy, without introducing premature deployment complexity.

## Delivered User Experience

| Capability | Result after implementation |
|---|---|
| Conversational AI query | A user submits a natural-language question through one AI endpoint and receives a persisted answer. |
| Persian and English scanner prompts | Users can ask financial screening questions in either language, subject to validation and clarification policy. |
| Stock-list answers | List answers are returned as structured tables with relevant metrics, data freshness, and presentation limits. |
| Explainable answers | Responses identify applied filters, sources, warnings, confidence score, and suggested follow-up questions. |
| Conversation history | Users can create, reopen, inspect, and continue generic AI conversations. |
| Usage visibility | Responses expose backend-calculated charged credits, remaining balance when permitted, pricing version, and cache status. |
| Partner access | SaaS partners can invoke the platform under isolated tenant and billing rules. |
| Admin data operations | Authorized operators can start ingestion work and inspect synchronization/provider operational status. |
| Governed financial meaning | Calculated and explained metrics resolve through versioned semantic definitions and bilingual aliases. |
| Platform quality controls | Internal evaluation/regression and operational telemetry support reliable AI workflow evolution. |

## Public API Outcome

### AI Facade And Conversations

The React chat UI uses only the AI facade for message execution:

```http
POST /api/ai/v1/query
```

Generic conversation endpoints support chat history:

```http
POST   /api/ai/v1/conversations
GET    /api/ai/v1/conversations
GET    /api/ai/v1/conversations/{conversationId}
GET    /api/ai/v1/conversations/{conversationId}/messages
DELETE /api/ai/v1/conversations/{conversationId}
```

### Supporting UI APIs

The completed backend will expose supporting read or account capabilities needed by the frontend:

```http
GET    /api/ai/v1/metadata/metrics
GET    /api/ai/v1/metadata/periods
GET    /api/ai/v1/metadata/symbols
GET    /api/ai/v1/metadata/industries
GET    /api/v1/usage/me
GET    /api/v1/watchlists/me
PUT    /api/v1/watchlists/me
GET    /api/v1/market/summary
```

Billing administration and data-ingestion administration will be secured operator or partner-account capabilities rather than chat UI routing decisions.

### Internal Scanner Operations

Scanner parsing and scanner execution are Application-layer capabilities invoked by orchestration. They are not public frontend APIs.

Examples of optional diagnostic endpoints, if required for operations, are internal only:

```http
POST /api/internal/scanner/parse
POST /api/internal/scanner/execute
```

## Target Backend Solution

| Module or project | Delivered responsibility |
|---|---|
| `FinancialCopilot.API` | HTTP endpoints, authentication integration, request/response contracts, middleware, and operational exposure. |
| `FinancialCopilot.Application` | Use cases, AI query orchestration, tool routing, scanner execution coordination, and application interfaces. |
| `FinancialCopilot.Domain` | Financial, identity, tenant, and shared domain rules without infrastructure coupling. |
| `FinancialCopilot.Infrastructure` | EF Core/PostgreSQL persistence, Redis, RabbitMQ, external data clients, AI-provider adapters, and security implementations. |
| `FinancialCopilot.Worker` | Background ingestion, normalization, metric recalculation, retryable processing, and scheduled/asynchronous work. |
| `FinancialCopilot.Billing` | Unified billing and credits bounded context, ledger, reservations, pricing policies, settlement contracts, and reporting contracts. |
| Test projects | Unit, integration, and architecture validation for behavior, API contracts, boundaries, and critical workflows. |

The system is initially delivered as a modular monolith. Billing, Scanner, AI model integration, Data Ingestion, Financial Semantics, Derived Features, AI Quality/Telemetry, and future Memory are explicit logical boundaries that can later be extracted only when deployment or organizational needs justify that cost.

## End-To-End AI Query Workflow

The main backend execution path will be:

```text
User Message
-> Authenticate Actor and Resolve Tenant
-> Resolve Billable Customer Account and Entitlements
-> Reserve Usage Capacity
-> AI Query Orchestrator
-> Intent Detection and Tool Routing
-> Scanner / Analysis / Summary / Future Tool Workflow
-> Semantic Metric Resolution, Data Fetching, Cached Data, and Derived Metrics
-> Answer Generation
-> Data Citations, Confidence Score, and Suggested Questions
-> Calculate and Commit Actual Usage
-> Persist Conversation and Messages
-> Return Structured Response Metadata
```

Microsoft Agent Framework workflows will coordinate model-assisted interpretation and permitted tools while deterministic backend functions enforce ordering and policy. An LLM will not be permitted to generate SQL directly, change financial values, assign confidence arbitrarily, or decide whether usage is charged.

## Scanner MVP Deliverables

### Query Interpretation And Execution

The scanner capability will:

- Interpret supported Persian and English natural-language filters.
- Convert requests into a schema-validated `ScannerQueryPlan`, never free-form SQL.
- Record whether a condition is explicit, a documented default, or confirmed through clarification.
- Ask for clarification when material meaning is ambiguous, such as an unspecified definition of high growth.
- Execute validated conditions against normalized local financial data and derived metrics.
- Resolve supported Persian/English financial wording through versioned semantic metric identifiers rather than hardcoded property assumptions.
- Sort, limit, rank, and return deterministic structured results.
- Surface missing, stale, unavailable, or fallback-sourced data warnings.

### Stock List Tables

When an AI answer includes a list of stocks, the response will contain a structured table. Default columns are:

- Symbol.
- Latest price, using live or low-latency data when available and otherwise the latest completed trading-day value.
- Price change percent.
- Market capitalization.
- Metrics directly relevant to the user's request, such as `P/E`, profitability growth, or sales growth.

The user may explicitly request different columns, subject to validation. The result table will return no more than 10 data columns to respect UI and data-fetch performance constraints. Price rows will include source and as-of metadata.

### Explainable Response

Each suitable AI response will support:

- Applied filter chips and interpretation notes.
- Data citations and source timestamps.
- Warnings for stale, incomplete, or fallback data.
- A backend-calculated Confidence Score with policy/version information.
- Relevant suggested follow-up questions.
- Usage and charging metadata calculated by Billing.

## Financial Data Platform

| Concern | Delivered approach |
|---|---|
| Structured financial data | Symbols, companies, industries, financial statements, monthly sales/production reports, market snapshots, and derived metrics are persisted in PostgreSQL. |
| Derived metrics | Backend-calculated metrics include growth measures, TTM values, ratios, and margins under versioned calculation policy. |
| Fresh market values | Lightweight current-price retrieval can be requested when available, with prior completed trading-day fallback. |
| Data ingestion | RabbitMQ-driven worker operations ingest, normalize, deduplicate, track sync runs, and trigger recalculation. |
| Cache and coordination | Redis supports query/result caching, rate limits, short-lived reservation coordination, locks, and workflow/session state. |
| Search evolution | Phase 1 uses PostgreSQL and indexing; textual, hybrid, or vector research retrieval is a later platform extension. |

Financial filtering will run against maintained local structured data rather than depend entirely on slow or unreliable runtime third-party requests.

## Financial Semantic Layer

Financial meaning will be governed through a versioned, auditable semantic catalog rather than embedded in prompts, controllers, or large formula-routing branches.

| Concept | Outcome |
|---|---|
| `FinancialMetricDefinition` | Stable meaning and identifier for a financial metric. |
| `MetricCode` | Canonical execution identifier used by scanner plans, calculators, and evidence. |
| `MetricVersion` and `MetricCalculationPolicy` | Historical definition and deterministic calculation behavior retained with computed observations. |
| `MetricAlias` | Persian and English terminology resolution to the same semantic identity where financially equivalent. |
| `MetricFormula` and `MetricDependency` | Governed dependencies and calculation evidence for reproducibility. |
| `MetricCategory`, `MetricUnit`, and `Requirement` | Classification, interpreted units, and source/period/freshness constraints. |

The initially supported Scanner metrics, including EPS, P/E, margins, growth measures, and cash-flow measures, are examples and not a closed list. The architecture supports hundreds of future ratios, indicators, aliases, period variants, and industry-specific policies through independently testable registered calculators such as `IFinancialMetricCalculator`, resolved through `IFinancialMetricRegistry`. Scanner plans retain canonical metric code/version evidence, and explanations cite the semantic definition and calculation-policy version applied.

```text
User expression
-> IMetricAliasResolver
-> Canonical MetricCode
-> IFinancialMetricRegistry
-> IMetricCalculationPolicyProvider
-> IFinancialMetricCalculator
-> MetricCalculationResult
```

The backend owns metric definitions, formulas, period handling, policies, dependencies, validation, and confidence rules. The AI layer may propose language interpretations, request clarification, and explain calculated evidence; it cannot define formulas or calculate financial values.

## Derived Feature Foundation

A future-compatible feature layer will prepare the platform for ranking, scoring, anomaly detection, recommendation systems, and portfolio intelligence while remaining lightweight:

```text
DerivedFeature
FeatureDefinition
FeatureSnapshot
FeatureVersion
FeatureComputationJob
FeatureDependency
```

Future signals may include momentum, relative strength, volatility, liquidity, growth consistency, earnings quality, and smart-money indicators. Features are asynchronously computable, historically snapshot-capable, deterministic/reproducible where defined as deterministic, and consumable through stable Application interfaces. This boundary is not a Phase 1 commitment to build full ML or feature-store infrastructure.

## AI Model Provider Layer

AI execution will be behind provider-neutral interfaces so business workflows do not depend on a specific model vendor.

| Provider mode | Intended support |
|---|---|
| Hosted model providers | OpenAI and Anthropic/Claude adapters through secured configuration. |
| Future hosted provider | An Abravran adapter integration point, implemented only after official contract and authentication documentation is available. |
| Local execution | Ollama or comparable local model runtimes where configured and operationally permitted. |
| Testing | A deterministic fake provider for reliable automated tests. |

Core abstractions include `IAiModelClient`, `IAiModelProviderResolver`, `IAiProviderCapabilityRegistry`, and `IAiExecutionTelemetrySink`. Routing selects a configured provider by required capabilities, tenant policy, and availability rather than by business code naming a vendor.

Provider adapters only translate model protocols and expose normalized execution facts. They do not perform financial interpretation, compute confidence, debit billing balances, or define public API responses.

## AI Quality And Operations Foundation

### Evaluation And Regression

The internal quality platform will model:

```text
GoldenQuestion
GoldenAnswer
EvaluationDataset
PromptVersion
EvaluationRun
EvaluationScore
RegressionResult
```

It will enable version-over-version measurement of scanner interpretation, semantic metric resolution, clarification decisions, ranking consistency, hallucination/citation controls, and financial-answer correctness. Structured outputs and deterministic values are scored against backend evidence; evaluation is not part of the public query critical path.

### Observability And Telemetry

The operations architecture will support:

```text
AiExecutionTrace
PromptTrace
ToolExecutionTrace
ProviderLatency
TokenUsage
CostTelemetry
WorkflowTelemetry
```

OpenTelemetry-compatible correlation will connect facade requests, conversations/messages, workflow stages, provider attempts, retries/fallbacks, tool calls, data evidence, confidence calculation, and billing outcomes. Telemetry can power internal dashboards or future approved Langfuse integration, subject to privacy/redaction/retention policy. The Billing ledger remains authoritative for charges.

## Conversation Memory Strategy

Phase 1 requires generic Conversation and Message persistence only. A future optional memory layer will distinguish:

- `ShortTermConversationMemory`.
- `LongTermUserMemory`.
- `PortfolioAwareMemory`.
- `PreferenceMemory`.
- `ResearchMemory`.
- `WatchlistMemory`.

Future memory is tenant-aware, consent-controlled for durable or sensitive content, protected from inappropriate provider/telemetry exposure, inspectable/revocable where stored, and explainable when it materially affects an answer. It is retrieved through controlled orchestration interfaces behind the same AI facade and does not replace authoritative conversation, watchlist, portfolio, financial-data, or billing records.

## Billing And Credits Deliverables

### Unified Billing Domain

`FinancialCopilot.Billing` will serve both organization partners and direct registered customers:

| Customer type | Billing behavior |
|---|---|
| SaaS organization | The organization is billed for usage by its end users. It may operate in prepaid, postpaid, or hybrid mode, with prepaid plus an approved credit line as the recommended initial mode. |
| Direct consumer | The registered customer receives subscription/allowance and wallet support; execution is rejected when no applicable balance is available. Individual overdraft is disabled by default. |

An authenticated `Actor` invokes an operation, but a resolved `CustomerAccount` is the billed entity. An optional partner `externalUserId` supports reporting, abuse controls, and sub-quotas without creating a FinancialCopilot wallet for that partner user.

### Accounting Model

The accounting source of truth will be an immutable `UsageLedger`. A `Wallet` is only a fast materialized balance snapshot.

The billing domain includes:

- `CustomerAccount`, `Wallet`, `UsageLedger`, `SubscriptionPlan`, `CreditLine`, `InvoiceAccount`, and `UsageReservation`.
- Versioned operation-based pricing rather than a fixed one-query/one-credit assumption.
- Internal concepts such as `UsageUnit`, `ComputeCost`, `OperationCost`, `ProviderCost`, and `PricingPolicy`.
- Idempotent reservations, commits, releases, refunds, adjustments, and reconciliation records.

Organization spending capacity is bounded:

```text
Available Spending Capacity = Wallet Balance + Credit Line - Reserved Amount
```

Unlimited negative balances will not be supported.

### Query Charging Flow

```text
Authenticate Actor
-> Resolve CustomerAccount
-> Validate Entitlements and Pricing Policy
-> Reserve Capacity Before Expensive Execution
-> Execute AI Workflow
-> Calculate Actual Operation and Provider Cost
-> Commit Usage Ledger Entry or Release Reservation
-> Update Wallet Projection
-> Return Usage Metadata
```

The query response will contain billing metadata such as charged credits, permitted remaining balance, pricing-policy version, and cache status. Provider failures, cancellation, timeout, retry, clarification, and cache-hit scenarios will follow explicit accounting rules.

## Security, Tenancy, And Operations

The completed platform will implement:

- JWT bearer authentication for owned application users.
- API key authentication for SaaS clients, with future OAuth client-credential support.
- Tenant isolation of credentials, configuration, usage, policies, analytics, and reporting.
- Clear separation of authentication identity, tenant scope, partner end-user references, and billed customer account.
- Authorization for public, partner, and admin operations.
- Rate limiting and correlation/audit information for sensitive or billable execution.
- Secured provider credentials and operational health/status observation.

## Quality And Verification Deliverables

Completion of the task set requires tests and validation covering:

- Domain rules, metric calculations, scanner parsing/execution, confidence policy, and billing accounting.
- API authentication, tenant context, facade response contracts, conversations, and protected administration.
- Idempotent ingestion, provider failure handling, cache behavior, reservation/ledger consistency, and retry outcomes.
- Architecture constraints that keep Application and Domain independent of infrastructure/vendor SDK concerns.
- Deterministic AI provider fakes so automated tests do not require a hosted or local LLM.
- Semantic-definition versioning, bilingual aliases, extensible metric-calculator registration, and historical result reproducibility.
- Future feature-snapshot reproducibility contracts, AI evaluation baselines, telemetry correlation/redaction, and consent-aware memory policy enforcement when those extensions are implemented.

## Delivery Sequence

The intended dependency order is:

```text
Project Foundation
-> Authentication and Tenant Context
-> Billing and Credits Domain Foundation
-> Financial Domain Model
-> Third-Party Financial Data Provider Abstraction
-> Data Ingestion and Normalization
-> Derived Metrics Engine
-> AI Model Provider Abstraction Foundation
-> AI Query Orchestration and Scanner Parsing
-> Scanner Execution Engine
-> Explainable Results
-> Phase 1 Usage Metering Integration
-> Cache and Performance
-> Admin Data Operations

Platform evolution foundations:
-> Financial Semantic Layer and Ontology
-> Derived Feature Foundation
-> AI Evaluation and Regression
-> AI Observability and Telemetry
-> Conversation Memory Strategy
```

## Deferred Or Future Expansion

Unless promoted into a separate delivery increment, the completed Phase 1 specification does not require full production delivery of:

- Automated bank payment gateway settlement and invoice delivery.
- Portfolio analysis, deep research, Codal analysis, or autonomous agent workflows beyond their extensible contracts.
- Elasticsearch/OpenSearch and vector retrieval.
- Separately deployed microservices.
- A concrete Abravran model-provider implementation without its official integration contract.
- Advanced derived-feature score implementations and ML/feature-store infrastructure beyond foundational contracts.
- Production evaluation dashboards, external observability sinks, or durable personalized memory without approved operating/privacy scope.

## Final Target State

After implementation of all documented tasks, FinancialCopilot will provide a secured, tenant-aware, billable AI-native Financial Intelligence Platform with a single frontend-facing conversation facade, internally orchestrated financial scanning, explainable structured results, provider-neutral model execution, governed financial semantics, future-ready feature/evaluation/telemetry/memory boundaries, locally queryable financial data, and accounting-grade billing foundations for both SaaS partners and direct customers.
