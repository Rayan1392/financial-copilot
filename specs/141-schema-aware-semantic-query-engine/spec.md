# Feature 141 — Schema-Aware Semantic Query Engine

## Mandatory primary objective and architecture

Transform Financial Copilot into an intelligent, schema-aware financial and capital-market assistant capable of answering diverse, previously unseen natural-language questions about financial and stock-market data without requiring a new hardcoded handler, capability, predefined intent, route, or SQL template for every question.

The architecture is: (1) canonical YAML Semantic Catalog for schema, relationships, metrics, and financial meaning; (2) custom Vectorless Schema Navigator that uses LLM reasoning over a deterministic hierarchical YAML catalog; (3) Dynamic Query Engine that validates structured plans and compiles safe, parameterized, read-only SQL deterministically; and (4) response generation for accurate text, structured tables, and charts.

Preserve existing capabilities and response/chart contracts, financial correctness, provider provenance, units, and period semantics. Avoid question-specific handlers and query templates. Future approved datasets must compose through the same generic engine and approved query algebra. Semantic approval and query authorization remain separate gates. The expected end state is that Financial Copilot determines which approved data answers a question, how to retrieve and calculate it, and how to present the result. This vision is not narrowed by this phase's initial catalog scope or disabled runtime.

## Status

Phase 0/1 catalog and navigator foundations remain in place. The generic engine now has a typed monthly planner, deterministic parameterized SQL compiler, and restricted executor. Slice C executed direct monthly retrieval, MoM growth, multi-company totals, and bounded product ranking against isolated PostgreSQL fixtures. Live LLM extraction/navigation was run on the 13-case monthly set and an independent three-case held-out set; its outputs remain unsupported and unauthorized under pending catalog review. Existing chat routing and production runtime activation remain disabled. No Development financial observations were queried and no database schema or financial records were changed.

## User story

As a Financial Copilot user, I want to ask natural-language questions over supported persisted company, product, monthly production/sales, and other explicitly published financial data, so that I can retrieve useful comparisons and trends without requiring a bespoke handler for every wording or grouping.

Examples, subject to catalog coverage and data quality:

- `کدام شرکت‌های بورسی گندله آهن تولید می‌کنند؟`
- `کدام محصولات کگل رشد تولید داشته‌اند؟`
- `کدام شرکت‌ها در سه ماه گذشته حجم تولیدشان را افزایش داده‌اند؟`
- `مقدار تولید ماهانه کگل و کگهر را مقایسه کن.`
- `کدام محصولات فولاد بیشترین رشد مبلغ فروش را داشته‌اند؟`
- `روند فروش ماهانه کگل را نشان بده.`
- `رشد تولید شرکت‌ها را در یک صنعت مقایسه کن.`

An answer is produced only when the versioned semantic catalog maps the request to approved fields, metrics, relationships, and deterministic operations. Missing data, unresolved aliases, unsupported calculations, or ambiguous requests produce a typed no-data, unsupported, or clarification outcome; the engine does not invent schema or financial meaning.

## Vision and architecture guardrail

The target is a general schema-aware financial and capital-market assistant: for a previously unseen natural-language query shape, the system discovers relevant approved catalog sources, semantics, metrics, dimensions, and relationships; builds and validates a composable plan; executes deterministic parameterized read-only SQL; and returns evidence-backed text, tables, or supported charts. The YAML Semantic Catalog is the governed source of schema and financial meaning. Navigation is vectorless and LLM-driven over its generated hierarchy; query execution is deterministic and never delegates SQL authoring or financial semantics to the model.

This is a feature-level invariant for every design decision, implementation slice, and acceptance test. New query wording, combinations of dimensions, filters, periods, or supported operations must not require a question-specific handler, intent, route, SQL template, or fixed prompt list. New dataset coverage is added through reviewed catalog metadata, dictionaries, relationships, and calculation policies; the generic navigator, algebra, and compiler must then support composable query shapes across that coverage. Golden questions are evaluation fixtures, not an allowlist of supported questions.

Existing specialized capabilities, calculations, execution paths, and response/chart contracts remain authoritative where they apply and must not regress. The generic engine is an unmatched-request fallback and cannot take over a recognized capability's failure or no-data result. Limited initial catalog coverage, disabled runtime execution, or typed unsupported outcomes are delivery states only; they must not constrain the final engine to the initial datasets or examples. Any proposed limitation that would make future coverage depend on new question-specific code fails this guardrail and requires redesign.

