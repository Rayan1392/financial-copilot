# Feature 126 Observability Remediation Review

## Scope

This review covers implementation readiness of `observability-remediation-plan.md` only. No code,
migration, or existing specification was modified.

## Verdict

`NEEDS_CHANGES`

The ownership and database boundaries are consistent, and the metric label plan is acceptable.
Implementation is blocked by unresolved durability, ordering, sink-integration, and health-contract
decisions below.

## 1. Architecture consistency

No implementation-blocking architecture finding.

- Feature 126 remains responsible for ingestion observability, lease/fencing evidence, provider
  endpoint outcomes, and handoff status.
- Feature 125 remains the authority for calculation, publication, ranking, freshness interpretation,
  and watch behavior. The plan correctly treats handoff acceptance as the Feature 125 boundary.
- Feature 114 remains visualization-only and does not regain provider acquisition or ingestion
  ownership.

## 2. Durable evidence design

### Blocking finding O-01 — Run identity and recovery linkage are underspecified

The plan requires a stable unique `run_id` and says that a recovered run receives a new id linked to
the abandoned run, but it does not define the generation and persistence protocol or a required
`recovered_from_run_id`/attempt-link field. It is therefore not possible to prove that startup,
crash recovery, retry, and takeover produce one unambiguous run lineage without duplicate or
orphaned evidence.

Before implementation, define:

- the exact run-id generation format and owner of allocation;
- the atomic point at which the id is persisted before provider work;
- the required parent/recovery link for a takeover; and
- whether a same-day retry after a terminal failure is a new run or a new event sequence on the
  same run.

### Blocking finding O-02 — Event ordering and terminal idempotency are not implementable yet

`event_sequence` is described as monotonic and `(run_id, event_sequence)` is the idempotency key,
but the plan does not specify who allocates sequence numbers when lease renewal, checkpoints,
recovery, stale-owner events, and terminal finalization can race. It also requires exactly one
terminal event without defining the sink-side compare-and-set/upsert rule for duplicate terminal
submissions or out-of-order delivery.

Before implementation, define a single ordering authority and atomic contract, including:

- sequence allocation or a deterministic state-transition sequence derived from the fenced lease;
- accepted predecessor states for every lifecycle transition;
- stale-owner event handling; and
- sink behavior for duplicate, late, and out-of-order events.

### Blocking finding O-03 — Crash evidence is not guaranteed when the telemetry sink is unavailable

The plan makes durable telemetry acknowledgement a readiness condition and says telemetry
backpressure is unhealthy, but it does not state the fail-closed behavior for an enabled worker
whose sink is unavailable after readiness or during a run. Without that decision, the worker could
continue ingestion with no durable `run_started`, checkpoint, or terminal evidence—the original
production blocker in another form.

Before implementation, specify that provider work and handoff are prohibited unless the required
run event is durably acknowledged, and define bounded behavior for exporter outage, queue overflow,
ack timeout, and recovery after the sink returns.

## 3. Operational feasibility

### Blocking finding O-04 — Telemetry sink and delivery contract are not concrete enough

“An approved OpenTelemetry-compatible collector and log/trace backend, or the platform equivalent”
does not identify the production sink, retention policy, authentication/configuration contract,
durable acknowledgement semantics, query API, or materialized latest-run view required by the plan.
The implementation cannot produce a verifiable dashboard, alert link, restart query, or integration
test without selecting the sink contract.

Before implementation, approve one sink path and specify its retention, delivery mode, retry/queue
behavior, acknowledgement meaning, schema versioning, access control, and query/dashboard
integration. The process-local registry must not be used as a fallback source of truth.

### Blocking finding O-05 — Worker health/readiness publication contract is missing

The plan defines readiness and unhealthy conditions but not where the worker exposes them, how the
existing host/API health surface consumes worker state, or whether a separate worker health probe
is required. It also does not define the exact fail-open/fail-closed behavior for telemetry outage,
lease loss, and disabled mode.

Before implementation, define the externally consumed readiness/liveness contract, including probe
endpoint or metric names, state transitions, startup/shutdown behavior, and the rule that an enabled
worker cannot be considered ready while required durable evidence is unavailable.

## 4. Database boundary

No implementation-blocking database finding.

- No migration is required by the plan.
- No run-history table is introduced.
- `IndustryRelativeValuationSourceLeases` remains the lease/fencing and bounded terminal-marker
  boundary.
- `IndustryRelativeValuationSourceFacts` remains immutable ingestion evidence and is not repurposed
  as a run log.
- Feature 125 and Feature 114 persistence boundaries remain unchanged.

This conclusion depends on the external sink contract in O-04 being durable and queryable; moving
the full run evidence into PostgreSQL would change the approved database boundary and would require
a separate design decision.

## 5. Required changes before approval

Resolve O-01 through O-05 in the remediation plan or an implementation-specific contract before
production implementation review. No other implementation-blocking findings were identified.

Final verdict: `NEEDS_CHANGES`
