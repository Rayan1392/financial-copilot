# Feature 141 Ã¢â‚¬â€ Implementation Tasks

Tasks are grouped into deployable slices; catalog lifecycle must complete before any production Dynamic Query Agent activation. Each task depends on its listed prerequisites and, absent an exception, the completed preceding task in its slice. A task is complete only when its acceptance criteria and named tests/evidence pass. No task authorizes changes to V1 or replacement of established specialized routes.

## Mandatory objective gate

Tasks must advance the `spec.md` architecture: Financial Copilot answers previously unseen financial questions by composing the canonical YAML Semantic Catalog, Vectorless Schema Navigator, deterministic validated read-only query engine, and response generation. Do not introduce question-specific handlers, capabilities, predefined intents, routes, or SQL templates. Future catalog-only dataset expansion must use unchanged generic engine logic. Protect existing specialized capabilities, financial calculations, provider provenance, units, periods, and response/chart contracts. Semantic approval does not authorize queries; runtime activation stays blocked until explicit query authorization and all execution gates pass.

## Phase 0/1 execution status (2026-10-08)

- **Complete â€” T141-011, T141-012.** Evidence: all seven EF contexts build offline; live PostgreSQL objects and migration history were discovered read-only; offline and redaction checks passed.
- **Partial â€” T141-001, T141-010, T141-013.** Candidate scope is awaiting owner review; broad schema/rename fixtures and injected drift-class tests remain. Current evidence: `SemanticCatalog/catalog-scope.yaml`, `artifacts/schema-snapshot.json`, `artifacts/reconciliation.json`, and `artifacts/catalog-drift.json`.
- **Partial â€” T141-020, T141-021.** Deterministic YAML-subset package, stable physical IDs, source provenance, reviewed-semantic preservation, stale-object reporting, relationship/dimension reference checks, and offline generation exist. Full YAML schema enforcement, rename mappings, and comprehensive injected-drift cases remain.
- **Partial â€” T141-034.** Bounded company-scoped source-value observations and separate pending alias/category proposals are generated. Full review/refresh/retirement workflow is not implemented.
- **Partial â€” T141-033.** `discover` and self-test entry points exist; the full lifecycle command suite and CI wiring remain.
- **Partial â€” T141-031, T141-032, T141-033, T141-045, T141-070, T141-072 (2026-10-08).** Added deterministic curated Markdown and a generated 30-node bilingual hierarchy with separate navigation review, semantic approval, and query authorization. The canonical source hash now uses the versioned `feature141-semantic-catalog-v1` contract over parsed JSON-subset YAML, and the generated hierarchy hash is independently pinned by `manifest.yaml`. Python `catalog.py validate` checks package indexes, generated hierarchy/Markdown reproducibility, hashes, references, reachability, cycles, and the manifest lock without writing. The .NET `validate-catalog` command and runtime hierarchy provider independently recompute source and hierarchy hashes and fail closed on source drift, artifact edits, and manifest-lock mismatch. Focused Python and .NET tamper tests pass. No repository CI workflow exists to attach the validator to; the exact validation commands are documented in `tools/SemanticCatalog/README.md`. Approved navigation review and instrument-policy review remain; live-provider quality metrics remain unmeasured.
- No task in T141-022 or later is complete. All generated physical and value metadata remains non-queryable; runtime is off.

## Slice 0 Ã¢â‚¬â€ Schema and semantic surface inventory

### T141-001 Ã¢â‚¬â€ Verify generation sources and safe query scope

**Status: Partial.** The live inventory, source policies, and candidate/exclusion list are present in `SemanticCatalog/catalog-scope.yaml`; owner review/approval and the Phase 0 signed-scope gate remain.

- Inventory all EF DbContexts, migrations, provider mappings, views, database schemas, feature-specific source filters, product/company identities, units, and existing specialized capabilities.
- Produce a reviewed `catalog-scope.yaml` candidate list with included/excluded objects, purpose, data classification, row ownership, source policies, and owner.
- Verify the existing CLI/tooling project and connection-string configuration patterns.
- Acceptance: no unverified physical field, join, unit, or formula is marked queryable; sample question support is mapped to actual persisted source coverage or explicitly deferred.

## Slice 1 Ã¢â‚¬â€ Automatic PostgreSQL and EF schema discovery

### T141-010 Ã¢â‚¬â€ Define schema snapshot contracts

**Status: Partial.** The snapshot contract covers live/EF objects, columns, keys, relationships, indexes, constraints, and provenance. Multi-schema/casing and rename-map fixture coverage remains.

- Define provider-neutral discovery output for schemas, tables/views, columns, type/nullability/defaults, PK/FK, indexes, constraints, EF mappings and source provenance.
- Stable ID normalization and rename mapping; deterministic sort/serialization.
- Tests: stable IDs, multiple schemas, quoting/casing, nullability/type changes, FK/composite key and deterministic hashes.
- Dependencies: T141-001.
- Acceptance: snapshot contract can represent every discovered physical fact with source provenance and stable references without semantic approval fields being confused with database facts.

### T141-011 Ã¢â‚¬â€ Implement EF Core model discovery

**Status: Complete.** Evidence: offline snapshot enumerates all seven EF contexts; offline test verifies `LiveVerified=false` and no fabricated values.

- Discover mapped entities/owned types/table-splitting, column names, keys, relationships and provider types from all verified contexts using design-time configuration.
- Must function without connecting to a database. Exclude ignored/unmapped CLR properties.
- Tests: representative model snapshot and design-time failure diagnostics.
- Dependencies: T141-010.
- Acceptance: all verified EF contexts can produce an offline deterministic model snapshot; ignored/unmapped CLR members are absent.

### T141-012 Ã¢â‚¬â€ Implement PostgreSQL metadata discovery

**Status: Complete.** Evidence: live run captured PostgreSQL objects, columns, constraints, indexes, views, and migration history without printing the connection string.

- Query `pg_catalog`/`information_schema` for configured schemas, tables, views/materialized views, columns, types/nullability/defaults, PK/FK, indexes and constraints; do not read business rows.
- Use a metadata-only secret-managed connection and least-privilege account; redact connection credentials.
- Tests: real ephemeral/integration PostgreSQL schema and missing-permission/unavailable-DB cases.
- Dependencies: T141-010.
- Acceptance: configured schemas and required physical metadata are read without selecting business data; credentials are secret-managed and redacted.

### T141-013 Ã¢â‚¬â€ Hybrid reconciliation and schema drift report

**Status: Partial.** The live report classifies six object/index differences and removed EF-only catalog objects are retained and reported. Exhaustive injected drift-class tests remain.