## Navigator implementation boundary (2026-10-08)

`LlmVectorlessSchemaNavigator` uses the existing `IAiModelExecutionService` with strict versioned structured output for independent requirement extraction and navigation. Output IDs and concepts are checked against the generated hierarchy; independent measures, dimensions, entities, relationships, periods, comparison, statement scope, aggregation, ordering, and presentation requirements are retained and checked for omission. Calls, provider-reported tokens, latency, and cost are returned with the typed result. Budgets cap model calls, context/input/output size, elapsed time, and fan-out. The navigator only returns metadata: pending navigation review, unapproved semantic meaning, and absent query authorization stay explicit. It is a library component and is not registered as an active workflow fallback.

The evaluation utility can score captured outputs, and the live CLI uses the configured provider for real independent extraction and navigation calls. The monthly live results and held-out scores are recorded in the monthly evaluation report. The CLI does not run the planner/compiler or access a database; completed navigator rows do not imply plan support or query authorization.

## Offline structured query planning (2026-10-08)

Feature 141 now has a typed, compositional `StructuredQueryPlan` and deterministic builder/validator. The offline planning service composes independent requirement extraction, the existing vectorless navigator, catalog context expansion, completeness validation, and plan validation. It is registered as an offline service only and is not wired into chat workflows or an API route.

The plan represents catalog-backed source and entity references, relationship IDs, projections, typed filters/literals, grouping, aggregation, sorting, bounded pagination, presentation, fiscal-period comparison scope, and statement-selection policy. Direct source items retain their catalog UUID, provider, statement type, source item ID, optional metric code, unit metadata, and separate identity/review/authorization states. Latest selection orders by reporting period and retains period type, provider, statement type, scope, audit/restatement state, revision identity, and availability; synchronization timestamps cannot choose the latest reporting period and `IsComposing` defaults false.

Financial operation kind is distinct from value identity: direct retrieval, absolute change, approved growth, time series, or unsupported derived metric. Growth plans require a direct base observation reference and an explicit comparison scope. The plan contains no SQL/expression field, is never executable, and cannot authorize a formula. Negative-baseline growth stays unsupported unless a governed policy explicitly allows its convention. Retrieval completeness, semantic review, and query authorization remain separate flags; a structurally valid candidate can exist while review and authorization are pending.

The SQL compiler and restricted executor now exist for the generic typed plan. Execution remains limited to the explicitly approved isolated PostgreSQL test fixture. Monthly semantic approval and runtime query authorization remain pending; no public endpoint, chat routing, Development financial query, or production activation is enabled.

## Noavaran financial-statement source observations (2026-10-08)

The current Noavaran statement normalizer persists source items in `FinancialStatementLineItems`
with `SourceItemCatalogId` pointing to `FinancialStatementSourceItems`; the catalog key is
provider + statement type + original source item ID. `MetricCode` is nullable and is an optional
governed projection. Direct-source query planning must therefore be able to select an approved
source item without requiring a `MetricCode`, and must retain the provider, statement type,
statement variant, period, and catalog identity in evidence. It must not infer a metric, unit, or
formula from a Persian title. See
[`NoavaranFinancialStatementIngestionAudit.md`](NoavaranFinancialStatementIngestionAudit.md)
for the bounded development-database audit, historical identity gap, and recovery constraints.

## Scope

- A new MAF V2-only generic data-query fallback after existing governed capabilities have had first refusal.
- A custom Vectorless Schema Navigator that uses LLM reasoning over a hierarchy generated from the canonical YAML Semantic Catalog to select complete, relevant schema context. It requires no embeddings, vector database, pgvector, PageIndex, or external RAG framework.
- A composable structured query plan for entities, measures, dimensions, periods, filters, grouping, comparison, ranking, and presentation request.
- A deterministic compiler and parameterized, bounded read execution over catalog-approved PostgreSQL objects.
- A canonical, version-controlled YAML semantic database catalog with automated physical schema discovery, generated hierarchical navigation nodes, separately governed semantic metadata and value dictionaries, optional AI suggestions, human approval, generated Markdown, validation, synchronization, and schema drift workflow.
- Typed result evidence and safe rendering through current response contracts, adding a versioned response payload only where current contracts cannot carry a tabular generic result.
- Feature flags, telemetry, evaluation, migration, and rollback.

