# Feature 126 — CyclicalWaves Relative Valuation Ingestion

## 1. Feature overview

Feature 126 exists to provide one independent, automatic, daily owner for acquiring the
CyclicalWaves relative-valuation facts required by Feature 125.

Today, the relative-valuation flow is split across two schedules: Feature 114 fetches P/S data for
visualization, while Feature 125 P/E and equilibrium ingestion and calculation are triggered after
the NADPCO scheduled workflow. This couples Feature 125 readiness to an unrelated provider workflow,
allows adjacent behavior to be controlled by multiple operational switches, and creates a race risk
between separate P/S and Feature 125 ingestion paths.

After Feature 126 is implemented, one enabled daily pipeline will:

- take its complete admitted symbol universe only from `NoavaranEligibleCompanies`;
- acquire P/S, P/E, and equilibrium gauges from CyclicalWaves for every admitted symbol;
- turn one accepted P/S response into both Feature 114 visualization persistence and a Feature 125
  `PSGauge` source fact, without a second provider request;
- persist immutable, provider-scoped facts with freshness and identity evidence;
- isolate failures by symbol and metric;
- protect acquisition, writes, and handoff with a renewable database lease and fencing token; and
- hand the completed acquisition run to Feature 125's existing calculation/publication boundary.

The AI request path remains read-only. It consumes published Feature 125 data and never calls
CyclicalWaves directly.

## 2. Actors and system roles

| Actor or subsystem | Role in Feature 126 |
|---|---|
| CyclicalWaves ingestion worker | Hosted daily scheduler. When enabled, it attempts the current Tehran day, creates a scoped pipeline, and contains no provider or persistence logic. |
| Feature 126 pipeline | Sole owner of daily CyclicalWaves relative-valuation ingestion, exact eligible-universe consumption, bounded processing, lease/fencing, source-fact persistence, operational evidence, and the Feature 125 handoff. |
| Feature 125 calculation/publication | Existing authority for freshness interpretation, calculations, industry benchmarks, outlier handling, classification, ranking, publication selection, and watch evaluation/state. |
| Feature 114 visualization persistence | Existing authority for P/S validation and visualization snapshot, history, sync-state, lease, and read contracts. It receives the same accepted P/S result used for the Feature 125 source fact. |
| `NoavaranEligibleCompanies` view | Sole authority for deciding which `SymbolIsin` values are admitted to a Feature 126 run. |
| System operator | Enables or disables the single Feature 126 schedule, monitors evidence, and performs the ordered cutover or rollback. No manual Feature 126 API trigger is required or exposed. |

## 3. Approved run dispositions

The approved design decisions are part of this story's deterministic acceptance boundary:

- **AD-126-01 — Startup timing:** when enabled, the worker performs one bounded current-Tehran-day
  attempt on startup unless that day already has a successful completed marker.
- **AD-126-02 — Partial-run handoff:** metric-level failures do not prevent handoff after every
  admitted symbol has reached a terminal P/S, P/E, and equilibrium outcome. The handoff remains
  subject to live fencing and snapshot validation.

Run dispositions are defined consistently as follows:

- **Complete success:** every admitted row has a terminal outcome for every metric and no metric
  failed. It is eligible for the fenced Feature 125 handoff.
- **Partial success:** every admitted row has a terminal outcome for every metric, at least one
  metric failed, and the run was not cancelled and did not exceed its overall deadline. The live
  fenced owner is eligible for the Feature 125 handoff.
- **Cancelled or overall-timeout:** new work stops and the run is not a complete or partial success.
  It does not invoke a new Feature 125 handoff; it may record a terminal failure only while it still
  owns the live fencing token, otherwise its lease is left for expiry.
- **Pipeline or handoff failure:** the run is failed and may record a terminal failure only while it
  still owns the live fencing token.

Per-company timeout is a terminal metric outcome and may produce partial success. Overall run
timeout is an overall-timeout disposition and never means that the unprocessed universe is
complete.

## 4. User and system stories

### A. Daily automatic ingestion

**As a system operator,** I want the CyclicalWaves relative-valuation ingestion to run automatically
once per Tehran day so that all eligible symbols receive current source acquisition without a manual
API trigger.

Acceptance behavior:

- When enabled, the hosted worker performs one bounded current-Tehran-day attempt on startup and
  then observes the configured daily cadence. A successful completed marker for the current Tehran
  day makes the startup evaluation a no-op.
- The durable Tehran-date marker makes another startup or worker restart on a successfully completed
  day a no-op.
- A failed or incomplete current-day attempt may be retried safely; this does not permit multiple
  concurrent daily owners or duplicate unchanged facts.
- The run processes all admitted symbols and all three metric kinds before it may be classified as
  complete or partial success. Both dispositions are eligible for a fenced handoff; cancellation,
  overall timeout, and lease loss never hand off.
- No caller-supplied company, symbol, or manual sync API is part of the invocation boundary.

### B. Eligible universe

**As a system operator,** I want Feature 126 to use the existing authoritative eligibility view so
that it cannot diverge from the approved company universe.

Acceptance behavior:

- The admitted universe comes only from this exact projection:

  ```sql
  SELECT "SymbolIsin"
  FROM "NoavaranEligibleCompanies";
  ```

