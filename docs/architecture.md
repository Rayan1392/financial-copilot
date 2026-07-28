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
  FinancialCopilot.Billing/
tests/
  FinancialCopilot.UnitTests/
  FinancialCopilot.IntegrationTests/
  FinancialCopilot.ArchitectureTests/
```

The user initially listed API, Application, and Infrastructure. Keep a separate `Domain` project for financial concepts and add an isolated `FinancialCopilot.Billing` bounded-context module. Billing is part of the modular monolith initially, not an independently deployed microservice.

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
- Versioned Financial Semantic Layer definitions, bilingual aliases, metric dependencies, and calculation-policy contracts.
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
- AI model provider adapters for hosted and local execution, implementing provider-neutral contracts.
- Search provider implementation.
- Repository implementations.
- Data sync persistence.
- Conversation, Message, and usage ledger persistence.

The initial financial-data provider boundary exposes provider-neutral Application interfaces and uses an Infrastructure deterministic mock adapter until a production provider contract is selected. Infrastructure also contains a configurable typed HTTP provider client, raw-payload persistence, provider-health adapter, and timeout/retry/circuit-breaker handling; scanner services consume none of its provider-specific transport details.

### FinancialCopilot.Worker

Responsibilities:

- Background ingestion jobs.
- Third-party data synchronization.
- RabbitMQ consumers.
- Scheduled refresh of monthly production/sales data.
- Scheduled refresh of quarterly financial statements.
- Embedding/indexing jobs for textual analysis.
- Derived metric calculation jobs.
- Deterministic derived-feature computation jobs when feature definitions are promoted into calculation scope.

### FinancialCopilot.Billing

Responsibilities:

- Unified billing model for SaaS organization accounts and direct consumer accounts.
- `CustomerAccount`, `Wallet`, `UsageLedger`, `SubscriptionPlan`, `CreditLine`, `InvoiceAccount`, and `UsageReservation` business rules.
- Immutable ledger and materialized wallet balance projection.
- Operation-based pricing, entitlements, reservations, charge finalization, refunds, and reconciliation.
- Prepaid, postpaid, and hybrid organization billing modes.
- Direct-consumer subscriptions, top-ups, and payment gateway interfaces.
- Billing reports, invoice interfaces, and partner external-user attribution.

Billing calculation services are independent from agent and scanner implementations. The AI workflow calls billing interfaces; it does not own pricing or mutate balances.

## Bounded Contexts

The target modular boundaries are:

```text
FinancialCopilot.API
FinancialCopilot.Application
FinancialCopilot.Infrastructure
FinancialCopilot.AI
FinancialCopilot.Scanner
FinancialCopilot.Billing
FinancialCopilot.DataIngestion
```

They may initially share the deployable backend host and infrastructure project while their contracts and dependencies remain isolated. Separate deployments are a later operational decision, not a Phase 1 prerequisite.

## Architectural Flow

```text
React Chat UI / External Conversational Client
  -> POST /api/ai/v1/query
  -> FinancialCopilot.API AI Facade
  -> Billing: Resolve CustomerAccount / Validate Entitlement / Reserve Usage
  -> Application: AI Query Orchestrator
  -> Intent Detection
  -> Tool Routing / Use-Case Selection
  -> Scanner Tool / Single Stock Analysis / Market Summary / Portfolio / Deep Search
  -> Data Fetching / Cached Data / Third-Party APIs
  -> Explainable Answer Generation
  -> Data Citations / Confidence Score / Usage Accounting
  -> Billing: Calculate Actual Cost / Commit or Release Reservation / Append Ledger
  -> Conversation + Message Persistence
  -> Facade Response
