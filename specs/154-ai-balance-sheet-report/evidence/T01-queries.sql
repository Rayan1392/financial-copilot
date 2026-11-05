SHOW default_transaction_read_only;
SELECT current_user;
SELECT table_schema, table_name, table_type FROM information_schema.tables
WHERE table_name IN ('FinancialStatements','FinancialStatementLineItems','FinancialStatementSourceItems','NoavaranEligibleCompanies','IndustryGroups','Companies')
ORDER BY 1,2;
SELECT table_name, privilege_type FROM information_schema.table_privileges
WHERE grantee = current_user AND table_name IN ('FinancialStatements','FinancialStatementLineItems','FinancialStatementSourceItems','NoavaranEligibleCompanies','IndustryGroups','Companies')
ORDER BY 1,2;
SELECT column_name, data_type FROM information_schema.columns WHERE table_name='NoavaranEligibleCompanies' ORDER BY ordinal_position;
SELECT column_name, data_type FROM information_schema.columns WHERE table_name='IndustryGroups' ORDER BY ordinal_position;
\echo '== Companies columns'
SELECT column_name, data_type FROM information_schema.columns WHERE table_name='Companies' ORDER BY ordinal_position;
\echo '== Fixture companies'
SELECT "Id", "ProviderName", "ExternalCompanyId", "CompanySymbol", "TseSymbol", "Name" FROM "Companies"
WHERE "ExternalCompanyId" IN ('452','167','290') ORDER BY "ExternalCompanyId", "ProviderName";
\echo '== E-4 fixture statements (BalanceSheet, NoavaranCurrentApi)'
SELECT "ExternalCompanyId", "Id", "ExternalStatementId", "PeriodType", "PeriodEnd", "IsAudited", "IsRepresented", "IsComposing", "PublishedAt", "CompanyId" IS NULL AS company_id_blank
FROM "FinancialStatements"
WHERE "ProviderName"='NoavaranCurrentApi' AND "StatementType"='BalanceSheet' AND "ExternalCompanyId" IN ('452','167','290')
ORDER BY "ExternalCompanyId", "PeriodEnd" DESC, "IsComposing", "ExternalStatementId" DESC
LIMIT 80;
\echo '== Statement 409865 / 403733 any type'
SELECT "Id", "ExternalCompanyId", "ExternalStatementId", "StatementType", "PeriodType", "PeriodEnd", "IsAudited", "IsRepresented", "IsComposing", "PublishedAt"
FROM "FinancialStatements" WHERE "ExternalStatementId" IN ('409865','403733') ORDER BY 3,4,9;
\echo '== Fixture item values at 2026-09-22 standalone (primaries and candidates)'
SELECT s."ExternalCompanyId", s."ExternalStatementId", c."SourceItemId", c."TitleFa", l."Value"
FROM "FinancialStatements" s
JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId" = s."Id"
JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
  AND s."ExternalCompanyId" IN ('452','167','290') AND s."PeriodEnd"='2026-09-22'
  AND c."SourceItemId" IN (1,2,4,18,19,21,39,45,47,51,57,58,60,64,71,135,137,138,139,140,143,144,145,146,147,148,160,170,188,223,286,291,293,298,299,300,301,302,303,304,305,306,307,308)
ORDER BY 1,2,3;
\echo '== E-3 JalaliFiscalYearEnd evidence for fixture BS records'
SELECT s."ExternalCompanyId", s."ExternalStatementId", s."PeriodType", s."PeriodEnd", s."IsComposing",
  (SELECT e->>'evidence' FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s."WarningsJson"::jsonb)='array' THEN s."WarningsJson"::jsonb ELSE '[]'::jsonb END) e WHERE e->>'code'='JalaliFiscalYearEnd' LIMIT 1) AS fye,
  (SELECT e->>'assumedScale' FROM jsonb_array_elements(CASE WHEN jsonb_typeof(s."WarningsJson"::jsonb)='array' THEN s."WarningsJson"::jsonb ELSE '[]'::jsonb END) e WHERE e ? 'assumedScale' LIMIT 1) AS scale