- Compare EF and deployed physical snapshots; retain source-by-source facts and classify discrepancies, added/removed/modified items, inferred/unmapped database objects, and breaking changes.
- Offline mode reports `liveVerified=false`; publication gate requires current snapshot/live check.
- Tests: each schema drift class, EF-only mapping, DB-only view, and unchanged run.
- Dependencies: T141-011, T141-012.
- Acceptance: all physical diffs are classified; removals and incompatible changes block publication without deleting reviewed semantic metadata.

## Slice 2 Ã¢â‚¬â€ YAML generator, merge, and approval provenance

### T141-020 Ã¢â‚¬â€ Define canonical YAML schema and package

**Status: Partial.** The initial package and separated physical/semantic files exist; exhaustive YAML schema/unknown-field validation remains.

- Add versioned YAML schema and `SemanticCatalog/` structure for tables, relationships, metrics, dimensions, dictionaries, catalog manifest.
- Separate generated `physical` metadata from semantic `description`, aliases, policy and examples; define stable IDs, state/provenance, review metadata and replacement mapping.
- Reject duplicate keys, unknown fields, unsupported format versions and invalid statuses.
- Dependencies: T141-001, T141-013.
- Tests: schema fixtures for valid/invalid format, duplicate IDs/keys, provenance states and separated physical/semantic sections.
- Acceptance: YAML schema describes physical discovery, semantic approval, relationships, metrics, dimensions, dictionaries and manifest without asserting unverified objects.

### T141-021 Ã¢â‚¬â€ Generate deterministic YAML and preserve approved content

**Status: Partial.** Repeated live runs produced identical physical schema and catalog hashes; reviewed semantic metadata is preserved and marked stale after physical drift. Rename mapping and full drift fixture coverage remain.

- Generate physical files from schema snapshot, merge by stable ID and preserve reviewed/approved descriptions without overwrite.
- New objects/columns are discovered/unapproved; missing/modified objects produce obsolete/breaking state and report, never silent deletion.
- Tests: repeatability, manual metadata preservation, rename, deletion, type change, and generation with no live DB.
- Dependencies: T141-020.
- Acceptance: unchanged inputs produce stable YAML/hash; approved descriptions/aliases/policies are not overwritten; new physical items are non-queryable.

### T141-022 Ã¢â‚¬â€ Add human approval workflow

- Implement documented pull-request review flow with reviewer identity, approval timestamp/version, accepted/rejected AI proposals, and queryability approval.
- Until approval, metadata cannot enter runtime navigation hierarchy or query surface.
- Dependencies: T141-021.
- Tests: approve/reject/split proposal, reviewer attribution, stale approval after catalog hash change, runtime exclusion of unreviewed data.
- Acceptance: an unapproved generated or enriched entry cannot compile into SQL.

### T141-023 Ã¢â‚¬â€ Optional AI enrichment proposal command

- Generate Persian/English descriptions and terminology suggestions only from discovered schema and source material; output dry-run proposal JSON/YAML overlay with model/prompt/hash provenance.
- Enforce allowed output schema and compare every referenced object/field/relationship with physical snapshot. No model call creates formula or executable join.
- Tests: fabricated column/FK/formula rejected, suggestions marked unverified, provider unavailable graceful behavior.
- Dependencies: T141-020, T141-021.
- Acceptance: generated descriptions and synonym/relationship suggestions remain proposals; the command cannot approve or create executable metadata.

## Slice 3 Ã¢â‚¬â€ Catalog validation, Markdown, CI, and hierarchy

### T141-030 Ã¢â‚¬â€ Implement catalog validator

- Validate YAML, physical references, keys/FKs/join paths, metric dependencies/types/units, aliases after Persian normalization, descriptions, approval status, schema allowlist, query/tenant policy and obsolete entries.
- Report errors/warnings/breaking changes with stable codes and nonzero CLI exit for blocking errors.
- Tests: one fixture per validator rule and valid catalog acceptance.
- Dependencies: T141-020, T141-021, T141-022.
- Acceptance: errors/warnings have stable codes; invalid physical references, joins, formulas, aliases, approval states, and policy block publication.

### T141-031 Ã¢â‚¬â€ Generate Markdown documentation from YAML

**Status: Partial.** Curated catalog review Markdown is rendered deterministically from canonical YAML; full per-metric/relationship/dictionary docs, version/hash stamping, and stale-doc CI checks remain.

- Generate per-table/metric/relationship/dictionary pages, examples and supported operation docs; include catalog version/hash and no-edit generated marker.
- CI regenerates and fails when committed Markdown is stale. YAML remains the only semantic source.
- Dependencies: T141-020, T141-030.
- Tests: generated pages cover table/field, metric/formula reference, relationship, dictionary, example question, and supported operations; deterministic hash and diff test.
- Acceptance: documentation output is reproducible, version-stamped, and cannot be read back as a semantic source.

### T141-034 Ã¢â‚¬â€ Discover and govern product/value dictionaries

**Status: Partial.** Bounded company/provider-scoped observed values and separate non-executable proposals exist; governance lifecycle and comprehensive collision review remain.

- Discover distinct values only through bounded allowlisted data paths. Preserve company-scoped product identities; propose Persian/English labels, aliases, and cross-company categories separately; add human-review, refresh, stale/retire, and provenance lifecycle.
- Tests: same label across companies, ambiguous product equivalence, unit mismatch, source changes, aliases, category effective dates, role/row bounds.
- Dependencies: T141-001, T141-020, T141-021.
- Acceptance: observed values and proposals never become canonical equivalences without approval; product IDs/period history survive dictionary updates.



### T141-032 Ã¢â‚¬â€ Build and validate navigation hierarchy

**Status: Partial.** `catalog hierarchy build` deterministically generates bilingual YAML and Markdown with stable IDs, source references, relationships, provider/unit/period metadata, and a catalog hash. Navigation metadata approval, semantic approval, and query authorization are represented separately; all generated nodes remain pending review and non-queryable. Full validator, CI, cache, and approved-branch runtime behavior remain.

- Generate and validate a deterministic bilingual navigation hierarchy from approved catalog nodes, object/metric/dimension/relationship references, policies, and dictionaries; maintain optional exact-match/lexical lookup aids as supporting filters only.
- Hash/cache immutable node summaries by catalog version, hierarchy schema version, node ID and language; invalidate on catalog or dictionary change.
- Tests: tree determinism, shared cross-domain dimension, invalid node refs/cycles/unreachable nodes, lexical shortcuts, no unapproved hit, stable hash, stale hierarchy/cache fail-closed behavior.
- Dependencies: T141-030, T141-031, T141-034.
- Acceptance: vectorless hierarchy is the primary semantic navigation structure; lexical aids do not decide business-domain selection; all hierarchy references validate against approved YAML.

