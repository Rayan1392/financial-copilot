-- Generic repair template: reopen Noavaran Current monthly-backfill for a Jalali month where
-- some company-month runs were incorrectly left Completed without persisted MonthlyReports rows.
--
-- This template repairs BOTH durable layers used by the coordinator:
-- 1) "ProviderSyncRuns" company-month rows
-- 2) "MonthlyActivityBackfillStates" global backfill state
--
-- Verified source-of-truth from EF Core model/configuration:
-- - Sync run table: "ProviderSyncRuns"
-- - Persisted monthly rows table: "MonthlyReports"
-- - Global backfill state table: "MonthlyActivityBackfillStates"
-- - Global state columns: "SourceName", "IsCompleted", "CompletedAt", "LastStartedAt",
--   "PlannedMonthsJson", "RequestedBy"
-- - Backfill source name: 'NoavaranCurrentApi'
-- - Completed status value: 'Completed'
-- - Retryable status supported by code: 'Failed'
-- - Idempotency key format: nadpco-monthlybf-{yyyyMM}-{companyId}
--
-- Important:
-- PostgreSQL does not natively convert Jalali/Shamsi dates to Gregorian dates.
-- So you must supply BOTH:
--   1) the Jalali request window used by the backfill run, and
--   2) the Gregorian PeriodStart / PeriodEnd used by persisted MonthlyReports rows.
--
-- Recommended usage:
-- 1) Set the variables below.
-- 2) Run the preview SELECT first.
-- 3) Run the transaction.
-- 4) Verify Failed runs exist and MonthlyActivityBackfillStates.IsCompleted = false.
-- 5) Re-run POST /api/v1/admin/noavaran-current/monthly-backfill.
--
-- Example for 1405/03:
--   month_token       = 140503
--   start_jalali      = 1405/03/01
--   end_jalali        = 1405/03/31
--   period_start_greg = 2026-05-22
--   period_end_greg   = 2026-06-21

\set month_token '140503'
\set start_jalali '1405/03/01'
\set end_jalali '1405/03/31'
\set period_start_greg '2026-05-22'
\set period_end_greg '2026-06-21'

BEGIN;

WITH params AS (
    SELECT
        :'month_token'::text       AS month_token,
        :'start_jalali'::text      AS start_jalali,
        :'end_jalali'::text        AS end_jalali,
        :'period_start_greg'::date AS period_start_greg,
        :'period_end_greg'::date   AS period_end_greg
)
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
CROSS JOIN params AS p
WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = p.start_jalali
  AND r."SourceDateRangeEndJalali" = p.end_jalali
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = p.period_start_greg
        AND mr."PeriodEnd" = p.period_end_greg
  )
ORDER BY r."ExternalReference";

WITH params AS (
    SELECT
        :'month_token'::text       AS month_token,
        :'start_jalali'::text      AS start_jalali,
        :'end_jalali'::text        AS end_jalali,
        :'period_start_greg'::date AS period_start_greg,
        :'period_end_greg'::date   AS period_end_greg
)
UPDATE "ProviderSyncRuns" AS r
SET
    "Status" = 'Failed',
    "ErrorCount" = GREATEST(r."ErrorCount", 1),
    "ErrorMessage" = 'NoDataYet - vendor returned no monthly report rows for this company/month',
    "CompletedAt" = NULL
FROM params AS p
WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = p.start_jalali
  AND r."SourceDateRangeEndJalali" = p.end_jalali
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = p.period_start_greg
        AND mr."PeriodEnd" = p.period_end_greg
  );

WITH params AS (
    SELECT
        :'month_token'::text  AS month_token,
        :'start_jalali'::text AS start_jalali,
        :'end_jalali'::text   AS end_jalali
)
UPDATE "MonthlyActivityBackfillStates" AS s
SET
    "IsCompleted" = FALSE,
    "CompletedAt" = NULL
FROM params AS p
WHERE s."SourceName" = 'NoavaranCurrentApi'
  AND EXISTS (
      SELECT 1
      FROM "ProviderSyncRuns" AS r
      WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
        AND r."ProviderName" = 'NoavaranCurrentApi'
        AND r."Status" = 'Failed'
        AND r."SourceDateRangeStartJalali" = p.start_jalali
        AND r."SourceDateRangeEndJalali" = p.end_jalali
  );

WITH params AS (
    SELECT
        :'month_token'::text  AS month_token,
        :'start_jalali'::text AS start_jalali,
        :'end_jalali'::text   AS end_jalali
)
SELECT
    COUNT(*) FILTER (WHERE r."Status" = 'Failed')    AS "FailedRuns",
    COUNT(*) FILTER (WHERE r."Status" = 'Completed') AS "CompletedRuns",
    COUNT(*)                                         AS "TotalRuns"
FROM "ProviderSyncRuns" AS r
CROSS JOIN params AS p
WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."SourceDateRangeStartJalali" = p.start_jalali
  AND r."SourceDateRangeEndJalali" = p.end_jalali;

SELECT
    s."SourceName",
    s."IsCompleted",
    s."CompletedAt",
    s."LastStartedAt",
    s."RequestedBy"
FROM "MonthlyActivityBackfillStates" AS s
WHERE s."SourceName" = 'NoavaranCurrentApi';

WITH params AS (
    SELECT
        :'month_token'::text  AS month_token,
        :'start_jalali'::text AS start_jalali,
        :'end_jalali'::text   AS end_jalali
)
SELECT
    r."ExternalReference" AS "CompanyId",
    r."IdempotencyKey",
    r."Status",
    r."ErrorMessage"
FROM "ProviderSyncRuns" AS r
CROSS JOIN params AS p
WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Failed'
  AND r."SourceDateRangeStartJalali" = p.start_jalali
  AND r."SourceDateRangeEndJalali" = p.end_jalali
ORDER BY r."ExternalReference";

COMMIT;

WITH params AS (
    SELECT
        :'month_token'::text       AS month_token,
        :'start_jalali'::text      AS start_jalali,
        :'end_jalali'::text        AS end_jalali,
        :'period_start_greg'::date AS period_start_greg,
        :'period_end_greg'::date   AS period_end_greg
)
SELECT
    r."IdempotencyKey",
    r."ExternalReference",
    r."Status",
    r."ProcessedRecords",
    r."ErrorMessage"
FROM "ProviderSyncRuns" AS r
CROSS JOIN params AS p
WHERE r."IdempotencyKey" LIKE ('nadpco-monthlybf-' || p.month_token || '-%')
  AND r."ProviderName" = 'NoavaranCurrentApi'
  AND r."Status" = 'Completed'
  AND r."SourceDateRangeStartJalali" = p.start_jalali
  AND r."SourceDateRangeEndJalali" = p.end_jalali
  AND NOT EXISTS (
      SELECT 1
      FROM "MonthlyReports" AS mr
      WHERE mr."ProviderName" = 'NoavaranCurrentApi'
        AND mr."ExternalCompanyId" = r."ExternalReference"
        AND mr."PeriodStart" = p.period_start_greg
        AND mr."PeriodEnd" = p.period_end_greg
  )
ORDER BY r."ExternalReference";