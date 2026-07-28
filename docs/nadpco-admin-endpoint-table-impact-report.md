# NADPCO Admin Endpoint Table Impact Report

Date: 2026-06-20

## Scope

This report maps the following admin endpoints to the database tables they update:

1. `POST /api/v1/admin/nadpcoapi/incremental-sync`
2. `POST /api/v1/admin/noavaran-current/backfill`
3. `POST /api/v1/admin/nadpcoapi/fundamental-index-catch-up`
4. `POST /api/v1/admin/nadpcoapi/full-sync`
5. `POST /api/v1/admin/nadpcoapi/incremental-sync` (duplicate of item 1)

## Summary

These endpoints are orchestration endpoints. The controller methods mostly start a run or enqueue `DataSyncRequest` messages. The actual business-table writes occur later in the ingestion pipeline, mainly through:

- `FinancialDataSyncProcessor`
- `NadpcoApiCompanyNormalizer`
- `NadpcoApiFinancialStatementNormalizer`
- `NadpcoApiMonthlyActivityNormalizer`
- `NadpcoApiFundamentalIndexNormalizer`
- `NadpcoApiFundamentalIndexCoverageNormalizer`

## Endpoint To Table Matrix

| Endpoint | Sync / Run Tables | Business Data Tables | Conditional Tables | Notes |
| --- | --- | --- | --- | --- |
| `POST /api/v1/admin/nadpcoapi/incremental-sync` | `NadpcoApiSyncStates`, `ProviderSyncRuns`, `MetricRecalculationRequests` | `Companies`, `Industries`, `IndustryGroups`, `Markets`, `FinancialStatements`, `FinancialStatementLineItems`, `DerivedMetrics` | `MonthlyReports`, `MonthlyReportLineItems` | Monthly activity is only included when incremental monthly refresh is enabled for that run. |
| `POST /api/v1/admin/noavaran-current/backfill` | `NadpcoApiSyncStates`, `ProviderSyncRuns`, `MetricRecalculationRequests` | `Companies`, `Industries`, `IndustryGroups`, `Markets`, `FinancialStatements`, `FinancialStatementLineItems`, `MonthlyReports`, `MonthlyReportLineItems`, `DerivedMetrics` | None | This calls the same NADPCO scheduled sync service with `fullReload: true`. |
| `POST /api/v1/admin/nadpcoapi/fundamental-index-catch-up` | `FundamentalIndexCatchUpRuns`, `ProviderSyncRuns` | `NadpcoFundamentalIndexObservations` | None | This is the all-index coverage path. It does not promote data into `DerivedMetrics`. |
| `POST /api/v1/admin/nadpcoapi/full-sync` | `NadpcoApiSyncStates`, `ProviderSyncRuns`, `MetricRecalculationRequests` | `Companies`, `Industries`, `IndustryGroups`, `Markets`, `FinancialStatements`, `FinancialStatementLineItems`, `MonthlyReports`, `MonthlyReportLineItems`, `DerivedMetrics` | None | Same write path as current-API backfill. |
| `POST /api/v1/admin/nadpcoapi/incremental-sync` | Same as item 1 | Same as item 1 | Same as item 1 | Duplicate request in the original list. |

## Table Details By Endpoint

### 1. `POST /api/v1/admin/nadpcoapi/incremental-sync`

Always updates or inserts:

- `NadpcoApiSyncStates`
- `ProviderSyncRuns`
- `Companies`
- `Industries`
- `IndustryGroups`
- `Markets`
- `FinancialStatements`
- `FinancialStatementLineItems`
- `DerivedMetrics`
- `MetricRecalculationRequests`

May also update or insert:

- `MonthlyReports`
- `MonthlyReportLineItems`

Reason:

- The scheduled sync service always enqueues company catalog, financial statements, and curated fundamental indexes.
- It only enqueues monthly production/sales when the incremental monthly-activity scope is active.

### 2. `POST /api/v1/admin/noavaran-current/backfill`

Updates or inserts:

- `NadpcoApiSyncStates`
- `ProviderSyncRuns`
- `Companies`
- `Industries`
- `IndustryGroups`
- `Markets`
- `FinancialStatements`
- `FinancialStatementLineItems`
- `MonthlyReports`
- `MonthlyReportLineItems`
- `DerivedMetrics`
- `MetricRecalculationRequests`

Reason:

- The backfill coordinator calls the NADPCO sync service with `fullReload: true`.
- Full reload includes monthly production/sales, so monthly tables are part of the write path.

### 3. `POST /api/v1/admin/nadpcoapi/fundamental-index-catch-up`

Updates or inserts:

- `FundamentalIndexCatchUpRuns`
- `ProviderSyncRuns`
- `NadpcoFundamentalIndexObservations`

Does not update:

- `DerivedMetrics`

Reason:

- This endpoint enqueues `ProviderDataset.FundamentalIndexCoverage`.
- That dataset is normalized into the non-scannable coverage table `NadpcoFundamentalIndexObservations`.
- Promotion into `DerivedMetrics` belongs to the curated fundamental-index sync path, not the catch-up path.

### 4. `POST /api/v1/admin/nadpcoapi/full-sync`

Updates or inserts:

- `NadpcoApiSyncStates`
- `ProviderSyncRuns`
- `Companies`
- `Industries`
- `IndustryGroups`
- `Markets`
- `FinancialStatements`
- `FinancialStatementLineItems`
- `MonthlyReports`
- `MonthlyReportLineItems`
- `DerivedMetrics`
- `MetricRecalculationRequests`

Reason:

- This is the same NADPCO scheduled sync service as incremental sync, but with `fullReload: true`.
- Full reload includes monthly activity and therefore writes monthly report tables as well.

## Code References

- Controller routes:
  `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`
- Full/incremental NADPCO orchestration:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs`
- Current-API backfill coordinator:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CurrentApiIngestion.cs`
- Fundamental-index catch-up coordinator:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FundamentalIndexCatchUp.cs`
- Central processing pipeline:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- NADPCO normalizers:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/`
- EF table mappings:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`

## Important Distinction

`ProviderSyncRuns`, `NadpcoApiSyncStates`, `FundamentalIndexCatchUpRuns`, and `MetricRecalculationRequests`
are operational tables used for run tracking, idempotency, and deferred recalculation work.

The main business-data targets in this report are:

- `Companies`
- `Industries`
- `IndustryGroups`
- `Markets`
- `FinancialStatements`
- `FinancialStatementLineItems`
- `MonthlyReports`
- `MonthlyReportLineItems`
- `DerivedMetrics`
- `NadpcoFundamentalIndexObservations`
