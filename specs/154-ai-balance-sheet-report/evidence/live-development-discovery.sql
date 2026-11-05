SHOW default_transaction_read_only;
\echo '== eligible companies (live predicate) for candidates'
SELECT "ExternalCompanyId", "CompanySymbol", "GroupId", "ReportingType", ("IndustryId" IS NOT NULL) AS has_industry
FROM "Companies"
WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL AND "IndustryId" IS NOT NULL
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5','78ea1ed8-3242-4081-9e09-e6dbf34090e5')
  AND "ExternalCompanyId" IN ('452','167','290','12548','13156','12620','12939','13097','13150')
ORDER BY 1;
\echo '== eligible standalone BS snapshots (scope-agreeing, MillionRials) with FYE'
SELECT s."ExternalCompanyId", s."ExternalStatementId", s."PeriodType", s."PeriodEnd", s."IsAudited", s."IsRepresented", s."PublishedAt",
  s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd' AS fye, s."WarningsJson"::jsonb -> 0 ->> 'assumedScale' AS scale,
  s."WarningsJson"::jsonb -> 0 ->> 'isComposing' AS src_scope
FROM "FinancialStatements" s
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
  AND s."ExternalCompanyId" IN ('452','167','290','12548','13156','12620','12939','13097','13150')
  AND s."PeriodEnd" >= '2024-03-01'
ORDER BY 1, 4 DESC, 5 DESC, 6 DESC, 7 DESC NULLS LAST;

-- AC-S01 identity check (2026-11-05)
SHOW default_transaction_read_only;
\echo '== AC-S01: selected anchor row identity and CompanyId'
SELECT s."Id", s."ExternalStatementId", s."ExternalCompanyId", s."ProviderName", (s."CompanyId" IS NULL) AS company_id_null, s."PeriodEnd", s."PeriodType"
FROM "FinancialStatements" s WHERE s."Id" IN ('4ce25b02-7ca8-44c2-a71a-7520ac65cf90');
\echo '== all rows for ExternalStatementId 409865'
SELECT s."Id", s."StatementType", s."IsComposing", (s."CompanyId" IS NULL) AS company_id_null FROM "FinancialStatements" s WHERE s."ExternalStatementId"='409865' ORDER BY 2,3;

-- F154-V-05 candidates ساربیل (236) and منجیل (15763) (2026-11-05)
SHOW default_transaction_read_only;
\echo '== F154-V-05 candidates: live predicate classification'
SELECT "ExternalCompanyId", "CompanySymbol", "GroupId", ("IndustryId" IS NOT NULL) AS has_industry
FROM "Companies"
WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL AND "IndustryId" IS NOT NULL
  AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5','78ea1ed8-3242-4081-9e09-e6dbf34090e5')
  AND "ExternalCompanyId" IN ('236','15763') ORDER BY 1;
\echo '== F154-V-05 candidates: standalone BS snapshots with FYE (2024-03-01 onward)'
SELECT s."ExternalCompanyId", s."ExternalStatementId", s."PeriodType", s."PeriodEnd", s."IsAudited", s."IsRepresented", s."PublishedAt",
  s."WarningsJson"::jsonb -> 0 ->> 'jalaliFiscalYearEnd' AS fye, s."WarningsJson"::jsonb -> 0 ->> 'assumedScale' AS scale,
  s."WarningsJson"::jsonb -> 0 ->> 'isComposing' AS src_scope
FROM "FinancialStatements" s
WHERE s."ProviderName"='NoavaranCurrentApi' AND s."StatementType"='BalanceSheet' AND s."IsComposing"=false
  AND s."ExternalCompanyId" IN ('236','15763') AND s."PeriodEnd" >= '2024-03-01'
ORDER BY 1, 4 DESC, 5 DESC, 6 DESC, 7 DESC NULLS LAST;
