# Noavaran Amin Archive and Current API Source Strategy (spec 051)

## Summary

Noavaran Amin is **one logical vendor** with two physical source modes. Spec 051 corrects an earlier
model that treated each transport as its own vendor:

| Logical vendor | Physical source | Source mode | Persisted source name (was) |
|---|---|---|---|
| `NoavaranAmin` | `NoavaranArchiveSql` | `ArchiveOneTime` | `NoavaranArchiveSql` (`CodalDb`) |
| `NoavaranAmin` | `NoavaranCurrentApi` | `CurrentIncremental` | `NoavaranCurrentApi` (`NadpcoApi`) |
| `CyclicalWaves` | `CyclicalWavesApi` | `ExternalSnapshot` | `CyclicalWaves` |
| `Tsetmc` | `StockMarketDb` | `MigrationBridge` | `StockMarketDb` |

The archive (legacy CodalDB SQL snapshot) is imported **once**, audited, then frozen. Data from Shamsi
**1403** onward, where the archive lacks it, is owned by the current HTTP API. CyclicalWaves and
StockMarketDB remain independent.

## Authoritative model

`FinancialCopilot.Application.FinancialData.Providers`:

- `LogicalVendor`, `PhysicalSource`, `SourceMode` enums.
- `ProviderSources` — the single authoritative catalog. The persisted `ProviderName` on raw payloads
  and normalized rows uses `ProviderSources.*Name` constants; options classes, the provider router,
  normalizers, the scheduling guard, and provenance all derive their source name from here (one owner,
  no scattered literals).
- `ProviderSourceProvenance` — row/batch provenance value object (vendor, source, mode, ingestion run
  id, Shamsi source date range).
- `SourcePriorityOptions` / `ISourcePriorityResolver` — per-dataset source priority and the Shamsi
  `1403` boundary that splits archive-vs-current ownership of a period.
- `IIdentityConflictLog` / `IdentityConflict` — cross-source canonical-identity disagreement logging,
  preferring stable identifiers (`CoID`, ISIN, `InstCode`, `CompanySymbol`, normalized symbol).
- `ISourceFreshnessReader` / `SourceFreshness` — per-source freshness that reports a frozen archive
  distinctly from a current source.

## Provenance persistence (AC #7)

- `ProviderSyncRuns` (batch level): `LogicalVendor`, `PhysicalSource`, `SourceMode`,
  `SourceDateRangeStartJalali`, `SourceDateRangeEndJalali`.
- `Companies`, `FinancialStatements`, `MonthlyReports` (row level): `LogicalVendor`, `SourceMode`
  (the physical source is already carried by `ProviderName`).

`FinancialDataSyncProcessor` derives provenance from the request's `ProviderName` via `ProviderSources`
and stamps it on the run. `DataSyncRequest` gained `Mode` / `SourceDateRange*` so an orchestrator can
declare the import mode explicitly (the archive orchestrator stamps `ArchiveOneTime`; the current API
orchestrator stamps `CurrentIncremental`).

## Scheduling (AC #4, #5, #10)

- The archive source (`NoavaranArchiveSql`) is **not** driven by any recurring hosted worker. It runs
  only through the explicit DataAdmin endpoints `POST /api/v1/admin/codaldb/full-sync` /
  `.../incremental-sync` (manual maintenance/backfill). An architecture test
  (`NoavaranArchiveSource_IsNotDrivenByARecurringHostedWorker`) guards this.
- Recurring refresh belongs to the current API source via `NadpcoScheduledSyncWorker`.
- One-time archive **import run-state** semantics (the import-once-then-freeze record) are delivered by
  spec 052; spec 051 establishes the model and the scheduling guard.

## Health / freshness (tasks: provider health)

`GET /api/v1/admin/source-freshness` reports each catalogued source. Archive sources report
`IsFrozenArchive=true` once their one-time import has a successful run and are **not** flagged stale by
absence of recent runs; current sources report freshness against their last successful run.

## Identity resolution (AC #9)

Cross-source company/security identity prefers stable identifiers (`CoID`, ISIN, `InstCode`,
`CompanySymbol`, normalized symbol). Disagreements are logged via `IIdentityConflictLog`
(coalesced, bounded, never throwing) for review; conflicts never overwrite canonical data nor create
duplicate canonical identities. Full cross-source dedup/import is exercised in specs 052/053.

## Migration & rename

The physical source names were renamed in place (`CodalDb` → `NoavaranArchiveSql`,
`NadpcoApi` → `NoavaranCurrentApi`) across options, config sections (`appsettings.json`), the provider
router, normalizers, the admin controller, and the display resolver. The EF migration adds the
provenance columns/indexes and renames existing persisted `ProviderName` values so already-ingested
rows remain valid (no clean slate required for the rename itself).

## Out of scope (deferred)

- The full frontend admin console (order 57 / spec 055).
- Direct TSETMC ingestion (order 56 / spec 054).
- The actual one-time archive import (order 53 / spec 052) and current-API ingestion (order 54 /
  spec 053).
