# Specs Index

Each subfolder contains one user story and its implementation tasks. The numbered folders identify capabilities, not a strict implementation sequence; the dependency map below governs sequencing where capabilities overlap.

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
