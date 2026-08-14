# Feature 126 — CyclicalWaves Relative Valuation Ingestion

## Design status

`READY_FOR_DESIGN_REVIEW`

This document is design-only. It authorizes no production implementation, migration, or change to
Feature 125 business logic.

## 1. Context and problem

Feature 125 needs three independently persisted CyclicalWaves source facts for every eligible
company:

- P/S gauge: current `close` and historical `avg`;
- P/E gauge: current `close` and historical `avg`;
- equilibrium gauge: market-price `close` and equilibrium `balance`.

The current paths are split. Feature 114’s P/S visualization worker fetches P/S, while Feature
125’s P/E/equilibrium source ingestion and calculation are invoked as a post-step of
`NadpcoScheduledSyncCoordinator`. Feature 125 P/S projection currently reads an already-persisted
Feature 114 P/S snapshot.

This creates an unwanted dependency on the NADPCO scheduled workflow and on the timing of a second
P/S worker. It also leaves multiple operational switches controlling adjacent parts of the daily
relative-valuation flow.

Feature 126 introduces one independent daily CyclicalWaves pipeline. The AI request path remains
read-only and continues consuming published Feature 125 data.

## 2. Goals

Feature 126 will:

1. Run one automatic daily CyclicalWaves ingestion process.
2. Enumerate the authoritative `SymbolIsin` list from `NoavaranEligibleCompanies`.
3. Acquire P/S, P/E, and equilibrium data through the existing CyclicalWaves client policies.
4. Persist immutable, provider-scoped source facts for Feature 125.
5. Reuse Feature 114’s accepted P/S operation and visualization persistence.
6. Use one Feature 126 activation switch.
7. Use a database-backed renewable distributed lease with an owner fencing token and bounded
   concurrency.
8. Isolate failures by company and metric.
9. Trigger the existing Feature 125 calculation/publication boundary after acquisition.
10. Remove Feature 125 trigger ownership from `NadpcoScheduledSyncCoordinator` and verify it is
    inactive before Feature 126 activation.

## 3. Non-goals

Feature 126 does not:

- change Feature 125 formulas, IQR/outlier rules, benchmarks, classification, ranking, publication,
  watch state, or semantic/read behavior;
- create a second industry taxonomy or alter canonical company/industry identity;
- compare raw company P/E, P/S, price, or equilibrium values;
- fetch provider data on the AI request path;
- add a manual Feature 126 run API, per-company endpoint, P/E endpoint, equilibrium endpoint, or
  combined manual sync endpoint;
- create a second P/S HTTP or validation implementation;
- create a second authentication/resilience stack;
- add a run-history table or migration;
- make the pipeline wait for or trigger NADPCO scheduled synchronization.

## 4. Current architecture

```text
NadpcoScheduledSyncWorker
    -> NadpcoScheduledSyncCoordinator
        -> NADPCO catalog/financial synchronization
        -> IndustryRelativeValuationOrchestrationService
            -> IndustryRelativeValuationSourceIngestionService
                -> CyclicalWaves P/E and equilibrium
                -> latest persisted Feature 114 P/S snapshot
            -> Feature 125 calculation/publication

CyclicalWavesPsVisualizationSyncWorker
    -> CyclicalWavesPsVisualizationSyncService
        -> CyclicalWaves P/S provider
        -> Feature 114 P/S visualization tables
```

The current `IndustryRelativeValuationSourceFacts` and
`IndustryRelativeValuationSourceLeases` persistence already provide the source-fact and lease
boundary required by Feature 126. Feature 125 calculation and publication services already consume
persisted source facts and should remain the calculation authority.

## 5. Target architecture

```text
CyclicalWavesRelativeValuationWorker
    -> CyclicalWavesRelativeValuationPipeline
        -> distributed run lease
        -> exact NoavaranEligibleCompanies.SymbolIsin admitted list
        -> per company:
             shared Feature 114 P/S operation
             CyclicalWaves P/E gauge
             CyclicalWaves equilibrium gauge
        -> IndustryRelativeValuationSourceFacts
        -> existing Feature 125 calculation/publication boundary
        -> existing watch evaluation/publication flow
```

The pipeline owns source acquisition and the handoff. Feature 125 owns all downstream financial
interpretation and read behavior.

The pipeline does not call NADPCO synchronization, inspect NADPCO scheduler status, or require a
successful NADPCO run in the same process. Its allowed-symbol input is exactly the existing
`NoavaranEligibleCompanies` view projection, not an independently computed
`Companies`/`Industries` query. After admission, no company, industry, market-status, or provider-
scope predicate may remove a symbol from the run.

## 6. Ownership boundaries

### Feature 126 owns

- daily scheduling;
- one activation switch;
- consumption of the authoritative projection
  `SELECT "SymbolIsin" FROM "NoavaranEligibleCompanies"`;
- the renewable distributed run lease and fencing checks;
- bounded concurrency and per-company timeout;
- P/S/P/E/equilibrium acquisition orchestration;
- source-fact persistence and source-fact idempotency;
- correlation and bounded operational evidence;
- the handoff to existing Feature 125 calculation/publication.

### Feature 125 continues to own

- source-barrier and freshness interpretation at calculation time;
- normalization against company-specific historical/equilibrium references;
- industry benchmarks and IQR/outlier handling;
- classification, ranking, and publication selection;
- watch state and transitions;
- semantic capabilities, entity resolution, and read responses.

### Feature 114 continues to own

- the P/S visualization contract;
- P/S payload validation semantics;
- visualization snapshot, history, sync-state, and visualization-lease tables;
- existing P/S visualization read behavior.

Feature 126 reuses a shared scope-free P/S provider operation and Feature 114’s accepted P/S
validation/persistence semantics. It must not call any Feature 114 operation that enumerates or
rebuilds company scope, reinterpret Feature 114 fields, or implement a parallel P/S validator.

### Invocation boundary

Feature 126 has one invocation boundary: its enabled hosted daily worker. It exposes no sync API,
per-company trigger, provider-specific trigger, or caller-supplied symbol/company request. Existing
Feature 114 visualization admin operations, if retained, cannot call the Feature 126 pipeline or
Feature 125 handoff. The pre-cutover Feature 125 source-ingestion interface and its optional
`CompanyId` request path must not remain registered as an independently callable Feature 126 path
after cutover.

## 7. Scheduling lifecycle

### Worker and cadence

Add a bounded hosted worker named conceptually `CyclicalWavesRelativeValuationWorker`. It resolves a
scoped `CyclicalWavesRelativeValuationPipeline` per run and never contains provider or persistence
logic itself.

The configured `DailyCadenceMinutes` is the minimum interval between automatic attempts. The run
boundary also derives the Tehran business/calculation date using the repository’s existing Tehran
timezone helper (`Asia/Tehran`, with the existing Windows fallback `Iran Standard Time`).

The worker starts with an immediate attempt when enabled, then waits for the configured cadence.
The pipeline’s date/lease/idempotency rules prevent multiple successful daily publications when
the worker starts or restarts more than once during a Tehran day.

### Startup

