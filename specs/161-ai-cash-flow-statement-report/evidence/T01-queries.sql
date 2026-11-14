-- Session
SHOW default_transaction_read_only;

-- Identity
SELECT current_user, current_database();

-- Catalog, presence by fiscal year and inferred template
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT i."SourceItemId",i."TitleFa",s.template,s.fiscal_year,count(DISTINCT s."Id") AS statements,count(DISTINCT s."Id") FILTER (WHERE v.value IS NOT NULL) AS reported
FROM "FinancialStatementSourceItems" i LEFT JOIN v ON v.item=i."SourceItemId" LEFT JOIN s ON s."Id"=v."Id" WHERE i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow' GROUP BY 1,2,3,4 ORDER BY 1,3,4;

-- Sign survey
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT v.item,count(*) AS rows,count(*) FILTER (WHERE value>0) AS positive,count(*) FILTER (WHERE value<0) AS negative,count(*) FILTER (WHERE value=0) AS zero,count(*) FILTER (WHERE value IS NULL) AS nulls FROM v GROUP BY 1 ORDER BY 1;

-- Coverage and equations excluding nonzero 120 and duplicate items
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,fiscal_year,count(*) AS statements,count(*) FILTER (WHERE a128 IS NOT NULL) AS has128,count(*) FILTER (WHERE a128 IS NOT NULL AND a121 IS NOT NULL AND a122 IS NOT NULL) AS v3_comparable,count(*) FILTER (WHERE a128+a121=a122) AS v3_equal,count(*) FILTER (WHERE a128 IS NOT NULL AND a121 IS NOT NULL AND a123 IS NOT NULL AND a124 IS NOT NULL) AS r1_comparable,count(*) FILTER (WHERE a128+a121+a123=a124) AS r1_equal,count(*) FILTER (WHERE a126 IS NOT NULL AND a121 IS NOT NULL) AS i_comparable,count(*) FILTER (WHERE a126=a121) AS i_equal,count(*) FILTER (WHERE a127 IS NOT NULL AND a123 IS NOT NULL) AS f_comparable,count(*) FILTER (WHERE a127=a123) AS f_equal FROM pivot WHERE coalesce(a120,0)=0 AND duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Presentation, missing totals and duplicates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,fiscal_year,count(*) AS statements,count(*) FILTER (WHERE a120<>0) AS nonzero120,count(*) FILTER (WHERE a121 IS NULL OR a123 IS NULL) AS missing121_or123,count(*) FILTER (WHERE a128 IS NULL) AS missing128,count(*) FILTER (WHERE duplicate_rows>0) AS duplicates FROM pivot GROUP BY 1,2 ORDER BY 1,2;

-- FX cases excluding duplicates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,count(*) AS statements,count(*) FILTER (WHERE a35 IS NULL) AS fx_absent_or_null,count(*) FILTER (WHERE a35=0) AS fx_zero,count(*) FILTER (WHERE a35<>0) AS fx_nonzero,count(*) FILTER (WHERE a33+a124=a125 AND a35<>0) AS fx_already_closes_nonzero,count(*) FILTER (WHERE a33+a124+a35=a125 AND a35<>0) AS fx_separate_closes,count(*) FILTER (WHERE a35 IS NULL AND a33+a124=a125) AS absent_fx_closes,count(*) FILTER (WHERE a35 IS NULL AND a33+a124<>a125) AS absent_fx_fails FROM pivot WHERE duplicate_rows=0 GROUP BY 1 ORDER BY 1;

-- Section detail closure by company
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT p.template,p."ExternalCompanyId",count(*) AS statements,count(*) FILTER (WHERE p.a121 IS NOT NULL) AS investing_comparable,count(*) FILTER (WHERE p.a121 = d.investing) AS investing_equal,count(*) FILTER (WHERE p.a123 IS NOT NULL) AS financing_comparable,count(*) FILTER (WHERE p.a123 = d.financing) AS financing_equal FROM pivot p JOIN (SELECT "Id",sum(value) FILTER (WHERE item IN (10,48,46,14,83,47,11,15,12,17,70,71,103,104,130,129,4,3,42,95,96,13)) AS investing,sum(value) FILTER (WHERE item IN (23,24,5,6,117,119,21,131,40,41,37,132,133,134,114,135)) AS financing FROM v GROUP BY 1) d ON d."Id"=p."Id" WHERE p.duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Fixture companies and group IDs
SELECT "ExternalCompanyId","CompanySymbol","GroupId","ReportingType","PrecedencyRight","MarketId" FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "ExternalCompanyId" IN ('255','452') ORDER BY 1,2;

