# Feature 141 — Local PostgreSQL Execution Proof Status

**Status: BLOCKED before fixture setup or query execution.** The dedicated reader credential authenticated successfully, but the role is not isolated to the test database and no Feature 141 fixture objects are present.

## Authenticated, read-only verification

The opt-in readiness probe used `Feature141LocalPostgreSqlTestConfiguration.LoadFromUserSecretsForOptedInExecution()` with `FEATURE141_RUN_LOCAL_POSTGRES=1`. Credentials were not emitted in output or exceptions. No connection was made to `financial_copilot` or any other non-test database.

| Check | Observed result |
|---|---|
| Target connection | Authenticated to `financialcopilot_feature141_test` as `feature141_reader` |
| Server | PostgreSQL 17.2 at loopback `::1:5432` |
| Role attributes | Not superuser; cannot create databases or roles; no replication, BYPASSRLS, `pg_read_all_data`, or `pg_write_all_data` |
| Test database ACL | CONNECT allowed; CREATE and TEMP denied |
| Non-test database ACLs | CONNECT and TEMP are allowed on 32 non-test databases; `financial_copilot` is among them |
| Test database schema | `public`: USAGE allowed, CREATE denied |
| Existing Feature 141 fixture objects | No fixture-named relations found; no user relation rows were returned by the metadata inspection |
| Read-only enforcement | `current_setting('transaction_read_only')` returned `on` inside the probe transaction |
| Writes during probe | None; only metadata SELECTs were issued |

The test role is not a superuser and lacks global read/write roles, but database ACLs grant it CONNECT and TEMP across many databases. Database CONNECT alone does not establish schema/table access. Because connecting to protected databases is outside the fixture setup boundary, their schema and table ACLs were not inspected. This broad CONNECT/TEMP access is already sufficient to reject the reader for an isolated execution proof.

## Fixture and pipeline status

No synthetic company, statement, or `IncomeStatement/161` observation is present. Fixture-only query authorization is also not established. No DDL, seed operation, authorization grant, or application query was attempted. The source-resolution → planner → validator → fixture authorization → compiler → PostgreSQL → renderer path did not run; `3753 IRRPerShare` and its company, period, provenance, and Persian rendering remain unverified.

The repository’s Feature 141 handoff and planner pieces can construct offline planning inputs, but they do not constitute this live proof. The dedicated database still needs synthetic fixtures and a narrowly scoped test-only grant, and the reader’s database access must first be isolated.

## Exact administrator prerequisite

Have the PostgreSQL administrator isolate `feature141_reader` so it can connect only to `financialcopilot_feature141_test` (for example, with an appropriately ordered, role/database-specific `pg_hba.conf` policy or an equivalent dedicated test instance). Review effective schema/table grants in the protected databases and ensure the reader has no access there; do not rely on the current CONNECT ACL or reuse Feature 131 credentials. Then provide a separate authorized setup identity to create only synthetic Feature 141 fixtures and grant the reader SELECT on only the required fixture objects. Do not use the reader for DDL or reuse these fixture grants as production authorization.

Until those actions are completed and independently verified, do not seed fixtures or run the end-to-end query. No production catalog authorization, application routing, or protected database object was changed.

## Verification

- Integration test Release build: passed, 0 warnings and 0 errors.
- Feature 141 configuration tests: passed, 10/10.
- Offline source-planner authorization tests: passed, 5/5.
- Opted-in PostgreSQL readiness test: authenticated and completed read-only metadata inspection, then correctly failed its isolation gate after finding CONNECT on 32 non-test databases. No data or objects were modified.
- Broad LLM benchmarks: not run.
