# Bug: Single-Month Backfill Performs Synchronous Per-Company RabbitMQ Fan-Out

## Summary

`POST /api/v1/admin/noavaran-current/monthly-backfill/single-month` performed the complete
eligible-company fan-out inside the HTTP request. A large month could require hundreds of RabbitMQ
publishes, so proxy/client cancellation could stop the loop after only part of the work was queued.
The endpoint also gave operators no durable batch identifier with which to distinguish planned,
published, processed, failed, and retryable work.

## Production Evidence

- RabbitMQ messaging was enabled and the worker was consuming requests.
- The 1405/05 invocation advanced the durable backfill start timestamp and processed companies.
- Company `313` (`تکشا`) was retried from `NoDataYet` and completed once the vendor published data.
- RabbitMQ appeared empty because consumed/acknowledged messages are not a historical audit log,
  while the application records a company-month run only when processing begins.

## Root Cause

1. The single-month controller called `IMonthlyActivityBackfillCoordinator.StartAsync` directly
   with the HTTP cancellation token.
2. The coordinator published one company-month at a time.
3. `RabbitMqDataSyncRequestBus.PublishAsync` created and disposed a broker connection and channel
   for every company-month message.
4. Existing idempotency skipped only completed company-months with persisted rows. Concurrent or
   repeated starts could therefore publish duplicate queued/running keys.
5. There was no durable batch/outbox record before RabbitMQ publication.

## Remediation Plan

### Phase 1 — HTTP isolation and efficient broker publication

- [x] Isolate single-month requests from company-level broker publication.
- [x] Return `202 Accepted`; return `AlreadyInProgress` for a concurrent start.
- [x] Publish each relay fan-out as a batch over one RabbitMQ connection/channel.
- [x] Mark messages persistent and attach request/idempotency identifiers as message metadata.
- [x] Preserve retries for `NoDataYet` and failed company-months.
- [x] Add regression coverage for asynchronous dispatch, single-flight behavior, and batch
      publishing.

Phase 2 supersedes the temporary in-process Phase 1 queue: the HTTP request now commits the durable
batch/outbox plan and the worker-owned relay performs RabbitMQ publication.

### Phase 2 — durable orchestration and operator observability

- [x] Add a durable backfill batch/outbox model before broker publication.
- [x] Return a batch identifier from both monthly-backfill endpoints.
- [x] Track planned, published, processed, failed, and retryable counts by batch.
- [x] Acquire a durable lease for queued/running idempotency keys to suppress duplicate fan-outs
      across API instances and process restarts.
- [x] Add operator endpoints and a UI view for batch lifecycle and retry decisions.
- [x] Add publisher confirms and an outbox relay that safely resumes partially published batches.

## Durable Semantics

- The API atomically persists the global active-batch lease, batch row, backfill state, and ordered
  outbox rows before returning `202 Accepted`.
- A unique filtered `ActiveSlot` index enforces one active backfill across API instances.
- Relay rows are claimed with owner/expiry leases; expired claims resume after worker restarts.
- RabbitMQ messages are persistent, mandatory, and publisher-confirmed. Publication is at-least-once;
  deterministic consumer idempotency keys make a relay retry safe after a confirm/database race.
- `NoDataYet` remains terminal for the current batch but explicitly retryable by a later invocation.
- A publish-failed batch retains its active lease until its already-published messages finish, which
  prevents a new batch from duplicating in-flight work.

## Acceptance Criteria

- The single-month endpoint does not perform company-level RabbitMQ publication.
- A successful request returns `202 Accepted` with a durable batch ID after the plan is committed.
- Only one queued/publishing/in-progress backfill exists across application instances.
- The relay declares the queue once and uses one publisher-confirmed connection/channel per batch.
- Failed and `NoDataYet` company-months remain visible and retryable.
- Completed company-months with persisted report rows remain skipped.
- Operators can list batches or query one batch and see lifecycle timestamps, counts, and last error.
- The worker safely resumes pending or expired publication leases after a restart.

## Targeted synchronous recovery endpoint

For explicit operator recovery of one vendor company-month, the DataAdmin API also exposes:

`POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month`

The request contains `companyId`, `shamsiYear`, and `shamsiMonth`. This path deliberately bypasses
RabbitMQ and performs exactly one NADPCO request to `api/v2/MonthlyActivity/ProductSales` with equal
`fromDate`/`toDate` month tokens and `outputTypeId=0`. The returned payload is normalized and
persisted by the standard financial ingestion processor in the HTTP request scope. Every invocation
uses a unique sync idempotency key so an operator-requested refresh is not skipped because an older
successful run exists.

## Verification

- Release solution build: passed with zero warnings and zero errors.
- Focused monthly-backfill unit tests: 19 passed.
- Focused monthly-backfill endpoint integration tests: 9 passed.
- Full unit suite: 1,590 passed.
- Architecture suite: 11 passed.
- Focused synchronous company-month endpoint integration tests: 4 passed.
- Frontend production build: passed.
- Targeted frontend lint for the changed files: passed.
- The pre-existing full integration-suite baseline remains outside this bug's scope; focused endpoint
  coverage is green.
