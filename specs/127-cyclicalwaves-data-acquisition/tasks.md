# CyclicalWaves Data Acquisition Foundation - Implementation Tasks

Status: Implementation plan only. These tasks do not authorize code changes.

Source of truth: `specs/127-cyclicalwaves-data-acquisition/design.md`

The phases below implement only daily CyclicalWaves P/S gauge, Last P/S, P/E, and equilibrium acquisition,
immutable raw snapshots, acquisition checks, recovery, and provider protection. Structured consumer
read models are separate future work; `RawResponseJson` remains the canonical source of truth.

# Phase 1 - Foundation

## 127-P1-T01 - Add feature configuration

**Task description**

Define and register the `CyclicalWavesDataAcquisition` configuration section with the five approved
settings: `Enabled`, `Schedule`, `RequestDelayMilliseconds`, `TimeoutSeconds`, and `RetryCount`.
Bind configuration through the options pattern, validate enabled configurations at startup, and
keep the feature disabled by default. Interpret `Schedule` as a five-field cron expression in UTC.
Continue using the existing secret-backed CyclicalWaves base address and authentication settings;
do not duplicate credentials in the new section.

**Expected files/components**

- `CyclicalWavesDataAcquisitionOptions` in the CyclicalWaves data-acquisition infrastructure area.
- Options validator for cron syntax and numeric bounds.
- `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` registration.
- Worker/API configuration templates containing the disabled section and safe defaults.

**Dependencies**

- Approved design sections 12 and 13.
- Existing CyclicalWaves transport configuration and secret-loading conventions.

**Acceptance criteria**

- The section binds exactly the five approved feature settings.
- Defaults are `Enabled=false`, `RequestDelayMilliseconds=1000`, `TimeoutSeconds=30`, and
  `RetryCount=2`; the documented schedule example is `0 2 * * *`.
- Enabled configuration rejects an invalid cron expression, a negative delay, a non-positive
  timeout, or a retry count outside `0..5` before any provider request.
- The schedule is explicitly evaluated in UTC.
- No credentials or provider base address are duplicated into this feature section.

## 127-P1-T02 - Create database entities

**Task description**

Create persistence entities and EF Core configurations for `CyclicalWavesMetricSnapshots` and
`CyclicalWavesAcquisitionChecks`. Model all fields, relationships, lengths, required/nullability
rules, enum string values, timestamps, and consistency rules exactly as approved. Keep acquisition
storage provider-faithful: no parsed P/S, P/E, equilibrium, gauge, or consumer-facing fields may be
added.

**Expected files/components**

- `CyclicalWavesMetricSnapshotRow`.
- `CyclicalWavesAcquisitionCheckRow`.
- Metric-type and check-result persistence values.
- Dedicated EF Core entity configuration classes.
- `FinancialIngestionDbContext` `DbSet` properties.

**Dependencies**

- Task 127-P1-T01 for feature naming consistency.
- Approved design sections 7.1 and 7.2.
- Existing `Companies.Id` schema and Financial Ingestion persistence conventions.

**Acceptance criteria**

- Snapshot rows contain every approved column, including `RawResponseJson`, `ResponseHash`,
  `AcquisitionDateUtc`, `SourceEndpoint`, and `PreviousSnapshotId`.
- Check rows contain every approved column, including cycle/request/completion timestamps, result,
  snapshot link, final status, attempts, and bounded failure diagnostics.
- Company and self/snapshot relationships use restricted/no-action delete behavior.
- Changed/no-change rows require both `ResponseHash` and `SnapshotId`; failed rows require both to
  be null.
- `RawResponseJson` is mapped as PostgreSQL `text` and is the canonical source of truth.
- Entities contain no structured consumer projection fields.

## 127-P1-T03 - Create EF Core migration

**Task description**

Create one additive Financial Ingestion migration for the two feature tables, their foreign keys,
check constraints, latest/hash/restart/diagnostic indexes, and the defensive unique predecessor
constraint. Use PostgreSQL `NULLS NOT DISTINCT` semantics for predecessor uniqueness so competing
first snapshots are also rejected. Do not backfill or mutate existing data.

**Expected files/components**

- New migration under
  `FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations`.
- Updated `FinancialIngestionDbContextModelSnapshot`.
- Generated or explicit PostgreSQL DDL for `NULLS NOT DISTINCT` where required.

**Dependencies**

- Task 127-P1-T02.
- PostgreSQL version used by the Financial Ingestion database.

**Acceptance criteria**

- Applying the migration creates only `CyclicalWavesMetricSnapshots` and
  `CyclicalWavesAcquisitionChecks` plus their approved constraints and indexes.
- The snapshot latest-read and hash indexes match the approved column order.
- The check restart and diagnostic indexes match the approved column order.
- The predecessor uniqueness constraint includes company, provider, metric, and previous snapshot
  and treats null predecessors as equal.
- Existing tables and rows are unchanged and no backfill runs.
- The down migration drops only the two new tables in dependency-safe order.