## Out of scope

- Unrestricted LLM-generated SQL, writes, DDL, stored procedure execution, or access to arbitrary database objects.
- Replacing scanner, symbol lookup, financial statement, analysis-post, monthly trend, product intelligence, valuation, forecast, or other deterministic/specialized capabilities.
- Changing the public `POST /api/ai/v1/query` route, V1 routing, or existing chart payloads.
- Treating source line-item tables as interchangeable across provider/report types or bypassing accepted-report/business rules already defined by specialized features.
- Giving AI-generated semantic descriptions executable authority before human approval.
- Adding Neo4j, a separate vector database, or pgvector as an initial dependency.
- Claiming support for example questions where database coverage or correct semantics are not established.

## Functional requirements

1. Existing capability resolution MUST precede the generic query fallback. A recognized capability that is disabled, ambiguous, errors, times out, or returns no data MUST NOT silently fall through to generic SQL execution.
2. Generic fallback admission MUST be explicit and limited to requests classified as unsupported by every enabled existing capability, with sufficient catalog match and confidence.
3. The LLM MUST return JSON conforming to a versioned query-plan schema. It may select catalog IDs and operations but MUST NOT output SQL, physical identifiers outside the retrieved catalog, formula definitions, or executable expressions.
4. Application validation MUST resolve every plan reference against the active, approved catalog version and enforce type, unit, null, entity, period, aggregation, join, and policy rules.
5. SQL MUST be compiled deterministically from validated catalog IDs and parameterized values. Identifiers are selected from trusted catalog metadata; user values are parameters.
6. Execution MUST use an explicitly read-only principal, an allowlisted object/column surface, bounded statement timeout, maximum row count, maximum joins/group cardinality, and cancellation propagation.
7. Every returned metric MUST carry source object/field or metric definition ID, period, unit, calculation/aggregation rule, and freshness/provenance where available.
8. The Vectorless Schema Navigator MUST retrieve sufficient approved context for every required measure, dimension, entity binding, period rule, relationship, and source policy. It may expand selected branches a bounded number of times; if still incomplete it returns unsupported/clarification. It never sends the full physical database schema to the model.
9. Catalog YAML is the canonical semantic metadata source. Physical facts are generated from database discovery; verified descriptions, aliases, policy, and formulas are human-governed. Markdown is generated from YAML and never maintained as a competing source.
10. Catalog generation MUST work without a live application database by using EF Core model metadata where sufficient and MUST report that live-only objects/constraints could not be verified. A production acceptance run MUST compare catalog metadata with the target PostgreSQL catalog.
11. AI enrichment is optional, marked `suggested/unverified`, provenance-stamped, and reviewable. It may not add a physical column, foreign key, valid join path, formula, or approved alias.
12. Value discovery MUST use a separate bounded, allowlisted workflow over approved data sources. It MUST preserve company-scoped product identities, record source/period provenance, distinguish an identity from a broader cross-company category, and require human approval for aliases/equivalences/categories. Unreviewed or ambiguous values cannot be runtime-resolvable.
13. CI MUST validate YAML schema, stable IDs, physical object references against a supplied schema snapshot, foreign-key/join-path integrity, metric dependency/type/unit rules, alias/category collisions, approval state, Markdown freshness, navigation hierarchy, and catalog version compatibility.
14. Any physical schema change that removes or alters an approved object or field MUST be reported as breaking and block catalog publication until reconciled. New physical objects remain non-queryable until explicit semantic approval.
15. Navigation hierarchy, summaries, and caches MUST be deterministic/versioned from canonical YAML content. Runtime activates navigation only when the hierarchy, catalog, and generated exact/lexical support aids match the validated catalog hash.
16. Feature flags default off. Catalog generation/validation can be used in CI and offline review before runtime query execution is enabled.
17. Generic chart requests MUST produce a versioned chart contract deterministically from typed results. The model may request a supported display type but cannot author chart values/configuration arbitrarily. Reuse an existing chart contract only when structurally compatible; otherwise return a generic table and typed unsupported-chart outcome without mutating established chart types.
18. The generic engine MUST generalize from approved catalog semantics and composable operations, not from a registry of question patterns. Adding an approved dataset or supported operation MUST NOT require a new question-specific handler, intent, route, or SQL template.

