# First PostgreSQL Query Readiness â€” 2026-10-08

## Completed in this follow-up

- Added canonical relationship `rel_2588cf95d01a651a` for the application association `FinancialStatementLineItems.FinancialStatementId -> FinancialStatements.Id`.
- The relationship is many-to-one, both columns are non-null, and missing referential enforcement means orphan line items remain possible. The captured inventory has no physical FK for this edge. Its partial unique index constrains non-null source-item identity within a statement; no live duplicate/orphan scan was run.
- Semantic approval, runtime joinability and query authorization remain false for the relationship. Source item 161 remains queryable false with semantic review pending, unmapped MetricCode and unknown unit.
- Regenerated and validated the canonical hierarchy and manifest lock. Current source hash: `835b790297c8e08670353efdb98380d927f491597268f79b6040c44057380379`; hierarchy hash: `948bd802960e632bb542c1b52a4c1380d6b67a2d322c8ec3a9e69fd6303d0cc0`.
- Added the immutable runtime catalog snapshot builder and the Feature 141 company binder over the existing typed canonical resolver. The binder requires a tenant ID and carries only the canonical internal `Companies.Id`, preserving ambiguity and not-found results.
- Added least-privilege database-role setup and verification SQL in [development-postgresql-setup.md](../development-postgresql-setup.md). No role was created or altered.
- Verified the compiler can use another canonical source item from `BalanceSheet` without item-specific SQL. This is still offline compilation with a fixture company binding, not a database retrieval.
- Confirmed there is no Feature 141 executor/compiler reference in the API project. The chat route was not activated.

## Why no PostgreSQL query ran

No dedicated `Feature141:QueryExecution` settings or separately named restricted-role credentials were present in the inspected environment/user-secrets key names. Standard application database configuration exists, but that connection was intentionally not used for Feature 141. The existence or privileges of a database role were not independently inspected because no separately authorized restricted principal was available.

The live company UUID for Ú©Ú¯Ù„, persisted statement rows, duplicate/orphan state, and the item-161 stored value are therefore unverified. No numeric result or company/statement IDs are reported. Do not infer a missing row from this blocked run.

To proceed, the Development database owner must provision and independently verify the exact SELECT-only role in `development-postgresql-setup.md`, and provide the dedicated connection and explicit Development scope. Then run the existing Navigator â†’ company binder â†’ deterministic planner/validator â†’ compiler â†’ authorizer â†’ restricted executor path. The explicit Development grant must name only the reviewed source, three relationships, and selected physical object/column IDs; it must not alter canonical catalog approval flags. If Navigator completeness fails, stop before grant/execution. If multiple eligible latest observations remain, return Ambiguous.

## Verification

- `dotnet build src/backend/FinancialCopilot.sln -c Release --no-restore`: passed, zero warnings/errors.
- UnitTests: 1,952 passed.
- ArchitectureTests: 12 passed.
- Focused financial statement schema IntegrationTests: 3 passed (these are not PostgreSQL proof).
- Feature 141 compiler/catalog/company-binding tests: 10 passed.
- Semantic Catalog Python suite: 22 passed.
- Runtime and Python catalog integrity checks: passed with hashes above.
- `git diff --check`: passed; Git emitted existing line-ending normalization warnings for unrelated dirty Feature 140/spec and test project files.
- A solution-wide test run was not green: IntegrationTests reported 421 passed, 71 failed, and 41 skipped. Observed failures included Telegram membership setup (HTTP 400) and a V2 semantic insight billing assertion. At the time of the initial handoff no baseline run had been captured; the later baseline comparison is documented below.

## Remaining Feature 141 algebra

The compiler still supports only a single direct source item, one company, provider and statement type, and latest-period scalar/table retrieval. Generic joins, filters, grouping, aggregation, comparisons, explicit sorting, multiple entities, charts/time series, provider indicators and governed metric retrieval remain unsupported. Extend them only with separately reviewed catalog fields, grain, units, period semantics, and authorization.

## Authorization and baseline follow-up (2026-10-08)

- Confirmed the only configured DB credential key is the general `ConnectionStrings:FinancialCopilot`; no `Feature141:QueryExecution` environment or User Secrets keys exist. Secret values were not displayed. No restricted role can be independently identified or verified without a separately authorized principal, so the application connection was not used to query PostgreSQL.
- Tightened configuration validation: enabled execution now requires a Development host/environment, explicit opt-in, an independently configured role/database/connection, bounded limits, reviewed allowlists, exact tenant/company UUID pairs, and only `DirectValueRetrieval`. The executor has no app-connection fallback and pins transaction-local `search_path` to `pg_catalog, public`.
- Bound compiled queries and five-minute grants to an allowlisted tenant, exact company UUID and operation in addition to catalog hash, sources, relationships, and physical objects. Canonical source and relationship approval flags now flow into the compiled query; authorization rejects pending semantic review or query authorization. This preserves item 161 and its relationship as non-executable under their current catalog state.
- Re-ran IntegrationTests on clean `HEAD` (`22f1f252`) in a detached worktree and on the current worktree, saving TRX results under the machine temp directory. Baseline: 419 passed, 72 failed, 41 skipped. Current: 421 passed, 71 failed, 41 skipped; the current tree contains one additional test. Failure-name comparison found 70 common failed test names, no current-only failure names, and one baseline-only failure: `FinancialStatementSchemaTests.UniqueIndex_IsConfiguredOnTheNewTriple`. That test is in a currently modified IntegrationTests file; the comparison does not assign its change to Feature 141. Shared failures span admin operations, monthly sales, scanner/revenue-mix and insight routes, Telegram tests, financial-value search, and Slice1 PostgreSQL tests. TRX details confirm the Slice1 PostgreSQL tests fail because Docker/Testcontainers cannot start; the other shared failures are primarily response/value assertions, and their individual underlying causes were not established. Feature 141 has no API/chat route reference and added no IntegrationTests route, so the run provides no evidence of a Feature 141 regression.
- A real result remains blocked independently by missing restricted credentials/configuration, unverified DB/RLS permissions and company scope, and pending canonical approval for source item 161 and its required relationships. No PostgreSQL query or independent observation comparison was performed. No setup SQL, role change, or permission grant was executed.
- Release solution build passed with zero warnings/errors. UnitTests passed (1,953), focused Feature 141 compiler/authorization/configuration tests passed, canonical catalog hash validation passed, and SemanticCatalog Python tests passed (22). `git diff --check` passed; only existing line-ending warnings were emitted for unrelated files.

