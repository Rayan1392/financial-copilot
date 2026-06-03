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

Not implemented.