- **AD-126-01 — Approved:** on worker startup, if the current Tehran day has no successful
  completed run, the worker performs one bounded ingestion attempt. If the current Tehran day
  already has a successful completed run, startup is a no-op. Failed, incomplete, expired, or
  abandoned runs may retry safely under the lease, fencing, timeout, and idempotency rules below.
- `Enabled=false`: the worker logs a disabled state and performs no provider requests and no Feature
  125 handoff.
- `Enabled=true`: the worker evaluates the current Tehran day once on startup and applies
  AD-126-01.
- Startup does not require a user request or an API call.
- Startup does not run a company-catalog refresh.

### Missed-run recovery

On restart, the worker evaluates the current Tehran day against the durable marker stored in the
existing `IndustryRelativeValuationSourceLeases` row. If the row has a successful completion marker
for that Tehran date, the automatic attempt is a no-op. If the marker is absent or failed, or the row
contains an expired running owner token, the worker attempts the day. A prior partial run is safely
retryable only when it did not reach terminal completion and handoff; accepted facts are immutable
and unchanged observations are no-ops. A completed `PartialSuccess` has a durable `Succeeded`
marker and is therefore a startup no-op, just like `Success`.

The lease row uses its existing columns only. `Owner` stores a bounded state envelope containing
`Running|Handoff|Succeeded|Failed`, the Tehran calculation date, and the unique owner/fencing token;
`UpdatedAtUtc` records the last heartbeat or terminal transition; and `ExpiresAtUtc` records the
running lease expiry. A terminal marker has an already-expired `ExpiresAtUtc`, so it never blocks a
future acquisition. No new column, table, or migration is introduced.

- **Successful run:** after the full admitted universe has reached terminal per-metric outcomes and
  the fenced Feature 125 handoff returns, the current owner atomically replaces `Running` with
  `Succeeded` for the Tehran date. This covers observability states `Success` and `PartialSuccess`.
- **Failed run:** a pipeline-level failure, cancellation, lost lease, or failed handoff records
  `Failed` only if the caller still owns the live fencing token. Per-company/per-metric failures do
  not make the run structurally failed when the full universe was processed and the handoff
  completed; Feature 125 decides readiness from persisted facts.
- **Crash before completion:** the row remains `Running` until expiry. No success is inferred from
  facts or logs. After expiry, a new owner may atomically acquire the lease and retry the day.
- **Crash after completion:** the committed `Succeeded` marker makes restart a no-op for that day.
  If the crash occurs after the handoff but before the terminal marker commit, the retry is allowed;
  immutable fact identities and Feature 125’s same-date idempotency guard make it safe.
- **Missed day:** startup recovers the current Tehran day. The worker does not synthesize historical
  provider observations for older days; older recovery requires an explicit future product design,
  not a hidden manual Feature 126 trigger.

### Same-day retry

A failed, incomplete, expired, or abandoned source run may be retried on the same Tehran day. A
completed `Success` or `PartialSuccess` may not be retried automatically that day. An allowed retry:

- acquires the same distributed lease name;
- receives a new unique owner/fencing token;
- refetches only according to the configured retry/deadline policy;
- preserves all accepted historical facts;
- inserts only new source observations;
- invokes Feature 125 calculation again only through its existing same-date recalculation and
  publication rules.

A second worker instance that cannot acquire the lease exits without provider requests. A worker
that loses its token stops new work and is rejected from fact persistence and Feature 125 handoff.

## 8. Company eligibility

`NoavaranEligibleCompanies` is the authoritative source of allowed symbols. The Feature 126
pipeline must execute and receive exactly this projection as its admitted input universe:

```sql
SELECT "SymbolIsin"
FROM "NoavaranEligibleCompanies";
```

The view owns the complete eligibility decision. The result set from this exact query is fixed as
the admitted list for the run. Feature 126 must not recreate eligibility rules, filter `Companies`
independently, join `Industries` to decide inclusion, apply provider-scope, company-status, market-
status, or industry rules of its own, or accept company IDs/symbols from a caller. This remains true
after admission: no later mapping, P/S operation, page, batch, or provider operation may narrow the
admitted list.

The pipeline normalizes and validates the selected `SymbolIsin` only as an input-safety step needed
to call CyclicalWaves. An invalid or blank value from the authoritative view is recorded as an
input-quality failure and is not replaced by a separately discovered company. It is not a reason
for Feature 126 to invent a new eligibility rule.

The resulting flow is:

```text
NoavaranEligibleCompanies view
        |
        v
SymbolIsin list
        |
        v
Feature 126 CyclicalWaves pipeline
        |
        +--> P/S
        +--> P/E
        +--> Equilibrium
```

If later persistence, visualization, or Feature 125 handoff requires `CompanyId`, `IndustryId`, or
display metadata, Feature 126 resolves those values through the existing canonical mapping only
after the symbol has been admitted by the view. `Companies` and `Industries` may enrich an admitted
item; they may not admit, reject, or silently skip it. A missing mapping is a terminal mapping-
failure outcome for that admitted symbol and does not cause a fallback eligibility query.

The admitted list is processed in deterministic pages/batches for memory and provider-load
control. Paging is over the materialized result of the exact projection and is not a `Take`, limit,
or filter on eligibility. Every admitted row must reach a terminal per-metric outcome before the
run can be marked successful. Page size controls work partitioning only; it never truncates the
eligible universe.

## 9. P/S reuse strategy

### Shared operation

Extract a reusable, scope-free provider operation from the P/S request and validation logic.
Conceptually:

```text
AcquireAcceptedPsGauge(approvedSymbolIsin, correlation, cancellationToken)
    -> exactly one CyclicalWaves P/S provider request for the approved SymbolIsin
    -> existing P/S authentication, resilience, and payload validation semantics
    -> accepted P/S acquisition result with provider evidence
```

Its input is one `SymbolIsin` already admitted by Feature 126’s exact view query. It does not query
`NoavaranEligibleCompanies`, `Companies`, `Industries`, markets, or provider scope and cannot
rebuild or narrow company scope. Its output is the accepted P/S acquisition result; it performs no
daily scheduling and no second fetch.

Feature 126 is the sole daily caller and owner of this provider operation. From the single accepted
result it coordinates both the Feature 125 `PSGauge` source-fact projection and, when still needed,
Feature 114 visualization persistence. Feature 114 keeps its visualization tables, validation
contract, and read behavior, but does not own or perform a daily P/S provider fetch.

### Locked mapping

```text
circle-chart-data.close -> CurrentPS
circle-chart-data.avg   -> HistoricalAveragePS
```

`BoundaryAverage` remains a visualization boundary fact and is never used as
`HistoricalAveragePS`. The projection carries the accepted payload’s provider identity, source
observation identity, payload hash, fetch timestamp, endpoint, and identity evidence.

### Existing P/S worker transition

There must be exactly one owner of the daily P/S gauge provider request:

1. Before cutover, the existing Feature 114 P/S worker remains the daily request owner.
2. Feature 126 is deployed disabled while the shared scope-free provider operation and pipeline are
   verified without scheduled provider calls.