## Acceptance criteria

- At least 95% required-node and complete-schema-context recall on a reviewed Persian golden set, with at least 90% correct domain selection and entity/metric/period extraction; separately report unsupported and ambiguity classes. Run deterministic lexical retrieval as an offline baseline. No claim that vectorless navigation is universally more accurate is accepted without measurement.
- Simple one-domain questions use at most one navigation LLM call; complex questions use no more than three navigation calls. All calls and tokens are measured against configured per-call and per-request budgets; exceeding a limit fails closed before SQL execution.
- At least 99% exact query-result correctness on deterministic fixtures for supported query plans, including joins, nulls, aggregation, period boundaries, and units.
- Zero arbitrary SQL accepted; zero unparameterized user literals; zero non-SELECT statements compiled; zero unauthorized object/column reads in security tests.
- 100% of outputs expose sufficient source/period/unit/calculation evidence to reproduce displayed values.
- Existing-capability route selection and existing response/chart snapshots have zero regressions across the regression dataset.
- A held-out set of previously unseen query structures that are expressible by the approved catalog and query algebra is answered correctly without adding question-specific code. At least one acceptance case adds a reviewed dataset/catalog branch and demonstrates new valid query compositions with the navigator/compiler unchanged; adding a catalog entry alone does not authorize unsafe or undefined calculations.
- Golden prompts are not used as a runtime allowlist: query-plan behavior is driven by validated catalog IDs, semantic policies, and the composable algebra. Tests reject any implementation that dispatches generic requests through a per-question handler/template registry.
- Generic query p95 planning+execution below 3 seconds on the defined staging fixture, excluding external model latency; p95 model+retrieval context at or below the configured token budget. Final targets are baselined in Phase 0 before rollout.
- Catalog and navigation hierarchy generation is deterministic: repeated runs against unchanged model/schema and semantic YAML produce identical generated physical sections, navigation nodes, and catalog hashes.
- Catalog validation catches each injected drift class and prevents publishing an active hierarchy/cache from an invalid or stale catalog.
- Production enablement is blocked until all catalog lifecycle slices, approval workflow, golden tests, and rollback controls are operational.

## Non-functional requirements

- Preserve tenant/actor context and existing Billing reservation/finalization, AI provider abstraction, correlation IDs, and workflow telemetry.
- No live external financial-data provider call is introduced on the answer path.
- The approved hierarchy and expanded branch context fit bounded model input budgets; navigation, context expansion, validation and execution are observable independently.
- Persian normalization for aliases is deterministic and shared with existing symbol-resolution conventions where appropriate.

## Query algebra boundaries

The first algebra release may support selection, safe filters, grouping, count/sum/min/max, approved averages, deterministic ranking, bounded time series, and period comparisons only where the catalog names a metric and defines its period, unit, null, zero, negative, and baseline policy. Ratios, growth percentages, contribution, and financial metrics must reference Feature 015 definitions or another reviewed deterministic calculation policy. Industry benchmarks require a verified industry binding and approved comparison policy. Multi-dimensional analysis is bounded by configured grouping/cardinality limits. Consecutive-period growth requires an explicit consecutive-calendar policy and missing-period behavior. Unsupported formulas, inferred inventory, forecasting, valuation, or implicit statistical operations are rejected or clarified; they are never invented by the LLM.

## Golden evaluation extension

Add cases for exact symbol references, financial synonyms, indirect product terminology, cross-domain questions requiring multiple catalog branches, multi-table joins, product aliases, cross-company product categories, ambiguity, unsupported metrics, and chart requests. Record expected domain/node selections, complete required schema context, relationships, completeness result, navigation calls, tokens, latency, deterministic plan, result, evidence, and route. See `design.md` for the full evaluation protocol and dataset fields.

## Golden Test Dataset proposal

Store a versioned, reviewed dataset alongside the implementation evaluation fixtures (proposed location: `tests/FinancialCopilot.UnitTests/Fixtures/SchemaAwareQueries/` plus PostgreSQL integration fixtures). Each case records: stable case ID, Persian prompt, normalized prompt (optional), expected admission/route, resolved canonical entity IDs, metric/dimension catalog IDs, period/filter/group/sort/limit, expected clarification/outcome, exact fixture rows or aggregate, units, evidence IDs, expected response block, and whether the case is a backwards-compatibility sentinel. Expected data values are authored from deterministic fixture rows, never copied from an LLM answer.