- Feature 126 implements no independent company, industry, market-status, provider-scope, or other
  eligibility filter.
- The query result is materialized as the fixed admitted list for the run. Later mapping,
  persistence, provider, page, or batch steps cannot narrow it.
- `Companies` and `Industries` may enrich an already admitted symbol with canonical identifiers or
  display metadata, but cannot admit, reject, or silently skip a symbol.
- A blank, invalid, or unmapped admitted symbol receives a terminal input-quality or mapping-failure
  outcome; Feature 126 does not discover a replacement through another eligibility query.
- Deterministic paging partitions the complete materialized list and never truncates it. Every
  admitted row reaches a terminal outcome for each metric.

### C. P/S ingestion

**As Feature 125 and Feature 114,** we need one accepted CyclicalWaves P/S response to supply both
calculation input and visualization persistence without duplicate provider acquisition.

Acceptance behavior:

- Feature 126 makes one logical P/S acquisition invocation per admitted `SymbolIsin` through the
  shared, scope-free Feature 114 P/S acquisition and validation operation. That invocation owns the
  accepted response used by both consumers. Bounded physical HTTP transport retries may occur only
  inside the existing resilience policy and are not additional logical acquisitions.
- One accepted response produces both:
  - Feature 114 visualization snapshot/history/sync-state persistence; and
  - an immutable Feature 125 source fact with `SourceKind = PSGauge`.
- The mapping is exact:

  ```text
  close -> CurrentPS
  avg   -> HistoricalAveragePS
  ```

- `BoundaryAverage` is never used as `HistoricalAveragePS`.
- The shared operation does not enumerate or filter symbols and does not schedule a second logical
  acquisition. Tests distinguish the logical invocation count from physical HTTP attempt count.

### D. P/E ingestion

**As Feature 125,** I need accepted CyclicalWaves P/E gauge data persisted as source facts so that
the existing calculation can evaluate relative P/E.

Acceptance behavior:

- Feature 126 acquires P/E for every admitted symbol through the existing authenticated
  CyclicalWaves client policies.
- The mapping is exact:

  ```text
  close -> CurrentPE
  avg   -> HistoricalAveragePE
  ```

- An accepted observation is persisted as `SourceKind = PEGauge`.

### E. Equilibrium ingestion

**As Feature 125,** I need accepted CyclicalWaves equilibrium gauge data persisted as source facts so
that the existing calculation can compare current market price with equilibrium price.

Acceptance behavior:

- Feature 126 acquires equilibrium data for every admitted symbol through the existing authenticated
  CyclicalWaves client policies.
- The mapping is exact:

  ```text
  close   -> CurrentMarketPrice
  balance -> EquilibriumPrice
  ```

- An accepted observation is persisted as `SourceKind = EquilibriumGauge`.

### F. Failure isolation

**As a system operator,** I want provider and data-quality failures isolated by admitted symbol and
metric so that one failure does not abort the rest of the daily universe or erase prior valid facts.

Acceptance behavior:

- A timeout preserves prior facts, records `Timeout`, uses the bounded retry/deadline policy, and
  allows other metrics and symbols to continue.
- A `429` preserves prior facts, records `RateLimited`, respects existing backoff/throttling, and
  allows the bounded run to continue.
- A `5xx` preserves prior facts, records `RemoteServerFailure`, retries within the existing deadline,
  and then allows the run to continue.
- An invalid or malformed payload is rejected and recorded as `InvalidPayload`; it creates no ready
  fact.
- A provider ticker/ISIN that does not match the requested canonical identity is rejected and
  recorded as `IdentityMismatch`.
- A missing, non-finite, zero, or negative required operand is rejected for the affected fact.
- If one metric fails while another succeeds, the accepted metric is persisted and retained. The
  failed metric remains missing or stale for Feature 125's readiness decision.
- No per-symbol or per-metric failure stops the remaining universe. After every item has a terminal
  outcome, the run is classified using section 3. Complete and partial success are eligible for a
  fenced handoff; cancellation, overall timeout, and lease loss never hand off. Feature 125 remains
  responsible for deciding calculation readiness after an allowed handoff.

### G. Idempotency and concurrency

**As a system operator,** I want exactly one fenced daily ingestion owner and retry-safe persistence
so that restarts, overlap, and lease takeover cannot corrupt or duplicate results.

Acceptance behavior:

- At most one concurrent worker is the live daily owner. Every loser stops before any provider call,
  source-fact write, Feature 125 handoff, or terminal finalization.
- The live owner retains ownership through renewable heartbeats while processing or handing off.
- A worker whose ownership is stale, expired, or superseded stops new work and cannot persist a
  fact, complete a successful Feature 125 handoff, create a calculation/publication/watch/outbox
  side effect, or finalize the run.
- Every protected action—provider-work admission, source-fact persistence, Feature 125 handoff, and
  terminal finalization—is accepted only for the current live owner.
- Every Feature 126 handoff carries its explicit run identity (correlation id and Tehran calculation
  date), the current fencing token, and deterministic snapshot/version evidence for the admitted
  symbols and their P/S, P/E, and equilibrium source facts, including explicit missing markers and a
  digest of the ordered manifest.
