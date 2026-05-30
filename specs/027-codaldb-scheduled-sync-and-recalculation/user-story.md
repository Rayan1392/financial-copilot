# User Story — CodalDB Scheduled Sync & Derived-Metric Recalculation

> Depends on `021`–`026`. Schema reference:
> [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want CodalDB data synchronized on a nightly schedule and the meaningful growth/derived metrics
computed and stored automatically,
so that a query like *"list companies whose net profit grew 100% year over year"* returns
instantly from precomputed values, with no calculation at query time.

## Context — how data is provided (and the gap this closes)

The platform is **precompute-and-store**: `EfCoreScannerExecutionService` reads already-computed
rows from the `DerivedMetrics` table and never calculates at query time. After ingestion, the
normalizer writes a recalculation request to the `MetricRecalculationRequests` outbox table via
`StoredDerivedMetricRecalculationPublisher`.

**Current gap:** no worker consumes `MetricRecalculationRequests`, so derived/growth metrics are
not actually (re)computed in runtime today. This story closes that gap **using the existing
Billing outbox pattern** (`BillingOutboxProcessor` drained by `Worker` on a `PeriodicTimer`) and
adds a nightly CodalDB sync orchestrator. The derived-metric **calculation stays in the `006`
engine** (`DerivedMetricRecalculationCommand`); the new worker only triggers it — so there is one
source of calculation truth (no parallel "growth worker").

## End-to-end scenario: "net profit growth ≥ 100% YoY"

```
Nightly orchestrator (watermark on ModifiedDateTime)
  → DataSyncRequest(ProviderName="CodalDb", FinancialStatements, CoID) per changed company
  → RabbitMQ → DataSyncConsumerWorker → FinancialDataSyncProcessor.ProcessAsync
      → CodalDb normalizers persist NormalizedFinancialStatement* rows
      → StoredDerivedMetricRecalculationPublisher writes MetricRecalculationRequests (outbox)
  → MetricRecalculationProcessor (NEW) drains the outbox
      → resolves affected symbol + registered "meaningful" metrics (incl. NET_PROFIT_GROWTH_YOY)
      → DerivedMetricRecalculationCommand.ExecuteAsync → DerivedMetrics rows persisted
  → Scanner query: WHERE MetricCode='NET_PROFIT_GROWTH_YOY' AND Value >= 100  (instant read)
```

## Acceptance Criteria

- **Recalculation processor (closes the gap):** add a `MetricRecalculationProcessor` that reads
  pending `MetricRecalculationRequests` rows (`ProcessedAt IS NULL`), and for each, determines the
  affected symbol(s) and the set of **registered** derived/growth metrics that depend on the
  changed dataset ("meaningful items" = the metric catalog from `023`/`026`), builds
  `CalculateDerivedMetricCommand`s, invokes the existing
  `DerivedMetricRecalculationCommand.ExecuteAsync`, and marks the request processed. It is
  idempotent (re-processing a request does not duplicate `DerivedMetrics` rows, which are upserted
  on their unique key) and bounded (processes at most N requests per tick — Release-It! bounded
  batch).
- The processor is drained by a background worker on a configurable interval, mirroring the
  Billing `Worker`/`BillingOutboxProcessor`/`PeriodicTimer` pattern (a new hosted service or an
  added responsibility of the existing `Worker`), with `IntervalSeconds` configuration. This
  benefits **all** providers (CyclicalWaves included), not only CodalDB.
- **Nightly CodalDB sync orchestrator:** add a `CodalDbScheduledSyncService` (mirroring
  `CyclicalWavesFullSyncService`) that:
  - On each run computes the set of companies whose CodalDB `Companies.ModifiedDateTime` /
    `Statements.ModifiedDateTime` / `MonthlyActivity.ModifiedDateTime` / `FinancialRatios.
    ModifiedDateTime` is **newer than the last successful sync watermark** (incremental), and
    enqueues `DataSyncRequest`s (Symbols once, then FinancialStatements / MonthlyProductionSales /
    FinancialRatios per changed company) with `ProviderName = "CodalDb"`.
  - Persists/advances a **sync watermark** (last-synced `ModifiedDateTime` per dataset) so the
    next run only processes changes. A full reload is available as an explicit option.
  - Throttles concurrency (bounded parallelism) to avoid overloading CodalDB and RabbitMQ.
- **Scheduling trigger:** the nightly run is initiated by either a timer-based `BackgroundService`
  (configurable cron/interval) **or** an admin endpoint `POST /api/v1/admin/codaldb/full-sync`
  (and `…/incremental-sync`) under the existing `DataAdmin` policy, consistent with `012` and the
  CyclicalWaves full-sync endpoint. External schedulers can call the endpoint if preferred.
- **No query-time computation:** the scanner continues to read precomputed `DerivedMetrics`;
  growth filters (e.g. `NET_PROFIT_GROWTH_YOY >= 100`) evaluate against stored values. Confirm the
  value scale/encoding (percent vs fraction) matches the threshold semantics the parser produces.
- **Observability & resilience:** each scheduled run and each recalculation batch is logged with
  counts (companies enqueued, requests processed, metrics written, failures) and correlated via
  the existing telemetry (`018`); failures are isolated per company/request (one bad company does
  not abort the whole run), retried with bounds, and surfaced in `DataSyncRunRow` / logs.

## Technical Notes

- The recalculation processor is intentionally **provider-agnostic** and belongs to the ingestion
  module, not CodalDb — it simply drains the existing outbox. CodalDb is the first provider that
  truly exercises it at scale (2,362 companies × multiple periods).
- "Meaningful items" = the registered catalog. The processor must NOT attempt to compute
  unregistered metrics; resolving "which metrics depend on this dataset" should use the metric
  definitions' `DataRequirements`/dependencies rather than a hardcoded list.
- Watermarking uses CodalDB's `ModifiedDateTime` columns — a concrete advantage over CyclicalWaves
  (which had no change timestamps and required full reloads). Store the watermark in a small
  provider-sync-state table or reuse `DataSyncRunRow` history.
- Nightly cadence is a configuration default, not hardcoded; the same orchestrator supports
  on-demand admin-triggered runs.

## Dependencies

- `021`–`026` (provider, normalizers, metric catalog, growth calculators).
- `005` (`FinancialDataSyncProcessor`, `MetricRecalculationRequests` outbox,
  `DataSyncConsumerWorker`), `006` (`DerivedMetricRecalculationCommand`), `012` (admin endpoints +
  `DataAdmin` policy), `018` (telemetry). Pattern reference: Billing
  `Worker`/`BillingOutboxProcessor`; `CyclicalWavesFullSyncService`.
