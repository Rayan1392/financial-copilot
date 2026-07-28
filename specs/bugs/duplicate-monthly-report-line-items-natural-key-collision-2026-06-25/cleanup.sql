-- ============================================================
-- Cleanup: Remove duplicate MonthlyReportLineItems rows
-- and truncate CompanyProductRevenueMix for full re-sync
--
-- Safe to run multiple times (idempotent).
-- Run inside a transaction; inspect counts before COMMIT.
-- ============================================================

BEGIN;

-- Step 1: Preview which duplicate groups will be affected (read-only check)
-- SELECT "MonthlyReportId", "Title", COUNT(*) AS cnt
-- FROM "MonthlyReportLineItems"
-- GROUP BY "MonthlyReportId", "Title"
-- HAVING COUNT(*) > 1
-- ORDER BY cnt DESC;

-- Step 2: Delete duplicate rows, keeping the row with the LOWER UUID per (MonthlyReportId, Title).
-- "Lower UUID" is arbitrary but deterministic — we keep one row, whichever was inserted first.
DELETE FROM "MonthlyReportLineItems"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT
            "Id",
            ROW_NUMBER() OVER (
                PARTITION BY "MonthlyReportId", "Title"
                ORDER BY "Id"   -- keep the first-inserted (lowest UUID lexicographically)
            ) AS rn
        FROM "MonthlyReportLineItems"
    ) ranked
    WHERE rn > 1
);

-- Step 3: Truncate CompanyProductRevenueMix — will be re-populated by the re-sync job.
TRUNCATE TABLE "CompanyProductRevenueMix";

-- Step 4: Verify no duplicates remain before committing.
-- Expected: 0 rows
SELECT "MonthlyReportId", "Title", COUNT(*) AS cnt
FROM "MonthlyReportLineItems"
GROUP BY "MonthlyReportId", "Title"
HAVING COUNT(*) > 1;

-- If the above returns 0 rows, commit:
COMMIT;
-- Otherwise: ROLLBACK;