- Feature 125 validates the handoff's lease name, `Handoff` state, run date, unexpired lease,
  fencing token, and snapshot/version evidence before calculation and inside every transaction that
  can create a calculation, selection, publication, watch, or outbox side effect.
- A stale owner or changed source snapshot is rejected. A fencing rejection is `LeaseLost`, reports
  no successful handoff, and commits no downstream side effect.
- At most one live owner may invoke Feature 125 for a handoff attempt, and only for a run disposition
  allowed by section 3.
- A crash leaves `Running` for expiry; a new token may then recover and retry the current Tehran day.
- Reprocessing the same provider/source-kind/observation identity is an idempotent no-op. A corrected
  observation creates a new immutable fact and preserves the earlier version.
- A committed `Succeeded` marker makes further automatic attempts for that Tehran day no-ops.

### H. Feature boundaries

**As a product owner,** I want ingestion and valuation responsibilities to remain separated so that
Feature 126 does not change approved Feature 125 behavior.

Acceptance behavior:

- Feature 126 owns ingestion only: daily scheduling, eligible-universe consumption, acquisition,
  source-fact persistence, lease/fencing, bounded evidence, and submission of the fenced ingestion
  result at the Feature 125 handoff boundary. It owns no calculation, publication, or watch logic.
- Feature 125 owns calculation, freshness/readiness interpretation, classification, ranking,
  publication, and watch evaluation/state.
- Feature 114 owns P/S visualization validation and persistence contracts.
- Feature 126 invokes the existing Feature 125 calculation/publication boundary after all admitted
  rows reach terminal metric outcomes and the run disposition permits handoff under section 3; it
  does not reproduce calculation logic.
- The AI never calls CyclicalWaves directly and remains restricted to published Feature 125 data.
- Feature 126 exposes no manual sync, per-company, provider-specific, or combined ingestion API.
- Feature 126 neither calls nor waits for NADPCO synchronization, inspects NADPCO scheduler status,
  nor requires a successful or prior NADPCO run.
- After cutover, NADPCO owns none of Feature 125 source ingestion, calculation, publication, or watch
  triggering and cannot invoke the Feature 125 handoff boundary.

### I. Rollout safety

**As a system operator,** I want cutover and rollback to preserve single ownership so that duplicate
P/S requests and duplicate Feature 125 triggers cannot occur.

Acceptance behavior:

- `Feature126ActivationGuard` is an application-level policy boundary with this complete contract:

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

  `OwnerActivationStates` is one application options snapshot, not reconstructed from logs or
  runtime discovery.
- The guard returns only `Allowed` or `Rejected(reason)`. Its closed rejection reasons are
  `MissingConfigurationRevision`, `MissingDeploymentIdentifier`, and
  `ConflictingOwnerActivation`.
- The guard returns `Rejected(MissingConfigurationRevision)` for a blank candidate configuration
  revision, `Rejected(MissingDeploymentIdentifier)` for a blank deployment identifier, and
  `Rejected(ConflictingOwnerActivation)` when Feature 126 is enabled with either legacy owner. It
  returns `Allowed` for every other owner-state combination, including all owners disabled and both
  legacy owners enabled while Feature 126 is disabled.
- The guard is a pure configuration decision. Operational verification evidence, drain state, and
  live lease state remain rollout checks and are not guard inputs or outputs.
- Staged activation deploys Feature 126 disabled, validates shared P/S reuse and exact eligible-view
  consumption, disables both legacy owners, verifies both legacy boundaries inactive, and only then
  activates a new candidate revision in which Feature 126 is enabled and both legacy states are
  false.
- The old Feature 114 daily P/S provider-fetch schedule is disabled and verified unable to call the
  shared provider operation before Feature 126 is enabled.
- `NadpcoScheduledSyncCoordinator` is verified unable to trigger Feature 125 source ingestion or
  calculation after cutover and before Feature 126 is enabled.
- No production state permits the old P/S worker and Feature 126 to own daily P/S requests at the
  same time, or NADPCO and Feature 126 to both trigger Feature 125.
- Safe rollback first applies an allowed revision with all three owner states false, then drains or
  cancels Feature 126 and verifies no live fenced owner, provider request, or handoff remains, and
  only then restores selected legacy ownership in a later allowed revision.
- The guard is evaluated before startup scheduling, each attempt, the first provider request, and
  handoff; the legacy Feature 114 and NADPCO boundaries apply the same policy at their corresponding
  schedule and side-effect boundaries. A rejection leaves the evaluated boundary inert.

### J. Deterministic operational evidence

Each scheduled boundary evaluation emits exactly one bounded summary record, including disabled,
activation-guard-rejected, current-day-success no-op, and lease-contention exits. Enum values and
map keys use only these case-sensitive closed sets; aliases, numeric enum serialization, additional
keys, and arbitrary strings are rejected:

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

The summary uses the following canonical JSON serialization profile. It is the single executable
byte-for-byte serialization oracle:

- Encoding is UTF-8 without a byte-order mark.
- JSON is compact and contains no insignificant whitespace.
- Top-level properties use the fixed order specified below; nested objects use their fixed property
  order specified below wherever applicable.
- `/` is never escaped.
- Non-ASCII characters are never escaped.
- JSON control characters use only these canonical short escapes: `\b`, `\f`, `\n`, `\r`, and `\t`.
- Every remaining control character U+0000 through U+001F uses uppercase hexadecimal Unicode
  escaping in the exact form `\u00XX`.
