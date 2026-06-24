-- Update CompanyMonthlyActivityTrendSnapshots with IndustryId, IndustryTitle, CategoryId, CategoryTitle
-- sourced from the authoritative Companies → NormalizedIndustryRow / NormalizedIndustryGroupRow join.
--
-- Run this once after deploying the code fix, or whenever the Companies dimension tables are refreshed.
-- Safe to re-run: only updates rows where the joined value differs from the current stored value.

UPDATE "CompanyMonthlyActivityTrendSnapshots" s
SET
    "IndustryId"    = CAST(ind."ExternalId" AS integer),
    "IndustryTitle" = ind."Name",
    "CategoryId"    = CAST(grp."ExternalId" AS integer),
    "CategoryTitle" = grp."Name"
FROM "Companies" c
LEFT JOIN "Industries"      ind ON ind."Id" = c."IndustryId"
LEFT JOIN "IndustryGroups"  grp ON grp."Id" = c."GroupId"
WHERE c."ProviderName" = 'NoavaranCurrentApi'
  AND c."ExternalCompanyId" = s."ExternalCompanyId"
  AND (
      s."IndustryId"    IS DISTINCT FROM CAST(ind."ExternalId" AS integer)
   OR s."IndustryTitle" IS DISTINCT FROM ind."Name"
   OR s."CategoryId"    IS DISTINCT FROM CAST(grp."ExternalId" AS integer)
   OR s."CategoryTitle" IS DISTINCT FROM grp."Name"
  );
