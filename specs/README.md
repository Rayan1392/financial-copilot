# Specs Index

Each subfolder contains one user story and its implementation tasks. The numbered folders identify capabilities, not a strict implementation sequence; the dependency map below governs sequencing where capabilities overlap.

## Implementation Control File

Implementation agents must use [implementation-checklist.md](./implementation-checklist.md) as the ordered execution and completion ledger. Read the selected story and task file from this index, but mark work in the checklist only after its completion gate and verification evidence are satisfied.

---

## Phase 1 Scanner MVP Specs

1. `001-project-foundation`
2. `002-auth-and-tenancy`
3. `003-financial-domain-model`
4. `004-third-party-data-provider-abstraction`
5. `005-data-ingestion-and-normalization`
6. `006-derived-metrics-engine`
7. `007-natural-language-scanner-parser` — AI Query Orchestration and internal Scanner Tool parsing
8. `008-scanner-execution-engine`
9. `009-explainable-results`
10. `010-usage-metering-and-billing-readiness`
11. `011-cache-and-performance`
12. `012-admin-data-operations`
13. `013-billing-and-credits-domain` — unified SaaS organization and direct-consumer billing bounded context
14. `014-ai-model-provider-abstraction` — hosted and local LLM/provider-neutral execution contracts

---

## Platform Evolution Specs

These specifications extend the AI-native Financial Intelligence Platform architecture without expanding the Phase 1 Scanner MVP unless a capability is explicitly promoted into delivery scope:

15. `015-financial-semantic-layer` — versioned metric ontology, aliases, formulas, policies, and extensible calculation registration
16. `016-derived-feature-foundation` — reproducible feature snapshots and asynchronous feature-computation contracts
17. `017-ai-evaluation-and-regression` — internal datasets, baselines, prompt/workflow evaluation, and regression analysis
18. `018-ai-observability-and-telemetry` — correlated AI workflow, provider, tool, cost, and latency telemetry
19. `019-conversation-memory-strategy` — consent-aware memory scope, consent service, memory audit, and memory-context injection beyond Phase 1 conversation persistence

---

## AI Orchestration and Intelligence Specs

These specs evolve the AI orchestration layer from the Phase 1 manual pipeline to a production-grade agent/workflow model and add new query intent types through the single AI facade endpoint:

- `045-symbol-metric-point-lookup` — answer direct questions about a specific symbol's metric value ("PE حفاری چقدر است؟") using `SymbolLookup`, `ISymbolLookupParser`, `CompanyResolverService`, and `ISymbolMetricLookupService`. Post-068 lookup is `Companies -> ExternalCompanyId -> DerivedMetrics`; monthly-sales intents route to the monthly-sales snapshot renderer, not generic `REVENUE`.
- `046-dynamic-metric-alias-learning` — replace code-only alias expansion with a PostgreSQL-backed dynamic alias layer; unresolved user metric terms are logged, grouped into candidates, validated, and promoted to governed aliases. The learning loop may add aliases only; it must never create metric formulas, calculators, or SQL from LLM output.
- `047-microsoft-agent-framework-orchestration-v2` — migrate AI query orchestration to a Microsoft Agent Framework V2 workflow with Agents, Workflow steps, and narrow tool adapters over existing Application services; preserve `POST /api/ai/v1/query`, V1 rollback by configuration, billing reservation/finalization, provider-neutral model resolution, telemetry, and evaluation compatibility.
- `049-llm-provider-switching-deepseek-support` — add DeepSeek as a second LLM provider behind the existing `014` provider-neutral contracts; configuration-only switching between OpenAI and DeepSeek without changing business logic, billing behavior, or frontend integrations.
- `056-native-microsoft-agent-framework-workflows` — replace the manual V2 orchestration chain with native Microsoft Agent Framework Workflow primitives for step-based execution, workflow state, telemetry, and future Deep Research / Multi-Agent expansion while preserving existing API contracts and V1 rollback.

---

## Query Intelligence and Platform Learning Specs

These specs capture and act on unanswered queries to continuously improve metric coverage, data freshness, and AI intent recognition:

- `028-missing-answer-feedback-pipeline` — capture every unanswered or partially answered query (metric gap, calculation gap, data coverage gap, data quality, parser limitation) as classified structured feedback in PostgreSQL; fire-and-forget during query execution; admin visibility; feeds future catalog expansion and ingestion prioritization.
- `046-dynamic-metric-alias-learning` — (also listed under AI Orchestration) uses the feedback pipeline from `028` as its learning signal source.

