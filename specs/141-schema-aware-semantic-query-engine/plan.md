# Feature 141 — Implementation Plan

## Mandatory architecture invariant

Every implementation slice must preserve the `spec.md` objective: answer previously unseen financial questions by composing approved YAML catalog semantics through vectorless navigation and a generic validated query algebra. Do not add question-specific handlers, capabilities, intents, routes, or SQL templates. Catalog-only dataset expansion must work with the same generic engine. Preserve existing capabilities, financial calculations, provider provenance, units, period rules, and response/chart contracts. Semantic approval and runtime query authorization are independent gates; no phase activates generic database querying before both catalog and execution authorization gates pass.

## Delivery gates

Phase 0 and Phase 1 discovery/catalog foundation have been implemented; see `phase-0-1-report.md` and task evidence. Remaining slices are still planned. Implement in independently testable slices. The catalog generator, review flow, validation, documentation generation, and drift detection must be implemented and passing before the Dynamic Query Agent is enabled in any production environment.

### Feature-level architecture invariant

Every slice and acceptance test must advance the catalog-driven, composable query architecture described in `spec.md` and `design.md`. New query structures must be supported through approved catalog semantics and the shared query algebra, not per-question handlers, intents, routes, or SQL templates. Initial catalog coverage and disabled runtime execution are temporary MVP states, not final architecture limits. Preserve first refusal, calculations, execution behavior, and response/chart contracts of existing specialized capabilities. Phase exits and production gates include held-out query structures plus a catalog-only dataset expansion using the unchanged generic engine.

### Phase 0 — Repository and data contract inventory

- Confirm backend solution project/tooling layout, all EF DbContexts, migrations, configured PostgreSQL schemas, view usage, database grants, and deployment config patterns.
- Inventory all candidate query surfaces and classify them as existing specialized route, approved generic-read candidate, or excluded. Verify production/schema equivalence without reading financial rows.
- Map Feature 015 metric definitions and Features 076, 117–123, 129, 136, 131–132 plus scanner/lookup/chart contracts to the generic query boundary.
- Baseline representative query latency, model tokens, data coverage, source units, alias quality, and regression behavior.
- Exit: signed source inventory and approved initial catalog scope; uncertain objects remain excluded.

### Phase 1 — Physical schema discovery and deterministic YAML generation

- Implement EF model discovery, PostgreSQL metadata discovery, snapshot serialization, stable physical IDs, deterministic output, and hybrid diff.
- Implement merge preserving reviewed semantic metadata; mark stale/removed items and breaking drift without deleting.
- Connect using metadata-only credentials; support offline EF mode and clearly mark it unverified against live DDL.
- Exit: repeated deterministic output, integration fixture snapshots, and a staging PostgreSQL schema audit.

### Phase 2 — Catalog governance and lifecycle

- Add YAML schema, canonical package structure, approval/provenance states, schema and relationship validation, aliases, metric dependency/unit validation, and security/query policy checks.
- Add optional AI enrichment in dry-run suggestion mode and reviewer acceptance/rejection workflow (initially pull-request review is sufficient; no admin UI required).
- Generate Markdown from YAML; add docs-diff CI gate.
- Add drift report/CI validation and manual `catalog sync` workflow. Breaking changes fail publication; no automatic approval.
- Add approved-source product value discovery as a separate bounded workflow; create company-scoped identity, observed labels/codes, reviewed aliases, and optional reviewed cross-company category mappings. Keep unresolved equivalences pending review.
- Exit: all catalog lifecycle test cases pass; catalog is reviewed and versioned; generated docs are fresh; value discovery is bounded and has human approval controls.

### Phase 3 — Hierarchy generation and Vectorless navigation foundation

- Generate deterministic bilingual hierarchy nodes from approved YAML domains, metrics, dimensions, relationships, value dictionaries, and policies. Preserve single-source catalog IDs; validate node references, cycles/reachability, cross-domain links, and hash/version.
- Add `IVectorlessSchemaNavigator`, hierarchy provider, deterministic context expander, completeness validator, structured navigation result, bounded refinement, token/call/depth limits, and hash-keyed immutable summary cache.
- Keep exact identifier/alias matching and lightweight lexical filtering as supporting optimizations. Run lexical retrieval and optional offline-only embeddings as benchmarks, not production dependencies.
- Define query-plan JSON schema, typed outcomes, resolver requests, deterministic validation and logical-plan contracts without DB execution.
- Exit: catalog lifecycle complete; hierarchy generation is reproducible; vectorless node/context recall and completeness gate meet golden-set target without any unapproved identifier.

### Phase 4 — Deterministic compiler and read-only execution

- Implement the bounded composable algebra for selection/filtering, aggregation/grouping, ranking, trends, comparisons, growth only through governed definitions, and approved industry/product comparisons; document unsupported formulas and operations.
- Implement relational algebra compiler for the approved query operations and paths.
- Add SQL AST or equivalent generated-statement policy validation, parameter binding, transaction read-only enforcement, dedicated least-privilege DB role, timeout/row/cardinality limits, cancellation, and query telemetry.
- Add deterministic fixtures for joins, aggregation, unit/period rules, fan-out prevention, tenant boundaries, no-data, and cancellation.
- Exit: security and exact-result targets pass against integration PostgreSQL; catalog projection scope has security review.

