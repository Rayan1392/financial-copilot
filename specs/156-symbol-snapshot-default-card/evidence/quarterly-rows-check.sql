-- Feature 156: do the card's quarterly row IDs exist in real income statements? Read-only.
\pset pager off
SHOW default_transaction_read_only;
WITH picked AS (
  SELECT c."CompanySymbol", c."ExternalCompanyId"
  FROM "Companies" c
  WHERE c."ProviderName" = 'NoavaranCurrentApi'
    AND c."CompanySymbol" IN ('اخابر', 'کگهر', 'کگل', 'وبملت')
), latest AS (
  SELECT DISTINCT ON (p."CompanySymbol") p."CompanySymbol", s."Id", s."PeriodEnd", s."IsAudited"
  FROM picked p
  JOIN "FinancialStatements" s
    ON s."ExternalCompanyId" = p."ExternalCompanyId" AND s."ProviderName" = 'NoavaranCurrentApi'
  WHERE s."StatementType" = 'IncomeStatement' AND s."IsComposing" = false
  ORDER BY p."CompanySymbol", s."PeriodEnd" DESC
)
SELECT l."CompanySymbol", l."PeriodEnd", l."IsAudited",
       string_agg(c."SourceItemId"::text, ',' ORDER BY c."SourceItemId") AS items_present
FROM latest l
JOIN "FinancialStatementLineItems" i ON i."FinancialStatementId" = l."Id"
JOIN "FinancialStatementSourceItems" c ON c."Id" = i."SourceItemCatalogId"
WHERE c."SourceItemId" IN (15, 139, 143, 160, 306, 307)
GROUP BY l."CompanySymbol", l."PeriodEnd", l."IsAudited"
ORDER BY 1;