## 127-P1-T04 - Add repository and application contracts

**Task description**

Define the application-facing contracts and bounded value types needed by the client, persistence,
and worker phases. Contracts must represent metric type, terminal check result, accepted raw
response, provider failure, request timestamps, physical attempt count, company identity,
checkpoint lookup, latest snapshot lookup, failed-check recording, and atomic accepted-response
persistence. Do not expose consumer-specific parsed valuation fields.

**Expected files/components**

- `CyclicalWavesDataAcquisitionContracts.cs` in the Application ingestion area.
- `ICyclicalWavesDataAcquisitionClient`.
- `ICyclicalWavesDataAcquisitionRepository`.
- `ICyclicalWavesAcquisitionCompanySource` or equivalent read contract.
- `ICanonicalJsonHasher` or equivalent deterministic hashing contract.
- Bounded result/error records and enums.

**Dependencies**

- Task 127-P1-T02 for persistence semantics.
- Approved component boundaries and provider contract.

**Acceptance criteria**

- The client contract returns exact raw JSON, endpoint, status, first request time, accepted attempt
  time, completion time, and attempt count without requiring consumers to parse selected values.
- Repository contracts support latest comparison, successful atomic persistence, failed-check
  insertion, and current-cycle completion lookup.
- The company-source contract carries `ExternalCompanyId`, `CompanySymbol`, and `SymbolIsin` from
  `NoavaranEligibleCompanies`, plus the internal `CompanyId` required by persistence.
- Failure codes are bounded and include all design-defined cases.
- Contracts use `DateTimeOffset` for UTC instants and `DateOnly` for `CycleDateUtc`.
- No contract references downstream consumer behavior or structured consumer read models.

# Phase 2 - CyclicalWaves Provider Acquisition

## 127-P2-T01 - Implement acquisition client

**Task description**

Implement a dedicated raw-response CyclicalWaves acquisition client that sends one logical GET
operation at a time and returns the complete successful response text plus bounded transport
metadata. Capture `RequestedAtUtc` immediately before the first physical attempt,
`AcquisitionDateUtc` immediately before the successful physical attempt, and `CompletedAtUtc` when
the logical operation terminates. Read response content with a bounded streaming strategy and
never log response bodies or secrets.

**Expected files/components**

- `CyclicalWavesDataAcquisitionClient` in the CyclicalWaves provider infrastructure area.
- Raw acquisition result and failure mapping implementation.
- DI registration for `ICyclicalWavesDataAcquisitionClient`.
- Shared bounded response reader where appropriate.

**Dependencies**

- Tasks 127-P1-T01 and 127-P1-T04.
- Existing CyclicalWaves HTTP transport and authentication primitives.

**Acceptance criteria**

- Successful operations return the exact decoded UTF-8 response text without removing unknown
  properties or reserializing it.
- The result distinguishes logical checks from physical retry attempts.
- UTC timestamps are captured at the approved attempt boundaries through `TimeProvider`.
- Cancellation is propagated and does not become a synthetic provider failure.
- Logs and exceptions do not expose credentials, authorization headers, tokens, or raw responses.
- The client has no dependency on current P/S visualization behavior.

## 127-P2-T02 - Implement P/S endpoint integration

**Task description**

Add the P/S operation for `GET ps/circle-chart-data/{ISIN}`. Escape the normalized ISIN, retain the
actual relative endpoint, require a JSON object with numeric `a` through `f`, `close`, `start`,
`end`, `min`, `max`, and `avg`, and accept unknown additive properties. Return the complete raw
response for persistence; do not map the response into consumer gauge fields.

**Expected files/components**

- P/S method on `CyclicalWavesDataAcquisitionClient`.
- Minimal P/S contract validator used only to accept/reject the raw response.
- P/S provider-contract test fixtures.

**Dependencies**

- Task 127-P2-T01.
- Approved endpoint contract in design section 6.

**Acceptance criteria**

- The endpoint is exactly `ps/circle-chart-data/{escaped ISIN}` relative to the configured API base.
- Every required field is validated as a JSON number while zero remains a valid JSON numeric value.
- Unknown fields are accepted and retained in raw JSON.
- Malformed, non-object, truncated, or missing-field responses return stable contract failures and
  do not return an accepted payload.
- P/S acquisition is available and executed independently of any current P/S consumer.

## 127-P2-T09 - Implement Last P/S endpoint integration

**Task description**

Add the Last P/S operation for `GET ps-data/{ISIN}`. Escape the normalized ISIN and preserve the
actual relative endpoint. Validate the response envelope as an object containing `data`, with the
required fields `data.symbol` (string), `data.ticker` (string), `data.ps_ratio` (finite JSON
number), `data.close` (finite JSON number), and `data.date` (ISO calendar date). Preserve the
complete successful response as raw JSON; do not discard the nested envelope or map it directly
into the existing P/S gauge representation.

