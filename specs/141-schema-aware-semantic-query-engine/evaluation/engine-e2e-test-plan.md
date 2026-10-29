# Feature 141 isolated engine E2E test plan

## Boundary and fixtures

Run canonical intent extraction with the configured real `IAiModelExecutionService`, then bind its schema independent concepts through `CanonicalIntentCatalogBinding`. The binding result retains the original intent and rejects missing or ambiguous approved mappings. Only a `Bound` result may enter the existing Navigator and Planner; completeness, semantic review, query authorization and runtime grant checks remain separate. Use the production SQL compiler, bounded Npgsql executor and Persian response generator. Do not call the Chat/API facade.

Use the existing `PostgreSqlIntegrationFixture` (Testcontainers PostgreSQL 17) to create a disposable database, seed `engine-e2e-fixture.json` through the existing persistence models, and create a restricted SELECT-only role scoped to the exact fixture objects. Never point this suite at a Development or production connection string. Use a fixture-only authorization scope that cannot be loaded by production configuration. Record one trace per case at each stage; do not turn an unexecuted case into a pass.

`engine-e2e-cases.json` contains 20 prompts and independent proposed expected answers. The fixture is currently an evaluation design artifact, not a seeded or run database. E04 deliberately has no numeric expectation until a per-share source fixture is chosen; E11 requires approved product equivalence and E20 an explicit denial policy. These remain blocked until adjudicated.

## Required trace schema

Each case must record `question`, `canonicalIntent`, `boundCatalogReferences`, `navigator`, `planner`, `sql`, `parameters`, `authorizationDecision`, `postgresqlResult`, `expectedNumericResult`, `unitCorrect`, `periodCorrect`, `persianAnswer`, `semanticStatus`, `executionStatus`, `passFailReason`, `llmInputTokens`, `llmOutputTokens` and `elapsedMs`. Null means the stage was not reached; it must not be interpreted as success. Redact credentials and do not log raw connection strings.

## Release gate

Run all 20 in isolation and compare exact fixture values, units, periods, product identities, and typed rejections. `GO` requires 20 expected outcomes, zero wrong numbers, zero unauthorized executions and zero silent omissions. Report semantic and execution denominators independently. A missing approved mapping, missing product equivalence, missing fixture value, provider failure or unavailable container is `BLOCKED`.

## Current prerequisites

1. Product owner approval of canonical concept to source item/metric bindings for net profit, operating profit, EPS, production quantity, sales quantity and sales amount, including dimension, value form, reporting basis, statement scope and unit evidence. The current hierarchy has one approved concept binding, `operatingProfitPerShare`.
2. Reviewed company-scoped product identities and cross-company equivalence for `گندله` and `گندله سنگ‌آهن`; no spelling-based equivalence is authorized.
3. Reviewed policies for historical period selection, aggregation, ranking, multi-condition filters and population discovery, plus an explicit fixture-only authorization grant and verified restricted role.
4. A working Docker engine for Testcontainers and a seeded fixture that covers every expected numeric result.


## Vertical-slice runner (added 2026-10-28)

`tests/FinancialCopilot.IntegrationTests/Feature141EngineVerticalSliceTests.cs` contains the reusable
`Feature141EngineVerticalSliceRunner`. It seeds a deterministic fixture (one expected row plus decoy
rows for another company, another source item and an older period) into a disposable database, builds
the production composition root (`AddFinancialCopilotInfrastructure`) with both connection strings
redirected to that database, and runs the chain with production components only: live
`LlmCanonicalSemanticIntentExtractor`, `CanonicalIntentCatalogBinding` and the chat-path
`CanonicalSourceItemBindingResolver` + `FinancialSourcePlannerHandoff`, live `LlmVectorlessSchemaNavigator`
over the real `JsonCatalogNavigationHierarchyProvider`, `OfflineStructuredQueryPlanningService`,
`StructuredQueryCompiler`, `CatalogStructuredQueryAuthorizer`, `NpgsqlReadOnlyStructuredQueryExecutor`
and `PersianStructuredQueryResponseGenerator`. No governance flag is flipped by the test; execution
eligibility comes from the packaged catalog. The runner records a `Feature141VerticalSliceTrace` with
every stage (null means not reached) and writes it to
`evaluation/artifacts/engine-e2e-vertical-slice-<date>.json` without connection strings or secrets.
A negative control re-executes the same sealed query under the writable administrative principal
and must be refused (`database_read_only_identity_check_failed`). Add a case by adding a
`Feature141VerticalSliceCase` and asserting on the trace; the runner itself asserts nothing.

Database: when Docker is unavailable, `tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1 -Action start`
initialises a loopback-only PostgreSQL 18 instance on port 55441 with its own data directory and admin
role, and writes the admin connection string to an ACL-restricted `connection.txt`. Export it as
`FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` (the Feature 131 fixture refuses `financial_copilot*`
databases) and set `FEATURE141_RUN_LIVE_LLM_POSTGRES=1`; the live provider key comes from the API
Development configuration. `-Action destroy` removes the instance.

## Authoritative suite and baseline stabilization (2026-10-28)

`engine-e2e-suite-v1.json` is the single versioned evaluation suite (F141-E01..E20, legacy IDs mapped);
`engine-e2e-fixture-v1.json` separates rows the runner seeds from proposed, unapproved design values.
`Feature141EngineE2ESuiteTests` enforces the suite invariants (unique IDs, typed outcome, blocker for every
non-executable case, evidence artifact for every PASS, agreement with the runner's cases).
`Feature141LatestPeriodSemanticsPostgreSqlTests` covers latest-period selection (item present, item absent,
coexisting consolidation bases, same-PeriodEnd ties, older periods, NULL values, other providers) without
an LLM; each scenario executes twice and must be identical. Runner artifacts are written per run as
`engine-e2e-vertical-slice-<date>-<HHmmss>-<caseId>-<outcome>.json` and are never overwritten.
See `engine-baseline-stabilization-report.md` for commands, exit codes and findings.

## Monthly production quantity (2026-10-29)

F141-E07 runs through the runner's `MonthlyMeasure` kind: live canonical intent → navigator requirements →
deterministic Persian month normalization (`PersianReportingPeriodNormalizer`) replacing the model's bounds →
planner with the production company and company-scoped product resolvers → compiler → catalog authorizer →
restricted executor (SELECT on `MonthlyReports`, `MonthlyReportLineItems`, `Companies` only) → Persian answer.
Governance comes from the owner-approved catalog objects (measure, tables, relationships); the completeness
validator treats a reviewed-measure request like exact direct-source identity. Negative scenarios live in
`Feature141MonthlyProductionSemanticsPostgreSqlTests` (no LLM) and write `artifacts/monthly-production-semantics-<stamp>.json`.