---

## Data Provider Implementation Specs

These specs implement concrete financial-data providers behind the `004` abstraction and feed the
`005` normalized PostgreSQL tables. The authoritative source model after spec `051`:

| Logical vendor | Physical source | Source mode | Role |
|---|---|---|---|
| NoavaranAmin | `NoavaranArchiveSql` | ArchiveOneTime | Historical/archive SQL snapshot imported once and frozen |
| NoavaranAmin | `NoavaranCurrentApi` | CurrentIncremental | Current data from Shamsi 1403 onward via NADPCO HTTP API |
| CyclicalWaves | `CyclicalWavesApi` | ExternalSnapshot | Financial statements, ratios, monthly observations; no company catalog writes |
| Tsetmc | `StockMarketDb` | MigrationBridge | Current bridge for trading statistics; being migrated |
| Tsetmc | `TsetmcWebService` | DirectFeed | Direct TSETMC ASMX ingestion (cutover active, `PrimarySourceName = TsetmcWebService`) |

### CyclicalWaves

- `020-cyclicalwaves-data-provider` — CyclicalWaves HTTP API provider for financial statements, monthly observations, and valuation ratios. **No longer writes to the `Companies` catalog** (change request, order 48); resolves linkage through existing NADPCO-backed company/symbol metadata.
- CyclicalWaves sales and valuation fields are provider-precomputed company-level facts. Monetary sales fields are source-unit Rials and must be persisted as-is under passthrough/source policies; PE/PS ratios are unitless and must also be stored as-is. Do not apply Noavaran million-Rial conversion or raw line-item aggregation rules to CyclicalWaves values.
- `070-cyclicalwaves-monthly-sales-average-snapshot` — CyclicalWaves-only monthly/latest sales snapshots default to `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, YTD, and YTD-to-previous-month, with the mandatory Persian average header `متوسط فروش ۱۲ ماهه`; explicit same-period requests use prior-year same-month sales instead. This does not modify Noavaran spec 069 behavior.
- `071-cyclicalwaves-derivedmetrics-full-snapshot-persistence` — CyclicalWaves-only persistence fix so all supported `/api/custom-filtering/ticker/{ticker}` sales, profit, margin, monthly, and valuation snapshots reach `DerivedMetrics` with Rials passthrough evidence instead of only the narrow monthly-sales subset.

### Noavaran Amin Archive (NoavaranArchiveSql / CodalDB)

Historical SQL Server source. One-time import only; recurring sync targets the current API, not this source. Architecture test enforces no recurring hosted worker against the archive source.

1. `021-codaldb-provider-foundation` — read-only SQL gateway returning `ProviderRawPayload`, health, resilience, options/secrets, DI.
2. `022-codaldb-company-symbol-sync` — company/symbol normalization + canonical-symbol linkage (`InstCode`→ISIN→`CoTSESymbol`→`CompanySymbol`; never `InstrumentRef`).
3. `023-codaldb-financial-statement-ingestion` — curated income/balance line items, canonical audited/consolidated/restated variant selection, fiscal-period mapping.
4. `024-codaldb-monthly-activity-ingestion` — monthly production/sales normalization.
5. `025-codaldb-precomputed-ratios` — curated vendor-precomputed ratios persisted as scannable derived-metric observations (`codal-ratio-source-v1`).
6. `026-codaldb-derived-growth-metrics` — engine-derived YoY/QoQ growth for revenue, net profit, operating profit, gross profit, EPS, EBIT, equity + vendor-precomputed growth ratios.
7. `027-codaldb-scheduled-sync-and-recalculation` — incremental sync orchestrator (watermark on `ModifiedDateTime`) + provider-agnostic `MetricRecalculationProcessor` outbox drainer so derived metrics are precomputed after ingestion.
8. `029-financial-statement-schema-fix` — additive `StatementType` column on `FinancialStatements`, corrected unique key `(ProviderName, ExternalStatementId, StatementType)`, and fixes to all three statement normalizers (CyclicalWaves, CodalDb, configured HTTP provider) to prevent `PeriodType`/`StatementType` confusion.
9. `051-noavaran-archive-and-current-api-strategy` — correct source model: rename `CodalDb` → `NoavaranArchiveSql` and `NadpcoApi` → `NoavaranCurrentApi` throughout; add `ProviderSources` catalog, `LogicalVendor`/`PhysicalSource`/`SourceMode` provenance on `Companies`/`FinancialStatements`/`MonthlyReports`/`ProviderSyncRuns`; add `ISourceFreshnessReader`; establish 1403 Shamsi archive/current boundary.
10. `052-noavaran-archive-one-time-import` — DataAdmin lifecycle for the frozen archive: dry-run, import, validate, freeze, re-import actions with `ArchiveImportRuns` and `ArchiveFreezeStates` persistence; `ArchiveCoverageReader`; additive `SourceProvider` on scanner citations.

### NADPCO HTTP API (NoavaranCurrentApi)

Authenticated HTTP source at `https://data3.nadpco.com`. Coexists with the archive source and is the only recurring fundamentals sync target from Shamsi 1403 onward. **`NADPCO` is the authoritative company catalog source**; the `NoavaranEligibleCompanies` PostgreSQL view scopes per-company requests to equities only (`PrecedencyRight = 0`, markets: بورس/فرابورس/پایه).
Noavaran monthly activity is raw product/service line-item data. Monetary monthly sales values are source-unit million Rials and must be aggregated and normalized to the platform canonical monetary unit during ingestion/recalculation, never during AI query execution.