The Last P/S operation is a distinct `LastPS` metric stream. It must have its own checkpoint,
snapshot history, hash, timestamps, failure result, and endpoint metadata. The provider's `ticker`
and `date` fields are provider evidence and must not replace the platform company identity or
acquisition timestamp.

**Expected files/components**

- Last P/S method on `CyclicalWavesDataAcquisitionClient`.
- Minimal Last P/S envelope/field validator.
- Last P/S provider-contract fixtures and request tests.
- Metric descriptor and `LastPS` persistence mapping.

**Dependencies**

- Task 127-P2-T01.
- Task 127-P3-T01 for the metric-stream persistence contract.

**Acceptance criteria**

- The endpoint is exactly `ps-data/{escaped ISIN}` relative to the configured API base.
- A valid sample response is accepted with all five required nested fields preserved in raw JSON.
- `ps_ratio` and `close` accept zero and other finite JSON numbers.
- Missing envelope/fields, invalid types, invalid date, malformed JSON, and non-object responses
  produce stable contract failures and no accepted payload.
- Unknown additive properties at the root or under `data` are accepted and retained.
- Last P/S has `MetricType = LastPS` and is not conflated with the existing `PS` gauge stream.

## 127-P2-T10 - Implement Last P/E endpoint integration

Add the independent latest P/E operation for `GET pe-data/{ISIN}`. Validate the same nested
`data` envelope as Last P/S, using `data.pe_ratio`, `data.close`, and provider `data.date`; preserve
the complete response and use the provider date for duplicate detection.

- Last P/E has `MetricType = LastPE` and is not conflated with the P/E gauge stream.

## 127-P2-T03 - Implement P/E endpoint integration

**Task description**

Add the P/E operation for `GET pe/circle-chart-data/{ISIN}` using the same raw-preservation and
minimum-contract approach as P/S. The operation must remain a distinct metric acquisition with its
own endpoint, timestamps, result, and failure evidence.

**Expected files/components**

- P/E method on `CyclicalWavesDataAcquisitionClient`.
- Minimal P/E contract validator.
- P/E provider-contract test fixtures.

**Dependencies**

- Task 127-P2-T01.
- Approved endpoint contract in design section 6.

**Acceptance criteria**

- The endpoint is exactly `pe/circle-chart-data/{escaped ISIN}`.
- Required `a` through `f`, `close`, `start`, `end`, `min`, `max`, and `avg` numeric fields are
  validated without dropping unknown properties.
- The complete accepted raw JSON is returned unchanged for snapshot persistence.
- Contract failures produce stable error codes and no accepted response.
- The operation shares transport behavior but not result state with P/S or equilibrium.

## 127-P2-T04 - Implement Equilibrium endpoint integration

**Task description**

Add the equilibrium operation for `GET equilibrium/gauge/{ISIN}`. Validate a JSON object against
the documented gauge contract and verify that `enticker`, when present, equals the requested
trimmed uppercase ISIN using ordinal case-insensitive comparison. Preserve all documented and
unknown additive fields in the returned raw JSON.

**Expected files/components**

- Equilibrium method on `CyclicalWavesDataAcquisitionClient`.
- Minimal equilibrium contract and identity validator.
- Equilibrium provider-contract test fixtures.

**Dependencies**

- Task 127-P2-T01.
- Approved equilibrium contract and identity rule.

**Acceptance criteria**

- The endpoint is exactly `equilibrium/gauge/{escaped ISIN}`.
- A valid response retains every field, including provider `date` and `lastcaldate`, without using
  either as the acquisition timestamp.
- A present `enticker` mismatch returns `IdentityMismatch` and no accepted payload.
- Malformed, missing-contract, or identity-invalid responses cannot replace a valid snapshot.
- The complete accepted response is returned unchanged for persistence.

## 127-P2-T05 - Implement authentication and configuration handling

**Task description**

Connect the acquisition client to the existing secret-backed CyclicalWaves base address and
authentication transport. Keep feature scheduling/pacing settings in
`CyclicalWavesDataAcquisition` and provider credentials in the existing provider configuration.
Ensure any authentication refresh is bounded and participates in the single transport resilience
policy rather than introducing a nested retry loop.

**Expected files/components**

- Typed/named `HttpClient` registration for the acquisition client.
- Existing CyclicalWaves authentication handler/token cache integration where compatible.
- Service registration tests for options and client wiring.

**Dependencies**

- Tasks 127-P1-T01 and 127-P2-T01.
- Existing CyclicalWaves authentication contract.

**Acceptance criteria**

- Requests use the configured API base and secret-backed authentication without hardcoded or
  newly duplicated credentials.
- Persistent authentication failure is returned as a stable failed outcome.
- Authentication refresh cannot cause unbounded sends or a second general retry policy.
- Configuration validation occurs before the first enabled acquisition request.
- No secret value appears in logs, check diagnostics, or exception messages.

## 127-P2-T06 - Implement timeout policy