3. The existing Feature 114 worker’s daily provider-fetch schedule is disabled and verified
   inactive before Feature 126 is enabled.
4. Feature 126 becomes the sole daily P/S acquisition owner. It persists Feature 114 visualization
   data, if retained, from the same accepted result and emits the Feature 125 `PSGauge` projection
   without refetching.
5. The old Feature 114 worker may remain only for work that makes no P/S provider request. It may
   not delegate into the provider operation on its own schedule.
6. Existing Feature 114 admin endpoints, if retained, are legacy visualization operations and are
   not Feature 126 entry points. They must not be wired to the Feature 126 daily provider operation
   or its Feature 125 handoff.

Invariant: at every deployment state, exactly one scheduled component can own daily P/S provider
requests. After cutover that component is Feature 126, and the old P/S worker cannot race it.

## 10. P/E and equilibrium acquisition

Both operations reuse the existing authenticated `CyclicalWavesDataProviderClient` and its common
timeout, retry, throttling, bounded-response, telemetry, and token-cache policies.

### P/E

Endpoint:

```text
/api/pe/circle-chart-data/{ISIN}
```

Mapping:

```text
close -> CurrentPE
avg   -> HistoricalAveragePE
```

Persist as `SourceKind = PEGauge` in `IndustryRelativeValuationSourceFacts`.

### Equilibrium

Endpoint:

```text
/api/equilibrium/gauge/{ISIN}
```

Mapping:

```text
close    -> CurrentMarketPrice
balance  -> EquilibriumPrice
```

Persist as `SourceKind = EquilibriumGauge`.

The provider ticker/ISIN must match the requested canonical company identity. Missing, malformed,
non-finite, zero, and negative operands are not accepted as ready facts.

## 11. Persistence and freshness

### Reused tables

No new tables are needed. The pipeline reuses:

- `IndustryRelativeValuationSourceFacts`;
- `IndustryRelativeValuationSourceLeases` for renewable ownership, fencing, and the bounded durable
  terminal marker described in sections 7 and 13;
- Feature 125 calculation, metric, company-result, watch, evaluation, and outbox tables;
- Feature 114 P/S gauge, history, sync-state, and visualization-lease tables;
- `NoavaranEligibleCompanies` as the authoritative allowed-symbol view;
- `Companies` and `Industries` only for existing canonical mapping when a downstream persistence
  contract requires `CompanyId` or `IndustryId`, never for eligibility.

### Independent observations

CyclicalWaves does not provide a reliable common business date for the three gauges. Feature 126
therefore does not require matching provider dates or a shared provider generation.

Each accepted observation persists its own:

- `FetchedAtUtc`;
- `PersistedAtUtc`;
- source endpoint;
- immutable `SourceObservationId`;
- source watermark;
- payload hash;
- readiness and quality code;
- identity evidence;
- bounded raw payload under the existing audit convention.

Feature 125 continues selecting the latest valid fact independently per company and source kind,
then applies its own configured freshness thresholds at calculation time. Partial acquisition does
not delete or invalidate a prior valid fact.

### Fact identity and corrections

An unchanged provider observation identified by the same provider/source-kind/observation identity
is an idempotent no-op. A corrected provider observation creates a new immutable fact version. The
older fact remains available for calculation provenance and audit.

## 12. Failure handling

Failure is recorded per company and per metric. One failure never aborts the rest of the daily
company universe.

| Outcome | Terminal `FailureCode` | Source-fact behavior | Run behavior |
|---|---|---|---|
| 404 or 204/no data | `NoData` | Persist a non-ready outcome only if supported by the existing fact contract; never create a ready fact | Continue with other metrics/companies |
| Timeout after retry policy is exhausted | `Timeout` | Preserve prior facts | Continue |
| 429 after retry policy is exhausted | `RateLimited` | Preserve prior facts | Continue |
| 5xx after retry policy is exhausted | `RemoteServerFailure` | Preserve prior facts | Continue |
| Auth failure after token retry/invalidation policy is exhausted | `AuthenticationFailed` | Preserve prior facts | Continue; do not log secrets |
| Malformed payload | `InvalidPayload` | Reject | Continue |
| Oversized payload | `ResponseTooLarge` | Reject before unbounded processing | Continue |
| Identity mismatch | `IdentityMismatch` | Reject | Continue and alert through bounded operational logs |
| Zero/negative/non-finite value | `InvalidValue` | Reject the affected fact | Other metrics for the company may still succeed |
| Invalid/blank admitted input | `InputQualityFailure` | Create no ready fact | Complete all three metric outcomes deterministically |
| Missing canonical enrichment mapping | `MappingFailed` | Create no ready fact | Complete all three metric outcomes deterministically |
| Network failure after retry policy is exhausted | `NetworkFailure` | Preserve prior facts | Continue |
| Unclassified terminal metric failure | `UnexpectedFailure` | Preserve prior facts | Continue |
| Cancellation | `Cancelled` once for the run | Stop new work; write `Failed` only while still fenced owner | Preserve facts; never hand off; otherwise leave the stale lease for expiry |
| Overall timeout | `Timeout` once for the run | Stop new work; write `Failed` only while still fenced owner | Preserve facts; never hand off; otherwise leave the stale lease for expiry |
| Lease loss, including a Feature 125 fencing rejection | `LeaseLost` once for the run | Reject further writes | Never report successful handoff or publish |
| Non-fencing handoff failure | `HandoffFailed` once for the run | Preserve accepted facts | Do not mark the run successful |

If P/S succeeds while P/E fails, the P/S fact is retained and P/E remains missing/stale for Feature
125 readiness. The same rule applies to every metric independently.

## 13. Lease, idempotency, and concurrency

### Lease

Reuse `IndustryRelativeValuationSourceLeases` with a stable lease name for the Feature 126 source
run, such as `IndustryRelativeValuationSourceIngestion`. The lease is database-backed and uses the
existing owner, update-time, and expiry columns. Process-local locks are not correctness
mechanisms.

Each attempt generates a cryptographically unique 128-bit owner/fencing token. The existing
128-character `Owner` column uses this exact ASCII envelope and no other representation:

```text
v1|s=<state>|d=<yyyyMMdd>|w=<worker>|t=<token>
```

- fields appear exactly once and in the displayed order; field names and delimiters are
  case-sensitive;
- `<state>` is exactly one character: `R` = `Running`, `H` = `Handoff`, `S` = `Succeeded`, and
  `F` = `Failed`;
- `<yyyyMMdd>` is exactly eight ASCII digits representing a valid Gregorian Tehran calculation
  date;
- `<worker>` is 1–32 ASCII characters from `[A-Za-z0-9_-]`; an instance identity longer than 32
  characters must be reduced before envelope creation to a deterministic 32-character base64url
  SHA-256 prefix, without padding, and the already-reduced value is the identity used for logs and
  comparisons;
- `<token>` is exactly 32 lowercase hexadecimal characters encoding the generated 128-bit fencing
  token; leading zeroes are retained;
