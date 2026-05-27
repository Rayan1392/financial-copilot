# Implementation Checklist

## Purpose

This file is the implementation control checklist for FinancialCopilot. An implementation agent must read this file before starting backend work, select the first eligible unchecked item, implement the corresponding `user-story.md` and `tasks.md`, verify it, and update the checkbox and evidence in this file.

The checklist orders delivery by dependency and product priority rather than folder number. It preserves:

- The modular monolith architecture.
- `POST /api/ai/v1/query` as the single public chat-query endpoint.
- Deterministic financial execution and versioned semantic metrics.
- Explainable results and billing-grade accounting.
- Provider-neutral AI execution.
- Future-ready platform capabilities without premature infrastructure.

## How To Use This Checklist

1. Start with the first unchecked story whose prerequisite stories are complete or whose required existing implementation has been verified.
2. Read both the linked user story and task file before changing code.
3. Keep implementation within the story boundary; do not bypass dependency rules defined in `specs/README.md`.
4. Mark a story complete only after its required behavior, persistence/contracts where applicable, tests, and relevant documentation are verified.
5. Record concise evidence in the completion log, including test commands and important deferred items.
6. If existing code appears to satisfy a story, audit it against the acceptance criteria and tasks before checking the item.
7. Use `[~]` only while actively implementing one story in a working branch; replace it with `[x]` when verified or `[ ]` if work is paused/incomplete.

## Completion Gate

A checklist item may be changed to `[x]` only when applicable criteria are met:

- Acceptance criteria in the story are satisfied.
- Required tasks are implemented or explicitly documented as deferred by the story scope.
- Code follows the project dependency and SOLID rules.
- Public chat execution still uses only `POST /api/ai/v1/query`.
- Relevant unit, integration, and architecture tests pass.
- Contracts and documentation affected by behavior are updated.
- No LLM directly performs deterministic financial calculations, billing decisions, confidence calculation, or SQL execution.
- The completion log below identifies verification evidence.

## Ordered Delivery Checklist

### Stage 1 - Backend Foundation And Commercial Boundary

These stories establish the executable solution, actor/tenant isolation, and billing boundary required before billable AI workflows are expanded.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [x] | 01 | [001](./001-project-foundation/user-story.md) / [tasks](./001-project-foundation/tasks.md) | Project Foundation | Establish solution structure, configuration, test hosts, and architecture rules. Verify existing foundation before checking. |
| [x] | 02 | [002](./002-auth-and-tenancy/user-story.md) / [tasks](./002-auth-and-tenancy/tasks.md) | Authentication and Tenant Context | Depends on `001`; establish authenticated actor, tenant, API client, authorization, and rate-limit context. |
| [x] | 03 | [013](./013-billing-and-credits-domain/user-story.md) / [tasks](./013-billing-and-credits-domain/tasks.md) | Billing and Credits Domain | Depends on `001`, `002`; establish the isolated Billing bounded context, account model, ledger, pricing, entitlement, and reservation contracts. |

### Stage 2 - Governed Financial Data And Calculation Core

These stories establish canonical financial meaning, normalized source data, and deterministic calculation evidence used by the Scanner Tool.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [x] | 04 | [003](./003-financial-domain-model/user-story.md) / [tasks](./003-financial-domain-model/tasks.md) | Financial Domain Model | Depends on `001`; define financial entities, periods, invariants, and semantic metric primitives. |
| [x] | 05 | [015](./015-financial-semantic-layer/user-story.md) / [tasks](./015-financial-semantic-layer/tasks.md) | Financial Semantic Layer and Ontology | Depends on `003`; establish canonical `MetricCode`, bilingual aliases, versioned policies, and registry/strategy calculator contracts before expanding calculated metrics or parser assumptions. |
| [x] | 06 | [004](./004-third-party-data-provider-abstraction/user-story.md) / [tasks](./004-third-party-data-provider-abstraction/tasks.md) | Third-Party Data Provider Abstraction | Depends on `001`, `003`; isolate financial/market data providers and health/reliability behavior. |
| [x] | 07 | [005](./005-data-ingestion-and-normalization/user-story.md) / [tasks](./005-data-ingestion-and-normalization/tasks.md) | Data Ingestion and Normalization | Depends on `003`, `004`; ingest and persist normalized reproducible scanner inputs. |
| [x] | 08 | [006](./006-derived-metrics-engine/user-story.md) / [tasks](./006-derived-metrics-engine/tasks.md) | Derived Metrics Engine | Depends on `003`, `005`, `015`; implement deterministic, versioned calculations through registered metric strategies. |

