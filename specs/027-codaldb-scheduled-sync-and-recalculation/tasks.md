# Tasks

## Application/Infrastructure — Recalculation outbox processor (closes the gap, provider-agnostic)

- [ ] Add `MetricRecalculationProcessor` (ingestion module) mirroring `BillingOutboxProcessor`:
      `ProcessPendingAsync(int maxBatch)` reads `MetricRecalculationRequests` where
      `ProcessedAt IS NULL`, resolves affected symbol(s) + the registered metrics whose
      `DataRequirements`/dependencies match the changed `SourceDataset`, builds
      `CalculateDerivedMetricCommand`s, calls `DerivedMetricRecalculationCommand.ExecuteAsync`,
      then marks each request `ProcessedAt`. Bounded batch; idempotent (DerivedMetrics upsert).
- [ ] Add a `ProcessedAt` column to `MetricRecalculationRequestRow` (+ migration) if absent.
- [ ] Drain it from a background worker: either extend `FinancialCopilot.Worker/Worker.cs` or add
      `DerivedMetricRecalculationWorker : BackgroundService` with `PeriodicTimer` and an
      `IntervalSeconds` option (mirror `BillingMaintenanceOptions`). Register in the Worker host.
- [ ] Resolve "which metrics depend on a dataset" from the semantic catalog
      (`FinancialMetricRegistry` definitions/dependencies), not a hardcoded list.

## Infrastructure — CodalDB scheduled sync orchestrator

- [ ] Add `CodalDbSyncStateStore` (small table or reuse `DataSyncRunRow`) holding the per-dataset
      `ModifiedDateTime` watermark of the last successful sync (+ migration if a new table).
- [ ] Add `CodalDbScheduledSyncService` (mirror `CyclicalWavesFullSyncService`):
      - Query CodalDB for companies changed since the watermark (per dataset).
      - Enqueue `DataSyncRequest`s (`ProviderName = "CodalDb"`): Symbols once, then
        FinancialStatements / MonthlyProductionSales / FinancialRatios per changed company, via
        `IDataSyncRequestPublisher` (RabbitMQ).
      - Bounded concurrency; advance the watermark only after successful enqueue/processing.
      - Support an explicit full-reload mode (ignore watermark).
- [ ] Add an optional timer-based `CodalDbNightlySyncWorker : BackgroundService` (cron/interval
      via options) that invokes `CodalDbScheduledSyncService` — disabled by default; enabled by
      configuration.

## API — Admin triggers (012)

- [ ] Add `POST /api/v1/admin/codaldb/full-sync` and `POST /api/v1/admin/codaldb/incremental-sync`
      under the `DataAdmin` policy, invoking `CodalDbScheduledSyncService` (full vs. watermark).
      Mirror the existing CyclicalWaves full-sync endpoint shape and return a run summary.

## Observability (018)

- [ ] Emit structured counts per run (companies changed, requests enqueued/processed, metrics
      written, failures) and per recalculation batch; correlate via existing telemetry; record
      run status/errors in `DataSyncRunRow`. Per-company/per-request failure isolation + bounded
      retry.

## Tests

- [ ] `MetricRecalculationProcessorTests` (unit/integration, ~6 tests): pending request →
      DerivedMetrics written for dependent registered metrics; idempotent re-processing; bounded
      batch; unregistered metric not computed; failure on one request does not block others.
- [ ] `CodalDbScheduledSyncServiceTests` (unit, ~5 tests): only companies changed since watermark
      enqueued; watermark advances on success; full-reload mode ignores watermark; concurrency
      bounded.
- [ ] Integration test (end-to-end, EF in-memory + fake CodalDB): ingest a company whose net
      profit doubled YoY → recalc processor runs → scanner query
      `NET_PROFIT_GROWTH_YOY >= 100` returns that company from precomputed `DerivedMetrics`.
- [ ] Admin endpoint tests: `DataAdmin` authorized; non-admin/API-client/billing-admin rejected.
