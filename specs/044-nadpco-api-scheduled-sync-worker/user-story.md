# NADPCO API Scheduled Synchronization Worker

## User Story

As a data administrator,
I want NADPCO incremental synchronization to run automatically on a configurable schedule
so the normalized financial dataset stays fresh without requiring manual DataAdmin commands.

## Context

Spec `043-nadpco-api-sync-orchestration` defines the bounded manual orchestration pipeline for
`NadpcoApi`: company catalog, statements, fundamental indexes, product sales, and service sales.
This story adds an automatic background worker or scheduled job that invokes the same
orchestration path for incremental refreshes.

Scheduling must not introduce a second data-ingestion path. Raw payload storage, normalization,
derived-metric recalculation, scanner-cache invalidation, provider health, telemetry, and manual
DataAdmin operations remain owned by the existing provider-neutral and NADPCO orchestration
services.

## Acceptance Criteria

1. The system supports automatic scheduled incremental NADPCO synchronization through a
   background worker or scheduled job.
2. Scheduled sync can be enabled or disabled through configuration, with disabled as the safe
   default unless explicitly configured otherwise.
3. Scheduled sync cadence, execution time, dataset selection, batch size, concurrency, and retry
   policy are configurable.
4. Scheduled sync uses the same bounded NADPCO orchestration pipeline as manual DataAdmin full
   and incremental sync commands.
5. DataAdmin users can manually trigger the scheduled-sync workflow outside the configured
   cadence without bypassing locking, run-history, retry, alerting, or orchestration behavior.
6. The system has an explicit missed schedule recovery policy, configurable as skip, run once
   immediately, or catch up within a bounded maximum number of missed executions.
7. Scheduled sync never performs query-time synchronization and never calls remote NADPCO
   endpoints outside the approved orchestration/provider abstractions.
8. The system prevents overlapping scheduled sync executions with a distributed lock or persisted
   sync-run state that works across multiple worker instances.
9. The system enforces a configurable maximum run duration and treats over-limit runs as hung or
   timed out, releasing or expiring leases safely so future runs can proceed.
10. Every scheduled sync run is persisted with operational status, start time, end time, processed
   batch count, failed batch count, retry diagnostics, and last successful execution metadata.
11. Failed, partially successful, missed, skipped-overlap, and hung scheduled runs can emit
    operational alerts through the configured alerting sink without leaking secrets.
12. A protected health/status endpoint exposes scheduler readiness, enabled state, next due time,
    last successful execution, current lock/run state, and recent failure summary.
13. Failed scheduled runs retain enough diagnostics for DataAdmin operators to determine whether
   the next run can retry automatically or needs manual intervention.
14. Scanner caches are invalidated after successful normalized changes from scheduled sync through
   the existing data-sync/cache-invalidation path.
15. Manual DataAdmin sync operations and scheduled sync operations remain observable separately
    enough to identify the trigger source, but they share ingestion, normalization, and
    recalculation behavior.
16. Provider credentials, tokens, and secrets are never persisted in scheduled run history or
    emitted in logs.

## Out Of Scope

- Replacing manual DataAdmin NADPCO sync commands.
- Adding a separate scheduler UI.
- Query-time synchronization.
- Introducing a new normalization or scanner execution path.
- Assuming NADPCO has a reliable modified-since cursor where the vendor contract does not provide
  one.
- Removing or changing CodalDB synchronization.