### Stage 3 - Provider-Neutral AI And Internal Observability Foundation

These stories make AI execution replaceable and traceable before the public query workflow depends on it.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [x] | 09 | [014](./014-ai-model-provider-abstraction/user-story.md) / [tasks](./014-ai-model-provider-abstraction/tasks.md) | AI Model Provider Abstraction | Depends on `001`; implement provider-neutral AI contracts, deterministic fake provider, capability routing, and normalized execution facts. |
| [x] | 10 | [018](./018-ai-observability-and-telemetry/user-story.md) / [tasks](./018-ai-observability-and-telemetry/tasks.md) | AI Observability and Telemetry | Depends on `002`, `013`, `014`; implement the minimum OpenTelemetry-compatible correlation/trace contracts needed by AI facade workflows. Advanced dashboards remain incremental. |

### Stage 4 - Scanner MVP Through The Single AI Facade

These stories deliver the user-visible scanner workflow, explainability, accounting integration, performance, and data operations.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [x] | 11 | [007](./007-natural-language-scanner-parser/user-story.md) / [tasks](./007-natural-language-scanner-parser/tasks.md) | Natural Language Scanner Parser | Depends on `002`, `013`, `014`, `015`, `018`; implement AI facade orchestration, conversation contracts, intent/tool routing, semantic metric resolution, and validated plans. |
| [x] | 12 | [008](./008-scanner-execution-engine/user-story.md) / [tasks](./008-scanner-execution-engine/tasks.md) | Scanner Execution Engine | Depends on `006`, `007`; execute validated plans against deterministic data/metrics and produce table-ready results. |
| [ ] | 13 | [009](./009-explainable-results/user-story.md) / [tasks](./009-explainable-results/tasks.md) | Explainable Scanner Results | Depends on `008`, `015`; return citations, confidence, metric/policy evidence, follow-up suggestions, and structured answer presentation. |
| [ ] | 14 | [010](./010-usage-metering-and-billing-readiness/user-story.md) / [tasks](./010-usage-metering-and-billing-readiness/tasks.md) | Usage Metering and Billing Readiness | Depends on `007`, `009`, `013`, `014`; complete facade reservation/finalization and returned usage metadata using Billing contracts. |
| [ ] | 15 | [011](./011-cache-and-performance/user-story.md) / [tasks](./011-cache-and-performance/tasks.md) | Cache and Performance | Depends on `008`, `010`; add caching without bypassing accounting, evidence, or freshness. |
| [ ] | 16 | [012](./012-admin-data-operations/user-story.md) / [tasks](./012-admin-data-operations/tasks.md) | Admin Data Operations | Depends on `004`, `005`; expose authorized ingestion/health operations needed to operate the scanner datasets. |

### Stage 5 - Intelligence Platform Evolution

