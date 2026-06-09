# Noavaran Current API Ingestion (spec 053)

## Summary

The Noavaran Amin **current API** source (`NoavaranCurrentApi`, formerly modeled as `NadpcoApi` — see
spec 051) keeps normalized financial data fresh from the Shamsi **1403** boundary onward, after the
frozen archive (spec 052) stops updating. Most of the ingestion machinery already existed from the
NADPCO work (specs 042–044) and was renamed by spec 051; this story consolidates it behind a clear
current-API façade and adds the boundary-driven gap report, the reason-free DataAdmin backfill with a
one-off Shamsi-year override, and a separate current-API health surface.

## What already existed (specs 042–044 + 051)

- Scheduled incremental worker `NadpcoScheduledSyncWorker` (current API), with cadence/enabled/dataset
  selection in `NadpcoScheduledSyncOptions` (AC #2).
- Run history + status/runs endpoints under `/api/v1/admin/nadpcoapi/scheduled-sync/*` (AC #9).
- Per-dataset Shamsi start config (`StatementFromYear`, `FundamentalIndexFromYear`,
  `MonthlyActivityFromDate`) and the `CurrentApiBoundaryShamsiYear` (=1403) policy (AC #3).
- Provenance stamping `LogicalVendor=NoavaranAmin` / `SourceMode=CurrentIncremental` on every enqueued
  request and persisted row (AC #5); cache invalidation + derived-metric recalculation after
  normalized changes (AC #8); canonical symbol reuse via `CanonicalSymbolLinkageResolver` (AC #6).
- Separate code paths and run history for current-API sync vs the spec-052 archive import (AC #1, #10).

## What this story adds

### Boundary-driven gap report (AC #4)

`GET /api/v1/admin/noavaran-current/gaps` → `ICurrentApiGapReader`/`EfCoreCurrentApiGapReader`. For each
`(company, dataset, Gregorian fiscal year)` at/after the boundary, it compares current-API rows vs
archive rows and reports where the current API covers periods the archive lacks. Pure read; never
touches archive freeze/import state.

### DataAdmin backfill with one-off boundary override (AC #3)

`POST /api/v1/admin/noavaran-current/backfill` with optional `fromShamsiYear`. The override lowers the
current-API start boundary **for that run only** (no persisted config mutation); when omitted the
configured 1403 boundary is used. Monthly activity is always clamped to the vendor-permitted **1404**
boundary regardless of the override (spec 042 access constraint).

The override travels with each enqueued `DataSyncRequest` (`FromShamsiYearOverride`) so it reaches the
worker scope that performs the fetch. `FinancialDataSyncProcessor` applies it to a scoped
`INoavaranCurrentApiBoundaryOverride`, which the provider client consults when building `fromYear` /
`fromDate` query parameters (falling back to configured options when unset). The scheduled worker keeps
using the configured boundary.

### Separate current-API health (AC #9)

`GET /api/v1/admin/noavaran-current/health` → `ICurrentApiBackfillCoordinator.GetHealthAsync` combines
the current-API provider health (the `NoavaranCurrentApi` client, not the configured primary) with the
latest scheduled-sync execution and next-due time. Reported separately from the archive freeze/import
state (`/api/v1/admin/noavaran-archive/*`).

## Architecture

- **Application** `FinancialData/Ingestion/CurrentApiIngestionContracts.cs`: `CurrentApiCoverageGap`,
  `CurrentApiGapReport`, `CurrentApiBackfillRequest`/`Result`, `CurrentApiHealthStatus`,
  `ICurrentApiGapReader`, `ICurrentApiBackfillCoordinator`. `DataSyncRequest.FromShamsiYearOverride`
  and `INadpcoApiScheduledSyncService.ExecuteAsync(..., fromShamsiYearOverride)`.
- **Infrastructure** `FinancialData/Ingestion/CurrentApiIngestion.cs`: `EfCoreCurrentApiGapReader`,
  `CurrentApiBackfillCoordinator` (drives the existing `INadpcoApiScheduledSyncService` full sync — one
  ingestion path). `Providers/NadpcoApi/NoavaranCurrentApiBoundaryOverride.cs` (scoped per-run holder).
- No new persistence/migration: gap report reads existing normalized rows; backfill reuses the
  existing run history and `DataSyncRequest` pipeline.

## Failure isolation (AC #10)

The backfill coordinator runs through the current-API source only and reads provider/scheduled-sync
health; it has no dependency on `IArchiveFreezeStateStore`/`IArchiveImportCoordinator`, so a current-API
failure cannot change archive freeze/import state.

## Out of scope

- Re-importing archive data (that is the spec 052 re-import path).
- Replacing StockMarketDB trading-statistics ingestion.
