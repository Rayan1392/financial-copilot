-- Selective repair for duplicate Noavaran monthly reports whose non-canonical
-- ExternalReportId ends with ":category-{id}".
--
-- Keeps the canonical ProductSales/ServiceSales report row:
--   {ReportType}:{ExternalCompanyId}:{JalaliYear}-{JalaliMonth}:output-{OutputType|none}
-- Deletes line items under the category-suffixed duplicate row.
-- Deletes the duplicate MonthlyReports row.
-- Deletes affected CompanyProductRevenueMixes and CompanyMonthlyActivityTrendSnapshots rows so
-- the affected periods can be recalculated cleanly after the cleanup.

BEGIN;

DROP TABLE IF EXISTS tmp_monthly_report_category_suffix_repairs;

CREATE TEMP TABLE tmp_monthly_report_category_suffix_repairs AS
WITH report_pairs AS (
    SELECT
        bad."Id" AS duplicate_report_id,
        good."Id" AS canonical_report_id,
        bad."ProviderName",
        bad."ExternalCompanyId",
        bad."ReportType",
        bad."OutputType",
        bad."PeriodStart",
        bad."PeriodEnd",
        substring(good."ExternalReportId" from ':(\d{4})-\d{2}:output-')::int AS report_year,
        substring(good."ExternalReportId" from ':\d{4}-(\d{2}):output-')::int AS report_month
    FROM "MonthlyReports" bad
    JOIN "MonthlyReports" good
      ON good."ProviderName" = bad."ProviderName"
     AND good."ExternalCompanyId" = bad."ExternalCompanyId"
     AND good."ReportType" = bad."ReportType"
     AND good."OutputType" IS NOT DISTINCT FROM bad."OutputType"
     AND good."PeriodStart" = bad."PeriodStart"
     AND good."PeriodEnd" = bad."PeriodEnd"
     AND bad."ExternalReportId" ~ ':category-[0-9]+$'
     AND good."ExternalReportId" = concat(
            bad."ReportType",
            ':',
            bad."ExternalCompanyId",
            ':',
            to_char(
                EXTRACT(YEAR FROM ((bad."WarningsJson"::jsonb -> 0 ->> 'jalaliYear')::int || '-01-01')::date),
                'FM0000'
            ),
            '-',
            lpad((bad."WarningsJson"::jsonb -> 0 ->> 'jalaliMonth')::text, 2, '0'),
            ':output-',
            coalesce(bad."OutputType"::text, 'none'))
)
SELECT DISTINCT *
FROM report_pairs;

-- Detection preview.
SELECT *
FROM tmp_monthly_report_category_suffix_repairs
ORDER BY "ExternalCompanyId", "PeriodStart", "OutputType";

DELETE FROM "MonthlyReportLineItems" li
USING tmp_monthly_report_category_suffix_repairs repair
WHERE li."MonthlyReportId" = repair.duplicate_report_id;

DELETE FROM "MonthlyReports" mr
USING tmp_monthly_report_category_suffix_repairs repair
WHERE mr."Id" = repair.duplicate_report_id;

DELETE FROM "CompanyProductRevenueMixes" mix
USING tmp_monthly_report_category_suffix_repairs repair
WHERE mix."SourceProviderName" = repair."ProviderName"
  AND mix."ExternalCompanyId" = repair."ExternalCompanyId"
  AND mix."ReportYear" = repair.report_year
  AND mix."ReportMonth" = repair.report_month;

DELETE FROM "CompanyMonthlyActivityTrendSnapshots" snap
USING tmp_monthly_report_category_suffix_repairs repair
WHERE snap."SourceProviderName" = repair."ProviderName"
  AND snap."ExternalCompanyId" = repair."ExternalCompanyId"
  AND snap."ReportYear" = repair.report_year
  AND snap."ReportMonth" = repair.report_month;

COMMIT;