FROM "FinancialStatements" s
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."ExternalCompanyId" IN ('452','167','290')
ORDER BY 1, 4 DESC LIMIT 60;
SELECT "ExternalStatementId", "PeriodEnd", left("WarningsJson", 3000) FROM "FinancialStatements"
WHERE "ProviderName"='NoavaranCurrentApi' AND "StatementType"='BalanceSheet' AND "ExternalCompanyId"='452' AND "ExternalStatementId" IN ('409865','403733');
\echo '== E-5 eligible GroupIds (replicating the NoavaranEligibleCompanies view predicate on Companies)'
SELECT "ExternalCompanyId", "CompanySymbol", "GroupId", "ReportingType", "PrecedencyRight", "MarketId"
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')
  AND "ExternalCompanyId" IN ('452','167','290')
ORDER BY 1;
\echo '== E-5 eligible companies in configured bank GroupId c96520f9-1d2e-444a-ab46-5bdffd3797d2'
SELECT "ExternalCompanyId", "CompanySymbol", "ReportingType"
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')
  AND "GroupId" = 'c96520f9-1d2e-444a-ab46-5bdffd3797d2'
ORDER BY 2;
\echo '== E-5 ReportingType 1000003 (Banking) eligible companies and their GroupIds'
SELECT "GroupId", count(*) FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')
  AND "ReportingType" = 1000003
GROUP BY 1;
\echo '== E-5 duplicate eligible rows per fixture company'
SELECT "ExternalCompanyId", count(*) FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')
  AND "ExternalCompanyId" IN ('452','167','290') GROUP BY 1 ORDER BY 1;

