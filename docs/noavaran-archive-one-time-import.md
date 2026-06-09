# Noavaran Archive One-Time Import (spec 052)

## Summary

The Noavaran Amin **archive** source (`NoavaranArchiveSql`, the legacy CodalDB SQL snapshot — see
spec 051) is imported **once**, validated, then **frozen**. It is never refreshed by a recurring
worker. This story adds a DataAdmin-triggered import lifecycle on top of the existing archive
ingestion path; it does not introduce a second fetch/normalize pipeline.

## Lifecycle (DataAdmin only)

All endpoints are under `POST/GET /api/v1/admin/noavaran-archive/*` and require the
`AuthorizationPolicies.DataAdmin` policy.

| Action | Endpoint | Effect |
|---|---|---|
| Dry-run | `POST .../dry-run` | Reports how many companies/datasets **would** be imported; enqueues nothing, advances no watermark. |
| Import | `POST .../import` | Drives `CodalDbScheduledSyncService.ExecuteAsync(fullReload: true)` (the one source of truth) and records a run. Rejected if the archive is already frozen. |
| Validate | `POST .../validate` | Validates company/security mapping (AC #7) and records a run with the coverage summary. |
| Freeze | `POST .../freeze` | Marks the archive source frozen (AC #3). |
| Re-import | `POST .../re-import` | Controlled re-import of a frozen archive; **requires an explicit reason** (AC #5). |
| Freeze state | `GET .../freeze-state` | Current freeze marker (frozen flag, when, by which run, reason). |
| Runs | `GET .../runs?limit=N` | Recent archive import run history (AC #6). |
| Coverage | `GET .../coverage` | Coverage summary + company-mapping validation (AC #9). |

`Dataset` scoping (AC #8) is accepted on the request body (`companies`, `financialStatements`,
`monthlyActivity`, `financialRatios`, `derivedMetrics`); an empty selection means all archive
datasets.

## Architecture

- **Application** (`FinancialData/Ingestion/ArchiveImportContracts.cs`): `ArchiveImportAction`,
  `ArchiveImportRunStatus`, `ArchiveImportDataset`, `ArchiveImportRequest`/`Run`,
  `ArchiveFreezeState`, `ArchiveCoverageSummary`, `IArchiveImportCoordinator`,
  `IArchiveImportRunReader`, `IArchiveCoverageReader`, `IArchiveFreezeStateStore`.
- **Infrastructure** (`FinancialData/Ingestion/ArchiveImport.cs`): `ArchiveImportCoordinator`
  (lifecycle + freeze gate, orchestrates over `ICodalDbScheduledSyncService`),
  `EfCoreArchiveImportRunRepository` (run history + running-lease + hung recovery, mirroring the
  NADPCO scheduled-sync repository), `EfCoreArchiveFreezeStateStore` (single-row freeze marker keyed
  by source name), `EfCoreArchiveCoverageReader` (coverage by dataset + Gregorian fiscal year from
  `PeriodEnd`).
- **Persistence**: `ArchiveImportRuns` and `ArchiveFreezeStates` tables (migration
  `AddArchiveImportRunsAndFreezeState`).
- **Dry-run support**: `ICodalDbScheduledSyncService.ExecuteAsync` gained an optional `dryRun` flag
  that computes counts without enqueuing or advancing the watermark.

## Freeze gate (AC #3/#5)

`ArchiveFreezeStateRow` is the authoritative gate. Once frozen:
- a normal `Import` returns `RejectedFrozen`;
- a `ReImport` proceeds only when a non-empty `Reason` is supplied, and the reason is persisted on
  the run.

The spec-051 `ISourceFreshnessReader.IsFrozenArchive` remains an informational view; this story adds
the persisted gate that actually blocks accidental re-import.

## No recurring execution (AC #4)

The archive import is **not** registered as a hosted service. The architecture test
`NoavaranArchiveSource_IsNotDrivenByARecurringHostedWorker` fails if any `AddHostedService` line
references the archive sync or import.

## Scanner provenance (AC #10)

`ScannerTableRow.SourceProvider` and `DataCitation.SourceProvider` (and the API
`DataCitationResponse.SourceProvider`) carry the physical source name (e.g. `NoavaranArchiveSql`) for
each answered symbol, so explainable answers cite archive provenance for historical rows. The fields
are optional/additive — existing frontend rendering is unaffected.

## Applying the migration

`Database:ApplyMigrationsOnStartup` is `true`, so restarting the API applies
`AddArchiveImportRunsAndFreezeState`. Alternatively:

```
dotnet ef database update --project src/backend/FinancialCopilot.Infrastructure --startup-project src/backend/FinancialCopilot.API --context FinancialIngestionDbContext
```

## Out of scope

- Recurring updates from the archive source (it is frozen).
- Current-API ingestion from 1404 onward (order 54 / spec 053).
- Direct TSETMC trading statistics ingestion.