- Alternative equivalent JSON representations are invalid. For example, `"\n"` is valid and
  `"\u000A"` is invalid.
Timestamps are UTC strings formatted exactly as `yyyy-MM-dd'T'HH:mm:ss.fff'Z'`; `TehranDate` is
formatted exactly as `yyyy-MM-dd`; and `DurationMilliseconds` is the non-negative signed 64-bit
integer difference between the already-rounded serialized timestamps. `CorrelationId` is 1–64
characters. Serialization of the same summary is byte-for-byte deterministic regardless of
concurrency, retry timing, or dictionary insertion order.

Top-level properties appear in exactly this order:

```text
CorrelationId, StartedAtUtc, CompletedAtUtc, DurationMilliseconds, TehranDate, Enabled,
RunState, LeaseStatus, RecoveredLease, EligibleCompanies, AttemptedCompanies,
SucceededCompanies, FailedCompanies, MetricCounts, FailureCodeCounts, EndpointCounts,
TerminationCode, HandoffStatus, PublishedCount, InconclusiveCount
```

Cardinality is fixed as follows:

- `MetricCounts` has exactly three keys: `PSGauge`, `PEGauge`, and `EquilibriumGauge`; each has
  properties in exactly the order `Accepted`, `Unchanged`, and `Failed`.
- `FailureCodeCounts` always has exactly these 16 keys in this order: `NoData`, `Timeout`,
  `RateLimited`, `RemoteServerFailure`, `AuthenticationFailed`, `InvalidPayload`,
  `ResponseTooLarge`, `IdentityMismatch`, `InvalidValue`, `InputQualityFailure`, `MappingFailed`,
  `NetworkFailure`, `LeaseLost`, `HandoffFailed`, `Cancelled`, and `UnexpectedFailure`.
- `EndpointCounts` has exactly `PSGauge`, `PEGauge`, and `EquilibriumGauge` in that order; each has
  properties in exactly the order `Attempted`, `Succeeded`, and `Failed` for logical per-company
  endpoint operations. Transport retries do not increment `Attempted`. A logical endpoint
  operation is `Succeeded` only when its response passes endpoint validation, including when later
  persistence is `Unchanged`; every other terminal endpoint outcome is `Failed`.
- No raw payload, symbol list, ISIN list, exception collection, credential, or token is present.

Every count is a JSON number in the non-negative signed 64-bit range or JSON `null` exactly where
the state matrix declares it unavailable. A known empty count is `0`; an inapplicable status is the
literal bounded enum value `NotApplicable`; enum fields are never `null`; and required properties
are never omitted. A whole unavailable map is serialized in its full fixed shape with every leaf
set to `null`, never as `null`, `{}`, or an omitted property. Once provider work is permitted, all
metric, endpoint, and failure-code leaves initialize to `0` and remain non-null accumulated values
through every later terminal state. `PublishedCount` and `InconclusiveCount` remain `null` until a
successful handoff returns both values.

`FailureCodeCounts` counts terminal business outcomes only, never HTTP attempts, exceptions, logs,
or retries. It is accumulated once per run. Transport retries increment no failure counter. After
the bounded retry policy finishes:

- a terminal failed metric increments exactly one normalized `FailureCodeCounts` leaf for that
  admitted company and metric: P/S also increments `MetricCounts.PSGauge.Failed`, P/E also
  increments `MetricCounts.PEGauge.Failed`, and equilibrium also increments
  `MetricCounts.EquilibriumGauge.Failed`;
- if the logical endpoint was attempted, the same terminal metric failure increments that
  endpoint's `EndpointCounts.Failed`; a pre-endpoint failure such as `InputQualityFailure` or
  `MappingFailed` increments neither `EndpointCounts.Attempted` nor any other endpoint counter;
- one admitted company that terminally fails all three metrics contributes three metric failures
  and three failure-code increments even when the normalized code is the same; company aggregation
  adds no separate failure-code increment;
- a retried transport failure contributes nothing while retrying. Ultimate success contributes no
  failure code; ultimate failure increments exactly once under its final normalized code;
- `Cancelled` increments exactly once per cancelled run, `LeaseLost` exactly once per lease-lost run
  (including Feature 125 fencing rejection), and `HandoffFailed` exactly once for a non-fencing
  handoff failure; none of these run-level outcomes increments a metric or endpoint count;
- overall run timeout increments `FailureCodeCounts.Timeout` exactly once per run in addition to
  already-terminal metric failures. A pipeline failure before or outside metric processing
  increments `FailureCodeCounts.UnexpectedFailure` exactly once per run unless a more specific
  run-level rule applies;
- `NoData`, `InvalidPayload`, `IdentityMismatch`, `InvalidValue`, `InputQualityFailure`, and
  `MappingFailed` follow the same one-final-failed-metric rule, and no terminal metric outcome can
  increment more than one failure-code leaf; and
- unfinished metrics receive no synthetic failure. Terminal metric failures already reached before
  cancellation, timeout, lease loss, or handoff failure remain counted.

Counters are accumulated from terminal outcomes in deterministic admitted-company order and metric
order `PSGauge`, `PEGauge`, `EquilibriumGauge`.