- whitespace, escaping, percent-encoding, empty values, extra fields, duplicate fields, alternate
  order, unknown states, and trailing delimiters are invalid. Parsing splits on `|`, requires
  exactly five segments, validates each segment in order, and never accepts a partially parsed
  envelope.

The longest valid envelope is 87 ASCII characters, so it fits the existing 128-character limit
without truncation or a schema change. State transitions retain the same date, worker identity,
and fencing token and replace only the one-character state. Acquisition is one atomic database
compare-and-set: insert the named row if absent, or replace it only when it is terminal or expired.
A read followed by an unconditional update is forbidden. Concurrent acquisition permits exactly
one token to win.

The owner renews the lease on a heartbeat interval materially shorter than `LeaseMinutes` using a
conditional update whose predicate requires the same lease name, active state, and owner token.
Renewal extends `ExpiresAtUtc` and updates `UpdatedAtUtc`. Updating zero rows means ownership has
been lost; the stale worker cancels new provider work immediately.

Every source-fact write occurs in a transaction that locks or conditionally validates the live
lease row and the same owner token before inserting. If the token is stale or expired, the
transaction rejects the write. Before the Feature 125 handoff, an atomic conditional update changes
the same token’s state from `Running` to `Handoff` and renews expiry. Only the winner of that update
may submit the handoff; the heartbeat continues for the active `Handoff` state. Submission alone is
not authorization to calculate or publish: Feature 125 must validate the handoff fencing contract
in section 14 at its own boundary and again in every transaction that can create a downstream side
effect. The same fenced validation is mandatory before writing the terminal marker. Therefore a
stale owner cannot persist facts, complete a successful Feature 125 handoff, create publication
side effects, or overwrite the succeeding owner’s marker.

A new owner may recover an expired lease. Normal completion transitions the still-owned row to the
terminal marker rather than merely releasing it; crash recovery continues to rely on expiry.

Feature 125’s existing calculation-date publication guard remains separate from source acquisition
and prevents two same-date calculation versions from being selected incorrectly.

### Run identity

Every attempt has a generated correlation id and a Tehran calculation date. The correlation id is
included in structured logs and handoff diagnostics, but not in provider payload logs.

### Bounded concurrency

Company processing uses configured bounded concurrency and deterministic pages over the complete
admitted list. Provider requests may run concurrently per company only where the existing client
policies permit it; the EF `DbContext` write boundary must not be shared concurrently. A safe
initial implementation can process each page serially while retaining the configured bound for a
later factory-backed optimization. No page size or concurrency setting may truncate the run.

Per-company timeout and the overall worker cancellation token are mandatory. A slow company cannot
consume the entire daily run without a bounded outcome.

### Overlap and restart

- A second worker instance that cannot acquire the lease performs no provider calls.
- A crash leaves the lease to expire; a restart retries safely.
- An expired or superseded owner fails every fenced write and cannot complete a successful handoff
  or create downstream publication side effects.
- A same-day retry does not duplicate unchanged facts.
- A source-fact correction does not overwrite historical evidence.
- Feature 125 publication remains the only visibility gate for AI reads.

## 14. Feature 125 calculation handoff

After the source phase completes, the pipeline invokes the existing
`IndustryRelativeValuationOrchestrationService` calculation/publication boundary. It does not
reimplement calculation logic.

Immediately before invocation, the pipeline must win the atomic `Running` to `Handoff` transition
for its live owner token. The handoff is forbidden if the lease expired, renewal failed, another
owner replaced the token, or the transition affected zero rows.

The Feature 126 -> Feature 125 handoff request carries all of the following; none may be inferred
from ambient process state:

- `RunIdentity`: the Feature 126 correlation id and Tehran calculation date;
- `FencingToken`: the exact 128-bit token in the current `Handoff` owner envelope;
- `SourceSnapshotEvidence`: the calculation date and a manifest ordered by admitted `SymbolIsin`
  and then `PSGauge`, `PEGauge`, `EquilibriumGauge`. Each entry identifies the ordered immutable
  source-fact ids/versions visible for that company and source kind at handoff, or an explicit
  `Missing` marker when none exists. The request also carries a deterministic digest of the complete
  manifest. Feature 125 still applies its own validity, freshness, and latest-fact selection rules,
  but only against this verified snapshot. This uses existing source-fact identifiers/version
  values and creates no new persistence model.

Feature 125 treats this request as a fenced application boundary. Before calculation begins it
loads the existing `IndustryRelativeValuationSourceLeases` row and rejects unless the lease name,
`Handoff` state, run date, unexpired lease, and fencing token all match. It also verifies that the
source facts it will read match `SourceSnapshotEvidence`; missing, additional, or version-changed
facts reject the handoff rather than silently changing its input snapshot.

The same current-token predicate is enforced inside every Feature 125 database transaction that
can create a calculation, selection, watch, publication, or outbox side effect. The transaction
locks or conditionally validates the existing lease row so takeover cannot interleave between the
fence check and commit. For publication delivered through an outbox, the guarded creation of the
outbox row is the publication side-effect boundary. If any validation affects zero rows, observes
expiry, or finds a replacement token, Feature 125 returns a fenced `LeaseLost` rejection and commits
no side effects from that transaction. It never reports handoff success after such a rejection.
After a successful handoff returns, Feature 126 checks the same token again before committing its
`Succeeded` marker.

This extends fencing through the existing Feature 125 orchestration and persistence mechanisms. It
adds no coordinator, table, migration, public API, or ownership model. A stale Feature 126 owner may
call the in-process boundary, but it cannot obtain a successful calculation handoff or cause a
publication side effect.

**AD-126-02 — Approved:** metric-level failures do not prevent handoff. Once every admitted
`SymbolIsin` has reached terminal outcomes for P/S, P/E, and equilibrium, the live fenced owner may
handoff to Feature 125. A cancelled run never hands off. An overall timeout or cancellation before
terminal completion never hands off. Lease loss also never hands off because the caller is no
longer the live owner.

Partial provider success is valid input to the existing Feature 125 readiness model:

- canonical membership is not deleted because a provider fact is missing;
- stale/missing/invalid facts remain non-ready according to existing rules;
- a complete valid industry snapshot may publish when Feature 125’s own barriers and freshness
  requirements are satisfied;
- an inconclusive or failed calculation follows existing Feature 125 status/publication behavior;
- no AI response is generated directly by the ingestion worker.

Before Feature 126 can be enabled, `NadpcoScheduledSyncCoordinator` must no longer invoke Feature
125 source ingestion or calculation, and that inactive state must be operationally verified. This
precondition prevents double source runs and double calculation triggers. The NADPCO workflow
continues its own catalog and financial-data responsibilities.

## 15. Configuration

Define one Feature 126 section following repository configuration conventions:

```json
{
  "CyclicalWavesRelativeValuationSync": {
    "Enabled": false,
    "DailyCadenceMinutes": 1440,
    "CompanyPageSize": 250,
    "MaximumConcurrency": 4,
    "LeaseMinutes": 120,
    "LeaseHeartbeatSeconds": 30,
    "PerCompanyTimeoutSeconds": 120
  }
}
```