**Task description**

Apply `TimeoutSeconds` per physical HTTP attempt in the single acquisition resilience pipeline.
Differentiate host cancellation from an attempt timeout so shutdown can resume safely while an
exhausted timeout becomes a persisted provider failure.

**Expected files/components**

- Timeout stage in the acquisition HTTP resilience pipeline.
- Timeout-to-failure mapping in the client.
- Deterministic time-based tests using controlled handlers/time providers.

**Dependencies**

- Tasks 127-P1-T01 and 127-P2-T01.

**Acceptance criteria**

- Each physical attempt is bounded by configured `TimeoutSeconds`.
- Timeout is classified as transient for the bounded retry policy.
- Host cancellation exits promptly and is not persisted as `Timeout`.
- Timeout handling exists in one layer only.
- Attempt and completion timestamps remain accurate after a timed-out attempt.

## 127-P2-T07 - Implement retry policy

**Task description**

Implement the only general retry policy for acquisition requests. Retry network failures,
timeouts, HTTP `408`, `429`, and `5xx` with bounded exponential backoff and jitter. Honor a valid,
bounded `Retry-After` for `429`. Do not retry `204`, `404`, other non-approved `4xx`, malformed JSON,
contract failures, or identity mismatches.

**Expected files/components**

- Retry stage in the acquisition HTTP resilience pipeline.
- Stable HTTP/network failure classification.
- Attempt-count propagation into acquisition results.

**Dependencies**

- Tasks 127-P2-T05 and 127-P2-T06.

**Acceptance criteria**

- Total physical attempts never exceed `1 + RetryCount`.
- `RetryCount=0` sends exactly one physical request.
- Only approved transient outcomes are retried.
- A bounded valid `Retry-After` takes precedence for its retry delay.
- Malformed payloads and identity/contract failures are attempted once.
- No worker, service, endpoint method, or authentication component adds another general retry loop.

## 127-P2-T08 - Implement sequential request pacing

**Task description**

Create the cancellation-aware pacing boundary used between completed logical provider operations.
Apply `RequestDelayMilliseconds` exactly once after each attempted metric before the next metric or
company request. Retry backoff remains inside the transport pipeline and must not multiply the
logical pacing delay.

**Expected files/components**

- Request pacer or acquisition-service delay abstraction using `TimeProvider` where supported.
- Registration/wiring into the data acquisition service.
- Pacing tests with deterministic time control.

**Dependencies**

- Task 127-P1-T01.
- Logical operation result from task 127-P2-T01.

**Acceptance criteria**

- At most one logical provider operation starts at a time.
- The configured delay occurs once between logical operations, including after terminal failures.
- A zero delay is valid and does not add an artificial wait.
- Shutdown cancellation interrupts the delay promptly.
- Retry backoff and request pacing remain separate, non-nested concerns.

# Phase 3 - Persistence

## 127-P3-T01 - Implement snapshot persistence

**Task description**

Implement repository operations to load the latest snapshot for a company/provider/metric stream
and insert an immutable successor snapshot. Populate acquisition-time ISIN, provider, metric, exact
raw JSON, canonical hash, successful-attempt acquisition time, actual endpoint, predecessor link,
and local creation time. Never update or delete an existing snapshot during normal acquisition.

**Expected files/components**

- `CyclicalWavesDataAcquisitionRepository`.
- Latest-snapshot query ordered by acquisition and creation timestamps.
- Snapshot insertion mapping.
- DI registration for the repository contract.

**Dependencies**

- Tasks 127-P1-T02 through 127-P1-T04.

**Acceptance criteria**

- Latest lookup is scoped by `CompanyId`, `ProviderName`, and `MetricType`.
- Inserts populate every required snapshot column and the correct predecessor.
- Existing snapshot rows are never mutated to represent new responses.
- A first snapshot has a null predecessor; successors reference the immediately previous snapshot.
- Delete behavior and repository operations preserve immutable history.

## 127-P3-T02 - Implement acquisition check persistence

**Task description**

Implement repository operations for `Changed`, `NoChange`, and `Failed` acquisition checks.
Persist the daily cycle, company/ISIN/metric identity, request lifecycle timestamps, response hash
and snapshot link where applicable, endpoint, final status, attempt count, and bounded sanitized
failure details. Permit multiple real checks for the same cycle/company/metric.

**Expected files/components**

- Check insertion methods in `CyclicalWavesDataAcquisitionRepository`.
- Failure-code and diagnostic sanitizer.
- Current-cycle successful-check lookup.

**Dependencies**

- Tasks 127-P1-T02 through 127-P1-T04.

**Acceptance criteria**

- Changed/no-change checks require and store hash plus snapshot link.
- Failed checks store null hash/snapshot and a stable bounded failure code.
- `RequestedAtUtc` is null only for pre-request identity/configuration failures.
- Failure messages are at most 1,000 characters and contain no raw response, credential, header,
  token, or unbounded exception detail.