### T141-033 Ã¢â‚¬â€ Wire lifecycle CLI and CI pipeline

**Status: Partial.** `discover`/`--self-test` CLI paths and offline mode exist; other lifecycle commands and CI gates are pending.

- Provide `discover`, `sync`, `enrich --dry-run`, `values discover/sync`, `validate`, `docs`, and `hierarchy build` commands (final executable path confirmed by T141-001).
- CI uses a pinned schema snapshot or isolated PostgreSQL service; verifies generation/merge, catalog validation, docs freshness, dictionary review state, and hierarchy build.
- Completion gate: schema discovery, YAML generation, enrichment proposal, human approval, value discovery/review, docs, validation, hierarchy/caches, and drift detection all work before production agent flag can be enabled.
- Tests: CLI command integration, deterministic outputs, nonzero exit for invalid/drifting catalog, CI gate against stale generated docs/hierarchy, and connection secret redaction.
- Dependencies: T141-010 through T141-032 and T141-034.
- Acceptance: commands are deterministic, return nonzero on blocking failures, do not print secrets, and keep discovered/unreviewed metadata out of runtime hierarchy.

## Slice 4 Ã¢â‚¬â€ Vectorless navigation, query plan, and entity resolution

### T141-040 Ã¢â‚¬â€ Define generic structured query plan and outcomes

- Define versioned JSON schema for operation algebra, entity mentions, catalog metric/dimension IDs, period, filters, grouping, comparison, order/limit, presentation, and clarification state.
- Include typed unsupported/ambiguous/no-data/limit/error outcomes; do not create one hardcoded intent per question.
- Tests: Persian/English structured-output serialization and invalid-schema rejection.
- Dependencies: T141-032, T141-030.
- Acceptance: versioned plan covers supported query operations and explicit typed ambiguity/unsupported/incomplete outcomes without introducing one intent per phrase.

### T141-041 Ã¢â‚¬â€ Implement Vectorless Schema Navigator

- Propose `IVectorlessSchemaNavigator.NavigateAsync` with typed request/result, hierarchy provider, and deterministic context expander. Understand question, select one or more branches, and expand approved child records/joins.
- Use existing provider-neutral structured output capability; provide only approved outline/context and relevant route admission. Exact entity aliases may be resolved as deterministic shortcuts.
- Tests: one-call simple query, multiple domains, indirect Persian terms, known-node expansion, invented ID, prompt injection, max calls/depth/tokens, versioned cache invalidation and timeout.
- Dependencies: T141-032, T141-033, T141-034.
- Acceptance: simple requests target one navigation LLM call; complex/incomplete requests use at most three navigation calls; navigator returns selected nodes, deterministic expansions, requirements, and catalog version without SQL or invented IDs.

### T141-045 â€” Validate navigation-context completeness

**Status: Partial.** Added versioned typed navigation/context contracts and a deterministic completeness validator that compares independently supplied measures, dimensions, entities, relationships, source/period policies, comparison scopes, and presentation requirements against retrieved context. Requirement extraction, catalog identity providers, expansion implementation, and the full invalid-reference/refinement test matrix remain.

- Implement `ISchemaContextCompletenessValidator` to verify that selected context includes every required measure, dimension, canonical entity binding request, period policy, source/read-model policy, metric definition, and approved join path before query planning.
- Allow bounded refinement only when the navigator requests known child node IDs; after the call/depth/token limit, return typed incomplete/unsupported/clarification.
- Dependencies: T141-032, T141-041.
- Tests: missing each required slot class, valid multi-domain context, invalid/unknown IDs, unsafe relationship path, refinement cap, and unsupported question.
- Acceptance: incomplete context is always rejected before planning/SQL; completeness checks are deterministic and independent of model assertions.

### T141-044 Ã¢â‚¬â€ Integrate structured planner with navigator output

- The structured planner consumes `SchemaNavigationResult`, then emits the versioned composable query plan using only selected, validated catalog IDs. For simple questions it may be combined with final navigation output when schema completeness still receives independent application validation.
- Tests: structured JSON schema, exact metric/dimension/period fields, completeness mismatch, unsupported ratio/growth formula, Persian synonym, token/call telemetry.
- Dependencies: T141-040, T141-041, T141-045.
- Acceptance: no plan may refer to a non-retrieved/unapproved ID; unsupported operations remain typed and cannot expand the algebra.

### T141-042 Ã¢â‚¬â€ Resolve entities and normalize periods

- Reuse `ICompanyResolverService` for companies, preserve canonical identity and typed ambiguity where available; align with Feature 119 before broad rollout.
- Resolve products company-scoped using reviewed dictionaries and Features 129/136 policy; add other entity resolvers only for approved dimensions.
- Tests: Persian aliases, ticker/name ambiguity, Arabic/Persian character variants, product cross-company collision, Jalali periods, unsupported period.
- Dependencies: T141-034, T141-041.
- Acceptance: companies resolve through canonical company identity; products resolve only by approved company-scoped identities/aliases; unresolved and ambiguous outcomes never guess.

### T141-043 Ã¢â‚¬â€ Validate semantic plans and approved join graph

- Resolve IDs against exact catalog version; check table/column approval, metric units and calculation owner, allowed aggregation, filters, joins, cardinality, tenant policy, time range, and output limit.
- Reject ambiguous join paths, unsafe fan-out, unsupported operations and attempts to use specialized-only calculation data.
- Tests: join correctness, path ambiguity, cross-tenant plans, incompatible unit, unapproved field, missing metric policy.
- Dependencies: T141-040, T141-042, T141-044.
- Acceptance: every plan field and join is approved and complete; invalid units, missing period policy, cross-tenant access and unsafe fan-out are rejected before compilation.

## Slice 5 Ã¢â‚¬â€ Deterministic compiler and read-only execution

### T141-050 Ã¢â‚¬â€ Compile validated logical plan to SQL

- Compile only approved relational algebra to one parameterized SELECT/read-only CTE; generate identifiers from catalog metadata, values as parameters.
- Never accept LLM SQL. Add generated SQL AST/policy validation and query fingerprint.
- Tests: exact SQL shape, parameterization, injection payloads, no multiple statements, no unapproved identifiers/functions/operators.
- Dependencies: T141-043.
- Acceptance: compiler output is deterministic parameterized SELECT/read-only CTE using only validated identifiers and relationships; no model-provided SQL reaches executor.

### T141-051 Ã¢â‚¬â€ Add least-privilege runtime database role and bounded executor