The exact binding names remain implementation detail, but the section must contain one operational
activation flag. `CompanyPageSize` controls batching only and must never limit the number of
admitted symbols processed by a run. `Enabled=false` guarantees:

- no P/S daily relative-valuation fetch;
- no P/E fetch;
- no equilibrium fetch;
- no Feature 125 calculation trigger from Feature 126.

Feature 114’s existing settings may remain for its legacy visualization operation during
transition, but they must not be independently required to activate Feature 126. After cutover,
the Feature 114 daily P/S provider-fetch schedule is disabled; it is not delegated to a second
schedule. The new section is the only switch for the combined daily ingestion path.

Feature 125 read/calculation options remain separate product controls only if the repository’s
existing contract requires them; they must not silently re-enable source acquisition when Feature
126 is disabled.

## 16. Observability

Each scheduled boundary evaluation emits exactly one bounded summary record, including early exits
before a provider attempt. Enum serialization is case-sensitive and uses only the following values;
aliases, numeric serialization, and arbitrary strings are forbidden:

- `RunState`: `Disabled`, `ActivationGuardRejected`, `CurrentDaySucceededNoOp`, `Success`,
  `PartialSuccess`, `Failed`, `Cancelled`, `Timeout`, `LeaseLost`, `HandoffFailed`.
- `LeaseStatus`: `NotAttempted`, `Owned`, `Recovered`, `Contended`, `Lost`.
- `HandoffStatus`: `NotApplicable`, `Succeeded`, `Failed`.
- `TerminationCode`: `Disabled`, `ActivationGuardRejected`, `CurrentDayAlreadySucceeded`,
  `Completed`, `CompletedWithMetricFailures`, `PipelineFailure`, `Cancelled`, `OverallTimeout`,
  `LeaseLost`, `LeaseContended`, `HandoffFailed`.
- `FailureCode`: `NoData`, `Timeout`, `RateLimited`, `RemoteServerFailure`,
  `AuthenticationFailed`, `InvalidPayload`, `ResponseTooLarge`, `IdentityMismatch`, `InvalidValue`,
  `InputQualityFailure`, `MappingFailed`, `NetworkFailure`, `LeaseLost`, `HandoffFailed`,
  `Cancelled`, `UnexpectedFailure`.
- `EndpointName`: `PSGauge`, `PEGauge`, `EquilibriumGauge`.

### Canonical serialization

The summary is compact UTF-8 JSON without a byte-order mark or insignificant whitespace. JSON
property order is exactly the required-field order below. Strings use the JSON escaping rules,
without optional escaping of `/` or non-ASCII characters. Timestamps are UTC strings formatted
exactly as `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`; `TehranDate` is `yyyy-MM-dd`; and
`DurationMilliseconds` is the non-negative 64-bit integer difference between the already-rounded
serialized timestamps. `CorrelationId` is 1–64 characters. A required property is never omitted.

Top-level properties appear in this exact order:

```text
CorrelationId, StartedAtUtc, CompletedAtUtc, DurationMilliseconds, TehranDate, Enabled,
RunState, LeaseStatus, RecoveredLease, EligibleCompanies, AttemptedCompanies,
SucceededCompanies, FailedCompanies, MetricCounts, FailureCodeCounts, EndpointCounts,
TerminationCode, HandoffStatus, PublishedCount, InconclusiveCount
```

All counts are JSON numbers in the non-negative signed 64-bit range or JSON `null` exactly where
this section declares them unavailable. Known empty is `0`. Company counts are the four scalar
properties `EligibleCompanies`, `AttemptedCompanies`, `SucceededCompanies`, and
`FailedCompanies`. Publication and inconclusive counts are the scalar properties `PublishedCount`
and `InconclusiveCount`; both remain `null` until a successful handoff returns both values.

`MetricCounts` is always an object with keys in the exact order `PSGauge`, `PEGauge`,
`EquilibriumGauge`. Each value is an object whose properties appear exactly as `Accepted`,
`Unchanged`, `Failed`. `EndpointCounts` is always an object with the same three keys in the same
order. Each endpoint value has properties exactly as `Attempted`, `Succeeded`, `Failed`. Endpoint
counts describe logical per-company endpoint operations; transport retries do not increment
`Attempted`. A logical operation is `Succeeded` only when a response passes endpoint validation,
including when persistence is later `Unchanged`; every other terminal endpoint outcome is
`Failed`.

`FailureCodeCounts` is always an object containing all 16 keys, in exactly the `FailureCode` order
listed above. No map permits an additional key. A whole count family that is unavailable is still
serialized in its full object shape with every leaf set to `null`; it is never represented as
`null`, `{}`, or an omitted property. Once provider work is permitted, all metric, endpoint, and
failure-code leaves initialize to `0` and remain non-null accumulated values through every later
terminal state.

`FailureCodeCounts` represents terminal business outcomes only. It is accumulated for one run and
is not a count of exceptions, log records, HTTP attempts, or retry attempts. The rules are:

- a terminal failed metric contributes exactly one failure-code increment for that admitted
  company and metric. The increment uses the single final normalized `FailureCode` after all
  permitted transport retries are exhausted;
- P/S failure increments `MetricCounts.PSGauge.Failed` and exactly one corresponding
  `FailureCodeCounts` leaf. P/E does the same for `MetricCounts.PEGauge.Failed`, and equilibrium
  does the same for `MetricCounts.EquilibriumGauge.Failed`;
- when the logical endpoint was attempted, the same terminal metric failure also increments that
  endpoint's `EndpointCounts.Failed`. A failure before an endpoint can be attempted, such as
  `InputQualityFailure` or `MappingFailed`, does not increment endpoint counts;
- one admitted company with terminal failures in all three metrics contributes three metric
  failures and three failure-code increments, even when all three use the same code. Company-level
  aggregation adds no separate failure-code increment;
- a transport failure that is retried contributes nothing while retrying. If the logical metric
  operation ultimately succeeds, it contributes no failure code. If it ultimately fails, its final
  normalized reason (`Timeout`, `RateLimited`, `RemoteServerFailure`,
  `AuthenticationFailed`, `ResponseTooLarge`, `NetworkFailure`, or another applicable listed code)
  is incremented exactly once for that company and metric;
- a cancelled run increments `FailureCodeCounts.Cancelled` exactly once per run. Unfinished metrics
  receive no synthetic metric failure and no synthetic failure-code increment; metric failures that
  became terminal before cancellation remain counted;
- a run that loses its lease increments `FailureCodeCounts.LeaseLost` exactly once per run. A
  Feature 125 fenced rejection is this same lease-loss outcome, not an additional metric or handoff
  failure;
- a handoff that fails for a non-fencing reason increments `FailureCodeCounts.HandoffFailed`
  exactly once per run. It never changes a metric or endpoint count;
- an overall run timeout increments `FailureCodeCounts.Timeout` exactly once per run, in addition
  to any already-terminal metric failures. A pipeline failure before or outside metric processing
  increments `FailureCodeCounts.UnexpectedFailure` exactly once per run unless a more specific
  run-level rule above applies;