- Multiple failed attempts followed by success remain separately auditable.
- Completion lookup treats only `Changed` and `NoChange` as successful daily checkpoints.

## 127-P3-T03 - Implement complete raw response storage

**Task description**

Carry the exact accepted response string from the acquisition client to snapshot persistence and
store it in `RawResponseJson` without reserialization, field extraction, truncation, or conversion
to `jsonb`. Treat this column as the canonical evidence boundary. Do not place successful raw
response bodies in acquisition checks or logs.

**Expected files/components**

- Client-to-repository accepted-response mapping.
- Snapshot row `RawResponseJson` persistence.
- Regression fixtures containing formatting differences, Unicode, and unknown fields.

**Dependencies**

- Tasks 127-P2-T01 through 127-P2-T04.
- Task 127-P3-T01.

**Acceptance criteria**

- Database round-trip returns the same decoded UTF-8 text accepted by the client.
- Unknown properties, property order, whitespace, and provider date fields remain intact in stored
  raw JSON.
- No selected-value-only payload replaces the raw source.
- Acquisition checks contain only the hash/link and operational metadata, not duplicate raw JSON.
- No consumer-specific structured column is added to either acquisition table.

## 127-P3-T04 - Implement canonical response hashing

**Task description**

Implement deterministic canonicalization of the complete JSON document using JSON Canonicalization
Scheme behavior: recursively ordinal-sort object properties, preserve array order, and emit stable
strings, literals, and numbers. Hash canonical UTF-8 bytes with SHA-256 and return 64 lowercase
hexadecimal characters. Never use the canonical serialization as a replacement for stored raw JSON.

**Expected files/components**

- `CanonicalJsonHasher` implementing `ICanonicalJsonHasher`.
- Canonical JSON writer/helper with bounded parsing behavior.
- Unit fixtures for nested objects, arrays, Unicode, escapes, and number forms.

**Dependencies**

- Task 127-P1-T04.
- Approved canonicalization rules in design section 8.

**Acceptance criteria**

- Whitespace, object-property order, and equivalent numeric lexical forms produce the same hash.
- A semantic change to any nested property produces a different hash.
- Array order remains significant.
- No provider field is excluded from canonicalization.
- Hash output is deterministic lowercase SHA-256 hex of exactly 64 characters.
- Raw input text is not modified by hashing.

## 127-P3-T05 - Implement change detection

**Task description**

Compare the newly calculated canonical hash with the latest snapshot hash for the same company and
metric. Classify equal hashes as `NoChange`; classify a missing or different latest hash as
`Changed`. Preserve legitimate reversions by comparing only to the latest snapshot, never by
globally deduplicating a hash that appeared earlier in the stream.

**Expected files/components**

- Change-classification operation in the repository/application persistence service.
- Latest snapshot query from task 127-P3-T01.
- Changed/no-change result mapping.

**Dependencies**

- Tasks 127-P3-T01 and 127-P3-T04.

**Acceptance criteria**

- First accepted response is `Changed`.
- A hash equal to the latest snapshot is `NoChange` and creates no snapshot.
- A hash different from the latest snapshot is `Changed` and creates a successor.
- `A -> B -> A` is classified as three changed versions, not two globally unique payloads.
- Comparison never uses provider `date`, selected values, or raw-string formatting equality.

## 127-P3-T06 - Implement atomic persistence transaction

**Task description**

Implement the per-metric transaction that reads the latest snapshot, performs change detection,
inserts a snapshot only when changed, inserts the corresponding successful check, and commits both
together. On predecessor uniqueness conflict, roll back, reload the latest snapshot, and repeat the
comparison in a fresh bounded transaction. A provider failure writes only a failed check outside
the successful snapshot transaction.

**Expected files/components**

- Atomic accepted-response method in `CyclicalWavesDataAcquisitionRepository`.
- EF Core transaction boundary and unique-conflict classification.
- Persistence result containing check/snapshot identity and changed/no-change status.

**Dependencies**

- Tasks 127-P3-T01, 127-P3-T02, 127-P3-T04, and 127-P3-T05.
- Migration from task 127-P1-T03.

**Acceptance criteria**

- A changed response commits one snapshot and one linked `Changed` check or commits neither.
- An unchanged response commits one linked `NoChange` check and no snapshot.
- A failed transaction leaves no partial successful evidence.
- A predecessor conflict cannot create a branched or duplicate snapshot chain.
- Conflict recovery is bounded and uses a fresh compare; it does not introduce cross-run ownership
  infrastructure.
- Previously committed snapshots remain untouched on every failure path.

## 127-P3-T07 - Extend persistence for Last P/S metric stream

**Task description**

Extend the acquisition persistence model and additive PostgreSQL migration to support the distinct
`LastPS` metric stream in `CyclicalWavesMetricSnapshots` and `CyclicalWavesAcquisitionChecks`.
Update metric validation, enum/descriptor mappings, indexes, and any persistence contract tests.
Do not add consumer-specific parsed columns: the complete Last P/S response remains in
`RawResponseJson`, including `data.symbol`, `data.ticker`, `data.ps_ratio`, `data.close`, and
`data.date`.