```

The React UI has no responsibility for selecting a tool. A message that is ultimately handled by the Scanner Tool follows the same public facade contract as any other user message.

## Scanner Tool Flow

```text
AI Query Orchestrator selects Scanner Tool
  -> Metric and period extraction
  -> Result-column intent extraction
  -> Query plan generation
  -> Query plan validation
  -> Data availability check
  -> Query execution
  -> Ranking
  -> Table column policy resolution
  -> Batch latest-price resolution with live/previous-trading-day fallback
  -> Explainable Answer with Data Citations and Confidence Score
```

Scanner parsing, execution, and ranking are Application-layer services, such as `IScannerQueryParser`, `IScannerExecutionService`, and `IScannerResultRanker`. They are not public React UI endpoints.

When a routed answer is a list of stocks, `IScannerResultColumnPolicy` creates a structured table projection. Its default columns are symbol, latest price, price change percentage, market capitalization, and metrics relevant to the question. A user can explicitly request different columns, but the policy enforces a maximum of 10 displayed data columns and reports any omission or clarification requirement.

`IMarketQuoteResolver` obtains price and change values in a batched operation: use live/low-latency quotes when the provider reports them as available; otherwise fall back to the most recent completed trading-day statistics. Each row retains observation/source metadata for the Explainable Answer. The LLM neither selects the source of truth nor fabricates row values.

## Microsoft Agent Framework Orchestration

Microsoft Agent Framework is the required orchestration technology for AI orchestration V2.

The current V1 orchestration may remain available during migration for rollback and backward compatibility, but all new orchestration work must target the Microsoft Agent Framework V2 path unless a later spec explicitly says otherwise.

### Required V2 Orchestration Principles

- Use an Agent for conversational interpretation, intent/tool-selection suggestions, clarification wording, explanation prose, and follow-up suggestions.
- Use a Workflow for mandatory ordered backend processing where the system must guarantee validation, deterministic execution, accounting, audit, and persistence.
- Keep `IAiQueryOrchestrationService` as the stable Application-facing contract.
- Select the orchestration implementation by configuration, for example `AiOrchestration:Mode = V1 | MicrosoftAgentFrameworkV2`.
- Preserve `POST /api/ai/v1/query` as the only public chat-query endpoint.
- Wrap Application-layer operations through narrow tool/function adapters. Core services remain independent from Microsoft Agent Framework and AI providers.
- Use function invocation middleware for telemetry, authorization/correlation propagation, privacy policy enforcement, and tool-call auditing.
- Do not use middleware, prompts, or agent reasoning as the business source of truth.

### Dependency Boundary

Microsoft Agent Framework packages may be referenced only from the orchestration infrastructure/composition boundary that constructs agents, workflows, tools, middleware, and model-client bridges.

Microsoft Agent Framework types must not leak into:

- `FinancialCopilot.Domain`
- `FinancialCopilot.Billing`
- deterministic scanner execution services
- semantic metric catalog/calculator services
- public API request/response DTOs
- provider-neutral AI model contracts from `014`

Architecture tests must enforce this boundary.

### Required V2 Workflow

The scanner and symbol lookup response path must be explicit:

```text
AI Facade Request
  -> Resolve actor, tenant, authorization, conversation context
  -> Retrieve permitted memory context
  -> Billing: Validate entitlement and reserve usage
  -> Microsoft Agent Framework Agent: intent/tool-selection suggestion
  -> Workflow branch: Scanner | SymbolLookup | Clarification | Unknown
  -> Parse and validate structured plan/function input
  -> Execute deterministic Application service
  -> Resolve data citations and freshness
  -> Calculate deterministic confidence score
  -> Build explainable answer
  -> Collect missing-answer feedback where applicable
  -> Billing: finalize or release reservation exactly once
  -> Persist user/assistant messages and workflow evidence
  -> Facade Response