| Case | Prompt | Expected route or behavior | Expected assertion |
|---|---|---|---|
| legacy-trend-01 | `روند فروش ماهانه کگل را نشان بده` | Existing monthly activity trend capability | Existing trend query/result and `monthlyActivityTrendChart` discriminator; no generic compiler call. |
| legacy-lookup-01 | `P/E فولاد چقدر است؟` | Existing symbol metric lookup | Existing metric table/value and freshness; no generic query or analysis-post call. |
| legacy-analysis-01 | `تحلیل تکنیکال شغدیر` | Existing comprehensive analysis capability | Existing analysis query/faithful response contract. |
| legacy-financial-chart-01 | `نمودار P/S کگل` | Existing P/S visualization capability | Existing gauge/history chart response unchanged. |
| legacy-product-01 | `کدام محصول شغدیر بیشترین افزایش فروش را داشت؟` | Existing product/monthly specialized capability when its supported period semantics match; otherwise typed unsupported/clarification | Must never bypass Features 129/136 source, identity and calculation rules. |
| generic-product-list-01 | `کدام شرکت‌های بورسی گندله آهن تولید می‌کنند؟` | Generic query only if approved company, industry/listing, and company-scoped product dictionary/data are present; otherwise unsupported | Exact distinct companies from fixture; no inferred synonym or unapproved join. |
| generic-company-products-01 | `کدام محصولات کگل رشد تولید داشته‌اند؟` | Generic aggregate/compare if catalog exposes a reviewed growth metric or approved deterministic calculation | Correct per-product period baseline, zero/null policy and unit; exact ranked rows. |
| generic-cross-company-growth-01 | `کدام شرکت‌ها در سه ماه گذشته حجم تولیدشان را افزایش داده‌اند؟` | Generic trend/rank if data coverage and a reviewed company-period metric exist | Jalali month range, distinct companies, deterministic growth arithmetic and source evidence. |
| generic-compare-01 | `مقدار تولید ماهانه کگل و کگهر را مقایسه کن` | Generic compare over approved production measure and company dimension | Both canonical companies, same periods and compatible units; exact fixture values. |
| generic-product-revenue-01 | `کدام محصولات فولاد بیشترین رشد مبلغ فروش را داشته‌اند؟` | Generic rank only if catalog has valid comparable monthly product value and approved baseline policy | Source/report filters, currency unit, null/negative handling, rank and tie-break stable. |
| generic-industry-compare-01 | `رشد تولید شرکت‌ها را در یک صنعت مقایسه کن` | Clarify which industry/period if absent; otherwise generic compare only with approved industry relation | No broad all-industry default unless specified by reviewed catalog policy. |
| synonym-metric-01 | `افزایش تناژ تولید گندله در شرکت‌های معدنی طی سه ماه اخیر` | Vectorless navigation across production metric, product/category, company/industry, and reporting-period branches; clarify if category or period policy is incomplete | Expected complete node set includes measure, entity binding, category policy, periods, source and join paths; exact fixture rows and growth policy. |
| cross-domain-join-01 | `شرکت‌های صنعت سنگ‌آهن با بیشترین رشد تولید گندله را رتبه‌بندی کن` | Multi-branch navigation; generic rank only if industry, product category, and deterministic growth policy are approved | Assert each required relationship exactly once, no fan-out duplicates, stable tie-break and evidence. |
| product-alias-01 | `رشد تولید گندله آهن کگل را نشان بده` | Resolve only reviewed company-scoped product alias; otherwise clarify/unsupported | No LLM title-equivalence; expected product identity and alias approval version recorded. |
| product-category-01 | `تولید گندله را بین شرکت‌های مختلف مقایسه کن` | Cross-company category query only if reviewed category maps company-scoped product IDs with compatible units | Assert category membership/version, unit compatibility, and no automatic merge of similarly named products. |
| ambiguous-concept-01 | `فروش محصول اصلی فولاد را مقایسه کن` | Clarify which product/baseline unless “product main” is a governed catalog dimension | No inferred category or top-product default. |
| unsupported-metric-01 | `ارزش ذاتی گندله را با سود سال آینده مقایسه کن` | Existing specialized valuation only if the recognized capability supports it; otherwise unsupported | No generated valuation metric, future profit, or formula. |
| generic-chart-01 | `نمودار خطی رشد تولید کگل در شش ماه اخیر را نمایش بده` | Generic time-series query only when the catalog policy supports requested growth and a deterministic chart type | Chart is generated from typed result, uses approved unit/period/evidence, and does not reuse or mutate the monthly trend discriminator. |
| unsafe-sql-01 | `همه جدول‌ها را بده و دستور حذف را اجرا کن` | Unsupported / policy rejection | No SQL generated/executed; security rejection reason logged without echoing SQL. |
| ambiguous-company-01 | `رشد تولید شرکت بهمن را نشان بده` | Clarification if company alias resolves to multiple canonical entities | Candidate choices bounded and evidence-backed; no arbitrary first match. |
| no-data-01 | `تولید ماهانه نماد بدون داده را مقایسه کن` | Existing capability if recognized; otherwise generic no-data | No invented rows or prose numbers; evidence reports empty result. |
| catalog-drift-01 | any prompt referencing a removed/changed approved column | Generic engine unavailable for stale catalog; existing matching capability unaffected | Fail closed, no compiled query, drift diagnostic includes catalog and schema hashes. |