### Phase 5 — Agent and V2 fallback integration, disabled by default

- Add Vectorless Schema Navigator and structured plan interpretation via existing provider-neutral AI abstraction; no MAF contracts leak outside Infrastructure. Use one navigation call for simple questions; cap complex/incomplete navigation at three calls, with a separately metered planner call unless safely combined.
- Add fallback admission only after all existing capabilities resolve unsupported. Execution errors/no-data on recognized routes cannot trigger fallback.
- Preserve Billing, actor/tenant scope, response pipeline, existing payload discriminators and V1 freeze.
- Add the generic result contract only if existing table response shape cannot express it. Define a versioned generic chart contract built deterministically from typed results; initially table-only until web, persisted-message and Telegram chart rendering pass compatibility gates.
- Exit: regression suite proves legacy route selection and outputs unchanged with flag both off and on for eligible fallback examples.

### Phase 6 — Evaluation, shadow, canary and production gate

- Complete Persian golden dataset and human-reviewed expected plans/results.
- Run offline navigation/extraction/completeness/execution evaluation and compare against the deterministic lexical baseline; shadow logging with no query execution first; internal read-only canary second.
- Report selected-domain accuracy, required-node recall, complete-context recall, relationship coverage, unsupported detection, number of LLM calls, input/output tokens, navigation/query latency and configured-price cost estimate.
- Require stable quality and cost metrics, no security findings, complete catalog drift runbook, alerts, rollback tested, and reviewer sign-off.
- Enable by tenant/percentage only after all criteria in `spec.md` are met. Keep master flag off by default in configuration until operator-controlled enablement.

## Dependencies

- Existing V2 orchestration (Feature 047/056), AI provider abstraction (014), financial semantic layer (015), entity/frame work (117–123), observability/evaluation (017–018/123), existing PostgreSQL EF persistence and schema migrations.
- Specialized financial route policies in Features 076, 129, 136, 131–132 and current scanners/lookups/charts remain authorities for their scope.
- Human domain review for catalog descriptions, data units, formulas, join safety, and query approval.
- Deployment support for a dedicated read-only PostgreSQL role and secret-managed metadata connection for the generator.

## Migration and rollback

- Initial catalog is file-based, additive, and has no production DB schema migration. Generated artifacts are versioned and activated atomically by catalog hash.
- Any eventual DB catalog persistence, embeddings, vector database, pgvector, or external RAG framework is outside this decision and requires a separate reviewed ADR/migration if later justified.
- Keep user-facing routes and prior executors intact. `Enabled=false` disables only the new unmatched-request fallback; existing capabilities continue untouched.
- Roll back by disabling the master flag, reverting the catalog/hierarchy artifact hash, and revoking/restricting generic runtime SELECT grants if the fault is security-related. No data rollback is needed for read-only execution.
- Never rollback into generic fallback after a specialized capability failure; report its established error outcome.

## Catalog curation update (2026-10-08)

- Live read-only PostgreSQL discovery has verified 18 candidate tables and reconciled the TradingInstruments definition and market-data FKs to EF Core.
- Owner-confirmed company/instrument lineage and `IsRepresented=true` restatement meaning are recorded in the catalog with explicit provenance. Codal restated-row preference is current implementation behavior, not an approved financial-selection policy.
- No runtime work, queryability change, schema change, or financial calculation change is part of this catalog update. Human review remains required for Codal selection policy, `IsComposing` source semantics, and instrument-to-company identity ambiguity.


## Phase 3/6 implementation update (2026-10-08)

The navigation contracts now have an implementation over `IAiModelExecutionService` and the generated JSON hierarchy artifact. An independent structured extraction pass preserves entities, measures, dimensions, periods, comparisons, standalone/consolidated statement scope, aggregation, ordering, and presentation requirements. Navigation is strict/versioned, may return multiple branches, validates all returned IDs/concepts against catalog context, and is capped at two navigation passes after extraction. Context governance reports pending navigation/semantic approval and non-queryable state; it does not grant execution access.

The fake integration test proves the service wiring and usage propagation. The new evaluation script scores captured output JSONL against the golden or an external held-out JSON dataset. A live provider evaluation, model-quality baseline, and runtime V2 integration are still pending. This remains a library-only phase and leaves generic query planning, compilation, database execution, routing and production activation for their specified later gates.

## Financial retrieval policy gate

- [x] Export source-item definitions and existing MetricCode mappings through a read-only session; omit statement observations and external-provider calls.
- [x] Generate a deterministic first-class source-item dictionary and hierarchy leaves while keeping semantic approval and query authorization false.
- [x] Carry typed financial-value references and operations through the vectorless navigator contract.
- [ ] Review source-item meanings, aliases, units, and mappings before any are approved.
- [ ] Define provider-specific latest/tie-break behavior and owner-approved negative-baseline growth semantics.
- [ ] Add a generic plan validator/compiler only after policy and security approval; keep SQL execution disabled until then.