```

`IConfidenceScoreCalculator` calculates the displayed confidence from deterministic evidence and versioned policy. `IUsageChargeCalculator` and `IUsageAccountingService` calculate and persist displayed credit consumption from versioned charging policy. These workflow steps are mandatory and cannot be skipped, modified, or numerically decided by the LLM.

### Required Tool/Function Adapters

The V2 workflow should expose only narrow validated adapters over existing Application services, such as:

```text
ScannerPlanToolAdapter
ScannerExecutionToolAdapter
SymbolLookupToolAdapter
ExplainableAnswerToolAdapter
MemoryContextToolAdapter
BillingReservationFunction
BillingFinalizationFunction
MessagePersistenceFunction
MissingAnswerFeedbackFunction
```

These adapters may call existing Application services. They must not expose raw `DbContext`, arbitrary SQL, dynamic LINQ, billing ledger mutation, wallet mutation, tenant selection, actor selection, or unvalidated provider calls to the LLM.

### Provider-Neutral Model Execution

Microsoft Agent Framework orchestration must use the provider-neutral AI model execution boundary from `014`.

```text
Microsoft Agent Framework Agent/Workflow Step
  -> Requested capabilities
  -> IAiModelProviderResolver
  -> Compatible IAiModelClient
  -> Normalized AI result and usage facts
  -> Workflow telemetry, billing, evaluation, and audit
```

The workflow must not hardcode OpenAI, Claude, Ollama, Abravran, or any vendor SDK. Provider routing, fallback, tenant allow-lists, local-runtime restrictions, data residency, structured-output capability, and normalized usage facts remain owned by the provider-neutral AI abstraction.

### Backward Compatibility and Rollback

During migration:

- V1 orchestration remains available behind configuration.
- V2 must return the same public response contract shape as V1.
- New response fields must be nullable/additive.
- Existing frontend chat, conversation reload, scanner table, symbol lookup table, usage metadata, memory disclosures, citations, warnings, confidence, and billing behavior must continue to work.
- Operators must be able to switch back to V1 without changing frontend code or public API routes.

### Microsoft Agent Framework References

This approach follows Microsoft Agent Framework guidance: agents support tool-using conversation and model interaction, while workflows provide graph-based, type-safe coordination for multi-step tasks. Middleware can intercept and extend agent behavior for logging, safety, compliance, and custom policy without putting business rules into prompts.

Relevant Microsoft documentation:

- <https://learn.microsoft.com/en-us/agent-framework/overview/>
- <https://learn.microsoft.com/en-us/agent-framework/workflows/workflows>
- <https://learn.microsoft.com/en-us/agent-framework/agents/middleware/>
- <https://learn.microsoft.com/en-us/dotnet/ai/how-to/access-data-in-functions>


## AI Model Provider Abstraction

Model execution is separate from financial-data provider integration. Define provider-neutral AI contracts in the Application/AI boundary and implement hosted or local adapters in Infrastructure.

Supported deployment forms include:

- Hosted OpenAI adapter.
- Hosted Anthropic/Claude adapter.
- Hosted Abravran adapter placeholder, implemented only after its official API and authentication contract are available.
- Local Ollama adapter.
- Deterministic fake adapter for automated tests.

Use capability-based selection rather than vendor-dependent orchestration:

```text
AI Workflow Step
  -> Requested Capabilities (structured output / tools / streaming / embeddings)
  -> IAiModelProviderResolver
  -> Compatible Hosted or Local IAiModelClient Adapter
  -> Normalized Result and Execution Usage Facts