- Provision dedicated SELECT-only grants for approved read surface; enforce read-only transaction, statement timeout, row/join/cardinality limits, cancellation, concurrency and tenant filters.
- Configure no table access by default; prefer curated views when source-specific safeguards cannot be proven in joins.
- Tests: role denial of write/DDL/unapproved tables, timeout/cancel, row cap, tenant isolation.
- Dependencies: T141-050.
- Acceptance: dedicated principal cannot write or read outside the explicit approved surface; time, row, join and concurrency limits are enforced.

### T141-052 Ã¢â‚¬â€ Add evidence and typed result contract

- Return typed values/nulls, column labels/types/units, source/metric provenance, periods, freshness, catalog hash, truncation, and query fingerprint.
- Tests: reproducible evidence for each aggregate, missing values, unit conversions, no fabricated values, stable API serialization.
- Dependencies: T141-051.
- Acceptance: each displayed numeric value has reproducible source/metric, period, unit and calculation evidence; null/truncation/freshness remain explicit.

## Slice 6 Ã¢â‚¬â€ Response integration, routing, and compatibility

### T141-060 Ã¢â‚¬â€ Implement explicit unmatched-request admission

- Add V2-only fallback after all enabled governed/specialized routes. Define `Unmatched` separately from `ExecutionFailed`, `NoData`, and `Clarification`.
- A recognized capability failure/no-data/timeout never enters the generic path. Existing route ordering and V1 freeze remain intact.
- Tests: recognized trends, financial comparisons, price/financial charts, analysis queries and product analytics use existing route; fallback only for unmatched request.
- Dependencies: T141-041, T141-043.
- Acceptance: recognized capability success, no-data, clarification, ambiguity, failure or timeout never enters generic fallback; only explicit unmatched admission can.

### T141-061 Ã¢â‚¬â€ Add result to existing workflow and rendering contracts

- Reuse `AgentExecutedMessage`, response computation, conversation persistence, API envelope and current front-end/Telegram patterns. Add a versioned generic table content block only if audit shows current contract cannot represent it.
- Preserve `MonthlyActivityTrendResponse` / `monthlyActivityTrendChart` payloads and all existing discriminator mappings.
- Tests: old persisted response deserialization, web and Telegram generic table, chart compatibility snapshots.
- Dependencies: T141-052, T141-060.
- Acceptance: old response discriminators round-trip unchanged; generic chart content is generated and validated from typed result only.

### T141-062 Ã¢â‚¬â€ Add feature options, telemetry, evaluation hooks

- Add master-off, shadow and internal canary configuration; enforce fail-closed on stale catalog/hierarchy and do not touch specialized route flags.
- Record route admission, retrieval IDs, schema/version, usage, latency, SQL fingerprint, row counts and typed failure reason with existing redaction policy.
- Tests: flags off/on, shadow behavior, tenant gates, disabled/stale catalog, redaction and telemetry.
- Dependencies: T141-060, T141-061.
- Acceptance: master defaults off; stale hierarchy fails closed; telemetry captures route, node/context, call/token, query and failure data without unauthorized query prose/data.

## Slice 7 Ã¢â‚¬â€ Golden evaluation and production activation gate

### T141-070 Ã¢â‚¬â€ Build Golden Test Dataset and Vectorless evaluation runner

**Status: Partial.** Added `evaluation/navigation-golden-v1.yaml` with 10 requested representative query types plus three held-out compositional/unsupported cases, expected domains/nodes/catalog references, semantic policies, completeness expectations, and specialized-route precedence. No navigation evaluation runner or measured recall/correctness report exists yet.

- Include legacy and new Persian prompts covering exact symbols, financial synonyms, indirect product language, multi-domain/multi-table queries, product aliases/categories, ambiguity, unsupported metrics, and chart asks. Include held-out query structures that combine approved catalog concepts in ways not represented by one dedicated example. Store expected route, selected domains/node IDs, full required context, relationships, plan, canonical entities, query parameters, exact fixture rows/aggregates, outcome, and evidence.
- Compare Vectorless node/context recall and final correctness with a deterministic lexical baseline. Report domain selection, required-node recall, completeness, metric/dimension/entity extraction, joins, unsupported detection, LLM calls, tokens, retrieval/total latency, configured-price cost estimate, and regression delta. Embeddings are optional offline-only.
- Add a catalog-only coverage-expansion evaluation: add reviewed source/metric/relationship metadata for a dataset and prove new composable query structures work without changing question dispatch, navigator, planner, or compiler code. Golden prompts must not become a runtime allowlist.
- Dependencies: T141-001 through T141-062.
- Acceptance: meets spec thresholds with zero unsafe plans, zero security violations, and zero protected legacy route/response changes; held-out supported query structures succeed without question-specific code, and generic requests do not dispatch through per-question handler/template registries.
- Tests/evidence: frozen golden-run report, lexical-baseline comparison, fixture exact-result suite, and legacy regression snapshots.

### T141-071 Ã¢â‚¬â€ Run shadow and canary release gates

- Shadow first records interpretation and expected admission without querying user data; internal canary executes only reviewed catalog plans read-only.
- Production activation requires all catalog lifecycle slices complete; security review; acceptance thresholds; unchanged legacy snapshots; runbook, alerting, and rollback rehearsal.
- Dependencies: T141-062, T141-070.
- Tests/evidence: zero-query shadow verification, internal read-only canary results, flag rollback exercise, catalog-drift response exercise, signed production gate checklist.
- Acceptance: production remains disabled until all catalog generation/review/validation/hierarchy/value-dictionary requirements and quality/security gates pass.

## Catalog curation follow-up review

### T141-072 â€” Review instrument identity and statement flag semantics

**Status: Partial.** Product-owner-confirmed IsAudited/IsRepresented/IsComposing meanings and the generic-path parent-company default are recorded with provenance. The CodalDb `preferConsolidated=true` conflict is documented without behavior changes. Instrument-resolution collision/provider review and restated-selection/tie-break approval remain open.

- Review the 18-table candidate scope, physical TradingInstruments metadata, and all four required market-data foreign keys against the live snapshot.
- Confirm the optional instrument-to-company mapping policy, including provider namespace and ambiguous `Companies.InstrumentCode` matches; do not resolve by ticker/name.
- Approve or revise CodalDb preference for restated statements and `StmtId` tie-break behavior; verify upstream `IsComposing` true/false semantics.
- Keep `IsRepresented` as the physical field name and preserve `business-owner-confirmed` provenance for its Restated meaning.
- Acceptance: approval decisions are recorded separately; no candidate becomes runtime-queryable until explicit scope approval, and no code selection behavior changes in this review task.

### T141-073 â€” Resolve approved direct Noavaran source statement items

**Status: Pending.** Source observations can exist without a governed `MetricCode`; the generic
query path must resolve approved direct source items from the source-item catalog and related
statement observations.

