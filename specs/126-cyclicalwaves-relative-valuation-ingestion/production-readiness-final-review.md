# Feature 126 — Final Production Readiness Acceptance Review

## Scope

Final acceptance review after B-01 and B-02 remediation against:

- `production-readiness-final-review.md`
- `b01-durable-event-review.md`
- `observability-remediation-plan.md`

Only blocking findings are recorded. No production code, migration, or approved specification
was modified; only this review document was updated.

## Verification

- Feature 126 unit tests: **44/44 passed**.
- Architecture tests: **10/10 passed**.
- Feature 126 PostgreSQL integration tests: **8/8 passed**.
- The durable-event migration is scoped to `Feature126Events` and `Feature126EventStreams`,
  with their required keys/indexes; no unrelated schema change was identified.
- Production Compose mappings require Feature 126 activation, ownership flags, configuration
  revision, deployment identity, CyclicalWaves credentials, and Seq credentials through
  environment interpolation. Feature 114’s legacy owner and the NADPCO Feature 125 trigger are
  disabled in the production mapping.

## Confirmed controls

- **Ownership:** Feature 126 owns scheduled CyclicalWaves acquisition. NADPCO scheduled
  acquisition is a separate route and does not invoke Feature 125. Feature 125 owns calculation,
  publication, and watch behavior. Feature 114 remains visualization-only.
- **Durable authority:** PostgreSQL owns lifecycle state and sequence allocation. Event identity
  conflicts are rejected, exact replay is idempotent, terminal state is immutable, and Seq is
  post-commit telemetry only. The migration’s event identity and `(RunId, EventSequence)` keys
  enforce the durable uniqueness boundary.
- **Configuration and disabled behavior:** the production mappings and ownership guard inputs are
  explicit; disabled Feature 126 does not acquire its lease or call the provider.
- **Testing:** focused unit, architecture, and PostgreSQL integration coverage passed.

## Blocking findings

### B-03 — Durable append does not validate the live lease fence or takeover lineage

`Feature126PostgresEventSink` validates the request token against the per-run event-stream row,
but it never reads or compares the authoritative `IndustryRelativeValuationSourceLeases` row.
After lease expiry and takeover, the old run’s stream still contains the old token, so a stale
owner can append additional events to that stream if it presents that token. This does not satisfy
the remediation requirement that the live database lease fence reject stale-owner lifecycle
mutation.

The pipeline also sets `RecoveredFromRunId` from the caller’s pre-allocation correlation value,
not from the immediately superseded durable run identified during lease recovery. Therefore the
restart/takeover chain is not provably linked to the abandoned run.

Evidence: `Feature126PostgresEventSink.cs:36-48`; `RelativeValuationPipeline.cs:132-145`;
required live-fence and recovery-link behavior: `observability-remediation-plan.md` sections
O-01/O-02 and crash/restart behavior.

### B-04 — Health/readiness and metrics do not satisfy the operational contract

The management server returns before starting its listener when ingestion is disabled, so the
required `/health/live`, `/health/ready`, and `/metrics` surface is unavailable in disabled mode.
For enabled mode, startup readiness checks database connectivity and the Seq probe, but does not
read the `feature126` lease row, verify lease-renewal capability, or durably acknowledge startup
`run_started` evidence before reporting readiness. The worker’s event append path can also return
success after PostgreSQL commit even when Seq export is unavailable, while the readiness contract
requires telemetry-unavailable state to prevent new ingestion/handoff.

The exporter exposes only aggregate state/counters; the required low-cardinality endpoint result,
failure-code, terminal-outcome/lifecycle, run-duration, and terminal company progress metrics are
not published by `/metrics`. Consequently the endpoints cannot provide the required authoritative
distinction and operational evidence for all enabled, disabled, telemetry-unavailable,
database-unavailable, lease-lost, degraded, starting, ready, and stopping conditions.

Evidence: `Feature126Management.cs:143-166,183-204`; `Feature126PostgresEventSink.cs:95-98`;
`CyclicalWavesRelativeValuationWorker.cs:24-40`; required behavior and metrics:
`observability-remediation-plan.md` sections O-03, O-05, and 3.1.

## Issue classification

- **Blocking:** B-03, B-04.
- **Environment-only:** none. The PostgreSQL integration suite executed successfully.

## Verdict

**NOT_READY**