Company counts have this exact lifecycle:

- before the activation guard permits an attempt, and for a current-day-success no-op, all four
  company counts are `null`;
- after the guard permits work but before eligibility materialization completes,
  `EligibleCompanies=null` and the other three company counts are `0`; and
- after materialization, `EligibleCompanies` is the fixed admitted-row count;
  `AttemptedCompanies` increments when processing begins for an admitted row;
  `SucceededCompanies` increments when all three metric outcomes for the row are terminal and none
  failed; and `FailedCompanies` increments when all three are terminal and at least one failed.
  An interrupted row is attempted but belongs to neither terminal company count.

`RecoveredLease=true` if and only if the attempt acquired an expired prior ownership record.
`LeaseStatus=Recovered` while that recovered ownership is retained and changes to `Lost` if
ownership is later lost. A fresh owner uses `Owned`; failure to acquire uses `Contended`.

| Attempt state | Required deterministic representation |
|---|---|
| Disabled | `Enabled=false`, `RunState=Disabled`, `LeaseStatus=NotAttempted`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=Disabled`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| Activation guard rejection | `Enabled` equals the candidate Feature 126 flag; `RunState=ActivationGuardRejected`; `LeaseStatus=NotAttempted`; `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=ActivationGuardRejected`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| Startup current-day-success no-op | `Enabled=true`, `RunState=CurrentDaySucceededNoOp`, `LeaseStatus=NotAttempted`, `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=CurrentDayAlreadySucceeded`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| Success | `Enabled=true`; `RunState=Success`; `LeaseStatus=Owned` or `Recovered`; all company counts and all map leaves are non-null; every metric and endpoint `Failed` count and every failure-code count is `0`; `AttemptedCompanies=SucceededCompanies=EligibleCompanies`; `FailedCompanies=0`; `TerminationCode=Completed`; `HandoffStatus=Succeeded`; `PublishedCount` and `InconclusiveCount` are non-null. |
| PartialSuccess | `Enabled=true`; `RunState=PartialSuccess`; `LeaseStatus=Owned` or `Recovered`; every admitted symbol has three terminal metric outcomes; all company counts and all map leaves are non-null; `AttemptedCompanies=SucceededCompanies+FailedCompanies=EligibleCompanies`; at least one metric `Failed` count is greater than `0`; `TerminationCode=CompletedWithMetricFailures`; `HandoffStatus=Succeeded`; `PublishedCount` and `InconclusiveCount` are non-null. |
| Failed | `Enabled=true`; `RunState=Failed`; `LeaseStatus=Owned` or `Recovered`; company counts follow the lifecycle above; all map leaves are accumulated non-null values; `FailureCodeCounts.UnexpectedFailure=1` unless a more specific run-level rule applies; `TerminationCode=PipelineFailure`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. A failure after handoff begins uses `HandoffFailed`. |
| Cancelled | `Enabled=true`; `RunState=Cancelled`; `LeaseStatus=Owned` or `Recovered`; company counts follow the lifecycle above and all map leaves are accumulated non-null values; `FailureCodeCounts.Cancelled=1`; unfinished metrics add no synthetic failures; `TerminationCode=Cancelled`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| Timeout | `Enabled=true`; `RunState=Timeout`; `LeaseStatus=Owned` or `Recovered`; company counts follow the lifecycle above and all map leaves are accumulated non-null values; the run-level timeout adds exactly one to `FailureCodeCounts.Timeout`; unfinished metrics add no synthetic failures; `TerminationCode=OverallTimeout`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| LeaseLost | `Enabled=true`; `RunState=LeaseLost`; `LeaseStatus=Lost`; company counts follow the lifecycle above and all map leaves are accumulated non-null values; `FailureCodeCounts.LeaseLost=1`; unfinished metrics add no synthetic failures; `TerminationCode=LeaseLost`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| HandoffFailed | `Enabled=true`; `RunState=HandoffFailed`; `LeaseStatus=Owned` or `Recovered`; all admitted-symbol, metric, failure-code, and endpoint counts are non-null; `FailureCodeCounts.HandoffFailed=1`; `TerminationCode=HandoffFailed`; `HandoffStatus=Failed`; `PublishedCount=null`; `InconclusiveCount=null`. A fencing rejection is `LeaseLost`; a handoff that successfully returned both publication counts is not failed. |
| Lease contention | `Enabled=true`; `RunState=Failed`; `LeaseStatus=Contended`; `RecoveredLease=false`; all company counts and every metric, failure-code, and endpoint leaf are `null`; `TerminationCode=LeaseContended`; `HandoffStatus=NotApplicable`; `PublishedCount=null`; `InconclusiveCount=null`. |
| Recovery | `RecoveredLease=true`; every other field follows the terminal state reached by the recovery attempt. `LeaseStatus=Recovered` only while ownership is retained; if the recovered owner later loses ownership, it serializes `LeaseStatus=Lost` and follows the `LeaseLost` row. Recovery is not a `RunState` and never implies success. |

## 5. Acceptance criteria

1. **AC-01 — Disabled configuration:** Given `CyclicalWavesRelativeValuationSync.Enabled=false`,
   when the worker starts or reaches its cadence, then it records the disabled state and makes zero
   P/S, P/E, or equilibrium requests and zero Feature 125 handoffs.
2. **AC-02 — Automatic daily scheduling:** Given Feature 126 is enabled and the activation guard
   allows the candidate configuration, when the worker starts or reaches its configured cadence,
   then it attempts the current Tehran day automatically without an API call; a current-day
   `Succeeded` marker makes the evaluation a no-op, while failed or expired attempts remain safely
   retryable.
3. **AC-03 — Exact eligible-view result:** Given a run is the live owner, when it admits its universe,
   then the materialized admitted `SymbolIsin` set exactly equals the result returned by
   `NoavaranEligibleCompanies`; no caller input, enrichment, mapping, market status, industry,
   provider scope, or later processing step adds, removes, or silently skips an admitted row.
4. **AC-04 — Full-universe paging:** Given the admitted universe is larger than
   `CompanyPageSize`, when the run processes deterministic pages, then every admitted row reaches a
   terminal P/S, P/E, and equilibrium outcome; page size, concurrency, enrichment, mapping metadata,
   market status, industry, and provider scope do not truncate or narrow the universe.
5. **AC-05 — P/S single-invocation persistence:** Given an admitted `SymbolIsin`, when Feature 126
   acquires P/S, then exactly one logical P/S acquisition invocation is made through the shared
   operation. A valid accepted response from that invocation produces Feature 114 visualization
   persistence and one `PSGauge` source fact with `close -> CurrentPS` and `avg ->
   HistoricalAveragePS`, with no second logical acquisition and no use of `BoundaryAverage` as the
   historical average. Physical HTTP attempts are counted separately and may exceed one only within
   the bounded resilience policy.
6. **AC-06 — P/E acquisition and mapping:** Given a valid P/E response for an admitted symbol, when
   it is accepted, then `close` is persisted as `CurrentPE`, `avg` as `HistoricalAveragePE`, and the
   immutable fact has `SourceKind = PEGauge`.
7. **AC-07 — Equilibrium acquisition and mapping:** Given a valid equilibrium response for an
   admitted symbol, when it is accepted, then `close` is persisted as `CurrentMarketPrice`,
   `balance` as `EquilibriumPrice`, and the immutable fact has `SourceKind = EquilibriumGauge`.
8. **AC-08 — Persistence and freshness evidence:** Given any accepted gauge observation, when its
   source fact is persisted, then it carries its own `FetchedAtUtc`, `PersistedAtUtc`, endpoint,
   immutable `SourceObservationId`, source watermark, payload hash, readiness/quality code, identity
   evidence, provider scope, and bounded audit payload so Feature 125 can evaluate each source kind's
   freshness independently.
9. **AC-09 — Payload and identity rejection:** Given a malformed/invalid payload, oversized payload,
   identity mismatch, or missing/non-finite/zero/negative required value, when validation runs, then
   no ready fact is created, the bounded failure code is recorded, prior valid facts remain intact,
   and other metric and symbol work continues.
10. **AC-10 — Transient provider failure isolation:** Given a timeout, `429`, network failure,
    authentication failure, or `5xx`, when the existing client policy exhausts its bounded handling,
    then the applicable failure is recorded without secrets or raw payloads, prior facts are
    preserved, and the remaining metrics and admitted symbols continue.
11. **AC-11 — Partial metric success:** Given P/S succeeds while P/E or equilibrium fails (or any
    equivalent partial combination), when the symbol reaches terminal outcomes, then every accepted
    metric remains persisted, no earlier fact is deleted or invalidated, and Feature 125 receives the
    missing/stale metric state through its existing readiness rules.
12. **AC-12 — Single daily lease owner:** Given two or more worker instances attempt the same Tehran
    day, when ownership is resolved, then exactly one instance is observable as the live owner and
    every loser performs zero protected actions: no provider call, source-fact write, Feature 125
    handoff, or terminal finalization.
13. **AC-13 — Lease renewal and fencing:** Given a worker is processing, handing off, or finalizing,
    when its ownership becomes expired, stale, or superseded, then it stops new work and every later
    source-fact write, Feature 125 handoff, calculation/publication/watch/outbox side effect, or
    terminal finalization is rejected; only the current live owner can perform those actions while
    ownership is continuously renewed.
14. **AC-14 — Retry idempotency and recovery:** Given an unchanged observation, restart, crash, or
    same-day retry, when acquisition repeats, then unchanged fact identity is a no-op, corrections
    create new immutable versions, prior versions remain auditable, and Feature 125's same-date guard
    keeps publication retry-safe.
15. **AC-15 — Feature 125 fenced handoff:** Given a complete- or partial-success run in which every
    admitted row has terminal per-metric outcomes, when the live owner submits the Feature 125
    handoff, then the request contains the run identity, current fencing token, and deterministic
    source snapshot/version manifest and digest. Feature 125 validates the lease, `Handoff` state,
    run date, unexpired token, and exact snapshot evidence before calculation and in every
    side-effecting transaction. A stale owner or changed snapshot receives no successful handoff and
    causes zero calculation, selection, publication, watch, or outbox side effects. Cancellation,
    overall timeout, and lease loss invoke zero allowed handoffs. Feature 125 alone determines
    freshness, calculation, ranking, publication, and watch outcomes.
16. **AC-16 — Component boundaries:** Given Feature 126 is active, when ingestion and reads occur,
    then Feature 126 owns ingestion only, Feature 114 owns visualization only, Feature 125 owns
    calculation/publication/watch, NADPCO has no Feature 125 ownership after cutover, the AI makes no
    CyclicalWaves call, and no manual Feature 126 trigger exists.
17. **AC-17 — ActivationGuard policy:** Given `CandidateConfigurationRevision`,
    `DeploymentIdentifier`, and `OwnerActivationStates` containing `Feature126Enabled`,
    `LegacyFeature114PsOwnerEnabled`, and `NadpcoFeature125TriggerEnabled`, when the
    application-level `Feature126ActivationGuard` evaluates them, then it returns `Allowed` or one
    deterministic `Rejected(reason)` from the closed set `MissingConfigurationRevision`,
    `MissingDeploymentIdentifier`, and `ConflictingOwnerActivation`. It rejects Feature 126 enabled
    with either legacy owner and allows all other owner combinations. Operational evidence, drain
    state, and live leases are not inputs or outputs.
18. **AC-18 — Staged rollout and safe rollback:** Given a forward rollout, when ownership changes,
    then Feature 126 remains disabled while reuse and view consumption are validated, both legacy
    owners are disabled and verified inactive, and only a later allowed revision enables Feature
    126 with both legacy states false. Given rollback, an allowed all-disabled revision prevents new
    work, Feature 126 is drained and verified to have no live owner/request/handoff, and only a later
    allowed revision restores selected legacy owners. Every mixed state in which Feature 126 and
    either legacy owner are enabled is rejected.
19. **AC-19 — Deterministic bounded observability:** Given any scheduled boundary evaluation or
    terminal attempt state, when its single summary is emitted, then section J is the complete
    deterministic oracle: the canonical JSON serialization profile uses UTF-8 without a BOM,
    compact JSON without insignificant whitespace, fixed top-level and applicable nested property
    ordering, never escapes `/` or non-ASCII characters, uses only the canonical short escapes for
    JSON control characters, and uses uppercase `\u00XX` escaping for all remaining U+0000–U+001F
    characters. Equivalent JSON representations are invalid; for example, `"\n"` is valid and
    `"\u000A"` is invalid. The summary has the exact timestamp, Tehran-date, enum, number, `null`,
    zero, and `NotApplicable` representations and is byte-for-byte identical for the same summary
    values. Every required property and every fixed-map leaf is present. The exact row invariants in
    section J apply to `Disabled`, activation rejection,
    startup current-day-success no-op, `Success`, `PartialSuccess`, `Failed`, `Cancelled`, `Timeout`,
    `LeaseLost`, `HandoffFailed`, lease contention, and recovery, including
    `LeaseStatus=Lost` when a recovered owner later loses ownership. Terminal business outcomes—not
    HTTP attempts or retries—are counted: each terminal metric failure increments exactly one metric
    failure and one normalized failure-code leaf; it increments `EndpointCounts.Failed` only when
    that logical endpoint was attempted; pre-endpoint failures increment no endpoint counter; and
    pipeline-level unexpected failure, overall timeout, cancellation, lease loss, and non-fencing
    handoff failure follow their once-per-run mappings in section J. No credential, token, raw
    payload, symbol/ISIN list, exception collection, unbounded value, unknown enum, excess property,
    or excess map key is emitted.
20. **AC-20 — Persistence compatibility:** Given an initial attempt, interruption, restart,
    same-day retry, lease recovery, or unchanged replay, when Feature 126 persists and resumes its
    workflow, then existing persisted state supports the required fact, visualization, lease,
    handoff, and downstream-result lifecycle; retry, recovery, fencing, auditability, and idempotency
    behavior are preserved; previously persisted compatible state remains readable and usable; and
    activation or rollback introduces no destructive persistence behavior or loss of prior facts.
21. **AC-21 — NADPCO independence:** Given NADPCO is disabled, has failed, or has never executed,
    when Feature 126 runs, then it can perform its approved startup/cadence evaluation, become the
    live lease owner, materialize the `NoavaranEligibleCompanies` result, invoke CyclicalWaves,
    persist accepted source facts, and submit an allowed Feature 125 handoff. It makes no NADPCO
    workflow call, does not inspect or wait for NADPCO scheduler status, requires no prior or
    successful NADPCO run, and NADPCO cannot trigger Feature 125 after cutover.

## 6. Out of scope

- Changes to Feature 125 algorithms, formulas, IQR/outlier rules, benchmarks, classifications,
  rankings, publication selection, or watch behavior.
- Semantic-layer, entity-resolution, or AI read-model changes.
- AI prompt changes or any direct CyclicalWaves call from the AI request path.
- New valuation formulas or raw cross-company value comparisons.
- Manual sync APIs, per-company endpoints, provider-specific triggers, or combined Feature 126
  ingestion endpoints.
- A new P/S validator, authentication stack, resilience stack, or independent eligibility model.
- A database migration or schema change.
- Historical missed-day synthesis, a Feature 126 run-history table, or a triggering status endpoint.

## 7. Testing expectations

| Acceptance criterion | Expected test category | Expected proof |
|---|---|---|
| AC-01 | Unit, Integration | Disabled configuration produces no provider calls or handoff. |
| AC-02 | Unit, Integration | Startup immediately evaluates the current Tehran day; current-day success is a no-op and failed/expired attempts remain retry-safe. |
| AC-03 | Unit, Integration, Architecture conformance | The admitted set equals the view result, caller/enrichment inputs cannot change it, and structural proof confirms the approved exact projection. |
| AC-04 | Unit, Integration | A universe larger than page size is deterministically and completely processed. |
| AC-05 | Unit, Provider contract, Integration | One logical P/S invocation produces both persistence targets with exact mapping; separately counted HTTP attempts follow the bounded scripted retry policy. |
| AC-06 | Unit, Provider contract, Integration | P/E contract, exact field mapping, and `PEGauge` persistence. |
| AC-07 | Unit, Provider contract, Integration | Equilibrium contract, exact field mapping, and `EquilibriumGauge` persistence. |
| AC-08 | Unit, Integration | Each independent fact retains complete freshness, source, identity, quality, and audit evidence. |
| AC-09 | Unit, Provider contract, Integration | Invalid, oversized, mismatched, and non-positive/non-finite data are rejected without collateral loss. |
| AC-10 | Provider contract, Integration | Timeout, 429, network, auth, and 5xx policies isolate failures and sanitize evidence. |
| AC-11 | Unit, Integration | Partial metric success persists independently and preserves prior valid facts. |
| AC-12 | PostgreSQL concurrency, Integration | Concurrent attempts expose exactly one live owner; losers perform no provider call, fact write, handoff, or finalization. |
| AC-13 | Unit, PostgreSQL concurrency, Integration | Heartbeat, expiry, and takeover scenarios reject every protected action by stale owners. |
| AC-14 | Unit, PostgreSQL concurrency, Integration | Restart/retry is an unchanged no-op; correction versioning and same-date publication remain safe. |
| AC-15 | Unit, PostgreSQL concurrency, Integration | Complete and partial success carry run identity, fencing token, and exact snapshot/version evidence; stale tokens and changed snapshots yield no successful handoff or downstream side effects. |
| AC-16 | Unit, Integration | Ownership boundaries hold, no manual trigger exists, and AI reads are provider-call-free. |
| AC-17 | Configuration-policy test, Integration | The application-level guard deterministically evaluates the explicit revision, deployment identifier, and owner-state snapshot; exact allowed/rejected matrix and closed reasons are proven without runtime-evidence inputs. |
| AC-18 | Configuration-policy test, Rollout verification, PostgreSQL concurrency | Staged activation, forbidden mixed ownership, drain verification, and ordered safe rollback are proven independently of the guard's pure input contract. |
| AC-19 | Unit, Serialization contract, Integration | The complete section J state matrix, lifecycle equalities, failure/endpoint mappings, fixed shapes and orders, canonical formats and escaping, and forbidden values are asserted; identical summaries serialize to identical compact UTF-8 bytes, including recovered-then-lost ownership. |
| AC-20 | Integration, Recovery, Rollout verification | Existing persisted state remains compatible through initial execution, interruption, restart, recovery, unchanged replay, handoff, and rollback; retry/audit/idempotency behavior is preserved and no destructive persistence behavior or prior-fact loss occurs. |
| AC-21 | Unit, Integration | Feature 126 completes each permitted stage with NADPCO disabled, failed, and never-run, with zero NADPCO calls or status reads. |

Provider-contract coverage must exercise, as applicable to each P/S, P/E, and equilibrium operation:
`204`, `404`, malformed JSON, oversized responses, identity mismatch, non-finite values,
zero/negative operands, authentication failure, `429`, timeout, network failure, and `5xx`.

P/S provider-contract tests maintain two independent counters. The logical-acquisition counter is
exactly one per admitted `SymbolIsin`. The physical-HTTP-attempt counter is one for immediate
success and equals the deterministic number of scripted responses consumed for a retry case, never
exceeding the configured resilience bound. A transport retry never increments the logical counter
or creates a second persistence projection.

PostgreSQL concurrency coverage must use real concurrent transactions to prove atomic acquisition,
heartbeat ownership, expiry/takeover, fenced fact writes, fenced `Running` to `Handoff` transition,
Feature 125 validation of run identity/token/snapshot evidence at entry and in every side-effecting
transaction, and fenced terminal-marker writes. It must prove that a stale Feature 126 owner cannot
obtain handoff success or commit calculation, selection, publication, watch, or outbox effects.
These are mechanism-level proof notes for AC-12, AC-13, and AC-15.

Eligibility architecture proof must confirm that the implementation reads only:

```sql
SELECT "SymbolIsin"
FROM "NoavaranEligibleCompanies";
```

The literal query is a mechanism-level proof note for AC-03. Its acceptance result remains exact
set equality and absence of post-admission filtering.

Rollout verification must exercise the complete owner-state matrix through the application-level
guard using explicit candidate revision and deployment identifier inputs. Separately, it must
observe both legacy boundaries before enablement and after cutover, reject both forbidden mixed
ownership states, complete staged activation, and prevent legacy restoration until the safe rollback
sequence has disabled and drained Feature 126.

Status:
READY_FOR_USER_STORY_REVIEW