**Expected files/components**

- Metric type constants/descriptors and persistence mapping.
- Additive EF Core migration updating metric-type check constraints.
- Snapshot/check repository tests for `LastPS`.
- Schema and model snapshot updates.

**Dependencies**

- Tasks 127-P1-T02 and 127-P1-T03.
- Task 127-P2-T09.

**Acceptance criteria**

- `LastPS` is accepted by both acquisition tables without weakening validation for `PS`, `PE`, or
  `Equilibrium`.
- Last P/S snapshots and checks use the same predecessor, hash, and restart semantics as the
  existing metric streams.
- The migration is additive and does not rewrite or invalidate existing snapshots/checks.
- The stored raw response round-trips without loss or field extraction.

# Phase 4 - Worker

## 127-P4-T01 - Implement scheduled worker

**Task description**

Implement `CyclicalWavesDataAcquisitionWorker` as a hosted background service. When disabled, it
must perform no company reads, provider calls, or feature writes. When enabled, it must immediately
evaluate the current UTC cycle for startup recovery and then wait for future occurrences of the
configured UTC cron schedule. Await the complete cycle so another occurrence cannot start an
overlapping execution; skip an occurrence that becomes due while the current run is active.

**Expected files/components**

- `FinancialCopilot.Worker/CyclicalWavesDataAcquisitionWorker.cs`.
- Worker `Program.cs` hosted-service registration.
- Schedule calculation abstraction/helper.
- Structured cycle start/completion logging.

**Dependencies**

- Task 127-P1-T01.
- Phase 2 acquisition client and pacing.
- Phase 3 repository transaction.

**Acceptance criteria**

- Disabled configuration causes zero feature activity.
- Enabled startup evaluates the current `CycleDateUtc` without waiting for the next cron boundary.
- Future executions use the validated five-field UTC schedule.
- At most one cycle runs inside the worker at a time.
- Shutdown cancellation interrupts pending schedule delay and active work promptly.
- The worker does not expose a manual acquisition API or use messaging orchestration.

## 127-P4-T02 - Implement deterministic company iteration

**Task description**

Implement the company source and acquisition service traversal over every row in the existing
`NoavaranEligibleCompanies` view. Project `ExternalCompanyId`, `CompanySymbol`, and `SymbolIsin`
from the view, retaining its `Id` only for the existing `Companies.Id` persistence foreign key. Use
the view's `SymbolIsin` as the request identity with no `EnTicker` fallback. Reject a missing or
invalid ISIN by recording four failed checks. Sort valid companies by normalized ISIN and then
`CompanyId`; do not mutate the company catalog/view or apply another eligibility filter.

**Expected files/components**

- EF Core `CyclicalWavesAcquisitionCompanySource`.
- ISIN normalization/validation helper.
- `CyclicalWavesDataAcquisitionService` company loop.

**Dependencies**

- Task 127-P1-T04.
- Task 127-P3-T02 for identity-failure checks.
- Existing `NoavaranEligibleCompanies` view and `Companies.Id` schema.

**Acceptance criteria**

- Every row exposed by `NoavaranEligibleCompanies` reaches valid processing or four explicit
  identity-failure checks.
- The source reads `ExternalCompanyId`, `CompanySymbol`, and `SymbolIsin` from
  `NoavaranEligibleCompanies`; it does not enumerate the full `Companies` table.
- `SymbolIsin` is the only request identity and `EnTicker` is not used as a fallback.
- Missing identity produces `MissingSymbolIsin` and no provider call.
- Valid company order is stable across identical catalog states.
- The implementation performs no company/view insert, update, delete, or additional eligibility
  filter.

## 127-P4-T03 - Implement PS -> LastPS -> PE -> Equilibrium execution order

**Task description**

For each valid company, execute the four metric operations in the fixed order P/S gauge, Last P/S,
P/E, then equilibrium. For each metric, perform checkpoint lookup, provider acquisition, canonical
hashing, atomic accepted-response persistence or failed-check persistence, and request pacing before
moving to the next logical operation. Both P/S streams must remain part of this worker regardless of
current consumer behavior.

**Expected files/components**

- Per-company metric loop in `CyclicalWavesDataAcquisitionService`.
- Closed ordered metric descriptor list mapping metric to endpoint operation.
- Per-metric structured logging scopes.

**Dependencies**

- Tasks 127-P2-T02 through 127-P2-T04 and 127-P2-T08.
- Tasks 127-P3-T02, 127-P3-T04, and 127-P3-T06.
- Task 127-P4-T02.

**Acceptance criteria**

- The ordered list is exactly `PS`, `LastPS`, `PE`, `Equilibrium` and is not data-dependent.
- The next metric does not start until the preceding metric reaches a terminal outcome and pacing
  completes.