1. `038-nadpco-api-provider-foundation` — token authentication, secrets, resilience, health, raw payload capture, and provider routing.
2. `039-nadpco-api-company-catalog-sync` — `/api/v3/BaseInfo/Companies` normalization and cross-provider canonical symbol linkage. Authoritative company catalog: clean-slate backfill via `CompanyCatalogCleanSlate` DataAdmin operation; non-destructive daily `CompanyCatalogRefresh` scheduled run. Every NADPCO company field persisted in `Companies` or related normalized tables; no field remains evidence-only.
3. `040-nadpco-api-financial-statement-sync` — balance sheet, income statement, and cash flow API ingestion with curated item mappings and corrected `StatementType`.
4. `041-nadpco-api-fundamental-index-sync` — curated vendor-precomputed fundamental indexes persisted as source-marked scannable metrics.
5. `042-nadpco-api-monthly-activity-sync` — product-sales and service-sales monthly activity normalization.
6. `043-nadpco-api-sync-orchestration` — bounded full/incremental orchestration, per-dataset progress, overlap reconciliation, `CompanyCatalogCleanSlate`/`CompanyCatalogRefresh` run modes, DataAdmin operations, telemetry, and cache invalidation.
7. `044-nadpco-api-scheduled-sync-worker` — automatic scheduled incremental synchronization through the bounded orchestration pipeline; disabled by default pending vendor token confirmation; daily `CompanyCatalog` refresh included.
8. `050-nadpco-api-all-fundamental-index-catchup` — DataAdmin-only catch-up mode for all local NADPCO company fundamental indexes from Shamsi 1403 through 1405 using `CompanyFundamentalIndex/Values`; all vendor observations persisted to `NadpcoFundamentalIndexObservations`; only reviewed indexes promoted to governed `DerivedMetrics`.
9. `053-noavaran-current-api-ingestion` — current API ingestion from Shamsi 1403 onward: gap report (`GET /api/v1/admin/noavaran-current/gaps`), backfill with optional `fromShamsiYear` override, health endpoint; relies on the existing NADPCO sync pipeline (no second ingestion path).
10. `057-nadpco-monthly-activity-freshness-and-sales-lookup` — two-phase monthly-activity acquisition: Phase A DataAdmin-only reverse backfill walking Shamsi months newest-first with durable per-month progress and a completion marker; Phase B steady-state scheduled refresh of the previous Shamsi month only after the marker exists; governed `MONTHLY_SALES`, `MONTHLY_SALES_QUANTITY`, `MONTHLY_PRODUCTION_QUANTITY`, `MONTHLY_SALES_RATE` metrics with Persian aliases so AI answers monthly sales questions from normalized data. Monthly production/sales lookup responses intentionally omit `LATEST_PRICE` and `DAILY_CHANGE_PCT`.
11. `059-monthly-activity-output-type-segmentation` — fetch all 5 `outputTypeId` variants (0–4) per company-month from `ProductSales`; persist each as a separate `MonthlyReports` row with `OutputType` column; `MonthlyActivityOutputTypeResolver` routes AI queries to single-month or YTD rows, and grouped monthly production/sales views suppress market quote context.

---

## Trading Statistics Implementation Specs

These specs add trading statistics from TSE market data sources into the normalized time-series tables (`IntradayTradeSnapshots`, `DailyInstrumentTrades`, `IntradayIndexSnapshots`, `DailyIndexSnapshots`, `LatestMarketQuotes`).

