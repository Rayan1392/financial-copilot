# Tasks

1. Define scheduled sync configuration options:
   - enabled/disabled flag;
   - refresh cadence;
   - optional fixed execution time/window;
   - dataset selection for symbols, statements, fundamental indexes, product sales, and service
     sales;
   - batch size;
   - concurrency;
   - retry count, retry delay/backoff, and retryable failure categories;
   - missed schedule recovery policy (`Skip`, `RunOnceImmediately`, bounded catch-up);
   - maximum run duration / hung-job timeout;
   - alerting sink and alert severity thresholds.
2. Add an application-level scheduled sync contract that represents:
   - schedule trigger source;
   - requested datasets;
   - run mode (`Incremental` only by default);
   - run correlation id;
   - lock owner/lease metadata;
   - cancellation behavior;
   - manual trigger metadata;
   - missed schedule recovery decision;
   - maximum run deadline.
3. Add a background worker or scheduled job that:
   - honors the enabled flag;
   - computes the next due execution from cadence or fixed execution time;
   - detects missed scheduled executions and applies the configured missed schedule recovery
     policy;
   - invokes only the existing NADPCO bounded orchestration pipeline from spec `043`;
   - passes the configured dataset/batch/concurrency/retry settings without bypassing manual
     DataAdmin behavior.
4. Add a protected manual run trigger for DataAdmin operators that:
   - starts the scheduled-sync workflow on demand;
   - records trigger source as manual;
   - uses the same lock, run-history, retry, alerting, maximum-duration, and orchestration
     behavior as automatic scheduled runs;
   - rejects or queues the request according to the overlap policy when another run is active.
5. Add overlap prevention:
   - use a distributed lock, database-backed lease, or persisted in-progress run state;
   - include lock expiration/heartbeat behavior for crashed workers;
   - ensure multiple worker instances do not execute the same scheduled run concurrently.
6. Add maximum run duration and hung-job recovery:
   - apply a configured deadline to each run;
   - cancel or mark timed-out runs deterministically;
   - expire/release stale leases safely;
   - persist timeout/hung diagnostics;
   - ensure a later run can recover without duplicate overlapping work.
7. Persist scheduled run history with:
   - run id and trigger source;
   - configured schedule snapshot;
   - requested datasets;
   - start/end timestamps;
   - status (`SkippedDisabled`, `SkippedAlreadyRunning`, `Running`, `Succeeded`,
     `PartiallySucceeded`, `Failed`, `Cancelled`, `Missed`, `TimedOut`, `HungRecovered`);
   - processed/failed batch counts;
   - retry attempts and diagnostics;
   - last successful execution timestamp;
   - missed schedule policy decision;
   - manual trigger metadata when applicable;
   - alert emission status;
   - bounded non-secret error messages.
8. Add failure alerting:
   - define alert events for failed, partially successful, missed, skipped-overlap, timed-out,
     and hung-recovered runs;
   - route alerts through a replaceable alerting sink or existing observability abstraction;
   - include run id, trigger source, status, failed batch counts, and bounded diagnostics;
   - redact credentials, tokens, and provider secrets.
9. Ensure scheduled sync reuses existing side effects:
   - raw payload capture;
   - normalization;
   - metric recalculation requests;
   - scanner-cache invalidation;
   - provider health and telemetry;
   - NADPCO sync-state progression.
10. Expose DataAdmin operations for scheduled sync:
   - read status/history if not already covered by existing sync-state endpoints;
   - manually trigger a run;
   - inspect current lock/run state;
   - inspect last missed schedule decision and last alert state.
11. Add a protected health/status endpoint for the scheduler exposing:
   - enabled state;
   - readiness;
   - next due time;
   - last successful execution;
   - active run/lock owner and lease expiry;
   - recent failed/missed/timed-out run summary.
12. Add operational documentation covering:
   - safe defaults;
   - activation order after initial manual backfill;
   - recommended production cadence;
   - retry/failure recovery;
   - missed schedule recovery policy choices;
   - failure alerting configuration;
   - health check interpretation;
   - maximum run duration and hung-job recovery;
   - how to pause scheduled sync;
   - how to manually trigger scheduled sync;
   - how manual DataAdmin sync interacts with scheduled runs;
   - multi-instance locking behavior.
13. Add tests for:
   - scheduled execution when enabled;
   - no execution when disabled;
   - configurable cadence and fixed execution time;
   - dataset selection;
   - manual run trigger;
   - missed schedule recovery policy;
   - overlapping-run prevention;
   - lock expiration or stale in-progress recovery;
   - maximum run duration and hung-job recovery;
   - failed scheduled run persistence;
   - failure alerting;
   - scheduler health/status endpoint;
   - retry diagnostics;
   - successful run history and last-success metadata;
   - successful scanner-cache invalidation via the existing data-sync path.

## Implementation Status

Implemented in order 43.

Evidence highlights:

- `NadpcoScheduledSyncOptions` defines disabled-by-default cadence, dataset selection, batch,
  concurrency, retry, missed-schedule, max-duration, lease, and alert settings.
- `NadpcoScheduledSyncCoordinator` wraps the existing NADPCO bounded orchestration with persisted
  run history, retry, timeout, manual trigger metadata, overlap prevention, stale-lease recovery,
  and alert emission.
- `NadpcoScheduledSyncWorker` runs automatic incremental synchronization from the Worker host.
- DataAdmin endpoints expose manual trigger, status/health, active lock state, and run history.
- `NadpcoScheduledSyncRuns` persists run status, schedule snapshot, datasets, timings, batch
  counts, retry diagnostics, last success, lock metadata, manual metadata, and alert status.
- The implementation reuses the existing `INadpcoApiScheduledSyncService` orchestration, so raw
  payload capture, normalization, derived-metric recalculation, and scanner-cache invalidation stay
  on the same data-sync path as manual NADPCO operations.

Deferred implementation details:

- `ExecutionTimeUtc`, `BatchSize`, `MaxConcurrency`, and `DatasetSelection` are persisted and
  exposed as scheduler configuration snapshots; the existing NADPCO orchestration still owns the
  concrete bounded fan-out and provider read options.
- Missed-schedule bounded catch-up is represented by configuration and trigger source; deeper
  multi-run catch-up queueing remains a future enhancement.

## Change Request Tasks - 2026-06-05

- [ ] Include `CompanyCatalog` in the scheduled NADPCO dataset selection, with daily refresh as
      the default production recommendation once NADPCO credentials and initial backfill are
      verified.
- [ ] Ensure scheduled `CompanyCatalog` refresh performs idempotent insert/update only and never
      executes the clean-slate delete path.
- [ ] Add scheduler run-history fields or diagnostics that expose company-catalog processed row
      count, inserted row count, updated row count, failed row/batch count, and last successful
      company-catalog refresh timestamp where supported.
- [ ] Add tests proving the scheduled worker inserts newly discovered NADPCO companies on a later
      daily run.
- [ ] Add tests proving scheduled runs do not invoke CyclicalWaves for company catalog updates.
- [ ] Update operational documentation to distinguish initial clean-slate NADPCO company import
      from recurring scheduled company refresh.