- A successful response is persisted before the next provider call.
- All four metric checks retain independent timestamps, hash/result, attempts, and endpoint.
- Both P/S streams are acquired even before any future database-backed consumer migration.

## 127-P4-T04 - Implement restart continuation

**Task description**

Before each metric call, query for a successful `Changed` or `NoChange` check matching the current
`CycleDateUtc`, company, and metric. Skip completed metrics without creating another check. Retry
metrics that have no check or only failed checks. Preserve the cycle date captured when the run
started even if processing crosses UTC midnight.

**Expected files/components**

- Completion-check query in the repository.
- Resume decision in `CyclicalWavesDataAcquisitionService`.
- Startup cycle-date capture in the worker.

**Dependencies**

- Task 127-P3-T02.
- Tasks 127-P4-T01 and 127-P4-T03.

**Acceptance criteria**

- A committed successful same-cycle metric is skipped with zero provider calls and zero new checks.
- A failed-only same-cycle metric is retried.
- A metric with no same-cycle check is attempted.
- A run crossing midnight continues using its original `CycleDateUtc`.
- Restart after a committed transaction retains data and cannot duplicate the snapshot.
- Restart after an uncommitted transaction re-acquires the unfinished metric safely.

## 127-P4-T05 - Implement failure isolation

**Task description**

Wrap each logical metric operation in a failure boundary that records a stable failed check when
possible, emits bounded sanitized structured diagnostics, and proceeds to the next metric. Wrap
each company boundary so an unexpected company-level error does not terminate later company work.
Allow host cancellation to escape promptly. Preserve all previously committed snapshots.

**Expected files/components**

- Metric/company failure boundaries in `CyclicalWavesDataAcquisitionService`.
- Stable failure mapper and bounded diagnostic sanitizer.
- Cycle summary counters for changed, unchanged, failed, and skipped work.

**Dependencies**

- Task 127-P3-T02.
- Tasks 127-P4-T02 and 127-P4-T03.

**Acceptance criteria**

- A P/S failure proceeds to the same company's P/E and equilibrium operations.
- Any metric failure proceeds to later companies.
- Provider failure never deletes, updates, or invalidates an existing snapshot.
- Database failure rolls back partial work; unfinished work remains eligible on a later execution.
- Host cancellation is not swallowed or persisted as a provider failure.
- Logs contain bounded company/ISIN/metric/endpoint/status/attempt/duration/failure evidence and no
  raw responses or secrets.

# Phase 5 - Testing

## 127-P5-T01 - Add unit tests

**Task description**

Add deterministic unit coverage for configuration validation, ISIN resolution, endpoint
construction, timestamp capture, JSON contract validation, canonical hashing, change
classification, metric ordering, pacing, checkpoint decisions, failure mapping, and diagnostic
sanitization. Include the nested Last P/S response contract and `LastPS` metric mapping. Use fakes
and controlled time; do not make live provider or database calls.

**Expected files/components**

- `CyclicalWavesDataAcquisitionOptionsTests`.
- `CyclicalWavesAcquisitionClientTests` for pure request/result behavior.
- `CanonicalJsonHasherTests`.
- `CyclicalWavesDataAcquisitionServiceTests`.
- Test builders for companies, raw payloads, checks, and snapshots.

**Dependencies**

- Completed implementation from phases 1 through 4.

**Acceptance criteria**

- Tests cover enabled/disabled and all invalid configuration boundaries.
- Canonical hash tests cover whitespace, property order, numbers, nested objects, arrays, Unicode,
  escapes, semantic changes, and exact 64-character lowercase output.
- Service tests prove deterministic company and `PS -> LastPS -> PE -> Equilibrium` order with concurrency
  never exceeding one.
- Tests prove P/S acquisition is independent of current consumer behavior.
- Tests prove missing/invalid eligible-company ISIN behavior and cancellation-aware pacing.
- Tests prove failure diagnostics exclude raw payloads and secrets.

## 127-P5-T02 - Add provider contract tests

**Task description**

Test each endpoint through a scripted HTTP handler using representative complete payloads and all
approved transport/contract failure classes. Separately assert logical-operation count and physical
attempt count. Verify exact raw response preservation and correct platform timestamps without
calling the live provider.

**Expected files/components**

- `CyclicalWavesDataAcquisitionProviderContractTests`.
- P/S, Last P/S, P/E, and equilibrium JSON fixtures.
- Scripted HTTP/authentication handler and deterministic time fixture.

**Dependencies**

- Phase 2 implementation.
- Task 127-P3-T04 for canonical hash assertions where integrated.

**Acceptance criteria**

- Each endpoint accepts its representative contract and preserves every raw field.
- Last P/S validates and preserves the nested `data.symbol`, `data.ticker`, `data.ps_ratio`,
  `data.close`, and `data.date` fields, including valid zero values.