- Bind a direct-source measure by approved provider, `StatementType`, and original
  `SourceItemId`; join through `FinancialStatementLineItems.SourceItemCatalogId`.
- Keep direct source-item selection distinct from governed metric resolution. Never synthesize a
  `MetricCode`, unit, or financial formula from a Persian title.
- Preserve `IsComposing=false` as the default parent scope, honor explicit consolidated scope,
  and retain `IsRepresented` and `IsAudited` in source evidence and selection policy.
- Reject unreviewed catalog meanings and surface missing source identity as unavailable evidence;
  do not substitute raw JSON or a similar-title item silently.
- Acceptance: approved unmapped Noavaran observations are individually queryable with original
  source identity, value, unit, period, statement variant, and raw-payload provenance, while
  existing governed metric readers and specialized paths remain unchanged.
- Audit and migration/backfill constraints: see
  [`NoavaranFinancialStatementIngestionAudit.md`](NoavaranFinancialStatementIngestionAudit.md).





## Phase 2 navigator and evaluation progress (2026-10-08)

- **Partial â€” T141-031, T141-032, T141-045, T141-070.** Added `LlmVectorlessSchemaNavigator` on the existing provider-neutral `IAiModelExecutionService`, strict version-1 structured outputs, independent requirement extraction, catalog ID/concept validation, bounded context expansion/completeness refinement, and extraction-plus-navigation usage reporting. The JSON hierarchy provider returns approval and query-authorization state independently. A unit integration test exercises fake model responses. `evaluation/run_navigation_evaluation.py` scores the golden fixture or a caller-supplied held-out dataset from recorded JSONL predictions.
- The offline scorer reports per-case and aggregate domain/node/metric/dimension/relationship/scope/outcome measures and call/token/latency/reported-cost usage. A .NET CLI can run the navigator against the golden or an external held-out JSON dataset and emit the scorer's JSONL input. The live run was blocked before inference with `hosted_provider_credentials_missing`; no requests or quality scores were produced. Fake-model tests are contract evidence only.
- Verification: application and SemanticCatalog CLI builds succeeded; the complete UnitTests project passed 1,912 tests; the scoring utility parsed all 13 golden cases with zero predictions and correctly returned no quality scores.
- Run with the hierarchy path in `Feature141:SchemaNavigator:HierarchyPath`, budgets in `Feature141:SchemaNavigator:Budgets`, and credentials in existing provider settings: `dotnet run --project tools/SemanticCatalog -- evaluate-navigator --tenant-id <guid> --dataset <json-or-golden-yaml> --hierarchy specs/141-schema-aware-semantic-query-engine/artifacts/navigation-hierarchy.yaml --output <predictions.jsonl>`, then score with `python specs/141-schema-aware-semantic-query-engine/evaluation/run_navigation_evaluation.py --dataset <same-dataset> --predictions <predictions.jsonl> --output <report.json>`.
- Navigator budget defaults: at most three total calls including independent extraction (two navigation passes), depth two, 10,000 estimated catalog-context tokens, 18,000 estimated total input tokens (conservatively estimated at two characters/token), 24,000 hierarchy characters, 36,000 prompt characters, 700 output tokens per call, 20 second per-call timeout, 45 second total request timeout, fan-out 12, optional provider-reported cost ceiling. Provider-reported usage/cost telemetry still comes from the existing AI execution abstraction.
- Workflow integration, deployment hierarchy-path configuration, catalog review approval, canonical hash recomputation on read, and query authorization remain unimplemented. The navigator is registered for injection and harness use, but no V1/V2 routing or query execution behavior changed.

## Financial value retrieval and source-item catalog (2026-10-08)

- [x] Read-only export of the persisted `FinancialStatementSourceItems` definitions and existing mappings; no observations or provider calls.
- [x] Deterministic dictionary and navigation leaves for all 296 exported definitions; unknown units/titles remain unknown and query authorization is false.
- [x] Add typed financial reference and operation contracts, bounded candidate outline selection, and deterministic comparison guards.
- [x] Record the policy in the canonical catalog and golden evaluation expectations.
- [ ] Conduct semantic review and separately approve any query access.
- [ ] Implement a generic planner/compiler behind the same catalog path after the approval gates; no query route or executor was activated here.
- Verification for this slice: 1,920 UnitTests passed; 17 SemanticCatalog Python tests passed; .NET/Python canonical catalog and hierarchy hash validation passed; all 21 golden cases parsed and the eight financial retrieval cases passed a synthetic evaluator contract check.


## Live navigator evaluation ? 2026-10-08 (provider mismatch follow-up)

- [x] Fixed evaluator configuration to resolve the same Development environment and User Secrets as the API; kept provider selection, DI, structured-output adapter, endpoint, and model explicit and shared.
- [x] Added safe provider diagnostics (HTTP status, upstream error code/request ID and allowlisted quota headers); hard quota errors do not retry. CLI logs never include secret values.
- [x] Added environment/configuration precedence and missing-secret tests; added quota-no-retry metadata assertion; made root help/version side-effect-free.
- [x] Verified one real `fa-01` structured-output smoke: 2 calls, 10,662 input tokens, 1,081 output tokens, 13,257 ms model latency; IDs validated. The score is partial and the result is Unsupported under current catalog approval/query gates.
- [ ] Full golden quality evaluation remains blocked: the 21-case run stopped at `fa-02` when navigation output was truncated at the configured 700-token output limit. One row was written; no full-dataset score is reported.
- [ ] Dedicated `fa-value-01` smoke remains blocked before inference by `catalog_outline_budget_exceeded`; fix the bounded outline selection/size before claiming source-item retrieval quality.
- Verification: 12 unit tests passed; quota-no-retry integration test passed; catalog hash validation passed; root help/version left generated artifact hashes unchanged. See `evaluation/live-evaluation-report-2026-10-08.json`.
- The old quota response came from the evaluator's different, manually injected `OPENAI_API_KEY`; the corrected API-equivalent User Secrets configuration succeeded against OpenAI `gpt-5.6-luna` at `api.openai.com`. No fallback model or quota bypass was used. No statement observations or database queries were made.

## Navigator token budget and live evaluation closure (2026-10-08)

