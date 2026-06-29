-- Repair Noavaran Current monthly-backfill state for Jalali 1405/03.
--
-- This script fixes BOTH durable layers now used by the coordinator:
-- 1) ProviderSyncRuns company-month rows that were incorrectly left Completed with no persisted data
-- 2) MonthlyActivityBackfillStates global state that may still be IsCompleted = true
--
-- Verified source-of-truth from EF Core model/configuration:
-- - Sync run table: "ProviderSyncRuns"
-- - Persisted monthly rows table: "MonthlyReports"
-- - Global backfill state table: "MonthlyActivityBackfillStates"
-- - Global backfill state columns: "SourceName", "IsCompleted", "CompletedAt", "LastStartedAt",
--   "PlannedMonthsJson", "RequestedBy"
-- - Backfill source name: 'NoavaranCurrentApi'
-- - Completed status value: 'Completed'
-- - Retryable status supported by code: 'Failed'
-- - Idempotency key format: nadpco-monthlybf-{yyyyMM}-{companyId}
--
-- Jalali / Gregorian mapping for 1405/03:
-- - SourceDateRangeStartJalali = '1405/03/01'
-- - SourceDateRangeEndJalali   = '1405/03/31'
-- - MonthlyReports.PeriodStart = DATE '2026-05-22'
-- - MonthlyReports.PeriodEnd   = DATE '2026-06-21'
--
-- Purpose:
-- - Reset empty completed 1405/03 company-month runs to Failed so they are retryable
-- - Reopen the global monthly backfill state if retryable rows remain
-- - Verify that retryable company-months now exist for POST /api/v1/admin/noavaran-current/monthly-backfill
--
-- Safety:
-- - Does NOT touch company-months that already have persisted MonthlyReports rows
-- - Reopens MonthlyActivityBackfillStates only for SourceName = 'NoavaranCurrentApi'
--   and only when retryable 1405/03 rows exist after the repair

BEGIN;

-- Preview empty completed 1405/03 runs that will be converted to Failed.
SELECT
    r."Id",
    r."IdempotencyKey",
    r."ExternalReference",
    r."Status",
    r."ProcessedRecords",
    r."ErrorCount",
    r."ErrorMessage",
    r."RequestedAt",
    r."CompletedAt",
    r."SourceDateRangeStartJalali",
    r."SourceDateRangeEndJalali"
FROM "ProviderSyncRuns" AS r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = '1405/03/01'
  AND r."SourceDateRangeEndJalali" = '1405/03/31'
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = DATE '2026-05-22'
        AND mr."PeriodEnd" = DATE '2026-06-21'
  )
ORDER BY r."ExternalReference";

-- Step A: convert empty completed runs to retryable Failed runs.
UPDATE "ProviderSyncRuns" AS r
SET
    "Status" = 'Failed',
    "ErrorCount" = GREATEST(r."ErrorCount", 1),
    "ErrorMessage" = 'NoDataYet - vendor returned no monthly report rows for this company/month',
    "CompletedAt" = NULL
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = '1405/03/01'
  AND r."SourceDateRangeEndJalali" = '1405/03/31'
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = DATE '2026-05-22'
        AND mr."PeriodEnd" = DATE '2026-06-21'
  );

-- Step B: reopen the global backfill state when retryable 1405/03 runs exist.
UPDATE "MonthlyActivityBackfillStates" AS s
SET
    "IsCompleted" = FALSE,
    "CompletedAt" = NULL
WHERE s."SourceName" = 'NoavaranCurrentApi'
  AND EXISTS (
      SELECT 1
      FROM "ProviderSyncRuns" AS r
      WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
        AND r."ProviderName" = 'NoavaranCurrentApi'
        AND r."Status" = 'Failed'
        AND r."SourceDateRangeStartJalali" = '1405/03/01'
        AND r."SourceDateRangeEndJalali" = '1405/03/31'
  );

-- Verification 1: run-state summary for 1405/03 after repair.
SELECT
    COUNT(*) FILTER (WHERE r."Status" = 'Failed')    AS "FailedRuns",
    COUNT(*) FILTER (WHERE r."Status" = 'Completed') AS "CompletedRuns",
    COUNT(*)                                         AS "TotalRuns"
FROM "ProviderSyncRuns" AS r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."SourceDateRangeStartJalali" = '1405/03/01'
  AND r."SourceDateRangeEndJalali" = '1405/03/31';

-- Verification 2: global backfill state after repair.
SELECT
    s."SourceName",
    s."IsCompleted",
    s."CompletedAt",
    s."LastStartedAt",
    s."RequestedBy"
FROM "MonthlyActivityBackfillStates" AS s
WHERE s."SourceName" = 'NoavaranCurrentApi';

-- Verification 3: retryable candidates that POST /monthly-backfill can now re-enqueue for 1405/03.
-- With the fixed coordinator, any rows returned here mean the rerun should not return AlreadyCompleted.
SELECT
    r."ExternalReference" AS "CompanyId",
    r."IdempotencyKey",
    r."Status",
    r."ErrorMessage"
FROM "ProviderSyncRuns" AS r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Failed'
  AND r."SourceDateRangeStartJalali" = '1405/03/01'
  AND r."SourceDateRangeEndJalali" = '1405/03/31'
ORDER BY r."ExternalReference";

COMMIT;

-- Optional audit: if any rows still appear here, they are Completed but still missing MonthlyReports rows
-- and should be investigated before relying on reruns.
SELECT
    r."IdempotencyKey",
    r."ExternalReference",
    r."Status",
    r."ProcessedRecords",
    r."ErrorMessage"
FROM "ProviderSyncRuns" AS r
WHERE r."IdempotencyKey" LIKE 'nadpco-monthlybf-140503-%'
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = '1405/03/01'
  AND r."SourceDateRangeEndJalali" = '1405/03/31'
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = DATE '2026-05-22'
        AND mr."PeriodEnd" = DATE '2026-06-21'
  )
ORDER BY r."ExternalReference";