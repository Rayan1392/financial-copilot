# Feature 126 — B-01 Durable Event Append Re-Review

## Scope

Focused re-review after EventIdentityConflict remediation. This review covers only the durable
event append boundary, event identity integrity, idempotency, terminal immutability, and fencing.
Only blocking findings are reported.

## Verification

- **Durable append boundary:** `Feature126EventAppender` delegates lifecycle authority to
  `Feature126PostgresEventSink`; the sink opens one PostgreSQL transaction, serializes appends for
  a run with a transaction-scoped advisory lock, validates the durable stream, writes the event
  and stream mutation, and commits before the best-effort Seq export.
- **Exact replay:** an existing `EventId` is acknowledged as a duplicate only when all immutable
  identity/content fields match, including `RunId`, event type, predecessor, owner, fencing token,
  Tehran date, attempt/recovery fields, PostgreSQL-precision occurrence time, schema version, and
  JSON payload.
- **Conflicting reuse:** a reused `EventId` with a different payload, `RunId`, or terminal event
  raises `Feature126AppendRejection.EventIdentityConflict` before stream mutation. The event row
  remains unchanged.
- **Terminal immutability and fencing:** terminal stream state rejects later lifecycle appends;
  existing-run appends require the persisted fencing token. Stale owners are rejected before any
  lifecycle or event write.
- **PostgreSQL durability/concurrency:** `Feature126Events.EventId` is the primary key and
  `(RunId, EventSequence)` is unique. `Feature126EventStreams.RunId` is the stream primary key.
  The per-run transaction advisory lock serializes competing transitions; PostgreSQL constraints
  protect event identity and sequence uniqueness. Seq is post-commit telemetry and is not part of
  the authority.
- **Migration scope:** the remediation migration creates only `Feature126Events` and
  `Feature126EventStreams`, their required keys/indexes, and drops only those tables on rollback.
- **Tests:** 42/42 Feature 126 unit tests passed. Eight PostgreSQL integration tests cover restart
  replay, exact duplicate replay, payload conflict, cross-run conflict, terminal-event conflict,
  concurrent transitions, stale fencing, and terminal immutability. All eight were skipped in this
  environment because Docker/Testcontainers was unavailable; no test failure was observed.

## Blocking findings

None.

## Verdict

**APPROVED**