\echo '== E-3 JalaliFiscalYearEnd per fixture company and statement type (distinct values)'
SELECT s."ExternalCompanyId", s."StatementType",
  (s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd') AS fye, count(*), min(s."PeriodEnd"), max(s."PeriodEnd")
FROM "FinancialStatements" s
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."ExternalCompanyId" IN ('452','167','290')
GROUP BY 1,2,3 ORDER BY 1,2,3;
\echo '== E-3 BS records lacking parseable fiscal-year end (all companies)'
SELECT count(*) FILTER (WHERE (s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd') IS NULL) AS missing, count(*) AS total
FROM "FinancialStatements" s WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet'
  AND jsonb_typeof(s."WarningsJson"::jsonb)='array';
\echo '== E-3 transitional-year candidates: companies whose BS fiscal-year-end month changes'
SELECT x."ExternalCompanyId", c."CompanySymbol", array_agg(DISTINCT x.fye_month ORDER BY x.fye_month) AS months
FROM (SELECT s."ExternalCompanyId", split_part(s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd', '/', 2) AS fye_month
      FROM "FinancialStatements" s WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet'
        AND jsonb_typeof(s."WarningsJson"::jsonb)='array' AND (s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd') IS NOT NULL) x
LEFT JOIN "Companies" c ON c."ExternalCompanyId" = x."ExternalCompanyId" AND c."ProviderName"='NoavaranCurrentApi'
GROUP BY 1,2 HAVING count(DISTINCT x.fye_month) > 1 ORDER BY 1 LIMIT 15;

\echo '== E-1 primary/candidate coexistence across standalone BalanceSheet statements (all companies)'
WITH v AS (
  SELECT s."Id", s."ExternalCompanyId", c."SourceItemId" AS item, l."Value"
  FROM "FinancialStatements" s
  JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId" = s."Id"
  JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
  WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
    AND c."ProviderName"='NoavaranCurrentApi' AND c."StatementType"='BalanceSheet'
    AND c."SourceItemId" IN (135,137,138,139,140,143,147,148,298,299,300,301,302,303,304,305,306,293,307,308)
), pairs(p, f) AS (VALUES (135,298),(137,299),(138,300),(139,301),(140,302),(143,303),(147,304),(148,305),(306,293),(307,308))
SELECT pairs.p AS primary_item, pairs.f AS fallback_item,
  count(DISTINCT st."Id") FILTER (WHERE a."Value" IS NOT NULL OR b."Value" IS NOT NULL OR a."Id" IS NOT NULL OR b."Id" IS NOT NULL) AS statements_with_any,
  count(*) FILTER (WHERE a."Id" IS NOT NULL AND b."Id" IS NOT NULL) AS both_present,
  count(*) FILTER (WHERE a."Id" IS NOT NULL AND b."Id" IS NULL) AS primary_only,
  count(*) FILTER (WHERE a."Id" IS NULL AND b."Id" IS NOT NULL) AS fallback_only,
  count(*) FILTER (WHERE a."Id" IS NOT NULL AND b."Id" IS NOT NULL AND a."Value" IS DISTINCT FROM b."Value") AS disagree
FROM pairs
CROSS JOIN (SELECT DISTINCT "Id" FROM v) st
LEFT JOIN v a ON a."Id" = st."Id" AND a.item = pairs.p
LEFT JOIN v b ON b."Id" = st."Id" AND b.item = pairs.f
GROUP BY 1,2 ORDER BY 1;
\echo '== E-1 disagreement examples'
WITH v AS (
  SELECT s."Id", s."ExternalCompanyId", s."ExternalStatementId", s."PeriodEnd", c."SourceItemId" AS item, l."Value"
  FROM "FinancialStatements" s
  JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId" = s."Id"
  JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
  WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
    AND c."ProviderName"='NoavaranCurrentApi' AND c."StatementType"='BalanceSheet'
), pairs(p, f) AS (VALUES (135,298),(137,299),(138,300),(139,301),(140,302),(143,303),(147,304),(148,305),(306,293),(307,308))
SELECT pairs.p, pairs.f, a."ExternalCompanyId", a."ExternalStatementId", a."PeriodEnd", a."Value" AS primary_value, b."Value" AS fallback_value
FROM pairs JOIN v a ON a.item = pairs.p JOIN v b ON b."Id" = a."Id" AND b.item = pairs.f
WHERE a."Value" IS DISTINCT FROM b."Value"
ORDER BY 1, 3 LIMIT 30;
\echo '== E-1 per-company coverage for fixture companies (latest 4 standalone BS)'
SELECT s."ExternalCompanyId", s."ExternalStatementId", s."PeriodEnd",
  string_agg(c."SourceItemId"::text || '=' || coalesce(l."Value"::text,'null'), ', ' ORDER BY c."SourceItemId") AS items
FROM "FinancialStatements" s
JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId" = s."Id"
JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
  AND s."ExternalCompanyId" IN ('452','167','290') AND s."PeriodEnd" >= '2025-12-01'
  AND c."SourceItemId" IN (135,137,138,139,140,143,147,148,298,299,300,301,302,303,304,305,306,293,307,308)
GROUP BY 1,2,3,s."Id" ORDER BY 1,3 DESC;

\echo '== E-2 restated BalanceSheet records (IsRepresented=true) and matching originals'
SELECT s."ExternalCompanyId", s."ExternalStatementId", s."PeriodEnd", s."PeriodType", s."IsAudited", s."IsComposing", s."PublishedAt",
  (SELECT count(*) FROM "FinancialStatements" o WHERE o."ProviderName"=s."ProviderName" AND o."StatementType"='BalanceSheet'
     AND o."ExternalCompanyId"=s."ExternalCompanyId" AND o."PeriodEnd"=s."PeriodEnd" AND o."IsComposing"=s."IsComposing" AND o."IsRepresented"=false) AS originals_same_date,
  (SELECT string_agg(DISTINCT o."ExternalStatementId", ',') FROM "FinancialStatements" o WHERE o."ProviderName"=s."ProviderName" AND o."StatementType"='BalanceSheet'
     AND o."ExternalCompanyId"=s."ExternalCompanyId" AND o."PeriodEnd"=s."PeriodEnd" AND o."IsComposing"=s."IsComposing" AND o."IsRepresented"=false) AS original_statement_ids
FROM "FinancialStatements" s
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsRepresented"=true
ORDER BY s."ExternalCompanyId", s."PeriodEnd" DESC LIMIT 25;
\echo '== E-2 count of restated BS records'
SELECT count(*) AS restated, count(DISTINCT "ExternalCompanyId") AS companies FROM "FinancialStatements"
WHERE "ProviderName"='NoavaranCurrentApi' AND "StatementType"='BalanceSheet' AND "IsRepresented"=true;

\echo '== E-6 consolidated existence for fixture companies'
SELECT "ExternalCompanyId", EXISTS (SELECT 1 FROM "FinancialStatements" s WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet'
  AND s."ExternalCompanyId"=x."ExternalCompanyId" AND s."IsComposing"=true) AS has_consolidated,
  EXISTS (SELECT 1 FROM "FinancialStatements" s WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet'
  AND s."ExternalCompanyId"=x."ExternalCompanyId" AND s."IsComposing"=false) AS has_standalone
FROM (VALUES ('452'),('167'),('290')) AS x("ExternalCompanyId");
\echo '== E-6 a company with consolidated BS but no standalone BS'
SELECT s."ExternalCompanyId", c."CompanySymbol" FROM "FinancialStatements" s
LEFT JOIN "Companies" c ON c."ExternalCompanyId"=s."ExternalCompanyId" AND c."ProviderName"='NoavaranCurrentApi'
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet'
GROUP BY 1,2 HAVING bool_or(s."IsComposing") AND NOT bool_or(NOT s."IsComposing") LIMIT 5;

\echo '== E-8 catalog titles for rows 1,306,18,19,21,307,45 and long-term siblings 50,74,79'
SELECT "SourceItemId", "TitleFa", "TitleEn", "Unit" FROM "FinancialStatementSourceItems"
WHERE "ProviderName"='NoavaranCurrentApi' AND "StatementType"='BalanceSheet' AND "SourceItemId" IN (1,18,19,21,45,47,50,51,74,79,293,298,299,300,301,302,303,304,305,306,307,308)
ORDER BY 1;

SHOW default_transaction_read_only;
\echo '== live NoavaranEligibleCompanies definition (pg_views)'
SELECT definition FROM pg_views WHERE viewname = 'NoavaranEligibleCompanies';
SHOW default_transaction_read_only;
\echo '== FK constraints on Companies (GroupId, IndustryId)'
SELECT conname, pg_get_constraintdef(oid) FROM pg_constraint
WHERE conrelid = '"Companies"'::regclass AND contype = 'f' ORDER BY conname;
\echo '== E-5 with the live predicate (4 MarketIds; joins represented by non-null GroupId/IndustryId + FKs)'
SELECT "ExternalCompanyId", "CompanySymbol", "GroupId", "IndustryId" IS NOT NULL AS has_industry, count(*) OVER (PARTITION BY "ExternalCompanyId") AS eligible_rows
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5','78ea1ed8-3242-4081-9e09-e6dbf34090e5')
  AND "GroupId" IS NOT NULL AND "IndustryId" IS NOT NULL
  AND "ExternalCompanyId" IN ('452','167','290')
ORDER BY 1;
\echo '== rows in the configured bank GroupId under the live predicate'
SELECT count(*) AS bank_group_rows, count(*) FILTER (WHERE "ReportingType" = 1000003) AS banking_reporting_type
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi' AND "PrecedencyRight" = 0
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5','78ea1ed8-3242-4081-9e09-e6dbf34090e5')
  AND "GroupId" = 'c96520f9-1d2e-444a-ab46-5bdffd3797d2' AND "IndustryId" IS NOT NULL;