-- Fixture headers and metadata
SELECT "ExternalCompanyId","ExternalStatementId","StatementType","PeriodType","PeriodStart","PeriodEnd","IsAudited","IsRepresented","IsComposing","PublishedAt","WarningsJson" FROM "FinancialStatements" WHERE "ProviderName"='NoavaranCurrentApi' AND "ExternalCompanyId" IN ('255','452') AND "StatementType" IN ('CashFlow','IncomeStatement') AND "PeriodEnd"='2026-09-22' ORDER BY 1,2,3;

-- Fixture line items
SELECT s."ExternalCompanyId",s."ExternalStatementId",s."StatementType",s."IsComposing",i."SourceItemId",i."TitleFa",l."Value" FROM "FinancialStatements" s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id" JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" WHERE s."ProviderName"='NoavaranCurrentApi' AND s."ExternalCompanyId" IN ('255','452') AND s."PeriodEnd"='2026-09-22' AND ((s."StatementType"='CashFlow' AND i."StatementType"='CashFlow') OR (s."StatementType"='IncomeStatement' AND i."StatementType"='IncomeStatement' AND i."SourceItemId" IN (15,143,313))) ORDER BY 1,2,3,5;

-- Null versus absent rows
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT i."SourceItemId",count(*) AS statements,count(*) FILTER (WHERE v.item IS NULL) AS absent,count(*) FILTER (WHERE v.item IS NOT NULL AND v.value IS NULL) AS present_null,count(*) FILTER (WHERE v.value=0) AS present_zero,count(*) FILTER (WHERE v.value IS NOT NULL) AS reported FROM s CROSS JOIN "FinancialStatementSourceItems" i LEFT JOIN v ON v."Id"=s."Id" AND v.item=i."SourceItemId" WHERE i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow' GROUP BY 1 ORDER BY 1;

-- Item 128 coverage among eligible Codal-pair candidates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f LEFT JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template, fiscal_year, count(*) AS codal_pair, count(*) FILTER (WHERE a128 IS NOT NULL) AS has128, count(*) FILTER (WHERE a128 IS NULL) AS missing128 FROM pivot WHERE coalesce(a120,0)=0 AND a121 IS NOT NULL AND a123 IS NOT NULL AND duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Eligible-view: Catalog, presence by fiscal year and inferred template
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT i."SourceItemId",i."TitleFa",s.template,s.fiscal_year,count(DISTINCT s."Id") AS statements,count(DISTINCT s."Id") FILTER (WHERE v.value IS NOT NULL) AS reported
FROM "FinancialStatementSourceItems" i LEFT JOIN v ON v.item=i."SourceItemId" LEFT JOIN s ON s."Id"=v."Id" WHERE i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow' GROUP BY 1,2,3,4 ORDER BY 1,3,4;

-- Eligible-view: Coverage and equations excluding nonzero 120 and duplicate items
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,fiscal_year,count(*) AS statements,count(*) FILTER (WHERE a128 IS NOT NULL) AS has128,count(*) FILTER (WHERE a128 IS NOT NULL AND a121 IS NOT NULL AND a122 IS NOT NULL) AS v3_comparable,count(*) FILTER (WHERE a128+a121=a122) AS v3_equal,count(*) FILTER (WHERE a128 IS NOT NULL AND a121 IS NOT NULL AND a123 IS NOT NULL AND a124 IS NOT NULL) AS r1_comparable,count(*) FILTER (WHERE a128+a121+a123=a124) AS r1_equal,count(*) FILTER (WHERE a126 IS NOT NULL AND a121 IS NOT NULL) AS i_comparable,count(*) FILTER (WHERE a126=a121) AS i_equal,count(*) FILTER (WHERE a127 IS NOT NULL AND a123 IS NOT NULL) AS f_comparable,count(*) FILTER (WHERE a127=a123) AS f_equal FROM pivot WHERE coalesce(a120,0)=0 AND duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Eligible-view: Presentation, missing totals and duplicates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,fiscal_year,count(*) AS statements,count(*) FILTER (WHERE a120<>0) AS nonzero120,count(*) FILTER (WHERE a121 IS NULL OR a123 IS NULL) AS missing121_or123,count(*) FILTER (WHERE a128 IS NULL) AS missing128,count(*) FILTER (WHERE duplicate_rows>0) AS duplicates FROM pivot GROUP BY 1,2 ORDER BY 1,2;

-- Eligible-view: FX cases excluding duplicates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template,count(*) AS statements,count(*) FILTER (WHERE a35 IS NULL) AS fx_absent_or_null,count(*) FILTER (WHERE a35=0) AS fx_zero,count(*) FILTER (WHERE a35<>0) AS fx_nonzero,count(*) FILTER (WHERE a33+a124=a125 AND a35<>0) AS fx_already_closes_nonzero,count(*) FILTER (WHERE a33+a124+a35=a125 AND a35<>0) AS fx_separate_closes,count(*) FILTER (WHERE a35 IS NULL AND a33+a124=a125) AS absent_fx_closes,count(*) FILTER (WHERE a35 IS NULL AND a33+a124<>a125) AS absent_fx_fails FROM pivot WHERE duplicate_rows=0 GROUP BY 1 ORDER BY 1;