- `NoData`, `InvalidPayload`, `IdentityMismatch`, `InvalidValue`, `InputQualityFailure`, and
  `MappingFailed` follow the same one-final-failed-metric rule. No execution path increments more
  than one failure-code leaf for the same terminal metric outcome.

Thus the flat failure-code map is deterministic: metric failures are counted per metric per
company and aggregated per run; `Cancelled`, `LeaseLost`, `HandoffFailed`, overall timeout, and
pipeline failure are counted once per run; there is no additional per-company failure-code count.
Counters are updated from terminal outcome records in deterministic admitted-company and metric
order (`PSGauge`, `PEGauge`, `EquilibriumGauge`). Canonical serialization always emits the fixed
key order above, so concurrency, retry timing, and dictionary insertion order cannot change the
UTF-8 bytes.

Company-count lifecycle is exact:

- before the activation guard permits an attempt, and for a current-day-success no-op, all four
  company counts are `null`;
- after the guard permits work but before eligibility materialization completes,
  `EligibleCompanies=null` and the other three company counts are `0`;
- after materialization, `EligibleCompanies` is fixed to the admitted-row count;
  `AttemptedCompanies` increments once when processing begins for an admitted row;
  `SucceededCompanies` increments once when all three metric outcomes for that row are terminal
  and none failed; `FailedCompanies` increments once when all three are terminal and at least one
  failed. Interrupted rows are counted as attempted but in neither terminal company count.

`RecoveredLease=true` if and only if this attempt acquired an expired prior ownership record.
`LeaseStatus=Recovered` while that ownership is retained and changes to `Lost` if it is later lost.
A fresh owner uses `Owned`; failure to acquire uses `Contended`. Enum fields are never `null`; an
inapplicable handoff is `NotApplicable`.

The state matrix is authoritative:

| Attempt state | Deterministic summary contract |
|---|---|
| Disabled | `Enabled=false`, `RunState=Disabled`, `LeaseStatus=NotAttempted`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=Disabled`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| Activation guard rejection | `Enabled` is the candidate Feature 126 flag, `RunState=ActivationGuardRejected`, `LeaseStatus=NotAttempted`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=ActivationGuardRejected`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. The separate bounded guard diagnostic identifies only a fixed guard reason token and candidate revision, never owner data or evidence payload. |
| Startup current-day-success no-op | `Enabled=true`, `RunState=CurrentDaySucceededNoOp`, `LeaseStatus=NotAttempted`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=CurrentDayAlreadySucceeded`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| Success | `Enabled=true`, `RunState=Success`, `LeaseStatus=Owned` or `Recovered`; all company counts and all map leaves are non-null; every metric and endpoint `Failed` count and every failure-code count is `0`; `AttemptedCompanies=SucceededCompanies`, `FailedCompanies=0`; `TerminationCode=Completed`; `HandoffStatus=Succeeded`; publication and inconclusive counts are non-null. |
| PartialSuccess | `Enabled=true`, `RunState=PartialSuccess`, `LeaseStatus=Owned` or `Recovered`; every admitted symbol has three terminal metric outcomes; all company counts and all map leaves are non-null; `AttemptedCompanies=SucceededCompanies+FailedCompanies=EligibleCompanies`, and at least one metric `Failed` count is greater than `0`; `TerminationCode=CompletedWithMetricFailures`; `HandoffStatus=Succeeded`; publication and inconclusive counts are non-null. |
| Failed | `Enabled=true`, `RunState=Failed`, `LeaseStatus=Owned` or `Recovered`; company counts follow the lifecycle above and all map leaves are accumulated non-null values; `TerminationCode=PipelineFailure`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. A failure after handoff begins uses `HandoffFailed`. |
| Cancelled | Same count rules as `Failed`; `FailureCodeCounts.Cancelled=1`; unfinished metrics add no synthetic failures; `RunState=Cancelled`; `TerminationCode=Cancelled`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| Timeout | Same count rules as `Failed`; the run-level timeout adds exactly one to `FailureCodeCounts.Timeout`; unfinished metrics add no synthetic failures; `RunState=Timeout`; `TerminationCode=OverallTimeout`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| LeaseLost | Same count rules as `Failed`; `FailureCodeCounts.LeaseLost=1`; unfinished metrics add no synthetic failures; `RunState=LeaseLost`; `LeaseStatus=Lost`; `TerminationCode=LeaseLost`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| HandoffFailed | `Enabled=true`, `RunState=HandoffFailed`, `LeaseStatus=Owned` or `Recovered`; all admitted-symbol, metric, failure-code, and endpoint counts are non-null; `FailureCodeCounts.HandoffFailed=1`; `TerminationCode=HandoffFailed`; `HandoffStatus=Failed`; publication and inconclusive counts are `null`. A fencing rejection is instead `LeaseLost`. A handoff that returned both counts successfully is not failed. |
| Lease contention | `Enabled=true`, `RunState=Failed`, `LeaseStatus=Contended`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=LeaseContended`; `HandoffStatus=NotApplicable`; publication and inconclusive counts are `null`. |
| Recovery | `RecoveredLease=true`; all other fields follow the terminal row reached by the recovery attempt. `LeaseStatus=Recovered` unless ownership is later lost, when it is `Lost`. Recovery is not a `RunState` and never implies success. |

Fields forbidden from the summary and all logs are raw provider payloads, credentials, access or
refresh tokens, exception collections, stack traces containing request data, symbol/ISIN lists,
unbounded labels, and any enum or map key outside the contracts above. Payload hashes and canonical
company IDs may appear only in bounded per-operation diagnostics, not in the attempt summary.

No operational status endpoint may trigger a run. If existing read-only activity/status conventions
are extended later, they must remain observational only.

## 17. Rollout and transition

The rollout must avoid both duplicate provider requests and duplicate Feature 125 triggers.

### Activation guard policy boundary

`Feature126ActivationGuard` is an application-level policy boundary implemented with the existing
configuration binding and deployment metadata mechanisms. It is a pure decision contract: it does
not coordinate processes, publish authorization, persist evidence, or introduce a service, table,
migration, public API, or ownership model.

Its complete contract is:

```text
EvaluateActivation(
    CandidateConfigurationRevision,
    DeploymentIdentifier,
    OwnerActivationStates {
        Feature126Enabled,
        LegacyFeature114PsOwnerEnabled,
        NadpcoFeature125TriggerEnabled
    })
    -> Allowed
     | Rejected(reason)
```

`CandidateConfigurationRevision` is the non-empty revision/version already supplied for the bound
configuration artifact by the deployment/configuration mechanism. `DeploymentIdentifier` is the
non-empty identifier already supplied for the running deployment. `OwnerActivationStates` is read
from one application options snapshot and contains the three effective Boolean owner selections;
it is not reconstructed from logs or runtime discovery.

The closed set of rejection reasons is `MissingConfigurationRevision`,
`MissingDeploymentIdentifier`, and `ConflictingOwnerActivation`. Evaluation is deterministic:

- reject a missing or blank candidate configuration revision;
- reject a missing or blank deployment identifier;
- reject when `Feature126Enabled=true` and either legacy owner state is `true`;
- otherwise return `Allowed`.