These stories are part of the future-ready platform architecture. They should not delay the Phase 1 Scanner MVP unless explicitly promoted into a release milestone.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [ ] | 17 | [017](./017-ai-evaluation-and-regression/user-story.md) / [tasks](./017-ai-evaluation-and-regression/tasks.md) | AI Evaluation and Regression Framework | Depends on `007`, `008`, `009`, `014`, `015`; add internal golden datasets, prompt/workflow version evaluation, and regression reporting. |
| [ ] | 18 | [016](./016-derived-feature-foundation/user-story.md) / [tasks](./016-derived-feature-foundation/tasks.md) | Derived Feature Foundation | Depends on `005`, `006`, `015`; establish reproducible historical feature contracts and asynchronous computation without implementing a full ML platform. |
| [ ] | 19 | [019](./019-conversation-memory-strategy/user-story.md) / [tasks](./019-conversation-memory-strategy/tasks.md) | Conversation Memory Strategy | Depends on Conversation/Message delivery in `007` and tenancy in `002`; define or implement only approved consent-aware memory scope beyond Phase 1 persistence. |

## Milestone View

| Milestone | Stories required | Completion condition |
|---|---|---|
| Foundation ready | `001`, `002`, `013` | Secure modular backend and Billing boundary exist for subsequent work. |
| Deterministic financial core ready | `003`, `015`, `004`, `005`, `006` | Normalized data and governed deterministic metric results support scanning. |
| AI execution foundation ready | `014`, minimum required scope of `018` | Model execution is provider-neutral and correlated for operational tracing. |
| Phase 1 Scanner MVP ready | `007`, `008`, `009`, `010`, `011`, `012` plus prerequisite stages | React UI can replace mocks using the single AI facade with explainability, billing, and operable data workflows. |
| Platform evolution ready | `016`, `017`, `019` and expanded `018` as promoted | Future features, evaluation, and optional memory are delivered under approved scope. |

## Completion Log

Add one row only after verification. Do not mark an item complete solely because source files exist.