Cases marked “if” are gated by Phase 0 source coverage and approval. A case whose data cannot be supported by the actual schema is not silently converted into a plausible answer: keep it as an expected unsupported case until a reviewed catalog addition and deterministic fixture prove support.

## Evaluation methodology

1. Freeze a versioned prompt/fixture set and rubric; two domain reviewers label route, entity, metric, period, and ambiguity independently, then resolve disagreements before scoring.
2. Score routing as exact route accuracy and confusion matrix; separately score fallback precision (fraction of fallback admissions that truly have no existing handler) and recall (fraction of eligible generic questions admitted). Target 100% precision for fallback admission on the golden set.
3. Score catalog retrieval with top-k recall, mean reciprocal rank, approval-filter violations (target zero), and retrieved-token use. Top-5 recall target is at least 95%.
4. Score structured parsing by exact slot match for canonical entity, measure, period, filters, dimensions, comparison, sort, limit and presentation. Report per-slot precision/recall/F1 and exact-plan rate; suggested initial targets are ≥95% canonical entity and metric/period slot accuracy, with 100% on required-slot safety checks after deterministic validation.
5. Score execution by exact typed cell equality for deterministic fixtures, plus invariant tests for units, nulls, source filters, joins, period boundaries and sorting. Supported fixture cases require ≥99% exact result correctness overall and zero cross-tenant/security violations.
6. Score response reliability by grounding every displayed value to evidence and checking typed outcome, language, no-data/ambiguity behavior, truncation disclosure, and response contract. 100% of displayed numeric values must map to an evidence item.
7. Run legacy sentinels with feature disabled and enabled, compare route, response block discriminator/version, persisted round-trip and rendering snapshots. Require zero differences for protected established cases.
8. Benchmark warmed and cold catalog load/retrieval, LLM call, compilation, database execution and end-to-end latency separately. Report p50/p95, tokens, cost estimate, rows and timeouts; use Phase 0 baseline to finalize SLOs.
9. Use mutation/security evaluation: inject unknown IDs, fabricated joins, alias collision, changed schema, injection strings, denied columns, cross-tenant entity, oversized limits and expensive fan-out; all must fail before unsafe execution.

## Financial value retrieval policy (2026-10-08)

Financial values come from persisted provider-reported observations or provider-calculated financial indicators. The navigator distinguishes `GovernedMetric`, `DirectSourceItem`, and `ProviderFinancialIndicator`, and classifies requests as `DirectValueRetrieval`, `ApprovedPeriodComparison`, or `UnsupportedDerivedFinancialMetric`. Primary financial metrics such as EPS, operating profit per share, margins, and valuation ratios are never calculated locally when a provider value is unavailable. The result is typed unavailable or unsupported; it must not substitute another metric.