The two legacy owner states may both be true while Feature 126 is false because they own different
pre-cutover responsibilities: Feature 114 owns the daily P/S fetch and NADPCO owns the Feature 125
trigger. All three false is also allowed as a safe quiescent transition state. The guard validates
ownership compatibility only; the existing Feature 126 distributed lease remains the runtime
mutual-exclusion and fencing mechanism.

The Feature 126 worker evaluates the policy before startup scheduling, before each attempt, and
before its first provider request and handoff. The legacy Feature 114 owner and NADPCO trigger use
the same policy at their corresponding schedule and side-effect boundaries when the ownership
options are wired. A rejection leaves that boundary inert and emits the bounded guard-rejection
summary from section 16 with only the fixed reason token and candidate revision. Each evaluation
uses the currently bound snapshot; a configuration reload naturally causes the next boundary to
evaluate the new revision and states. There is no cached authorization, signature, evidence
document, distributed publication step, activation commit, or special staged runtime state.

Forward cutover uses only existing configuration/deployment operations:

1. Deploy Feature 126 disabled.
2. Disable the Feature 114 daily P/S owner and the NADPCO Feature 125 trigger through their existing
   configuration/wiring, then verify both boundaries are inactive.
3. Deploy or reload a new candidate configuration revision with Feature 126 enabled and both legacy
   owner states false. The guard returns `Allowed`; Feature 126 may then schedule work.

Rollback is the reverse safe sequence:

1. Deploy or reload a revision with all three owner states false so no new Feature 126 attempt can
   start.
2. Using the existing lease row and operational telemetry, wait for or cancel the live attempt and
   verify there is no live Feature 126 lease owner, provider request, or handoff.
3. Deploy or reload a revision with Feature 126 false and the required legacy owner states restored.

Operational verification and drain checks remain rollout steps, not inputs to
`Feature126ActivationGuard`. This keeps the policy executable without signed evidence, a
distributed authorization publisher, hidden infrastructure, or any new persistence.

1. **Prepare disabled:** deploy the shared P/S operation, Feature 126 pipeline, configuration
   binding, shared guard integration for all three owners, and tests with
   `CyclicalWavesRelativeValuationSync.Enabled=false`. Feature 126 makes no provider request and no
   Feature 125 handoff in this state.
2. **Validate source reuse:** verify P/S visualization persistence and the single accepted-payload
   projection without enabling the daily Feature 126 worker. Also verify that the worker reads only
   `SELECT "SymbolIsin" FROM "NoavaranEligibleCompanies"` and does not independently enumerate
   `Companies` or `Industries`.
3. **Stop old P/S ownership:** disable the existing Feature 114 worker’s daily P/S provider fetch
   and verify it cannot invoke the shared provider operation on its own schedule.
4. **Remove old Feature 125 trigger ownership:** disable/remove the Feature 125 source-ingestion and
   calculation invocation from `NadpcoScheduledSyncCoordinator` while Feature 126 remains disabled.
5. **Verify old owners inactive:** execute/observe an NADPCO scheduled run and the old P/S worker
   boundary. Verify zero Feature 125 trigger calls from NADPCO and zero daily P/S provider calls from
   Feature 114. Feature 126 must not be enabled until both checks pass.
6. **Activate Feature 126:** deploy or reload a new candidate configuration revision with Feature
   126 enabled and both legacy owner states false. Verify the application-level guard returns
   `Allowed` for that revision and deployment identifier before the first provider request.
7. **Execute first Feature 126 run:** allow the immediate startup attempt or restart the worker;
   verify one fenced owner, full-universe paging, one P/S request path, P/E/equilibrium acquisition,
   one Feature 125 handoff, and a durable `Succeeded` marker.
8. **Stabilize:** verify that NADPCO continues only its own responsibilities, Feature 125 publishes
   from the new source facts, and the legacy P/S worker remains provider-fetch-free.

There must be no production interval in which both the old P/S daily fetch and Feature 126 daily
P/S fetch are active, or in which both NADPCO and Feature 126 trigger the Feature 125 calculation.

Rollback before Feature 126 enablement keeps Feature 126 disabled. If legacy ownership must be
restored, restore the NADPCO Feature 125 trigger and/or old P/S daily owner only while Feature 126 is
verified disabled.

Rollback after cutover is ordered: disable Feature 126; verify that it has no live fenced owner and
cannot make provider requests or handoffs; then restore the selected legacy P/S owner and NADPCO
Feature 125 trigger. Never restore either old owner first, and never use rollback configuration that
allows Feature 126 and a legacy owner to be active simultaneously.

## 18. Security

- Reuse the existing CyclicalWaves token cache and authenticated HTTP client.
- Keep provider credentials in secrets/environment configuration.
- Do not expose provider credentials or raw payloads through logs or APIs.
- Preserve bounded response bodies and timeout limits.
- Treat provider identity and ISIN identity validation as mandatory anti-misattribution checks.
- Keep Feature 126 without manual API entry points, eliminating an additional privileged ingestion
  surface.
- Preserve existing DataAdmin authorization for legacy Feature 114 operations if those endpoints
  remain available; they do not authorize Feature 126 runs.

## 19. Testing strategy

### Acceptance-decision executable tests

| Acceptance criterion | Executable tests | Required assertions |
|---|---|---|
| AC-02 — startup behavior | `Startup_WhenCurrentTehranDayHasNoSucceededMarker_PerformsOneBoundedAttempt`; `Startup_WhenCurrentTehranDayAlreadySucceeded_IsNoOp`; `Startup_WhenCurrentTehranDayFailedIncompleteOrExpired_RetriesSafely` | A controllable Tehran clock and durable lease fixture prove exactly one bounded startup attempt when success is absent, zero provider calls/writes/handoffs when the current day already succeeded, and a newly fenced idempotent retry for each failed, incomplete, and expired prior state. |
| AC-15 — handoff | `TerminalMetricFailures_WhenAllAdmittedSymbolsTerminal_HandsOffOnce`; `CancelledBeforeTerminalCompletion_DoesNotHandoff`; `OverallTimeoutBeforeTerminalCompletion_DoesNotHandoff`; `LeaseLost_DoesNotHandoff`; `Handoff_WhenOwnerTokenIsStale_IsRejected`; `Handoff_TakeoverBeforeSideEffectCommit_RejectsWithoutPublication` | A universe containing metric-level failures still produces exactly one handoff after every admitted symbol has three terminal outcomes. Cancellation and overall timeout produce zero handoff calls. Every request carries run identity, fencing token, and snapshot/version evidence. Feature 125 validates the current token and snapshot at entry and transactionally at downstream side-effect boundaries; lease loss or takeover cannot return success or commit publication/outbox effects. |
| AC-19 — observability | `AttemptSummary_StateMatrix_MatchesContract` as a data-driven test with rows `Disabled`, `ActivationGuardRejected`, `CurrentDaySucceededNoOp`, `Success`, `PartialSuccess`, `FailedBeforeEligibility`, `FailedDuringProcessing`, `Cancelled`, `Timeout`, `LeaseLost`, `HandoffFailed`, `LeaseContention`, and `Recovery`; `AttemptSummary_CanonicalJson_IsByteForByteStable`; `AttemptSummary_Enums_RejectUnknownOrWrongCaseValues`; `AttemptSummary_RequiredFields_AreNeverOmitted`; `AttemptSummary_ForbiddenFields_AreNeverSerialized` | Exact property/key order, timestamp and date format, enum tokens, fixed map shapes, state-dependent values, `null`, zero, and `NotApplicable` match section 16 byte-for-byte. The two early-exit rows perform no owned side effects. Unknown values, excess keys, omitted fields, raw payloads, secrets, tokens, symbol/ISIN lists, and unbounded labels are rejected or absent. |

