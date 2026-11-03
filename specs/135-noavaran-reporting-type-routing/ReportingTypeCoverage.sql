-- Feature 135 rollout verification.
-- Run after applying the ReportingType migration and refreshing the Noavaran company catalog.
-- A malformed value cannot be persisted in Companies.ReportingType because the column is integer;
-- malformed provider payloads remain an ingestion/quarantine diagnostic rather than a row state.

SELECT
    CASE
        WHEN "ReportingType" IS NULL THEN 'missing'
        WHEN "ReportingType" IN (1000000, 1000005, 1000008) THEN 'supported'
        WHEN "ReportingType" IN (1000001, 1000002, 1000003, 1000004, 1000006, 1000007, 1000009)
            THEN 'unsupported'
        ELSE 'unknown'
    END AS reporting_type_state,
    COUNT(*) AS company_count
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi'
GROUP BY 1
ORDER BY 1;

SELECT
    "ExternalCompanyId",
    "CompanySymbol",
    "ReportingType"
FROM "Companies"
WHERE "ProviderName" = 'NoavaranCurrentApi'
  AND "ReportingType" IS NULL
ORDER BY "ExternalCompanyId";