- [x] Reduce navigation output to compact canonical decisions and preserve Responses API truncation metadata as `ModelOutputTruncated`.
- [x] Add one bounded adaptive retry and request-wide model-call, token, latency, context, and fan-out enforcement.
- [x] Rank Persian source-item candidates with bounded progressive disclosure; confirm item 161 wins over similar item 140 and resolve the direct-source reference by canonical ID.
- [x] Report context retrieval, semantic review, query authorization, and executability separately.
- [x] Isolate case failures, write JSONL incrementally, resume only failed cases, and retain failure rows without predictions.
- [x] Run 21-case live Golden Evaluation and score recorded rows. Nineteen cases completed; two stopped at request-wide input-budget limits. Aggregate and per-case report: [`live-evaluation-report-2026-10-08-final.md`](evaluation/live-evaluation-report-2026-10-08-final.md).
- [x] Run a distinct, unseen three-case held-out v3 fixture. Earlier held-out v1/v2 fixtures overlap baseline questions or were inspected before further tuning, so they are not used as the independent final score.
- [x] Run .NET navigator/provider tests, Python catalog/scorer tests, catalog integrity validation, and the relevant project build.
- [ ] Review and approve catalog semantics and query authorization. Query execution remains disabled; no approval is inferred from evaluation results.

## Navigator quality and recall optimization (2026-10-08)

- [x] Record verified baseline and per-case root-cause matrix: [baseline-failure-matrix-2026-10-08.md](evaluation/baseline-failure-matrix-2026-10-08.md).
- [x] Seed and prioritize deterministic hierarchy expansion from independently extracted catalog IDs; preserve fan-out, node, and authorization gates.
- [x] Add a regression proving required source item 161 is found through the 119-child IncomeStatement branch without selecting unrelated siblings.
- [x] Revalidate catalog hash and run the .NET solution build, 17 focused navigator/completeness tests, and 21 Python catalog/scorer tests.
- [x] Run complete live Golden and independent held-out v4; artifacts and before/after comparison: [quality-optimization-report-2026-10-08.md](evaluation/quality-optimization-report-2026-10-08.md).
- [ ] Raise metric recall from 0.8462 and source-item accuracy from 0.7500 to target; improve held-out v4 recall from 0.6667 and avoid unsupported entity-kind extraction.
- [ ] Complete semantic review and separate query authorization before any Query Planner execution work.

### Final evaluation correction

- [x] Retry `fa-07`, `fa-10`, and `fa-value-06` as failed rows only; all 21 latest rows score and none has a request-input-budget failure.
- [x] Capture independent held-out v5 separately; all recall dimensions are 0.6667; one case fails entity-kind validation.
- [ ] Resolve observed end-to-end node overflow (60 IDs against the intended 48) and exact-domain accuracy regression before marking navigator ready.

## Offline structured query planning (2026-10-08)

- [x] Add typed catalog-only plan algebra and deterministic builder/validator for source/entity references, filters, grouping, aggregation, sorting, bounded pages, presentation, periods, scopes, and financial operation kinds.
- [x] Add offline end-to-end composition service for independent requirement extraction, navigation, context expansion, completeness validation, and plan validation. Register it outside chat/workflow routing.
- [x] Keep retrieval completeness, semantic review, and query authorization distinct. Candidate plans remain non-executable while review/authorization is pending.
- [x] Validate provider + statement type + source item ID + stable catalog UUID for direct source references; preserve optional MetricCode, unit, provider, and reporting-period selection metadata.
- [x] Require direct base observations for approved-growth plan kinds; reject unsupported derived operations, unbounded pagination, unknown references/relationships, cross-tenant plans, formula authorization, composing-by-default, and synchronization-time recency.
- [x] Add offline tests for direct retrieval/comparison shape, growth identity, negative baseline rejection, generic filters/grouping/aggregation/sort/page composition, pending semantic review, source identity tampering, and complete service composition.
- [ ] Approve financial semantic mappings and source review; establish any approved growth/negative-baseline conventions. Until then, the plan is offline metadata only and execution remains blocked.
- [ ] Future compiler/executor work is a separate phase and must begin only after catalog review and explicit per-source query authorization.

## Deterministic SQL compiler and governed PostgreSQL executor (2026-10-08)

- [x] Add typed compiled-query/result contracts and an immutable catalog SQL snapshot boundary; compile only parameterized direct source-item latest-period queries from exact catalog IDs and verified relationship bindings.
- [x] Add a restricted Npgsql executor implementation with independent Development opt-in, short-lived source-scoped grant, designated database/role checks, read-only transaction verification, timeout/cancellation, bounded rows/bytes/concurrency, and sanitized diagnostics.
- [x] Register compiler/executor separately from all chat/workflow routing; defaults remain disabled and no general app connection string is reused.
- [x] Add focused unit tests for direct item without MetricCode, parameterization, source/hash/relationship rejection, and default-disabled execution.
- [x] Provide a runtime catalog snapshot builder that recomputes canonical/hierarchy hashes, resolves table/column/relationship/source IDs, and preserves pending approval state.
- [ ] Complete semantic and query authorization for any source; do not modify existing pending flags for tests.
- [ ] Configure a designated Development PostgreSQL database and independently restricted SELECT-only role, then run disposable PostgreSQL end-to-end tests for item 161 and safety cases.
- [ ] Extend generic algebra only where catalog-bound fields, relationships, grain, units, tenant scope, and financial semantics can be enforced.
- Current verification: Release solution build succeeds and 10 focused compiler/catalog/company-binding tests pass. Actual PostgreSQL execution and item 161 numeric evidence are blocked by absent explicit development authorization/database configuration. This phase is partial; it does not meet production activation criteria.

### Additional end-to-end blockers

- [x] Add the stable, verified application-level relationship from `FinancialStatementLineItems.FinancialStatementId` to `FinancialStatements.Id`; explicitly record that the captured live schema has no physical FK and keep semantic/runtime authorization pending.
- [x] Add a trusted runtime snapshot builder that recomputes/validates catalog integrity and exposes physical IDs plus separate semantic/runtime approval metadata.
- [x] Add a typed Feature 141 company-binding adapter over the existing canonical resolver; it preserves ambiguity and passes only the internal stable `Companies.Id` to a plan.
- [ ] Update the synthetic compiler tests to disposable PostgreSQL only after the preceding bindings and restricted database authorization exist.

## First PostgreSQL integration readiness follow-up (2026-10-08)

