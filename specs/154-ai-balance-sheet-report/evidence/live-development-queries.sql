-- Feature 154 live Development verification: the independent SQL behind live-development-verification.json.
-- Run as feature141_app_reader with PGOPTIONS='-c default_transaction_read_only=on' (no secrets in this file).
-- Source of truth for the executed text: tests/FinancialCopilot.IntegrationTests/BalanceSheetReport154LiveDevelopmentTests.cs
-- Discovery queries and their output: live-development-discovery.sql / live-development-discovery-output.txt

-- 0. Safety -----------------------------------------------------------------------------------------------
SHOW default_transaction_read_only;                       -- expected: on
-- Executed through FinancialIngestionDbContext on the same read-only connection; expected SQLSTATE 25006:
-- UPDATE "Companies" SET "Name" = "Name" WHERE false;

-- 1. Classification substitute (the role has no SELECT on "NoavaranEligibleCompanies", "IndustryGroups", "Industries")
--    Live deployed view predicate (pg_views, see T01-development-evidence.md): 4 MarketIds + INNER JOINs to
--    IndustryGroups/Industries. The joins are represented by non-null GroupId/IndustryId, which
--    FK_Companies_IndustryGroups_GroupId and FK_Companies_Industries_IndustryId guarantee.
SELECT "GroupId"
FROM "Companies"
WHERE "Id" = @canonical_company_id AND "ProviderName" = 'NoavaranCurrentApi' AND "ExternalCompanyId" = @external_company_id
  AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428', 'a3ccb30a-caed-4f26-a84a-ac0eb8c78c76',
                     '86c05022-632c-44cd-96c9-5c4f58c51ef5', '78ea1ed8-3242-4081-9e09-e6dbf34090e5')
  AND "GroupId" IS NOT NULL AND "IndustryId" IS NOT NULL;

-- 2. Expected snapshot (anchor: no date filter; baseline: @period_end and, for same stage, @period_type) ----------
--    Eligible standalone balance sheet, MillionRials, source scope agreeing; §5.4 ranking with stable tiebreak.
SELECT s."Id", s."ExternalStatementId", s."PeriodEnd", s."PeriodType"
FROM "FinancialStatements" s
WHERE s."ProviderName" = 'NoavaranCurrentApi' AND s."StatementType" = 'BalanceSheet' AND s."IsComposing" = false
  AND s."PeriodType" IN ('ThreeMonths', 'SixMonths', 'NineMonths', 'TwelveMonths')
  AND (s."WarningsJson"::jsonb -> 0 ->> 'assumedScale') = 'MillionRials'
  AND COALESCE((s."WarningsJson"::jsonb -> 0 ->> 'isComposing')::boolean, false) = false
  AND s."ExternalCompanyId" = @external_company_id
  AND (@period_end::date IS NULL OR s."PeriodEnd" = @period_end::date)
  AND (@period_type::text IS NULL OR s."PeriodType" = @period_type::text)
ORDER BY s."PeriodEnd" DESC, s."IsAudited" DESC, s."IsRepresented" DESC, s."PublishedAt" DESC NULLS LAST,
         s."ExternalStatementId"::bigint DESC, s."Id" DESC
LIMIT 1;

-- 3. Stored values of the selected statement (source item id -> value) ------------------------------------------
SELECT c."SourceItemId", l."Value"
FROM "FinancialStatementLineItems" l
JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
WHERE l."FinancialStatementId" = @statement_id
  AND c."ProviderName" = 'NoavaranCurrentApi' AND c."StatementType" = 'BalanceSheet';

-- Derived expectations (computed in the test from query 3, never from the payload):
--   ratio      = item147 / item138 * 100 when item138 > 0
--   change     = anchor - baseline (both stored values present)
--   percent    = (anchor - baseline) / baseline * 100 only when baseline > 0
--   pp change  = ratio(anchor) - ratio(baseline), from unrounded ratios