These tests are acceptance gates, not illustrative examples. AC-02, AC-15, and AC-19 cannot be
accepted by log inspection or a manual run alone.

### Unit tests

- Single activation switch: disabled means zero provider calls and zero handoff.
- Activation guard inputs are exactly candidate configuration revision, deployment identifier, and
  the three owner activation states; outputs are exactly `Allowed` or one closed-set
  `Rejected(reason)` result.
- Missing revision, missing deployment identifier, and each forbidden Feature 126/legacy-owner
  combination fail closed at startup, schedule, and side-effect boundaries.
- Configuration reload evaluates the newly bound snapshot without cached authorization, signed
  evidence, distributed publication, or hidden persisted state.
- Tehran date, cadence, and the approved AD-126-01 startup behavior.
- Same-day retry, lease expiry, lease contention, cancellation, heartbeat renewal, and atomic
  acquisition.
- Lease expiry while an old worker is running gives a new owner a different fencing token; the
  stale owner cannot persist a fact, renew, write a terminal marker, complete a successful Feature
  125 handoff, or create downstream publication/outbox side effects.
- The lease `Owner` envelope round-trips each state, rejects non-canonical or malformed input,
  preserves leading token zeroes, deterministically reduces overlength worker identities, is at
  most 87 ASCII characters, and never truncates against the existing 128-character column.
- Durable `Succeeded`/`Failed` marker transitions and crash-before/crash-after-completion recovery.
- Exact `NoavaranEligibleCompanies` view projection is the sole eligibility input; changes to
  `Companies`/`Industries` rules do not change the Feature 126 symbol list.
- `Companies`, `Industries`, market status, and provider scope can enrich but cannot filter the
  admitted list.
- Invalid/blank view symbols are handled as input-quality failures without inventing fallback
  eligibility rules.
- The shared P/S operation accepts one approved `SymbolIsin`, performs no scope query, preserves
  visualization values, and emits one projection from one provider response.
- P/S `close`/`avg` mapping and explicit rejection of `BoundaryAverage` as the baseline.
- P/E `close`/`avg` mapping.
- Equilibrium `close`/`balance` mapping.
- Immutable fact identity, unchanged no-op, and corrected-observation versioning.
- Per-metric failure isolation and prior-fact preservation.
- Exact observability enum validation, canonical property/key order, fixed map shapes,
  required/forbidden fields, unavailable representation, and every row of the section 16 state
  matrix, including guard rejection and startup current-day-success no-op.
- Failure-code tests prove one final code per failed company/metric, zero increments for transport
  retries, and exactly one run-level increment for cancellation, lease loss, handoff failure,
  overall timeout, and otherwise-unclassified pipeline failure.

### Provider-contract tests

Cover 404, 204, malformed JSON, oversized bodies, identity mismatch, non-finite values,
zero/negative operands, authentication failure, 429, timeout, network failure, and 5xx for each
relevant provider operation. Assert that raw payloads and secrets are absent from logs.

### Integration tests

- One run enumerates exactly the `SymbolIsin` values returned by
  `NoavaranEligibleCompanies`, without a caller-supplied company ID.
- Full eligible-universe processing is mandatory: no admitted `SymbolIsin` may be omitted because
  of batch size, concurrency, mapping metadata, market status, industry, or provider scope.
- `CompanyId`/`IndustryId` are resolved only after view admission and cannot add or remove symbols
  from the run.
- A universe larger than `CompanyPageSize` is processed completely across deterministic pages;
  every admitted row receives terminal per-metric outcomes, proving paging without truncation.
- One accepted P/S payload produces both the Feature 114 snapshot and Feature 125 `PSGauge` fact.
- One daily run persists all three source kinds where the provider returns valid data.
- Partial P/E/equilibrium failure does not erase valid P/S or prior facts.
- After all admitted symbols reach terminal metric outcomes, partial metric failure still permits
  exactly one live-owner Feature 125 handoff; cancellation or overall timeout before that point
  permits none.
- Two worker instances result in one atomic lease owner and no duplicate daily run.
- Lease heartbeat and takeover tests prove that an expired/stale owner cannot write facts or invoke
  a successful Feature 125 handoff after a new fencing token wins.
- The Feature 126 -> Feature 125 handoff carries run identity, fencing token, and deterministic
  source-snapshot/version evidence; Feature 125 rejects mismatched snapshots and stale tokens.
- Feature 125 validates the live token inside each calculation/publication/outbox side-effect
  transaction, and takeover between handoff submission and commit yields no stale-owner side effect.
- Feature 125 calculation/publication is invoked once through the new fenced handoff boundary.
- Cutover verification proves the NADPCO Feature 125 trigger is disabled before Feature 126 can be
  enabled.
- Candidate-rollout verification proves the guard reads the existing configuration revision,
  deployment identifier, and owner-state snapshot and requires no additional infrastructure.
- Start-order and configuration-reload verification proves each of the three potential owners
  independently rejects forbidden mixed ownership before its provider or handoff boundary.
- Rollback verification proves Feature 126 is disabled and has no live owner before any legacy
  Feature 125 or P/S owner is restored.
- The old Feature 114 P/S worker cannot race Feature 126 and makes zero daily provider requests
  after cutover.
- `NadpcoScheduledSyncCoordinator` no longer invokes Feature 125 after cutover, including when an
  NADPCO scheduled run overlaps a Feature 126 run.
- AI reads remain provider-call-free and consume published Feature 125 rows.
- Serialized attempt summaries for every section 16 row, including activation-guard rejection and
  startup current-day-success no-op, exactly match the canonical section 16 bytes.

### Schema verification

Verify that the design uses only existing tables and produces no EF model or migration change.

## 20. Non-blocking implementation follow-ups

AD-126-01 and AD-126-02 are approved in sections 7 and 14. The activation guard and observability
contracts are closed in sections 17 and 16. None remains an open acceptance decision.

1. Should read-only operational status be added to an existing data-sync monitor in a later slice?
   It must not be a manual trigger and is not required for the schema-free first implementation.
2. Is serial per-company processing acceptable for the first production rollout, or should the
   implementation introduce a scoped `DbContextFactory` to safely use the configured concurrency
   immediately? The design recommends serial writes first unless load testing proves the need for
   parallelism.

Status: `READY_FOR_DESIGN_REVIEW`