- `030-stockmarketdb-trading-statistics-sync` — registered TSE instruments, intraday trade snapshots, daily instrument trades (`Tse.TradeRefined`), intraday indices, historical daily-index backfill (`Tse.IndexNew`), and PostgreSQL latest-quote projections from the `StockMarketDB` MS SQL Server bridge.
- `054-stockmarketdb-to-tsetmc-direct-feed-migration` — planned migration from the `StockMarketDB` bridge to direct TSETMC ASMX ingestion. Phase 1: bridge stabilization + provenance stamping + `IMarketQuoteSourcePriority` config. Phase 2: `TsetmcWebServiceClient` (raw SOAP 1.1, no WCF), direct feed into canonical tables with `TsetmcWebService` provenance. Phase 2b: `TsetmcPollingWorker` on Iranian market calendar (intraday every 60 s during 09:00–12:30 IRST, end-of-day after market close, Sat–Wed only). Phase 3: parallel validation + `MarketQuoteMismatches`. Phase 4: config-driven cutover (`PrimarySourceName = TsetmcWebService`, set to `StockMarketDb` to roll back).
- `064-trading-instrument-unification` — `TradingInstruments` promoted to a single provider-neutral dimension owned exclusively by the TSETMC feed. `StockMarketDbSyncService.PersistInstrumentsAsync` converted to no-op; `TsetmcDirectFeedSyncService` auto-creates stub rows for unseen InsCodes so no trade records are silently dropped; `ProviderName` filter removed from instrument map lookups.

---

## Frontend Integration Specs

These specs replace the Lovable/TanStack prototype's Supabase chat persistence and canned financial responses with the implemented .NET backend boundaries:

1. `031-frontend-authenticated-api-bridge` — replace Supabase authentication with backend-owned ASP.NET Core Identity, JWT access tokens, refresh-token rotation, permission policies, Billing-owned plan capabilities, and AI-credit enforcement.
2. `032-frontend-chat-conversation-cutover` — route chat, history, structured explainability, and usage results through the single AI facade; remove `generateMockReply` and local credit decrement.
3. `033-frontend-usage-watchlist-market-summary` — replace sidebar credits, watchlist mocks, and context-panel mocks with normalized backend projections from `LatestMarketQuotes` and `GET /api/v1/usage/me`.
4. `034-frontend-assisted-query-metadata` — populate optional assisted filter controls from governed metadata; prompt still goes only through the AI facade.
5. `036-frontend-local-api-connectivity` — align browser/server API base URL configuration, local credentialed CORS origins, and auth smoke verification.
6. `037-frontend-admin-panel` — permission-aware React administration panel over the Admin Management API.
7. `048-frontend-ai-orchestration-v2-awareness` — frontend awareness of MAF V2 orchestration metadata: diagnostics display, V1/V2 rollout toggle, evaluation mode, and orchestration version badge; preserves existing chat experience.
8. `055-frontend-data-management-console` — Data Management Console for operating archive imports, Noavaran current API syncs, StockMarketDB/TSETMC bridge and direct feed, provider health, run history, and reconciliation from a protected admin UI.
9. `058-live-data-sync-monitor` — live monitoring page: backend `PollingDataSyncActivityMonitor` aggregates all provider run-state rows into a snapshot endpoint and an SSE stream; frontend subscribes and shows live run cards, record counts, error counts, and duration without page refresh.

---

## Administration Specs

These specs expose controlled backend administration surfaces over existing Identity, tenancy, and Billing boundaries:

1. `035-admin-identity-and-entitlement-management` — manage users, roles, permission mappings, tenant memberships, plans, plan capabilities, subscriptions, credit adjustments, immutable usage-ledger reads, and security/Billing audit visibility with granular permission policies.
2. `037-frontend-admin-panel` — expose the implemented admin contracts through a protected, permission-aware React administration experience without moving domain rules into the UI.

---

## Comprehensive Analysis Specs

These specs add a separate bounded context for CyclicalWaves تحلیل جامع (Comprehensive Analysis) content — independent of the financial data ingestion pipeline:

- `065-cyclicalvawes-comprehensive-analysis-sync` — Bearer-token auth client with 10-day token cache and auto-refresh on 401; full sync (all pages × all 7 allowed tag categories); daily Hangfire incremental sync (`filter[from_date]=yesterday`); `ComprehensiveAnalyses`, `ComprehensiveAnalysisTags`, `ComprehensiveAnalysisCategories`, `ComprehensiveAnalysisSyncLog` persistence; `PlainTextSummary` column populated at write time (HTML stripped); health check.
- `066-comprehensive-analysis-ai-query` — close the gap between stored content and AI-answerable questions: `ComprehensiveAnalysis` intent type; `IComprehensiveAnalysisQueryParser` (LLM structured output → symbol names, topic tags, date); `IComprehensiveAnalysisQueryRepository` (EF Core tag-based retrieval, 2 000-char summary cap); `QueryComprehensiveAnalysisUseCase`; `ComprehensiveAnalysisToolAdapter` registered in MAF V2 tool registry; V1 orchestration branch for rollback; `ComprehensiveAnalysisResult` with `DataCitation` per item; confidence score; missing-answer feedback; message persistence for conversation reload.

---

## Coherence Rules

- The React UI submits user Messages only through `POST /api/ai/v1/query`; scanner parser/execution, symbol lookup, and comprehensive analysis operations are internal Application capabilities — never public endpoints.
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
- `019` separates optional consent-aware memory from required Conversation and Message persistence.
- `028` feedback collection is fire-and-forget and must never alter the query response or throw exceptions into the scanner/lookup execution path.
- `029` fixes the `StatementType` column and normalizer bugs; all three normalizers (CyclicalWaves, CodalDb/NoavaranArchiveSql, configured HTTP provider) must use the corrected `StatementType` field consistently.
- `031` establishes backend-owned web identity and permission authorization before frontend calls protected .NET endpoints. Billing remains the source of truth for subscription-plan capabilities, quotas, and AI-credit reservation.
- `032` removes mock chat behavior and keeps every user prompt on `POST /api/ai/v1/query`.
- `033` exposes read models for UI widgets; it must return unavailable values honestly instead of fabricating unsupported market analytics.
- `034` may add discovery controls, but it never exposes scanner execution as a frontend API.
- `036` keeps local browser and server API routing aligned without changing Identity domain behavior or weakening credentialed CORS origin restrictions.
- `035` exposes controlled admin APIs over `031` Identity and `013` Billing boundaries. It does not duplicate entitlement logic, mutate wallet projections directly, or authorize by hardcoded role or plan names.
- `037` consumes `035` through the existing `031` frontend auth bridge. Frontend permission checks control navigation and actions for usability only; backend policies remain authoritative.
- `038`–`044`, `050`, `053`, `057`, `059` add `NoavaranCurrentApi` as the recurring financial-data source. Remote payload DTOs remain Infrastructure concerns; normalized PostgreSQL rows, governed metric semantics, deterministic recalculation, and scanner reads remain provider-neutral. All per-company current-API requests target only `NoavaranEligibleCompanies` (`PrecedencyRight = 0`, بورس/فرابورس/پایه); the company-catalog sync stays unscoped.
- `044` schedules automatic NADPCO incremental synchronization only by invoking the bounded orchestration from `043`; it must not introduce a second ingestion, normalization, recalculation, or scanner-cache invalidation path.
- `045` adds a `SymbolLookup` intent branch to the existing AI facade; it must not add a new public endpoint, duplicate billing accounting logic, or introduce a separate conversation persistence path. The scanner screener path (`007`/`008`) remains unaffected.
- `046` adds aliases only; it must never create metric definitions, formulas, calculators, SQL, or billing behavior from user prompts or LLM output.
- `047`/`056` introduce MAF V2 orchestration as a backward-compatible layer; existing Application services (`IScannerQueryParser`, `IScannerExecutionService`, `ISymbolLookupParser`, `ISymbolMetricLookupService`, `IExplainableAnswerBuilder`, `IConfidenceScoreCalculator`, `IBillingFacadeHook`) remain the source of truth; the MAF framework is isolated to Infrastructure/composition and must not be referenced from Domain, Billing, or scanner deterministic services.
- `049` adds DeepSeek behind the existing `014` provider-neutral contracts; it must not change business logic, billing behavior, or scanner/lookup deterministic execution.
- `051` is the source-model authority: `NoavaranArchiveSql` is frozen archive; `NoavaranCurrentApi` is the only recurring fundamentals target. No spec may introduce a recurring hosted worker that ingests from `NoavaranArchiveSql`; the architecture test `NoavaranArchiveSource_IsNotDrivenByARecurringHostedWorker` enforces this.
- `054`/`064` migrate market data ownership: `TradingInstruments` is owned exclusively by the TSETMC feed; `StockMarketDb` no longer writes to `TradingInstruments`; `LatestMarketQuotes` source is config-driven via `IMarketQuoteSourcePriority`.
- `065`/`066` form a pair: `065` owns the sync pipeline and persistence schema; `066` owns the AI query boundary. `066` must not duplicate the upsert or sync logic; `065` must populate `PlainTextSummary` and expose `SyncedAt` in the read model (see `065-amendment.md` in `066`). The `ComprehensiveAnalysisToolAdapter` is an Infrastructure concern; `QueryComprehensiveAnalysisUseCase` and `IComprehensiveAnalysisQueryRepository` are Application concerns. LLM must never receive raw HTML.