| Date | Spec | Status | Verification evidence / notes |
|---|---|---|---|
| 2026-05-26 | `001-project-foundation` | Completed | Audited existing modular solution, API middleware/endpoints, Billing module, and architecture tests. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 4, Integration 16, Architecture 1. |
| 2026-05-26 | `002-auth-and-tenancy` | Completed | Added explicit `ActorType` and canonical `ActorId` current-context contract for user/API-client Billing handoff without billing decisions in auth. Verified JWT/API-key, 401/403, tenant/client access and per-actor rate limit behavior. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 5, Integration 16, Architecture 1. |
| 2026-05-26 | `013-billing-and-credits-domain` | Completed | Implemented the isolated Billing foundation: account modes/credit-line policy, immutable usage and financial ledgers, atomic/idempotent reservation/finalization/expiry/refund/adjustment persistence, wallet projection, versioned operation pricing, usage/admin reads, invoice/subscription/payment boundary contracts, transactional outbox records and retry diagnostics, and scheduled worker maintenance for reservation expiry plus optional configured outbox dispatch. AI facade charging/usage metadata is assigned to `010`; normalized provider facts to `014`; telemetry correlation to `018`; live payment/subscription/invoice/outbox transport and scale-out claiming remain explicitly deferred commercial/operations integrations. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 40, Integration 49, Architecture 1. |
| 2026-05-26 | `003-financial-domain-model` | Completed | Added normalized financial entities and provenance/quality evidence, validated symbol/period/metric/version/percentage primitives, supported monthly/3/6/9/12-month/latest-month/latest-quarter/TTM period semantics, reusable YoY/MoM comparison policy, extensible registered metric identity and calculation-policy input contracts, and derived-metric version evidence aligned for `015`/`006`. Recorded downstream ownership for aliases, ingestion persistence, and calculation execution in the feature task log. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 51, Integration 49, Architecture 1. |
| 2026-05-26 | `015-financial-semantic-layer` | Completed | Added versioned semantic definitions, bilingual alias and ambiguity resolution, effective-version/policy/dependency services, calculator strategy registry contracts, Phase 1 governed catalog registration, QoQ comparison semantics, dependency-version evidence for derived observations, scanner/explanation handoff contracts, EF semantic catalog read models, and authenticated metrics metadata API. Production numeric calculators remain owned by `006`, complete parser orchestration by `007`, and answer assembly by `009`. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 59, Integration 52, Architecture 2. |
| 2026-05-26 | `004-third-party-data-provider-abstraction` | Completed | Added Application financial-provider/health/raw-payload/error and batch quote contracts; Infrastructure raw payload persistence; deterministic mock adapter with live/previous-trading-day fallback; configurable typed HTTP adapter with configuration-based credentials, logging/error mapping, timeout/retry/circuit-breaker handling; and DI/configuration wiring. Ingestion processing is assigned to `005`, and protected provider-health HTTP operations to `012`. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 59, Integration 57, Architecture 2. |
| 2026-05-27 | `005-data-ingestion-and-normalization` | Completed | Verified worker/RabbitMQ data-sync consumption boundary and completed build wiring; added PostgreSQL EF migrations for normalized ingestion tables and required raw provider payload storage; verified raw-before-normalization behavior, normalized symbol/statement/monthly upserts, sync run status/failure persistence, idempotent repeated consumption, and derived-metric recalculation request enqueueing with new integration tests. Status HTTP endpoints remain owned by `012`; deterministic calculation execution remains owned by `006`. `dotnet restore src/backend/FinancialCopilot.sln` and `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 59, Integration 61, Architecture 2. |
| 2026-05-27 | `006-derived-metrics-engine` | Completed | Added deterministic registered calculator strategies for quarterly net-profit growth, monthly sales YoY/MoM growth, TTM sales, TTM earnings, TTM EPS, P/E, and P/S; extended semantic definitions/policies for required dependencies; added normalized metric input strategies, application calculation/recalculation command contracts, persisted derived observations with policy/version/source/dependency evidence, and PostgreSQL migration. Tests cover missing/zero denominators, quote as-of/source retention, independent calculator registration, and ingestion-to-persisted-derived-metric execution. `dotnet restore src/backend/FinancialCopilot.sln` and `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 66, Integration 63, Architecture 2. |
| 2026-05-27 | `014-ai-model-provider-abstraction` | Completed | Added provider-neutral Application contracts for completions, structured output, tools, streaming, embeddings, health, capability discovery, routing, and normalized execution usage facts; capability-based tenant/residency/local policy resolver; schema validation with audited fallback attempts; Infrastructure configuration, deterministic fake adapter, hosted contracted-transport adapter boundary, Ollama local adapter, contract-pending Abravran boundary, and metadata-only telemetry sink. Concrete hosted vendor transport activation awaits an approved selected-provider contract; AI facade/scanner orchestration is owned by `007`, Billing handoff by `010`, and expanded trace persistence by `018`. `dotnet restore src/backend/FinancialCopilot.sln` and `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 71, Integration 67, Architecture 3. |
| 2026-05-27 | `018-ai-observability-and-telemetry` | Completed | Added Application observability contracts: `AiErrorCategory` enum (8 domain categories), `PromptRedactionPolicy` with DefaultSafe (no-capture default), `PromptTrace`, `ToolExecutionTrace`, `ProviderLatency`, `TokenUsage`, `CostTelemetry`, `AiExecutionTrace`, `WorkflowTelemetry`, and `IAiWorkflowTelemetrySink` interface. Infrastructure: OpenTelemetry-compatible `AiObservabilityActivitySource` static class with `FinancialCopilot.AI` ActivitySource, span/attribute name constants, and span factory methods; `LoggingAiWorkflowTelemetrySink` using Activity.Current enrichment and structured ILogger output with privacy-safe redaction enforcement; enhanced `LoggingAiExecutionTelemetrySink` with token/cost/cache/retry structured fields. DI registration for `IAiWorkflowTelemetrySink`. Advanced dashboard sinks and OTel SDK exporters remain incremental; facade orchestration is assigned to `007`. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 84, Integration 83, Architecture 3. |
| 2026-05-27 | `007-natural-language-scanner-parser` | Completed | Implemented Application contracts: `ScannerQueryPlan`, `ScannerCondition`/`ScannerMetricReference` with filter-origin and policy-version evidence, `ScannerQueryPlanValidator`, `IScannerQueryParser`; AI orchestration contracts (`IAiIntentDetector`, `IAiQueryOrchestrationService`, `IBillingFacadeHook`); Conversation/Message repository interfaces and DTOs. `LlmAiIntentDetector` (Scanner/Clarification/Unknown classification), `LlmScannerQueryParser` (LLM proposes user terminology; backend resolves canonical MetricCode via `IMetricAliasResolver` with BCP-47 normalization; handles Resolved/Ambiguous/NotFound; 10-column overflow guard; PolicyVersion v1), `ScannerQueryPlanValidator`. `AiQueryOrchestrationService` orchestrates intent→parse→persist with `IBillingFacadeHook` reservation/finalization; `NoOpBillingFacadeHook` for Phase 1. Infrastructure: `ConversationDbContext` with `ConversationRow`/`MessageRow`, EF migration `InitialConversations`, `ConversationRepository`/`MessageRepository`. `AiFacadeController` implements `POST /api/ai/v1/query`, `GET /api/ai/v1/conversations`, `GET /api/ai/v1/conversations/{id}`, `GET /api/ai/v1/conversations/{id}/messages`. Tests: 8 unit tests (plan validation, English/Persian alias resolution, unrecognized terms, column limit, filter-origin), 6 integration tests (end-to-end query, conversation history, multi-turn). `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 96, Integration 90, Architecture 3. |
| 2026-05-27 | `008-scanner-execution-engine` | Completed | Implemented Application contracts: `IScannerExecutionService`, `IScannerResultColumnPolicy`, `IScannerResultRanker`, `IMarketQuoteResolver`; DTOs: `ScannerTableColumn/Cell/Row`, `ScannerExecutionFacts`, `ScannerTableResult`, `ScannerExecutionRequest`. `ScannerResultColumnPolicy` builds default (Symbol/Company/Price/Change%/MarketCap) plus condition-metric and user-requested columns, capped at 10. `ScannerResultRanker` scores by how strongly values beat thresholds, ties broken alphabetically. Infrastructure: `EfCoreScannerExecutionService` executes AND-condition plan against `FinancialIngestionDbContext.DerivedMetrics`, resolves market quotes in batch via `IMarketQuoteResolver`, surfaces `Live`/`PreviousTradingDay`/`Persisted`/`Missing` cell freshness status, and emits `ScannerExecutionFacts`; `ProviderMarketQuoteResolver` wraps `IMarketDataProvider`. `AiQueryOrchestrationService` extended with `IScannerExecutionService`; calls execution only when parse succeeds and no clarification required. `AiQueryResponse`/`AiQueryHttpResponse` extended with `ScannerTableResult`/`ScannerTableResponse` (columns, rows, executionFacts, warnings). `AiFacadeApiFactory` made non-sealed; adds in-memory `FinancialIngestionDbContext` so existing AI facade tests keep passing without real PostgreSQL. Tests: 8 unit tests (column policy caps, dedup, metric columns, ranker order, missing-value row handling); 6 integration tests (PE<6 returns 2 matching symbols, default columns include PE_TTM, LIVE symbol has Live price freshness, FALLBACK has PreviousTradingDay freshness, dual-condition AND filter returns only 1 match, execution facts counts). `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 104, Integration 96, Architecture 3. |

## Agent Handoff Rule

At the start of an implementation turn, the agent must:

1. Read this checklist, `specs/README.md`, and the selected story/tasks.
2. Review current code and existing uncommitted changes before implementation.
3. Update only the selected item to `[~]` while it is actively being worked on.
4. Implement and verify the story.
5. Change `[~]` to `[x]` and add completion evidence only after meeting the completion gate.