```

Recommended contracts:

```csharp
public interface IAiModelClient
public interface IAiModelProviderResolver
public interface IAiProviderCapabilityRegistry
public interface IAiExecutionTelemetrySink
```

The provider adapter is responsible for vendor protocol translation, capability reporting, failures, and normalized usage telemetry. It is not responsible for:

- financial filter semantics,
- scanner plan validation,
- confidence calculation,
- charge calculation,
- wallet mutation,
- public response contract decisions.

Hosted and local adapters may expose different usage/cost information. Billing consumes normalized execution facts under versioned pricing policy and does not depend on any provider SDK.

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
- short-lived usage reservation coordination,
- distributed locks for idempotent charge/settlement processing,
- AI session and streaming state,
- conversation read-model cache where safe,
- provider access token cache,
- rate-limit counters.

Use RabbitMQ for:

- data ingestion jobs,
- metric calculation jobs,
- textual analysis jobs,
- embedding/indexing jobs,
- retryable provider calls.
- invoice/settlement and payment-reconciliation jobs,
- Codal parsing, AI summarization, and cache-warming jobs when introduced.

## Search Recommendation

Use PostgreSQL full-text search first for simple textual search and metadata filtering. Add Elasticsearch or OpenSearch only when one or more of the following become true:

- large volume of textual reports,
- complex Persian full-text search requirements,
- faceted search across reports, industries, symbols, and dates,
- separate ranking/search relevance requirements,
- heavy concurrent search traffic.

For Phase 1 Scanner, Elasticsearch is optional. Design an `ISearchIndex` abstraction, but start with PostgreSQL unless report search becomes a bottleneck.

For later Codal/research/news workloads, the architecture may extend to PostgreSQL plus Elasticsearch/OpenSearch plus vector storage for Persian full-text, semantic, and hybrid retrieval. This future retrieval stack is separate from billing correctness and should not delay the Phase 1 scanner.

## AI Recommendation

For Phase 1, the AI Query Orchestrator detects intent and routes a user Message to an Application-layer use case. When the Scanner Tool is selected, AI should not directly execute SQL. It should produce a structured `ScannerQueryPlan` JSON that is validated by the backend before execution.

Use LLM for:

- natural language parsing,
- Intent Detection and Tool Routing suggestions,
- metric synonym mapping,
- ambiguity detection,
- explanation text generation.

Obtain these LLM capabilities through provider-neutral AI model contracts. A configured provider may be cloud hosted or local; all structured scanner plans remain backend-validated before execution.

Use deterministic code for:

- financial calculations,
- filtering,
- ranking,
- subscription and usage metering,
- account resolution, reservations, operation pricing, wallet projection, invoicing, and payment reconciliation,
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
- Immutable billing ledger and tenant-isolated billing access.

## Future Evolution

The module boundaries and billing ledger are intended to support later AI agents, autonomous research, portfolio copilots, ranking engines, alerts, financial monitoring, B2B AI APIs, mobile applications, and enterprise integrations without redesigning account charging or AI workflow enforcement.

## Financial Semantic Layer

The platform requires a formal semantic layer for financial meaning. The Phase 1 metric catalog is an initial set, not a closed enumeration. A future production catalog may include hundreds of metrics, aliases, formulas, indicator policies, industry-specific variations, and Persian/English terminology.

Model concepts include:

```text
FinancialMetricDefinition
MetricCode
MetricVersion
MetricCalculationPolicy
MetricAlias
MetricCategory
MetricUnit
MetricFormula
MetricDependency
Requirement
MetricCalculator
MetricResolutionResult
MetricCalculationContext
MetricCalculationResult
```

EPS, P/E, margins, growth measures, and cash-flow measures are examples only; they are not the complete or fixed domain scope. Scanner interpretation must resolve terminology through versioned semantic metric definitions and canonical `MetricCode` values, and calculated observations and explanations must retain the definition/policy version used. Calculator implementations should be registered through strategies/DI, for example `IFinancialMetricCalculator` and `IFinancialMetricRegistry`, rather than routed through central `switch`/`case` or `if`/`else` formula logic.

The required semantic execution path is:

```text
User expression
  -> IMetricAliasResolver
  -> Canonical MetricCode
  -> IFinancialMetricRegistry
  -> IMetricCalculationPolicyProvider
  -> IFinancialMetricCalculator
  -> MetricCalculationResult