## Token budgets, source navigation, and live evaluation (2026-10-08)

- [x] Measure the baseline `fa-02` truncation and `fa-value-01` outline overflow; record the provider completion reason and pre-change sizes.
- [x] Compact navigation output; retain truncation metadata; allow one bounded retry under request-wide limits.
- [x] Normalize Persian source text, rank a bounded candidate set, prune only its lowest-ranked tail to fit, and resolve independently validated direct source references to catalog nodes.
- [x] Enforce four total calls per request, at most three navigation calls, 18,000 input tokens, 4,200 output tokens, 45-second request timeout, 20-second call timeout, an 18,000-character outline, 10,000 estimated context tokens, and bounded depth/fan-out.
- [x] Keep retrieval completeness, semantic review, query authorization, and executability as separate outcomes.
- [x] Make the CLI incremental and failure-isolated, record failure rows without predictions, retry only failed rows, and score structured `IsComposing` expectations.
- [x] Run the final golden set and a separate, unseen held-out v3 set with the configured Development OpenAI provider. Results are in the final evaluation report and scored JSON artifacts.
- [ ] Review semantic meaning and query authorization. All completed cases remain non-executable until those gates are approved; two golden cases hit the request-wide input budget.
- [ ] Implement a generic planner/compiler only after semantic review and authorization. Query execution remains disabled.

## Recall optimization follow-up (2026-10-08)

- [x] Build a per-case baseline failure matrix distinguishing extraction, reference resolution, hierarchy recall, request budget, and governance.
- [x] Prioritize deterministic catalog branches from independent required IDs; preserve fan-out/node bounds and surface partial expansion.
- [x] Retain the conservative request input reservation after prompt-only reservations exceeded the 18,000-token request cap in a live run.
- [x] Run and score all 21 Golden cases with the configured Development provider: node recall 0.9103, metric recall 0.8462, dimension recall 0.9048, source-item reference accuracy 0.7500, and zero request-wide input-budget failures.
- [x] Run a newly composed independent held-out v4 set separately: node/metric/dimension recall 0.6667; one independent entity-kind validation failure.
- [x] Add regression coverage for required-reference priority through a 119-child source-item branch; focused .NET tests pass.
- [ ] Improve independent terminology/entity extraction, prevent unsupported entity-kind output, improve source-item accuracy, and raise held-out recall before Query Planner readiness review.
- [ ] Review catalog semantics and query authorization. Query execution remains disabled.

### Final score correction

- [x] Retry the three failed final-code Golden rows with the existing resume mode; final scoring has no request-input-budget failures.
- [x] Run a fresh independent held-out v5 fixture; node/metric/dimension recall 0.6667 and one extraction validation failure.
- [ ] Fix the end-to-end selected-node overflow (observed maximum 60 vs intended 48), exact-domain regression (0.0000), and remaining metric/source-item/held-out recall gaps before readiness review.

## Compiler and executor implementation status (2026-10-08)

The compiler and bounded Npgsql executor are implemented as an isolated Feature 141 service slice and registered without workflow/route integration. Only direct source-item, latest-period scalar/table retrieval is supported. Focused tests exercise parameterization, absent `MetricCode`, immutable catalog hash gating, source identity, relationship authorization, and default-disabled execution. No live database query ran: catalog query authorization remains pending, and no explicit development database/role/source allowlist is configured. The remaining plan is to produce the validated runtime SQL snapshot, obtain separately reviewed development-only source/field/relationship authorization and a SELECT-only DB role, add disposable PostgreSQL tests, then extend the safe algebra in catalog-driven increments. Production route activation remains out of scope.

### First real PostgreSQL query follow-up (2026-10-08)

Completed: canonical application relationship metadata and validated runtime snapshot construction. Blocked: generic ambiguity-safe company binding and independent least-privilege Development database setup. Keep generic execution disabled and do not report a numeric result until the full Navigator-to-executor chain succeeds against the restricted role.
The company binding seam is now implemented as an adapter over the existing canonical resolver, with tenant-required binding and typed ambiguity. Runtime verification of the ??? row still depends on the authorized Development PostgreSQL setup.

## Slice C plan status (2026-10-08)

- Completed: typed comparison, sum aggregation, ranking, and multi-company query support through the generic compiler/executor.
- Verified: isolated PostgreSQL execution for direct retrieval, MoM growth, company totals, product ranking, ambiguity, revision/output filtering, and authorization rejection.
- Pending: owner review/query authorization; company-by-sales/growth ranking; industry/category policy; planner+compiler live inference evaluation; Development activation.
- Evaluation gate: only 6/8 completed original rows and 2/2 completed held-out rows extracted the expected measure. Five original rows and one held-out row failed extraction/navigation limits.
