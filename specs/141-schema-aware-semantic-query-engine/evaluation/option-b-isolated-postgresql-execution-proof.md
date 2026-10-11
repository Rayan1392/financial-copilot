# Option B — Isolated PostgreSQL Execution Proof

Date: 2026-10-10

## Result: BLOCKED before database setup

No PostgreSQL query was executed. A real value, reporting period, database provenance, and Persian answer therefore cannot be reported. No database or authorization configuration was changed.

## Isolation and authorization findings

- The repository has an isolated `PostgreSqlIntegrationFixture` using Testcontainers with a PostgreSQL 17 Alpine container. It requires Docker. The Docker CLI is installed, but a bounded `docker ps` call did not return within 30 seconds and had to be interrupted; no container inventory was obtained.
- Two system PostgreSQL services are running and listening on ports 5432 and 5433. Their service names/data directories do not identify them as disposable test instances. I did not connect to either service or create databases/roles there because their isolation from Development/production data could not be established.
- All 296 source registry entries remain `not-authorized`/`queryable=false`. The existing opt-in `DevelopmentStructuredQueryAuthorizer` only issues grants in Development and requires the plan’s semantic approval and query authorization to already be true. It is not a fixture-only authorization mechanism and cannot safely authorize source item 161 under the current registry state.
- No test-only grant was added, and no general catalog authorization changed. Thus the required fixture-only scope for exactly `IncomeStatement/161` was not established.

## Requested execution path

`IncomeStatement/161` exact-title resolution and typed source-to-plan validation remain covered by the preceding offline proof. This task did not proceed past its required isolation/authorization gate:

`Exact Source Resolution → Typed Binding → Existing Planner → Plan Validation → BLOCKED (no isolated database + no fixture-only authorization)`

No SQL was compiled or executed for this task; no restricted PostgreSQL role identity or transaction-read-only check was observed. No fixture value, unit, period, company identity, or source-row provenance was validated. No Persian response was rendered because there was no database result.

## Negative-test status

- Unauthorized source cannot be granted through the existing Development authorizer unless plan authorization is already true; this remains the active boundary. Unit-level checks were rerun, but no PostgreSQL attempt was made.
- Consolidated `IncomeStatement/143` without qualifier evidence remains typed `Incomplete` (`statement_scope_evidence_unavailable`) in the preceding handoff test.
- Missing observation → typed no-data, incompatible-unit rejection at the database boundary, and restricted-role write rejection were **not executed against PostgreSQL**. They remain requirements for a resumed isolated proof, not passing database claims.

## Verification

- Focused Release unit tests (`FinancialSourcePlannerHandoffTests` and `StructuredQueryCompilationTests`): 34 passed, 0 failed. These are unit tests, not PostgreSQL execution evidence.
- Release solution build (`--no-restore`): passed, 0 warnings/errors.
- No LLM benchmarks, Development/production database access, routing changes, or catalog authorization changes.

## Remaining prerequisites

Resume only when Docker/Testcontainers is available or a database is explicitly identified and provisioned as disposable; a restricted read-only role is verified against that isolated database; a test-only authorization grant is bound to exactly the fixture source and database; a company-scoped fixture and predetermined `IRRPerShare` observation are present; and the existing no-data, unit-rejection, and write-denial checks can run within that same harness. No existing Development or production database should be repurposed for this proof.