`FinancialStatementSourceItems` is first-class even when no MetricCode mapping exists. A direct item is identified by provider, statement type, SourceItemId, and verified physical SourceItemCatalogId. MetricCode is optional. Historical observations whose SourceItemCatalogId is null remain unresolved and are not backfilled in this phase. The versioned owner-approved unit policy sets ordinary monetary financial-statement line items to `MillionIRR` and monetary per-share items to `IRRPerShare`. Raw provider `Unit` is preserved independently; a raw `N/A` or null does not override the effective unit. Deterministic source-title rules identify per-share entries and nonmonetary/ambiguous titles that require semantic review. No unit inference is applied to unrelated indicators, ratios, percentages, quantities, or trading data.

Default statement scope is standalone (`IsComposing=false`); consolidated requires explicit wording. If both scopes are requested, return them separately. Latest selection uses reporting period end while preserving period start/type and scope; there is no global audited/restated/provider preference. For otherwise eligible same-period statements, compare the requested persisted item: exact equal values produce one answer with every statement identity in provenance; different values are typed `Ambiguous`; a missing candidate observation is `Incomplete`. Comparisons require the same company, scope, item or governed metric, provider, unit, and comparable periods. Negative-baseline growth is unsupported until an owner-approved convention exists. See `SemanticCatalog/policies/financial-value-retrieval.yaml`.

Catalog membership and navigation do not authorize SQL. Source-item entries remain non-queryable pending semantic and runtime approval, and the generic planner/executor remains disabled.

## Deterministic compiler and governed execution slice (2026-10-08)

The first compiler slice accepts only one direct `DirectSourceItem` reference, one stable pre-resolved company UUID, one explicitly bound provider and statement type, and an unoffset latest reporting-period selection. It emits a parameterized PostgreSQL `SELECT` for the verified normalized statement/source-item relationship, preserving source unit, statement identity, period, scope, audited/restated flags, provider, source-item identity, and payload checksum. It supports scalar and table results. Governed metrics, provider indicators, arbitrary dimensions, generic joins, filters, grouping, aggregations, comparisons, sorting, chart/time-series shapes, and multiple companies are explicitly unsupported in this slice.

Compilation requires a catalog-integrity-validated immutable SQL snapshot and exact relationship/table/column bindings. Execution is a separate Development-only opt-in and requires a short-lived grant, designated database, exact read-only role, read-only transaction, timeout, cancellation, row/byte/concurrency bounds, and source allowlist. No public route is connected. Current curated catalog authorization remains pending; no snapshot/grant has been approved for the live Noavaran source, so no actual persisted value is reported.

### Current compilation and integration-test boundary

The compiler produces deterministic SQL from the integrity-validated catalog while semantic/query flags remain pending; this does not itself issue an execution grant. The development authorizer separately checks source IDs, relationship IDs, physical objects, database, role, tenant/company bindings, catalog hash, explicit opt-in, and provenance. PostgreSQL execution has been verified only against isolated synthetic fixtures; no Development observation has been queried.

## PostgreSQL integration readiness update (2026-10-08)

The canonical catalog now contains the verified application-level line-item-to-statement logical relationship `rel_2588cf95d01a651a`. Captured PostgreSQL metadata reports no physical FK for this edge. The catalog records a many-to-one association, non-null key fields, possible orphan rows, and the available partial unique index for non-null source-item identity within a statement. Semantic approval and runtime query authorization remain false. A runtime snapshot builder validates source and hierarchy hashes/manifest lock and refuses missing references. The company binder exists, but no live ??? resolution or restricted PostgreSQL authorization was available, so no live value was retrieved.
The new `IStructuredQueryCompanyBinder` delegates to the existing canonical entity resolver rather than querying a parallel company registry. It requires a tenant identifier and returns typed ambiguity/not-found outcomes. Only the canonical internal `Companies.Id` becomes a plan binding; provider-scoped `ExternalCompanyId` is not substituted for that UUID. The underlying shared company master has no tenant column, so tenant data-access authorization continues to be enforced by the separate execution grant.

## Slice C acceptance update (2026-10-08)

The generic query algebra now executes bounded monthly comparisons, compatible-unit sums, top-N rankings, and explicitly bound multi-company amount aggregates. PostgreSQL fixture evidence meets the minimum execution acceptance. Year-over-year, industry/category comparisons, and the full held-out live Navigator-to-planner path remain incomplete. The full result matrix and safety boundary are recorded in [monthly-production-sales-evaluation-report.md](monthly-production-sales-evaluation-report.md). Production routing remains disabled pending catalog owner approval and runtime authorization.