- [x] Catalog application-level relationship `rel_2588cf95d01a651a`, with many-to-one grain, non-null join keys, possible orphan behavior (no FK), partial-unique-index semantics for non-null source-item identity, and provenance.
- [x] Regenerate hierarchy and manifest lock through `catalog.py hierarchy build`; validate deterministic artifacts.
- [x] Add the runtime snapshot builder and reject catalog hash drift/missing references while preserving approval state and unit/provider metadata.
- [x] Require verified source identity and verified relationships in direct-source compilation; keep catalog query authorization independent.
- [x] Add a canonical item-161 snapshot test and an additional BalanceSheet source compile test using the same implementation.
- [x] Add a binder over the existing canonical entity resolver with typed Found/Ambiguous/NotFound/Missing outcomes and a tenant-presence gate.
- [x] Prepare least-privilege Development PostgreSQL role creation/grant/verification instructions without executing them. See [development-postgresql-setup.md](development-postgresql-setup.md).
- [ ] Obtain an explicit Development scope, provision the restricted PostgreSQL role/connection and exact source/relationship/object grants, then independently verify them.
- [ ] Run Navigator-to-executor with a real company binding and verify statement/observation evidence. No observation value is reported until this completes.
- Duplicate/orphan observation scans were not run because no separately authorized restricted database principal is available. The captured partial unique index only constrains non-null source-item identity within one statement; cross-revision ambiguity remains a typed runtime outcome.
- Verification: Release solution build succeeds; 1,953 UnitTests, 12 ArchitectureTests, 3 financial schema IntegrationTests, 22 catalog/scorer Python tests, and 10 focused Feature 141 tests pass. The solution-wide IntegrationTests suite also has 71 failures; see the implementation handoff, and do not treat this status as a green full-solution test run.

## First real Development PostgreSQL query unblock (2026-10-08)

- [x] Recheck Development environment variables and API User Secrets by key name only; confirmed no Feature141 executor keys, no displayed secrets, and only the general application connection is configured.
- [x] Add startup validation for explicit Development configuration, restricted connection identity, reviewed scope/allowlists, company UUID scope, operation allowlist, and resource bounds. Keep executor disabled by default and do not add general-connection fallback.
- [x] Bind compiled query and short-lived grant to exact resolved Company.Id and `DirectValueRetrieval`; set transaction-local `search_path`; reject compiled queries whose canonical source/relationship governance state is still pending.
- [x] Add regression coverage for pending source/relationship authorization, company mismatch, exact operation scope, disabled execution, and enabled-config validation.
- [x] Compare current IntegrationTests with detached clean `HEAD` `22f1f252`: baseline 419 passed / 72 failed / 41 skipped; current 421 passed / 71 failed / 41 skipped. TRX failure-name comparison: 70 common failed test names, no current-only failure names, one baseline-only failure (`FinancialStatementSchemaTests.UniqueIndex_IsConfiguredOnTheNewTriple`). The six Slice1 PostgreSQL failures explicitly report Docker/Testcontainers unavailable; other common failures are response/value assertions and remain individually unclassified.
- [x] Release solution build passed; UnitTests 1,953 passed; catalog hash validation passed; SemanticCatalog Python tests 22 passed.
- [ ] Obtain independently issued Feature 141 PostgreSQL credentials and Development scope; verify role attributes, memberships, effective privileges, search_path, RLS policies, tenant/company boundary, and resource settings.
- [ ] Complete separate semantic review and canonical query authorization for source item 161 plus its required relationships. Do not use an execution grant to override pending catalog flags.
- [ ] Run the approved real-company Navigator-to-executor query and independently compare its persisted observation; repeat for a separately approved source item. Current query and independent DB verification remain unperformed.

## Narrow approval and readiness package (2026-10-08)

- [x] Produce a one-query source/field/relationship approval package for Noavaran `IncomeStatement` source item 161 with verified catalog and persisted UUIDs; keep metadata verification, semantic approval, and execution authorization separate.
- [x] Enumerate exact four-table column IDs and three relationships, including nullable keys, physical-FK versus application association, orphan risks, unknown unit provenance, and latest-period tie ambiguity. Keep all approval metadata pending.
- [x] Correct the proposal's application order: owner semantic decisions are now persisted in the canonical policy overlay and deterministically reapplied during source-item generation; SQL authorization remains independently false. No signature or query grant is inferred.
- [x] Add an offline Development preflight command that validates canonical/hierarchy hashes, checks environment key names only, emits no secret values, makes no database connection, and runs no financial query.
- [x] Prepare separate Development-only read-role setup, verification, and revocation SQL with exact column grants and expected database-name guards. No role or permission change was executed.
- [x] Extend canonical Company Resolver coverage for a newly persisted Company; existing identity ambiguity tests and exact tenant/company authorizer-pair checks pass.
- [ ] Obtain reviewer decisions and independently recorded approval provenance; provision/verify the dedicated Development principal and exact tenant/company pair. Execution eligibility remains false.
- Verification: `preflight_feature141.py --json` reported catalog/hierarchy valid, all source/table/relationship approval states pending, no Development config key names or pair grant found, role unverified, live tie state unknown, and execution ineligible. Catalog hashes: `835b790297c8e08670353efdb98380d927f491597268f79b6040c44057380379` / `948bd802960e632bb542c1b52a4c1380d6b67a2d322c8ec3a9e69fd6303d0cc0`. Python tests: 24 passed; UnitTests: 1,954 passed; Release solution build: 0 warnings/errors. No PostgreSQL query ran.


# Owner-confirmed unit policy and statement tie follow-up (2026-10-08)

- [x] Version and record owner-approved financial statement units, separate from provider `Unit` values.
- [x] Regenerate the 296-item source dictionary with deterministic per-share and review-required classifications and effective-unit provenance.
- [x] Produce JSON and Markdown ambiguity review reports; retain source titles and identities.
- [x] Record semantic approval of Noavaran IncomeStatement source item 161 without MetricCode and without changing SQL authorization.
- [x] Carry effective unit and provenance in catalog snapshot, compiled query, and execution result metadata.
- [x] Replace latest-statement `LIMIT 1` behavior with candidate observation enumeration and exact item-value tie resolution; preserve all equal-value source identities, return `Ambiguous` for conflicts and `Incomplete` for missing observations.
- [x] Add regression coverage for units, raw `N/A`, item 161, equal/different/missing ties, and separate period scopes.
- [x] Rebuild and validate generated dictionary, hierarchy, and canonical hashes.
- [ ] Independently approve the reported ambiguous titles before assigning effective units.
- [ ] Keep PostgreSQL execution disabled until the independent Development database, restricted role, per-object query authorization, and exact tenant/company grant prerequisites are satisfied.

# Monthly production and sales expansion review (2026-10-08)

- [x] Add 13 required monthly production/sales questions and 10 essential PostgreSQL scenarios to `artifacts/monthly-production-sales-evaluation.json`.
- [x] Record the initial monthly domain metadata coverage and blockers in `monthly-production-sales-evaluation-report.md` (superseded by the implementation evidence below).
- [x] Add fixture contract tests that confirm monthly table/metric catalog presence, disabled runtime authorization, and unexecuted PostgreSQL scenario labels.
- [x] Run all SemanticCatalog Python `unittest` tests: 27 passed.
- [ ] Complete the reusable plan/compiler for grouping, aggregation, ranking, multi-company comparisons, and approved temporal comparisons; current direct-value/time-series slice is documented in `monthly-production-sales-evaluation-report.md`.
- [ ] Obtain semantic and query authorization for each approved monthly object/column/relationship/operation before any generic execution.
- [ ] Execute the remaining PostgreSQL scenarios against disposable fixtures; the current isolated fixture covers latest production, chronological sales amount, accepted revision/OutputType filtering, ambiguous competing rows, restricted role, production authorization rejection, and Feature 131 regression.
- [ ] Score the 13 prompts through the actual Navigator/Planner/Compiler/Executor pipeline and retain per-case structured results.

