# Noavaran Current Monthly Backfill Investigation

## Executive summary

The original ambiguity was resolved in two layers:

1. Per-company/month truth must come from persisted monthly report rows, not from a bare `ProviderSyncRuns.Status = Completed`.
2. The global backfill flag in `MonthlyActivityBackfillStates` must not permanently block reruns when retryable company-months still exist.

Current fixed behavior:

- `POST /api/v1/admin/noavaran-current/monthly-backfill` skips only company-months whose deterministic run key has both:
  - a completed sync run, and
  - persisted `MonthlyReports` rows for that same company/month.
- `Failed` runs for deterministic keys like `nadpco-monthlybf-{yyyyMM}-{companyId}` are re-enqueued on rerun.
- completed-but-empty / no-data-yet company-months remain retryable.
- if the global backfill state is marked completed but retryable company-months still exist, the coordinator reopens that state and does not return `AlreadyCompleted`.
- `AlreadyCompleted` is returned only when all planned company-months are truly completed with persisted rows, or when no retryable candidates remain.

## Evidence from code

### 1) Admin endpoint

- `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`
- Endpoint: `POST /api/v1/admin/noavaran-current/monthly-backfill`
- The controller delegates directly to `IMonthlyActivityBackfillCoordinator.StartAsync(...)`.

### 2) Global backfill state table and columns

Verified from EF Core row/configuration/model snapshot:

- Row type:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
  - `MonthlyActivityBackfillStateRow`
- EF configuration:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- Model snapshot / actual table+columns evidence:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/FinancialIngestionDbContextModelSnapshot.cs:1355-1380`

Actual table / columns:

- table: `MonthlyActivityBackfillStates`
- key column: `SourceName`
- columns:
  - `SourceName`
  - `IsCompleted`
  - `CompletedAt`
  - `LastStartedAt`
  - `PlannedMonthsJson`
  - `RequestedBy`

### 3) Per-company/month run table and columns

Verified from EF Core configuration/model snapshot:

- row type:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
  - `DataSyncRunRow`
- configuration:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- model snapshot:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/FinancialIngestionDbContextModelSnapshot.cs:631-705`

Actual table / columns used here:

- table: `ProviderSyncRuns`
- relevant columns:
  - `Id`
  - `IdempotencyKey`
  - `ProviderName`
  - `ExternalReference`
  - `Status`
  - `ProcessedRecords`
  - `ErrorCount`
  - `ErrorMessage`
  - `RequestedAt`
  - `StartedAt`
  - `CompletedAt`
  - `SourceDateRangeStartJalali`
  - `SourceDateRangeEndJalali`

### 4) Monthly persistence source of truth

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
  - `NormalizedMonthlyReportRow`
- persisted table used by the coordinator:
  - `MonthlyReports`

This is the durable source of truth for “company-month actually has monthly data”.

### 5) Backfill coordinator logic

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityBackfillCoordinator.cs`

Relevant behavior now implemented there:

- deterministic keys remain `nadpco-monthlybf-{yyyyMM}-{companyId}`
- `StartAsync(...)` no longer trusts `MonthlyActivityBackfillStates.IsCompleted` blindly
- if global state says completed, the coordinator first calls `GetProgressAsync(...)`
- `GetProgressAsync(...)` derives durable progress from:
  - `MonthlyActivityBackfillStates.PlannedMonthsJson`
  - `ProviderSyncRuns`
  - persisted `MonthlyReports`
- if retryable company-months remain, `GetProgressAsync(...)` reopens the global state by setting:
  - `IsCompleted = false`
  - `CompletedAt = null`
- rerun skip logic uses `QueryCompletedKeysWithPersistedRowsAsync(...)`, which only skips a key when persisted monthly rows exist for that company/month

### 6) Worker / normalization behavior

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`

Relevant behavior now implemented:

- monthly production/sales runs that persist zero rows are treated as retryable, not terminally completed
- legacy completed-empty monthly runs do not short-circuit future reruns as “already processed”

## Answer to the main question

If you call:

- `POST /api/v1/admin/noavaran-current/monthly-backfill`

again for the same Jalali period, the current implementation will:

- re-enqueue company-months whose deterministic run row is `Failed`
- re-enqueue company-months that have no run row yet
- skip only company-months that already have persisted monthly report rows for that month
- not return `AlreadyCompleted` merely because `MonthlyActivityBackfillStates.IsCompleted = true` if retryable company-months still exist

So for your concrete 1405/03 case:

- if 668 company-months are `Failed`, rerunning POST should re-enqueue those 668 retryable rows even if the old global state had previously been marked completed

## Exact source of truth by case

| Case | Durable evidence now used | Rerun behavior |
|---|---|---|
| company was called and data was successfully saved | persisted `MonthlyReports` rows for that company/month, plus completed run | skipped |
| company was called but Noavaran returned no report yet | no persisted `MonthlyReports` rows; run remains/gets retryable (`Failed` or no-data-yet classification) | re-enqueued |
| company was not called at all | no `ProviderSyncRuns` row for that deterministic key | enqueued |
| call failed / transient error | `ProviderSyncRuns.Status = Failed` | re-enqueued |

Important: the global table `MonthlyActivityBackfillStates` is not the per-company/month truth. It is only an aggregate marker and can now be reopened when retryables remain.

## Global backfill statuses

The progress model now distinguishes these aggregate states:

- `Pending`
- `InProgress`
- `Retryable`
- `CompletedWithFailures`
- `Completed`

Operational meaning:

- `Completed` = all planned company-months have persisted monthly rows
- `CompletedWithFailures` = the pass reached terminal month-level accounting, but retryable failed / no-data-yet company-months remain
- `Retryable` = retryable rows exist and there are still pending months/candidates in the plan
- `InProgress` = work is actively underway / partially done
- `Pending` = not started yet

`CompletedWithFailures` is resumable, not terminal.

## Risks / data freshness issue

Without this fix, gradually published monthly reports could be frozen out forever:

- an early call with HTTP 200 but zero persisted rows could be treated as done
- a completed global flag could make POST return `AlreadyCompleted` before retryable rows were reconsidered
- later monthly publications would never be rechecked

That behavior was not suitable for a source that publishes reports over several days.

## Recommended behavior

For gradually published monthly reports, rerunning monthly backfill for the same Jalali month must:

- skip only company-months with actual persisted monthly rows
- retry failed / no-data-yet / never-attempted company-months
- ignore or reopen the global completed flag when durable per-company/month state says retryables remain

That is now the implemented behavior.

## Data repair for existing bad state

Resetting empty completed `ProviderSyncRuns` rows to `Failed` is necessary but not always sufficient in old data, because the global row may still have:

- `MonthlyActivityBackfillStates.IsCompleted = true`

Checked-in repair scripts:

- specific 1405/03 script:
  - `docs/sql-queries/reset-noavaran-current-monthly-backfill-empty-completed-1405-03.sql`
- generic template:
  - `docs/sql-queries/reset-noavaran-current-monthly-backfill-empty-completed-template.sql`

These scripts:

1. convert completed-empty company-month rows to `Failed`
2. reopen `MonthlyActivityBackfillStates` by clearing `IsCompleted` / `CompletedAt` when retryables remain
3. verify that retryable 1405/03 candidates exist for rerun

## Regression tests added

- `tests/FinancialCopilot.UnitTests/MonthlyActivityBackfillCoordinatorTests.cs`
  - completed global state + failed retryables => rerun re-enqueues retryable rows
  - completed global state + no-data-yet retryables => rerun re-enqueues retryable rows
  - completed global state + no retryables => POST returns `AlreadyCompleted`
- `tests/FinancialCopilot.UnitTests/NadpcoApiMonthlyActivityNormalizerTests.cs`
  - zero-row monthly ingestion remains retryable
  - legacy completed-empty monthly runs do not block retry

## Acceptance criteria

- Given global backfill state is completed but retryable company-month rows exist, POST does not return `AlreadyCompleted`.
- Given 1405/03 has failed deterministic keys like `nadpco-monthlybf-140503-{companyId}`, POST re-enqueues them.
- Given a company-month has persisted monthly rows for the requested month, rerun skips it.
- Given no retryable company-months remain, POST returns `AlreadyCompleted`.
- Given month status is `CompletedWithFailures`, rerunning POST resumes only failed / no-data-yet company-months.
- GET progress distinguishes completed, completed-with-failures, in-progress, pending, and retryable aggregate states.