```

For example, a Persian expression for latest-quarter net profit growth may resolve to `NET_PROFIT_GROWTH_QOQ` or `NET_PROFIT_GROWTH_YOY` depending on the validated context. If the comparison basis is ambiguous, the backend returns clarification rather than silently choosing a calculation.

The backend owns canonical definitions, formulas, policies, period handling, dependencies, validation, and confidence rules. The LLM may identify candidate terminology, request clarification, and compose explanation text, but it cannot establish metric meaning, formula selection, version selection, or numeric calculation. Financial terminology must be extensible through semantic definitions and calculator strategies, not hardcoded procedural logic. Detailed evolution rules are documented in `docs/financial-intelligence-platform-capabilities.md` and `specs/015-financial-semantic-layer`.

The initial registered catalog exposes governed public metric metadata through authenticated `GET /api/ai/v1/metadata/metrics`. EF Core semantic read models retain definition, alias, policy, and dependency version structures for later normalized data and calculation persistence. Calculator interfaces and DI resolution are established here; executable metric strategy implementations and persisted calculated observations are delivered by the Derived Metrics Engine.

## Derived Feature Foundation

Future intelligence capabilities consume reproducible derived features such as momentum, liquidity, volatility, growth consistency, relative strength, or earnings quality. The platform defines `DerivedFeature`, `FeatureDefinition`, `FeatureSnapshot`, `FeatureVersion`, `FeatureComputationJob`, and `FeatureDependency` as a lightweight evolution boundary. The foundation persists versioned definitions, historical snapshots with dependency/input-fingerprint evidence, and idempotent computation jobs.

Feature snapshots are deterministic and historically traceable where the definition is deterministic. Worker/RabbitMQ flows process requested calculations and publish completed or failed outcomes. Production formulas for advanced feature scores remain separately scoped. This is not a Phase 1 requirement to build a full ML platform or new feature-store infrastructure.

## AI Evaluation And Observability

AI workflow quality and operation need dedicated internal boundaries:

- Evaluation models include `GoldenQuestion`, `GoldenAnswer`, `EvaluationDataset`, `PromptVersion`, `EvaluationRun`, `EvaluationScore`, and `RegressionResult`.
- Evaluation measures structured interpretation, semantic metric resolution, clarification, ranking consistency, evidence/citation completeness, and protection of deterministic financial/billing output.
- Operational models include `AiExecutionTrace`, `PromptTrace`, `ToolExecutionTrace`, `ProviderLatency`, `TokenUsage`, `CostTelemetry`, and `WorkflowTelemetry`.
- Use OpenTelemetry-compatible correlation across API request, Message, workflow, tool, provider, data lookup, confidence function, Billing reservation/ledger, and persistence.
- Apply explicit prompt/response privacy, redaction, retention, and tenant policy. Operational telemetry does not replace the Billing ledger.

Potential later telemetry integrations include internal dashboards and Langfuse where approved by data protection policy. Evaluation and observability are internal platform capabilities, not frontend tool-selection APIs.

## Conversation Memory Evolution

Conversation persistence is required for Phase 1. Advanced memory is separate future functionality, with potential types including `ShortTermConversationMemory`, `LongTermUserMemory`, `PortfolioAwareMemory`, `PreferenceMemory`, `ResearchMemory`, and `WatchlistMemory`.

Future memory must be tenant-aware, subject- and purpose-scoped, explicitly consented where durable or sensitive, protected from provider/log leakage, user-controllable, and explainable when it materially informs an answer. Orchestration may later retrieve authorized memory through stable Application interfaces behind the same AI facade. Advanced memory/vector infrastructure is not introduced merely for the Scanner MVP.

The Application layer exposes `IMemoryContextProvider`, `IMemoryConsentService`, `IMemoryControlService`, `IMemoryAuditService`, and `IMemoryProtectionPolicy` extension points. Phase 1 registers disabled context, consent, and control implementations; the existing orchestrator does not request optional memory. A later approved workflow must evaluate tenant/subject ownership, purpose, active consent, retention, provider-prompt eligibility, and telemetry eligibility before memory can influence an answer, and must return a material-use disclosure.