---

## Recommended Delivery Dependencies

```text
001 Project Foundation
  -> 002 Authentication and Tenant Context
  -> 013 Billing and Credits Domain Foundation
  -> 003 Financial Domain Model
     -> 015 Financial Semantic Layer
     -> 004 Third-Party Data Provider Abstraction
        -> 005 Data Ingestion and Normalization
           -> 006 Derived Metrics Engine
  -> 014 AI Model Provider Abstraction
     -> 018 AI Observability and Telemetry
     -> 007 AI Query Orchestration and Scanner Parsing
        -> 008 Scanner Execution Engine
           -> 009 Explainable Results
              -> 010 Phase 1 Usage Metering Integration
                 -> 011 Cache and Performance
                    -> 012 Admin Data Operations
        -> 045 Symbol Metric Point Lookup
        -> 047 Microsoft Agent Framework V2
           -> 056 Native MAF Workflows
        -> 049 LLM Provider Switching (DeepSeek)
  -> 028 Missing-Answer Feedback Pipeline
  -> 046 Dynamic Metric Alias Learning
```

Financial data provider chain (coexisting sources, after MVP):

```text
003 + 004 + 005
  -> 021-027  Noavaran Archive (CodalDB) — one-time import
  -> 051      Noavaran Source Strategy (rename + provenance)
     -> 052   Noavaran Archive One-Time Import lifecycle
     -> 038-044 + 050 + 053 + 057 + 059  NADPCO Current API
  -> 020      CyclicalWaves (financial data only, no catalog writes)
  -> 029      Financial Statement Schema Fix
  -> 030      StockMarketDB Trading Statistics
     -> 054   TSETMC Direct Feed Migration
     -> 064   Trading Instrument Unification
```

Frontend and administration (after `031`):

```text
031 Backend Identity
  -> 032 Chat Cutover
  -> 033 Usage / Watchlist / Market Summary
  -> 034 Assisted Query Metadata
  -> 036 Local API Connectivity
  -> 035 Admin Management APIs
     -> 037 Admin Panel
  -> 048 AI Orchestration V2 Awareness
  -> 055 Data Management Console
     -> 058 Live Data Sync Monitor
```

Comprehensive Analysis content:

```text
001 + 002 + 012
  -> 065 Comprehensive Analysis Sync
     -> 066 Comprehensive Analysis AI Query (depends also on 047/056, 009, 028)
```

Future platform capabilities:

```text
003 + 015 + 006
  -> 016 Derived Feature Foundation

014 + 007/009
  -> 017 AI Evaluation and Regression
  -> 018 AI Observability and Telemetry

Conversation persistence
  -> 019 Consent-Aware Memory Strategy
```

Payment gateway automation, invoice delivery automation, deep research, portfolio tools, Elasticsearch/OpenSearch, and vector retrieval remain later increments unless separately promoted into delivery scope.

# Monthly Production and Sales Trend Specs

This package adds two specs for turning Noavaran Amin monthly production/sales history into an AI-readable trend layer and a chart-ready AI response.

## Specs

| Spec | Purpose |
|---|---|
| `076-nadpco-monthly-activity-trend-snapshot` | Persist deterministic company-month production/sales trend snapshots from Noavaran monthly activity data. |
| `077-ai-monthly-production-sales-trend-query` | Route Persian trend/chart questions to a dedicated AI provider and return chart-ready annual comparison data. |

## Implementation Order

1. Implement spec 076 first.
2. Backfill snapshots from 1403 onward.
3. Implement spec 077 after the snapshot table is available.
4. Add frontend rendering later using the `monthlyActivityTrendChart` structured content block.

## Core Rule

The AI must not calculate historical production/sales trends from raw Noavaran line items at query time. It should read one derived trend table, plus company resolution if needed.
