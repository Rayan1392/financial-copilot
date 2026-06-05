# Tasks

1. Define per-dataset sync state and watermark strategy for companies, statements, fundamental
   indexes, product sales, and service sales.
2. Document overlap-window reconciliation for endpoints without a reliable modified-since
   filter.
3. Add a bounded orchestration service with configurable batch sizes, concurrency, retry, and
   per-batch failure isolation.
4. Reuse the existing raw-payload, normalization, recalculation, telemetry, and scanner-cache
   invalidation paths.
5. Add protected DataAdmin endpoints or commands for full sync, incremental sync, sync-state
   reads, and provider health.
6. Add operational documentation for credentials, activation order, initial backfill,
   recurring refresh cadence, and failure recovery.
7. Add tests for batching, progress advancement, overlap reconciliation, failed-batch retry,
   partial failure isolation, authorization, telemetry, and cache invalidation.

## Implementation Status

Completed on 2026-06-03.

- Added `NadpcoApiSyncStates` persistence and per-logical-dataset progress for company
  catalog, statements, fundamental indexes, product sales, and service sales.
- Documented the no-modified-since limitation and overlap-window reconciliation strategy.
- Added `NadpcoApiScheduledSyncService` with bounded concurrency, full/incremental modes,
  per-company failure isolation, and provider-specific `DataSyncRequest` publishing through the
  existing raw-payload/normalization/recalculation/cache-invalidation path.
- Added protected DataAdmin full-sync, incremental-sync, and sync-state endpoints.
- Added unit and integration tests for batching, overlap progress, failure isolation, state reads,
  and authorization.

## Change Request Tasks - 2026-06-05

- [x] Add or update DataAdmin orchestration so operators can run NADPCO company-catalog-only
      refreshes.
- [x] Add an explicit clean-slate company refresh mode that deletes existing PostgreSQL
      `Companies` rows and then imports NADPCO `/api/v3/BaseInfo/Companies`.
- [x] Ensure ordinary daily company refresh mode is idempotent: insert new `coID` rows, update
      changed metadata, and avoid duplicate symbols.
- [x] Record run mode in sync-run telemetry: `CompanyCatalogCleanSlate`,
      `CompanyCatalogRefresh`, `FullSync`, or `IncrementalSync`.
- [x] Ensure scanner cache invalidation and metric/linkage follow-up behavior is triggered when
      company or symbol metadata changes.
- [x] Add authorization, telemetry, idempotency, clean-slate, and failure-recovery tests for the
      company-catalog-only orchestration path.

Order `46` implementation notes:

- Added `NadpcoApiSyncRunMode` with `FullSync`, `IncrementalSync`,
  `CompanyCatalogCleanSlate`, and `CompanyCatalogRefresh`.
- Added `POST /api/v1/admin/nadpcoapi/company-catalog/clean-slate` and
  `POST /api/v1/admin/nadpcoapi/company-catalog/refresh`.
- The clean-slate route runs the order `45` cleanup service, invalidates scanner cache
  immediately after destructive metadata cleanup, and then enqueues the NADPCO Symbols/company
  catalog request through the existing raw-payload/normalizer path.
- The refresh route only enqueues the NADPCO Symbols/company catalog request. Idempotent insert,
  update, and duplicate-symbol behavior is owned by `NadpcoApiCompanyNormalizer`, already covered
  by the spec `039` tests.
- `NadpcoApiSyncStates.LastRunMode` records the most recent mode for DataAdmin telemetry.
  Scheduled daily invocation is still owned by order `47`.

