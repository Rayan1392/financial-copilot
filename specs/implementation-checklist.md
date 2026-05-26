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
| [~] | 03 | [013](./013-billing-and-credits-domain/user-story.md) / [tasks](./013-billing-and-credits-domain/tasks.md) | Billing and Credits Domain | Depends on `001`, `002`; establish the isolated Billing bounded context, account model, ledger, pricing, entitlement, and reservation contracts. |

### Stage 2 - Governed Financial Data And Calculation Core

These stories establish canonical financial meaning, normalized source data, and deterministic calculation evidence used by the Scanner Tool.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [ ] | 04 | [003](./003-financial-domain-model/user-story.md) / [tasks](./003-financial-domain-model/tasks.md) | Financial Domain Model | Depends on `001`; define financial entities, periods, invariants, and semantic metric primitives. |
| [ ] | 05 | [015](./015-financial-semantic-layer/user-story.md) / [tasks](./015-financial-semantic-layer/tasks.md) | Financial Semantic Layer and Ontology | Depends on `003`; establish canonical `MetricCode`, bilingual aliases, versioned policies, and registry/strategy calculator contracts before expanding calculated metrics or parser assumptions. |
| [ ] | 06 | [004](./004-third-party-data-provider-abstraction/user-story.md) / [tasks](./004-third-party-data-provider-abstraction/tasks.md) | Third-Party Data Provider Abstraction | Depends on `001`, `003`; isolate financial/market data providers and health/reliability behavior. |
| [ ] | 07 | [005](./005-data-ingestion-and-normalization/user-story.md) / [tasks](./005-data-ingestion-and-normalization/tasks.md) | Data Ingestion and Normalization | Depends on `003`, `004`; ingest and persist normalized reproducible scanner inputs. |
| [ ] | 08 | [006](./006-derived-metrics-engine/user-story.md) / [tasks](./006-derived-metrics-engine/tasks.md) | Derived Metrics Engine | Depends on `003`, `005`, `015`; implement deterministic, versioned calculations through registered metric strategies. |

### Stage 3 - Provider-Neutral AI And Internal Observability Foundation

These stories make AI execution replaceable and traceable before the public query workflow depends on it.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [ ] | 09 | [014](./014-ai-model-provider-abstraction/user-story.md) / [tasks](./014-ai-model-provider-abstraction/tasks.md) | AI Model Provider Abstraction | Depends on `001`; implement provider-neutral AI contracts, deterministic fake provider, capability routing, and normalized execution facts. |
| [ ] | 10 | [018](./018-ai-observability-and-telemetry/user-story.md) / [tasks](./018-ai-observability-and-telemetry/tasks.md) | AI Observability and Telemetry | Depends on `002`, `013`, `014`; implement the minimum OpenTelemetry-compatible correlation/trace contracts needed by AI facade workflows. Advanced dashboards remain incremental. |

### Stage 4 - Scanner MVP Through The Single AI Facade

These stories deliver the user-visible scanner workflow, explainability, accounting integration, performance, and data operations.

| Done | Order | Spec | User story | Dependency / implementation intent |
|---|---:|---|---|---|
| [ ] | 11 | [007](./007-natural-language-scanner-parser/user-story.md) / [tasks](./007-natural-language-scanner-parser/tasks.md) | Natural Language Scanner Parser | Depends on `002`, `013`, `014`, `015`, `018`; implement AI facade orchestration, conversation contracts, intent/tool routing, semantic metric resolution, and validated plans. |
| [ ] | 12 | [008](./008-scanner-execution-engine/user-story.md) / [tasks](./008-scanner-execution-engine/tasks.md) | Scanner Execution Engine | Depends on `006`, `007`; execute validated plans against deterministic data/metrics and produce table-ready results. |
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
| 2026-05-26 | `013-billing-and-credits-domain` | Partially verified | Implemented Billing domain/contracts and EF Core/PostgreSQL persistence for accounts, wallets, reservations, usage/financial ledgers, subscriptions, and invoice profiles. Added atomic/idempotent reservation hold and charge-or-release finalization services, persisted failure reasons, abandoned-reservation expiry recovery, optimistic wallet protection, idempotent currency transaction accounting separate from credits with atomic payment/top-up/invoice-settlement outbox event recording, operation/outcome pricing including configurable reduced partial-completion charges and configured embedding/RAG/background-operation categories, self-service reads, authenticated API-client-scoped organization usage reporting, tenant-scoped `BillingAdmin` wallet/usage/invoice/credit-adjustment APIs, an auditable idempotent usage-refund workflow linked to original charges with cumulative over-refund protection, transactional outbox records for reservation/commit/release/expiry/adjustment/refund transitions, and a transport-neutral pending outbox processor with durable completion/retry diagnostics. Outbox worker scheduling/transport and distributed claiming, AI workflow/facade charging integration, payment gateway/subscription execution, and automated invoice settlement remain. `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed: Unit 38, Integration 47, Architecture 1. |

## Agent Handoff Rule

At the start of an implementation turn, the agent must:

1. Read this checklist, `specs/README.md`, and the selected story/tasks.
2. Review current code and existing uncommitted changes before implementation.
3. Update only the selected item to `[~]` while it is actively being worked on.
4. Implement and verify the story.
5. Change `[~]` to `[x]` and add completion evidence only after meeting the completion gate.