- Coverage includes unknown properties, reordered properties, Unicode ticker text, and zero numeric
  values.
- Coverage includes `204`, `400`, `401`, `404`, `408`, `429` with `Retry-After`, `5xx`, network
  failure, timeout, truncated/malformed/non-object JSON, missing fields, numeric overflow/non-finite
  input, and equilibrium identity mismatch.
- Physical attempts never exceed `1 + RetryCount`; non-retryable payload failures use one attempt.
- One logical operation yields one terminal acquisition result regardless of retries.
- Provider `date` and `lastcaldate` never become acquisition/request timestamps.

## 127-P5-T03 - Add PostgreSQL integration tests

**Task description**

Run persistence tests against PostgreSQL with the real EF Core migration and constraints. Verify
schema mapping, exact raw JSON round-trip, first/changed/unchanged/failed transactions, links,
indexes, foreign keys, consistency constraints, and rollback behavior.

**Expected files/components**

- `CyclicalWavesDataAcquisitionPersistenceTests` in IntegrationTests.
- PostgreSQL test fixture applying the Financial Ingestion migration.
- Failure injection around the atomic transaction.

**Dependencies**

- Phase 1 migration.
- Phase 3 persistence implementation.

**Acceptance criteria**

- First success persists one snapshot and one linked `Changed` check.
- Changed success persists one correctly linked successor and check.
- Unchanged success persists no snapshot and one linked `NoChange` check.
- Failed acquisition persists only a failed check and retains the prior snapshot.
- Raw JSON text round-trips without reserialization or loss of unknown fields.
- Atomic failure leaves neither a changed snapshot nor a successful check.
- Database constraints reject invalid result/hash/snapshot combinations and destructive FK actions.

## 127-P5-T04 - Add duplicate prevention tests

**Task description**

Prove duplicate prevention at the hash-comparison, transaction, and database-constraint layers.
Exercise repeated unchanged responses, stale checkpoint replay, predecessor conflicts, and
legitimate response reversion. Use real PostgreSQL for predecessor uniqueness behavior.

**Expected files/components**

- Duplicate scenarios in persistence integration tests.
- Focused canonical hash/change-detection unit tests.
- Concurrent transaction fixture limited to defensive predecessor conflict validation.

**Dependencies**

- Tasks 127-P3-T04 through 127-P3-T06.
- Task 127-P5-T03 test infrastructure.

**Acceptance criteria**

- Repeating an unchanged accepted response creates no additional snapshot.
- Replaying an already committed response when checkpoint lookup is bypassed still creates no
  snapshot.
- Two insert attempts claiming the same predecessor cannot both commit.
- Conflict handling reloads and reclassifies without corrupting or branching history.
- `A -> B -> A` creates three ordered snapshots despite the repeated hash for `A`.
- Acquisition checks remain append-only evidence and are not globally deduplicated.

## 127-P5-T05 - Add restart recovery tests

**Task description**

Test worker/service continuation across simulated shutdown points: before request, during request,
after response but before commit, after commit, between metrics, and between companies. Verify
same-cycle completion lookup, failed-check retry eligibility, original cycle-date retention across
midnight, and startup recovery behavior.

**Expected files/components**

- `CyclicalWavesDataAcquisitionRecoveryTests`.
- Controllable worker host, cancellation, time, client, and repository fixtures.
- PostgreSQL-backed commit/rollback recovery cases.

**Dependencies**

- Phase 4 implementation.
- Tasks 127-P5-T03 and 127-P5-T04 infrastructure.

**Acceptance criteria**

- Work committed before shutdown remains available and is skipped on same-cycle restart.
- Work not committed before shutdown is safely re-acquired.
- Restart creates no duplicate snapshot in any simulated interruption point.
- Metrics with failed-only checks remain retryable; successful metrics are not called again.
- An interrupted cycle continues remaining metrics and companies in deterministic order.
- A cycle crossing UTC midnight retains the date captured at its start.

## 127-P5-T06 - Add worker failure isolation tests

**Task description**

Verify end-to-end worker behavior for endpoint-specific failures, complete company failures,
persistence failures, schedule overlap, pacing, and host cancellation. Assert that all unaffected
metrics and companies continue and that maximum observed provider concurrency remains one.

**Expected files/components**

- `CyclicalWavesDataAcquisitionWorkerTests`.
- Scripted multi-company provider and repository fakes.
- Hosted-service integration fixture with deterministic scheduling/time.

**Dependencies**

- Phase 4 implementation.
- Phase 2 provider failure classifications.

**Acceptance criteria**

- P/S or Last P/S failure still permits the remaining metrics for the same company.
- Any company failure still permits every later company to run.
- Provider and persistence failures retain all earlier committed snapshots/checks.
- Maximum observed logical and physical provider concurrency is one.
- Pacing occurs once between logical operations, including after failure.
- A schedule occurrence during an active cycle does not create an overlapping run.
- Host cancellation exits promptly, and a later startup continues unfinished work.