## Generic monthly retrieval implementation progress (2026-10-08)

- [x] Add catalog-driven direct monthly measure mappings and the verified application-level report-to-line-item relationship; keep canonical source/table/relationship query authorization disabled.
- [x] Extend the existing typed query plan with verified product identity binding, bounded monthly periods, and time-series presentation.
- [x] Compile latest eligible monthly production, sales quantity, and sales amount reads plus bounded chronological series through the existing deterministic SQL compiler. Provider, report type, output type, accepted revision, company, product, range, and row limits are parameterized or catalog mapped.
- [x] Reuse the company-scoped product resolver in the generic offline planner through an adapter; require resolved product provenance before compilation.
- [x] Fail closed for provider-reported sales rate until its rate unit is verified. Do not derive sales rate from amount and quantity.
- [x] Execute compiled monthly SQL against an isolated disposable PostgreSQL 18 database through a restricted SELECT-only role and the shared read-only executor; verify latest production, chronological amount series, rejected revision and cumulative output-type exclusion, and ambiguity for multiple accepted same-period rows.
- [x] Confirm canonical production catalog remains unauthorized and the test-only approval is confined to the isolated fixture snapshot.
- [x] Run focused planner/compiler tests (32 passed), monthly plus Feature 131 PostgreSQL tests (7 passed), and Semantic Catalog integrity validation.
- [x] Run the Release solution build and test suites: build succeeded (0 warnings/errors); 1,967 unit and 12 architecture tests passed; broad integration suite reported 428 passed, 71 failed, and 35 skipped; 27 SemanticCatalog Python tests and catalog integrity validation passed. See `monthly-production-sales-evaluation-report.md` for scope and failure notes.
- [ ] Wire and score monthly questions through live Navigator/Planner/Compiler/Executor orchestration; report the 13-question accuracy separately from compiler behavior.
- [ ] Implement approved comparisons/ranking/aggregation only after unit, grain, and cross-company product-equivalence rules are reviewed.

## Slice C task status (2026-10-08)

- [x] Implement bounded MoM growth/absolute-change comparison, sum aggregation, product ranking, and explicit multi-company aggregation in the existing plan/compiler path.
- [x] Execute direct retrieval, MoM, multi-company totals, top-N, revision/provider/output-type filters, ambiguity, and authorization checks against isolated PostgreSQL.
- [x] Run the 13-case monthly set and a separately authored three-case held-out set through the configured live Navigator; preserve exact prediction artifacts.
- [x] Compare the full IntegrationTests failure set against clean HEAD; no working-tree-only failures.
- [x] Extend PostgreSQL evidence to YoY, mixed-unit rejection, zero/negative baseline, missing-month rejection, SQL parameterization, and quantity ranking.
- [ ] Implement company-by-sales/growth ranking and reviewed industry/product-category filters; the current ranking support is top-N products within one company.
- [ ] Improve extraction validation and Navigator relevance/precision, then evaluate live Planner -> Compiler eligibility using reviewed catalog metadata and authorized identity bindings.
- [ ] Keep Development execution and production routing disabled until catalog approvals and grants are explicitly configured.

## Option B Phase 1 — benchmark and canonical intent (2026-10-10)

- [x] Add a typed, schema-independent canonical intent contract covering measures, operation/comparison, reporting period, named companies and unbound population, products/categories, boolean and numeric filters, aggregation/ranking, output shape, provenance, and clarification.
- [x] Add independent deterministic validation for required arguments, periods, units, contradictory conjunctions, ranking/aggregation completeness, population-scope consistency, and provenance.
- [x] Add provider-neutral, bounded LLM extraction and a shadow CLI that compares legacy requirements extraction and Option B on identical Persian input without invoking downstream query stages.
- [x] Add versioned correction manifest, benchmark builder, and distinct failure taxonomy/scorer. Historical source datasets and run artifacts are preserved.
- [x] Run the full existing 60-case development/historical-v2/v3 shadow set and publish per-question field correctness, failure categories, tokens, and latency; preserve raw rows and a reproducible scorecard.
- [x] Keep unreviewed cases out of exact semantic-gold denominators and report the 49-case adjudication backlog.
- [x] Record a no-go Phase 2 recommendation based on the current incomplete gold, non-comparable measure-concept labels, and observed qualifier/unit misses.
- [ ] Adjudicate remaining original 50 and historical holdout gold; only reviewed cases contribute exact semantic accuracy.
- [ ] Align canonical measure concepts with an explicit, language-independent gold vocabulary and update semantic scoring without rewriting source labels to fit model outputs.
- [x] Run Release solution build (0 warnings/errors), 2,033 UnitTests, 12 ArchitectureTests, 4 scorer/builder Python tests, and SemanticCatalog source/hierarchy integrity validation.
- [ ] Review per-question unit, threshold, aggregation, ranking, and clarification disagreements.
- [ ] Freeze scoring rules and evaluate newly authored unseen questions before making any Phase 2 routing recommendation; keep routing and query execution disabled meanwhile.

## Option B Phase 1.5 — canonical measures and holdout

- [x] Add an independent business-measure concept registry with reviewed Persian aliases and no physical catalog identifiers.
- [x] Preserve requested dimensions, value form, reporting basis, statement scope, grain, requested unit, and unresolved source-unit evidence.
- [x] Add deterministic canonicalization, ambiguity retention, unknown-term preservation, unit/qualifier validation, and unsupported-operation handling.
- [x] Add v3 reviewed-gold overlay and scoring with source-question hashes and reviewer/blinding provenance; retain 30 adjudicated rows and 1 disputed row without changing source datasets.
- [x] Add a new, frozen 12-question first-pass holdout, distinct from the historical 60; keep it unchanged after evaluation.
- [x] Run same 60 and holdout once in shadow-only mode. Both were inconclusive for semantic quality because Option B provider failures occurred on every case (60/60 and 12/12).
- [x] Release tool build; focused semantic tests 14/14; Python scoring/builder tests 5/5; catalog integrity passes.
- [ ] Obtain independent human/domain review of v3 gold and resolve the `fa-value-02` source-vs-correction conflict.
- [ ] Restore provider availability, then evaluate semantic accuracy with nonzero valid-output denominators before considering Phase 2; do not activate routing or execution based on these failed runs.