-- Eligible-view: Section detail closure by company
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT p.template,p."ExternalCompanyId",count(*) AS statements,count(*) FILTER (WHERE p.a121 IS NOT NULL) AS investing_comparable,count(*) FILTER (WHERE p.a121 = d.investing) AS investing_equal,count(*) FILTER (WHERE p.a123 IS NOT NULL) AS financing_comparable,count(*) FILTER (WHERE p.a123 = d.financing) AS financing_equal FROM pivot p JOIN (SELECT "Id",sum(value) FILTER (WHERE item IN (10,48,46,14,83,47,11,15,12,17,70,71,103,104,130,129,4,3,42,95,96,13)) AS investing,sum(value) FILTER (WHERE item IN (23,24,5,6,117,119,21,131,40,41,37,132,133,134,114,135)) AS financing FROM v GROUP BY 1) d ON d."Id"=p."Id" WHERE p.duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Eligible-view: Item 128 coverage among eligible Codal-pair candidates
WITH company AS (
 SELECT "ExternalCompanyId", CASE WHEN count(DISTINCT "GroupId")=1 THEN CASE WHEN bool_and("GroupId" = %(bank_group)s::uuid) THEN 'bank' ELSE 'general' END ELSE 'unknown' END AS template
 FROM "NoavaranEligibleCompanies" WHERE "ProviderName"='NoavaranCurrentApi' AND "GroupId" IS NOT NULL GROUP BY 1
), s AS (
 SELECT f.*, coalesce(c.template,'unknown') AS template, coalesce(split_part(f."WarningsJson"::jsonb->0->>'jalaliFiscalYearEnd','/',1),'unknown') AS fiscal_year
 FROM "FinancialStatements" f JOIN company c ON c."ExternalCompanyId"=f."ExternalCompanyId"
 WHERE f."ProviderName"='NoavaranCurrentApi' AND f."StatementType"='CashFlow' AND f."IsComposing"=false
), v AS (
 SELECT s."Id", i."SourceItemId" AS item, l."Value" AS value
 FROM s JOIN "FinancialStatementLineItems" l ON l."FinancialStatementId"=s."Id"
 JOIN "FinancialStatementSourceItems" i ON i."Id"=l."SourceItemCatalogId" AND i."ProviderName"='NoavaranCurrentApi' AND i."StatementType"='CashFlow'
), pivot AS (
 SELECT s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year,
 max(v.value) FILTER (WHERE item=33) AS a33, max(v.value) FILTER (WHERE item=35) AS a35,
 max(v.value) FILTER (WHERE item=120) AS a120, max(v.value) FILTER (WHERE item=121) AS a121,
 max(v.value) FILTER (WHERE item=122) AS a122, max(v.value) FILTER (WHERE item=123) AS a123,
 max(v.value) FILTER (WHERE item=124) AS a124, max(v.value) FILTER (WHERE item=125) AS a125,
 max(v.value) FILTER (WHERE item=126) AS a126, max(v.value) FILTER (WHERE item=127) AS a127, max(v.value) FILTER (WHERE item=128) AS a128,
 count(v.item)-count(DISTINCT v.item) AS duplicate_rows
 FROM s LEFT JOIN v ON v."Id"=s."Id" GROUP BY s."Id",s."ExternalCompanyId",s."ExternalStatementId",s.template,s.fiscal_year
)
SELECT template, fiscal_year, count(*) AS codal_pair, count(*) FILTER (WHERE a128 IS NOT NULL) AS has128, count(*) FILTER (WHERE a128 IS NULL) AS missing128 FROM pivot WHERE coalesce(a120,0)=0 AND a121 IS NOT NULL AND a123 IS NOT NULL AND duplicate_rows=0 GROUP BY 1,2 ORDER BY 1,2;

-- Eligible-view: bank universe and statement coverage
SELECT e."ExternalCompanyId",e."CompanySymbol", count(DISTINCT s."Id") AS cash_flow_statements
FROM "NoavaranEligibleCompanies" e LEFT JOIN "FinancialStatements" s ON s."ProviderName"=e."ProviderName" AND s."ExternalCompanyId"=e."ExternalCompanyId" AND s."StatementType"='CashFlow' AND s."IsComposing"=false
WHERE e."ProviderName"='NoavaranCurrentApi' AND e."GroupId"=%(bank_group)s::uuid GROUP BY 1,2 ORDER BY 1,2;
