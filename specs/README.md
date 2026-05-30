# Specs Index

Each subfolder contains one user story and its implementation tasks. The numbered folders identify capabilities, not a strict implementation sequence; the dependency map below governs sequencing where capabilities overlap.

## Implementation Control File

Implementation agents must use [implementation-checklist.md](./implementation-checklist.md) as the ordered execution and completion ledger. Read the selected story and task file from this index, but mark work in the checklist only after its completion gate and verification evidence are satisfied.

## Phase 1 Scanner MVP Specs

1. `001-project-foundation`
2. `002-auth-and-tenancy`
3. `003-financial-domain-model`
4. `004-third-party-data-provider-abstraction`
5. `005-data-ingestion-and-normalization`
6. `006-derived-metrics-engine`
7. `007-natural-language-scanner-parser` - AI Query Orchestration and internal Scanner Tool parsing
8. `008-scanner-execution-engine`
9. `009-explainable-results`
10. `010-usage-metering-and-billing-readiness`
11. `011-cache-and-performance`
12. `012-admin-data-operations`
13. `013-billing-and-credits-domain` - unified SaaS organization and direct-consumer billing bounded context
14. `014-ai-model-provider-abstraction` - hosted and local LLM/provider-neutral execution contracts

## Platform Evolution Specs

These specifications extend the AI-native Financial Intelligence Platform architecture without expanding the Phase 1 Scanner MVP unless a capability is explicitly promoted into delivery scope:

15. `015-financial-semantic-layer` - versioned metric ontology, aliases, formulas, policies, and extensible calculation registration
16. `016-derived-feature-foundation` - reproducible feature snapshots and asynchronous feature-computation contracts
17. `017-ai-evaluation-and-regression` - internal datasets, baselines, prompt/workflow evaluation, and regression analysis
18. `018-ai-observability-and-telemetry` - correlated AI workflow, provider, tool, cost, and latency telemetry
19. `019-conversation-memory-strategy` - future consent-aware memory beyond Phase 1 Conversation persistence

## Data Provider Implementation Specs

These specs implement concrete financial-data providers behind the `004` abstraction and feed the
`005` normalized PostgreSQL tables. They follow the spec format but sit outside the ordered MVP
delivery checklist (they are added as data sources become available).

- `020-cyclicalwaves-data-provider` — CyclicalWaves HTTP API provider (Tehran Stock Exchange).
- **CodalDB** (MS SQL Server, vendor: Noavaran Amin Data Processing Company) — schema reference
  in [../docs/codaldb-datasource.md](../docs/codaldb-datasource.md), delivered as a dependency
  chain that **coexists** with CyclicalWaves:
  1. `021-codaldb-provider-foundation` — read-only SQL gateway returning `ProviderRawPayload`,
     health, resilience, options/secrets, DI.
  2. `022-codaldb-company-symbol-sync` — company/symbol normalization + canonical-symbol linkage
     (`InstCode`→ISIN→`CoTSESymbol`→`CompanySymbol`; never `InstrumentRef`).
  3. `023-codaldb-financial-statement-ingestion` — curated income/balance line items, canonical
     audited/consolidated/restated variant selection, fiscal-period mapping.
  4. `024-codaldb-monthly-activity-ingestion` — monthly production/sales normalization.
  5. `025-codaldb-precomputed-ratios` — curated vendor-precomputed ratios persisted as scannable
     derived-metric observations (`codal-ratio-source-v1`).
  6. `026-codaldb-derived-growth-metrics` — engine-derived YoY/QoQ growth for revenue, net
     profit, operating profit, gross profit, EPS, EBIT, equity + vendor-precomputed growth ratios.
  7. `027-codaldb-scheduled-sync-and-recalculation` — nightly incremental (watermark on
     `ModifiedDateTime`) sync orchestrator + the missing derived-metric recalculation outbox
     processor, so growth metrics are precomputed and the scanner reads them at query time.

## Coherence Rules

- The React UI submits user Messages only through `POST /api/ai/v1/query`; scanner parser/execution operations are internal Application capabilities.
- `003` defines financial concepts and calculation policy primitives; `006` implements and persists derived metric calculations.
- `004` implements provider clients and provider-health capability; `012` exposes protected admin operations that invoke that capability.
- `005` implements ingestion consumers and synchronization state; `012` exposes admin commands that enqueue/inspect those jobs.
- `013` owns billing domain rules and full billing evolution; `010` is the Phase 1 AI-query metering slice implemented against `013` contracts.
- `009` owns explainable answer presentation and backend Confidence Score; `010`/`013` own usage and charging metadata displayed beside the same answer.
- `011` may improve latency with cache hits, but it must still execute Billing accounting policy and return freshness/usage metadata.
- `004` concerns financial/market data providers; `014` concerns LLM/embedding model providers and keeps vendor SDKs outside business use cases.
- `007` invokes model capabilities through `014` interfaces; model providers never own scanner validation, confidence calculation, or Billing decisions.
- `015` extends `003` metric vocabulary and governs the semantic definition/version contracts used by deterministic calculations in `006`; metric growth must not create hardcoded parser or formula-routing logic.
- `007` resolves user metric language through `015` semantic definitions; `009` cites resolved metric/policy versions in explanations.
- `016` consumes normalized data and versioned metric results for future features; it does not make advanced ranking or ML a Phase 1 prerequisite.
- `017` evaluates parser/orchestration/explanation behavior internally and never becomes a public API dependency.
- `018` observes execution across `014`, orchestration, tools, and Billing; observability is not the accounting source of truth.
- `019` separates optional consent-aware future memory from required Conversation and Message persistence.

## Recommended Delivery Dependencies

```text
001 Project Foundation
  -> 002 Authentication and Tenant Context
  -> 013 Billing and Credits Domain Foundation
  -> 003 Financial Domain Model
  -> 004 Third-Party Data Provider Abstraction
  -> 005 Data Ingestion and Normalization
  -> 006 Derived Metrics Engine
  -> 014 AI Model Provider Abstraction Foundation
  -> 007 AI Query Orchestration and Scanner Parsing
  -> 008 Scanner Execution Engine
  -> 009 Explainable Results
  -> 010 Phase 1 Usage Metering Integration
  -> 011 Cache and Performance
  -> 012 Admin Data Operations
```

Payment gateway automation, invoice delivery automation, deep research, portfolio tools, Codal analysis, Elasticsearch/OpenSearch, and vector retrieval remain later increments unless separately promoted into MVP scope.

Future platform capability delivery can proceed incrementally after or alongside the stable MVP boundaries:

```text
003 Financial Domain Model
  -> 015 Financial Semantic Layer foundation
  -> 006 Derived Metrics Engine integration
  -> 016 Derived Feature Foundation

014 AI Model Provider + 007/009 AI workflows
  -> 017 AI Evaluation and Regression
  -> 018 AI Observability and Telemetry

Conversation persistence
  -> 019 Consent-Aware Memory Strategy
```
