# Feature 141 â€” Repository and Alternatives Research

## Research status and evidence

Repository discovery used the indexed `D-Source-TahlilApp-AI` codebase graph and focused source snippets, plus direct reading of README/spec files. The graph was already indexed, so no re-index was required. This is a design investigation; it does not inspect production data or assert that every deployed database has the same migration state as the repository.

Primary code references:

- V2 orchestration dispatch: `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/MicrosoftAgentFrameworkAiQueryOrchestrationService.cs`.
- Native workflow and V2 agent execution: `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, especially `Build`, `ExecuteAgentStepAsync`, response/result handling, and `BuildMonthlyActivityTrendContent`.
- Public AI facade: `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs` and `src/backend/FinancialCopilot.Application/AI/Orchestration/AiOrchestrationContracts.cs`.
- Company resolution: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs` and `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/CompanyResolutionContracts.cs`.
- EF persistence: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs`, `FinancialIngestionRows.cs`, and `FinancialIngestionConfigurations.cs`.
- Trend contracts/use case: `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs`, `CompanyMonthlyActivityTrendSnapshotContracts.cs`, and `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs`.
- Frontend chart path: `src/frontend/src/lib/chat.functions.ts`, `src/frontend/src/components/app/message-list.tsx`, `monthly-activity-trend-chart.tsx`, and image export/view-model modules.
- Semantic dialogue conventions: `specs/117-*` through `specs/123-*`; monthly product source policy: `specs/129-*` and `specs/136-*`; persisted company trend rules: `specs/076-*` and README.

## Findings

1. The runtime already has native MAF V2 orchestration and typed semantic frames; a parallel public endpoint or separate agent facade would duplicate existing orchestration.
2. Current workflow has established direct semantic payload mapping and specialized raw-message branches. An explicit fallback needs careful priority placement and a typed â€œunmatchedâ€ admission state distinct from failure/no-data.
3. The `FinancialIngestionDbContext` exposes a wide financial and market persistence surface, including user/notification operational data. Enumerating DbSets is not a security model. Queryable metadata must use an independently reviewed allowlist and least privilege.
4. Existing monthly trend answer path is already persisted, typed, converted and charted. README explicitly says trend path does not aggregate raw Noavaran line items at response time. Generic query must preserve this route and must not reimplement a competing trend formula.
5. Monthly normalized reports and line items contain useful raw product observations, but feature 129/136 document strict report type, output type, period, product identity, unit and source revision requirements. A generic SQL join cannot ignore those rules. Use specialized read models or publish safe views with those constraints encoded.
6. CompanyResolverService is reusable for current ticker/name identifiers. It returns a resolved company or null; ambiguous fragments return null, and product resolution is separate/company-scoped. Do not represent it as a complete general entity resolution system.
7. `PhaseOneFinancialSemanticCatalog` and Feature 015 already govern financial metric semantics. New database catalog is a physical/schema-and-dimension semantic layer, not a second metric formula authority.
8. Current frontend has known chart-specific content and mapping. Reuse is conditional on shape compatibility; generic chart design is not proven by the current monthly activity trend contract.
9. Feature specs use numbered directories and commonly include story/design/tasks; recent specs vary in filename casing. Feature 141 uses `spec.md`, `design.md`, `plan.md`, `tasks.md`, and `research.md`, and is indexed in `specs/README.md`.

## Catalog discovery alternatives

### EF Core model only

Pros: works offline, maps application entities/columns/relations, uses existing `FinancialIngestionDbContext` and EF configuration. Cons: cannot verify deployed migration state, manually created views, actual indexes/constraints, database-only objects, or schema drift. It is a local baseline rather than production truth.

### PostgreSQL catalog only

Pros: observes physical deployed DDL, indexes, constraints, views and actual types. Cons: may lack application/domain meaning, entity mapping intent, offline reproducibility, or semantic source policies; the generator also needs secured DB connectivity.

### Hybrid (recommended)

Combine both sources with provenance. EF is available offline and gives model intent; `pg_catalog` is authoritative for deployed physical existence and constraints. Compare discrepancies rather than choosing silently. Production publication requires a live/snapshotted PostgreSQL verification.

## ADR 141-A — Custom Vectorless Schema Navigation over Canonical YAML Catalog

**Decision:** the selected primary retrieval approach is a custom LLM-powered Schema Navigator over a compact hierarchical representation generated deterministically from approved YAML catalog metadata. Exact matching, entity/symbol resolution, reviewed aliases, and lightweight lexical filtering are supporting optimizations only. No production embedding generation, vector similarity, pgvector, external vector database, PageIndex, or other external Vectorless RAG framework is selected.

**Why this fits the repository:** the repository has a large EF persistence surface but the approved runtime catalog is intended to remain a deliberately curated subset. The source is structured, versioned YAML with explicit metrics, relationships, policies, dimensions, and dictionaries rather than a collection of arbitrary document pages. The navigator can reason over business descriptions and choose multiple domains; deterministic expansion then reads exact catalog references and approved relational joins. The approach keeps infrastructure small and leaves schema/financial governance in application code.

**Why not PageIndex:** there is no document-page indexing problem to solve. PageIndex or another external RAG framework would add an integration, lifecycle, and operational dependency while the retrieval corpus and navigation graph are controlled YAML artifacts.

**Why not embeddings first:** the current catalog size and paraphrase failure rate are unknown. Embeddings add generation/provider/version/index lifecycle and ranking behavior, but do not enforce correctness or authorization. A deterministic lexical baseline is useful for comparative measurement and exact aliases; it is not the selected primary semantic strategy. A small embedding prototype may be run offline for an objective benchmark only, with no production dependency.

**How navigation works:** an initial model call receives a compact bilingual catalog outline, selects stable domain/node IDs, and states required concepts. The application expands selected child records and approved relationship paths deterministically, then validates required measures, dimensions, entities, period policies, source restrictions, and joins. If context is incomplete, a bounded follow-up call may select known branches. Unknown IDs are rejected. Incomplete context stops before planning/execution and produces a typed unsupported/clarification outcome. The hierarchy is a discovery aid; it is not the relational join graph.

**Cost and latency:** simple requests target one navigation call; multi-domain/incomplete requests may use up to three navigation calls total, with a separately metered plan call unless combined safely. Input outline, output schema, depth, expansion fan-out, and total token/time budget are capped. Cache immutable node summaries by catalog hash/node/language; revalidate cached IDs and policies each request. No numerical cost promise is made because configured provider prices and model context sizes vary. Baseline tokens, p50/p95 latency, and estimated cost on the real approved catalog are Phase 0 measurements.

**Risks and mitigations:** wrong branch selection, model variance, prompt injection, added latency/cost, and dropped context at depth limits are measured against a golden set. Mitigations include reviewed descriptions, structured outputs, IDs visible only from current context, exact ID validation, completeness validation, bounded iterative refinement, cache versioning, read-only compilation, traceable evidence, and fail-closed outcomes. There is no assertion that Vectorless RAG is universally more accurate than embeddings or lexical search.

**Revisit criteria:** reconsider after catalog-size and golden-set measurements demonstrate persistent required-node/context recall failures or unacceptable p95 latency/cost despite hierarchy and prompt improvements. Any production embedding index or external framework requires a new ADR, security review, migration plan, and quantified benchmark benefit.

The repository's EF context exposes many DbSets, but that does not establish deployed object count, catalog document count, data completeness, or query volume. Phase 0 must measure schema inventory and source coverage; only approved objects enter the hierarchy.
## SQL generation alternatives

### LLM-generated SQL with constraints

Can be flexible but still requires SQL parsing, allowlist proof, correct metric semantics, join correctness, parameter extraction, cost control, tenant-policy checks, and dialect/version handling. A `SELECT`-only regex is insufficient. Rejected for initial execution.

### Structured plan and deterministic compiler (recommended)

LLM chooses from a typed query algebra and catalog IDs. Compiler owns safe operators, joins, formula policies and parameterization. This has bounded expressiveness, but can be tested against golden plans and expanded only with reviewed catalog capability. It best fits the financial-domain requirement for reproducible results.

## Hierarchy and catalog lifecycle trade-offs

- YAML gives PR review, reproducible releases and clear separation between generated physical facts and reviewed human semantics. It requires a merge tool that preserves semantic metadata and carefully reports schema drift.
- Runtime DB catalog tables could support admin editing but add migration, audit, cache invalidation, and dual-source conflict. Not needed for initial authoring; YAML is canonical.
- Markdown improves human navigation but can drift; generation plus CI diff makes it a view, not a second source.
- AI enrichment reduces blank-document work but hallucinates meaning; proposal-only output, strict JSON, physical ID checks and mandatory review contain that risk.

## Known unknowns to resolve before implementation

- Actual production schemas, views, database roles, migration parity, and source fields behind provider-specific data require environment verification.
- Cross-company industry query support depends on real industry linkage completeness and semantics; DbSets alone do not prove it.
- Product dictionaries and aliases may be company-specific and source-versioned; title-only inference is unsafe.
- Exact generic table response support across stored assistant messages and Telegram requires implementation audit.
- Performance/token targets are proposed gates and must be calibrated using the actual reviewed catalog and provider configuration.

## Curated catalog verification update (2026-10-08)

- A bounded metadata-only live PostgreSQL discovery (`pg_catalog`, read-only session) confirmed `public.TradingInstruments`; the EF Core model and PostgreSQL agree on columns, primary/unique keys, and its optional `NormalizedCompanyId -> Companies.Id` FK. The four market tables each have a required FK from `TradingInstrumentId` to `TradingInstruments.Id` (`ON DELETE CASCADE`).
- Product-owner-confirmed historical lineage keeps the two identity paths separate: Companies flow through TSETMC -> Novaran Amin -> Novaran API -> Financial Copilot; TradingInstruments came from TSETMC direct ingestion/historical migration. Current source code still contains the TSETMC direct-feed service and end-of-day polling worker. Repository worker defaults enable TSETMC polling and disable StockMarketDB polling; deployed overrides were not verified. StockMarketDbSyncService deliberately does not persist instrument rows.
- `Id` is the internal instrument UUID. TSETMC `InsCode` maps to `InstrumentCode`; the current adapter derives `ExternalInstrumentId` as a deterministic GUID from InsCode; source `InstrumentID` (ISIN-like) maps to `InstrumentIsin`. `NormalizedCompanyId` is a distinct optional canonical-company FK. The current linker matches InsCode text against `Companies.InstrumentCode`, chooses latest synchronization then Id, and has no Company.ProviderName filter; this is an unresolved cross-provider ambiguity because Company.InstrumentCode is not unique.
- The direct feed can create minimal instrument stubs for unseen source codes. `InstrumentKind` has no reviewed taxonomy in this catalog. Index observations use the same instrument dimension, but an index classification cannot be inferred from a ticker/name or undocumented kind code.
- Market price/index fields are PostgreSQL numeric values without unit metadata; source normalizers copy numeric values without a common conversion. Preserve `ProviderName`/`SourceKind`, apply source-specific scaling only after the mapping is verified, and do not interpret latest timestamp as complete coverage.
- Product-owner-confirmed `IsRepresented=true` means Restated (تجدید ارائه‌شده); the persisted field remains `IsRepresented` and no `IsRestated` column is added. `IsComposing` is passed through from provider DTOs. Codal selection uses it as a consolidated preference flag, but upstream semantic evidence is insufficient to assert the exact true/false mapping.
- CodalDb selection remains source code behavior: group by `(PeriodEnd.Date, PeriodType)`, then audited true, represented true, composing true by default (reversed only when `preferConsolidated=false`), then ascending `StmtId`. The owner confirmed represented means restated but did not approve preferring restated reports. This is a human financial-policy decision; no selection logic changed.
- The refreshed snapshot remains a development-configured database connection, not certified production. Reconciliation still reports known unrelated object/index drift; this update does not modify database or financial rows.



## Navigator implementation findings (2026-10-08)

Code graph discovery confirmed the reusable provider-neutral `IAiModelExecutionService` already routes structured-output workloads through configured AI clients and records provider/model, tokens, duration, provider-reported cost, retries, telemetry, and usage accumulation. Feature 141 adds a distinct `SchemaNavigation` workload and keeps provider-specific client concerns out of the navigator. An integration test substitutes only that application abstraction with a deterministic fake.

The generated hierarchy is JSON-compatible YAML/JSON and includes catalog identity, references, and three separate governance states. Current entries are pending navigation review and not queryable, which the context provider preserves. The implementation is registered for dependency injection but is not invoked by the active MAF workflow. The initial preflight in the prior phase stopped with `hosted_provider_credentials_missing` before inference; the later provider-configuration correction enabled the real live runs recorded below. The scoring script reports only observed outputs and does not infer quality from fake responses.

## Navigator token and retrieval evaluation update (2026-10-08)

- The live provider mismatch is resolved: the CLI loaded the API-equivalent Development User Secrets configuration and used OpenAI `gpt-5.6-luna` through the existing provider-neutral execution service. No credentials were printed, no provider switch was made, and evaluation did not query financial observations.
- Baseline `fa-02` reached navigation with a 700-token output ceiling. Its older response contract repeated extracted requirements, and the Responses API returned `incomplete_details.reason=max_output_tokens`; the adapter had discarded this completion metadata. The navigator now uses a compact typed decision, records response/usage metadata, reports `ModelOutputTruncated`, and permits one bounded retry.
- Baseline `fa-value-01` produced a 19,597-character outline against an 18,000-character effective limit. The old construction included broad hierarchy detail and retrieved against 296 source items without adequate Persian normalization. The current outline is relevance-ranked and progressively pruned, contains at most 12 source candidates (trimmed to fit, with at least four retained when available), and seeds exact direct-source nodes from independently validated canonical references. In the final inspection it was 15,654 characters; source item 161 scored 1018, ahead of similar item 140 at 1011.
- The final golden live artifact has 21 case rows; 19 completed inference and two stopped with typed request-wide input-budget failures. Aggregate captured usage is 42 model calls, 218,146 input tokens, 15,062 output tokens, 224,796 ms reported model latency, and no provider-reported cost. The scorer's detailed results are in [`scored-navigator-optimization-golden-final-v2-live.json`](evaluation/scored-navigator-optimization-golden-final-v2-live.json).
- The independent held-out v3 fixture is new and separate from the previous three-case fixture, whose case IDs overlap the golden set. Its three cases produced 6 calls, 24,151 input tokens, 2,178 output tokens, and 33,695 ms latency. Metric recall was 0.8333, dimension recall 1.0, node recall 0.4206, and exact domain accuracy 0.6667. Two cases were unsupported at current governance gates and one needed clarification. Cost metadata was unavailable. See [`scored-navigator-heldout-v3-final-live.json`](evaluation/scored-navigator-heldout-v3-final-live.json).
- Retrieval completeness is recorded separately from semantic review and query authorization. Across the 19 completed golden rows, retrieval was complete in 14; all 19 remained unapproved for semantic review and query authorization, and none was executable. This is expected under the current catalog state and keeps successful retrieval from implying permission to query.
- The canonical hierarchy/source hash remains `b7ce865504f0b2b78321cd15a17f64b3b11bebb2ea8182bd8709bdc9e71935f4`. .NET navigator tests and Python catalog/scorer tests passed. No SQL compiler/executor or runtime workflow route was enabled.

## Persisted source-item evidence (2026-10-08)

A read-only export captured 296 existing `FinancialStatementSourceItems` definitions and their persisted MetricCode mappings. The export queried only catalog definitions and mapping records; it contained no statement observation values and called no Noavaran endpoint. Twelve rows had a persisted MetricCode mapping; the remaining definitions remain direct source references without a governed mapping.

Verified example: provider `NoavaranCurrentApi`, statement type `IncomeStatement`, SourceItemId `161`, SourceItemCatalogId `3cafc7d3-b0ab-4341-85ef-ebcbc9d60528`, Persian title `سود عملیاتی هر سهم ` (including the source trailing space), null English title, source unit `N/A`, and null MetricCode. This confirms a catalog identity only; this phase did not verify that an observation exists for a company or period. The title does not establish normalized unit or query authorization.

The persisted line-item FK is nullable and uses `ON DELETE SET NULL`; old null identities remain unresolved. No historical backfill, observation edits, value reads, SQL execution for user queries, or external API calls were performed.


## Live navigator evaluation ? 2026-10-08 (provider mismatch follow-up)

**Status: PARTIAL.** The evaluator previously skipped ASP.NET Development User Secrets and was manually run with `OPENAI_API_KEY`; that environment credential differed from the API's Development User Secret. The evaluator now follows API configuration precedence (environment, launch profile, appsettings, Development User Secrets, environment variables, then Feature 141 path overrides). Provider-neutral DI, structured output, model selection, endpoint, and adapter remain shared with the application. No credential values are printed or stored.

The corrected live smoke used OpenAI `gpt-5.6-luna` at `api.openai.com` and recorded a real structured result for `fa-01`: 2 logical calls, 10,662 input tokens, 1,081 output tokens, 13,257 ms model latency. Provider cost metadata was absent. Catalog IDs validated, but the result was `Unsupported` because the selected catalog nodes are pending semantic/navigation approval and not queryable. The one-case score is in `evaluation/scored-smoke-live-2026-10-08.json`; this is transport/contract evidence, not evidence of acceptable navigation quality.

The 21-case live run attempted `fa-01` and `fa-02`, wrote one prediction row, and stopped at `fa-02` with `structured_output_truncated` during navigation at the configured 700-output-token cap. The API-equivalent credential made real requests successfully; no quota error recurred. A separate `fa-value-01` smoke stopped before inference with `catalog_outline_budget_exceeded`. Therefore no full-dataset score is available. In all, seven real upstream requests occurred across the initial diagnostic smoke, corrected smoke, and partial full run; the dedicated financial smoke sent none. No DB query or generic query execution occurred.

Regression checks passed: 12 evaluator-loader/navigator unit tests, the quota-without-retry integration test, canonical catalog/hierarchy hash validation, and root help/version artifact-hash checks. Exact outcomes are recorded in `evaluation/live-evaluation-report-2026-10-08.json`. The held-out fixture overlaps the baseline and was not run. No provider fallback, billing change, or quota bypass was attempted.

## Recall optimization follow-up (2026-10-08)

The recorded baseline showed distinct causes: `fa-07` and `fa-11-heldout` hit the request input budget; `fa-04` selected a nearby direct source item but missed its independently required governed metric; broad branches caused low node recall; and some consolidated-source questions remained ambiguous. The complete matrix is in [baseline-failure-matrix-2026-10-08.md](evaluation/baseline-failure-matrix-2026-10-08.md).

The hierarchy provider now seeds the most specific catalog nodes matching independent metric, dimension, relationship, and financial references plus their ancestors. Under wide branches it prioritizes children carrying required references and excludes unrelated siblings when relevant children exist. The existing 12-child and 48-node bounds remain; diagnostics leave any omitted requirement incomplete. The implementation uses catalog relationships and canonical IDs, not question-specific rules.

Final live Golden: 21/21 cases produced rows; no request-wide input-budget failures. Node recall 0.9103 (baseline 0.4840), metric recall 0.8462 (0.6154), dimension recall 0.9048 (0.8095), and source-item reference accuracy 0.7500 (0.7500). Usage was 42 calls, 217,823 input tokens, 15,303 output tokens, and 226,960 ms model latency. Provider cost was unavailable.

The new independent held-out v4 scored node, metric, and dimension recall at 0.6667 each: 7 calls, 33,020 input tokens, 2,818 output tokens, and 39,498 ms latency. One case failed independent extraction validation on an unsupported entity kind; two retrieved all expected nodes but included extra branches. See [quality-optimization-report-2026-10-08.md](evaluation/quality-optimization-report-2026-10-08.md). The 0.90 metric recall and 0.90 source-item accuracy targets remain unmet.

The exact NoavaranCurrentApi / IncomeStatement / SourceItemId 161 selection remains intact. Semantic approval and query authorization remain separate and pending. No query route, SQL execution, local primary-value calculation, auto-approval, Feature 082 backfill, or Feature 140 change was introduced. Query Planner readiness remains blocked on held-out recall, metric/source resolution, precision, and governance review.

## Final bounded-run correction (2026-10-08)

This addendum supersedes the preceding v4/v3 final-score figures. After the initial-selection bound regression and its focused test, the final-code Golden rows were regenerated; failed rows `fa-07`, `fa-10`, and `fa-value-06` were retried through the existing failed-row resume path. All 21 cases now score with no request-input-budget failures. Latest-row Golden metrics are node recall 0.9160, metric recall 0.8462, dimension recall 0.9048, domain accuracy 0.0000, source-item reference accuracy 0.7500; 42 calls, 217,771 input tokens, 15,480 output tokens, 212,814 ms in the scored rows. Retry traffic adds 6 calls; total observed traffic across the initial final-code run and those retries is reported separately in the evaluation JSON.

Fresh independent held-out v5 recall is 0.6667 for nodes, metrics, and dimensions; it used 5 calls, 18,527 input tokens, 2,280 output tokens, and 30,294 ms. One requirements extraction failed validation on an unsupported entity kind. The final Golden artifact also contains 60 selected node IDs in one row despite the intended provider cap of 48; end-to-end node bounding therefore remains unresolved. Exact domain accuracy regression and this cap issue keep Query Planner readiness blocked.

## Persisted statement read-model verification and first SQL slice (2026-10-08)

The EF Core mappings confirm `FinancialStatements`, `FinancialStatementLineItems`, `FinancialStatementSourceItems`, and `Companies` are the relevant persisted relations. `FinancialStatementLineItems.SourceItemCatalogId` is nullable and references `FinancialStatementSourceItems.Id` with `ON DELETE SET NULL`; the direct query joins through this UUID and does not require `MetricCode`. Source catalog identity is uniquely provider + statement type + source item ID. `FinancialStatements.CompanyId` is a nullable FK to `Companies.Id`. Statement rows expose `PeriodEnd`, `PeriodType`, `IsComposing`, `IsAudited`, `IsRepresented`, `ExternalStatementId`, `PeriodStart`, `PublishedAt`, and `SourcePayloadChecksum`; `IsRepresented` preserves the restated flag. The reviewed financial policy confirms standalone `IsComposing=false` by default and no global audited/restated/provider preference. Existing specialized CodalDb selection and financial retrieval services are unchanged.

The generated catalog candidate for these physical relations remains `provenance.verified=false`, `candidate-pending-review`, and `queryable=false`. Tests use an isolated synthetic integrity-validated snapshot only to verify compilation behavior; this is not evidence of production approval or PostgreSQL correctness. No development database name, restricted role, explicit opt-in, or reviewed source allowlist is configured in the runtime environment. The Noavaran item 161 observation was not queried, and no numeric result is known from this work.

The direct compiler fixture uses the real canonical catalog IDs for source item 161 (`source_item_682beceadddc1458`, UUID `3cafc7d3-b0ab-4341-85ef-ebcbc9d60528`) and the four persisted table candidates. It adds only a test-scoped `FinancialStatementId -> FinancialStatements.Id` relationship to exercise compilation; that relationship is absent from the canonical relationship catalog and is not a claim of approval or live schema verification. The fixture also uses a synthetic company UUID. Therefore tests verify SQL construction/guards only, not an actual company observation.

## Integration follow-up (2026-10-08)

A cataloged application-level line-item/header relationship and immutable runtime snapshot builder now close two metadata gaps. The physical inventory still says the relationship has no database FK; row-level orphan/duplicate validation was not performed. A Feature 141 company binder now reuses the existing canonical resolver and its typed ambiguous/not-found outcomes, returning only internal `Companies.Id`; live data resolution was not run. The existing resolver reads shared master data and has no tenant key, so tenant enforcement remains in the separate execution grant. No dedicated restricted Development PostgreSQL principal/configuration is present, so the requested actual query remains blocked.
The company-binding adapter now reuses `ICanonicalQueryEntityResolver`, which already supports exact tickers, normalized names, company identity fields, and typed ambiguity. It returns only the canonical internal company UUID. Its unit tests are fixtures; they do not confirm that the live database contains a unique ??? match. The existing resolver's provider preference is NoavaranCurrentApi; provider-scoped external IDs remain distinct from the canonical UUID.

## Monthly contract findings (2026-10-08)

The Noavaran monthly normalizer copies `SalesRate` from the provider field (with legacy aliases) and persists `Unit` separately as the product quantity unit. The inspected contract provides no verified rate-unit field or conversion rule. SalesRate therefore stays unsupported with `monthly_provider_rate_unit_unverified`; it is never derived from amount divided by quantity. The generic PostgreSQL fixture confirms `SalesAmount` values remain in million IRR and quantities retain row-level units. No Development observations were queried.

Clean HEAD comparison: 419 passed, 72 failed, 41 skipped; working tree: 421 passed, 71 failed, 42 skipped. The 71 working-tree failures exactly match clean HEAD; one clean-HEAD-only financial schema index assertion disappeared, and there were no new failures. Existing monthly endpoint failures predate this work.
