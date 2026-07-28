# NADPCO Monthly Report Identity Canonicalization Report

## Summary

The wrong `:category-{id}` suffix was allowed by the bug docs, not by the current normalizer code.
The exact spec package that introduced/permitted the wrong identity contract was:

- `specs/bugs/nadpco-monthly-report-lineitems-extra-products-data-integrity-2026-06-24.md`
- `specs/bugs/nadpco-monthly-report-lineitems-extra-products-data-integrity-2026-06-24-tasks.md`

Those files described `categoryId` as part of the fallback report identity and proposed cleanup
steps around that model. The live code on disk now uses a canonical shared builder in
`NadpcoApiMonthlyActivityNormalizer.BuildExternalReportId` and does not append category metadata.

## Canonical Contract

- Product sales fallback key:
  `ProductSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-{outputType}`
- Service sales fallback key:
  `ServiceSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-none`
- `categoryId` and category title are line-item evidence only.
- The same contract applies to current ingestion, monthly backfill, and fallback identity when
  `activityId` is absent.

## Changed Spec Files

- `specs/042-nadpco-api-monthly-activity-sync/user-story.md`
- `specs/042-nadpco-api-monthly-activity-sync/tasks.md`
- `specs/053-noavaran-current-api-ingestion/user-story.md`
- `specs/057-nadpco-monthly-activity-freshness-and-sales-lookup/user-story.md`
- `specs/059-monthly-activity-output-type-segmentation/user-story.md`
- `specs/059-monthly-activity-output-type-segmentation/tasks.md`
- `specs/bugs/nadpco-monthly-report-lineitems-extra-products-data-integrity-2026-06-24.md`
- `specs/bugs/nadpco-monthly-report-lineitems-extra-products-data-integrity-2026-06-24-tasks.md`
- `specs/bugs/duplicate-monthly-report-line-items-natural-key-collision-2026-06-25.md`
- `specs/implementation-checklist.md`

## Changed Code And Tests

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`
  - already canonical in the live worktree; no further product code change was required here.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
  - already contains the logical-period uniqueness guard in the live worktree.
- `tests/FinancialCopilot.UnitTests/NadpcoApiMonthlyActivityNormalizerTests.cs`
  - added canonical fallback-key regression coverage.
  - added numeric-`CategoryID` natural-key regression coverage.
- `tests/FinancialCopilot.UnitTests/MonthlyActivityBackfillCoordinatorTests.cs`
  - corrected a stale fallback `ExternalReportId` fixture to the canonical `YYYY-MM` form.

## SQL Cleanup Script

- `specs/bugs/repair-category-suffixed-monthly-reports-2026-06-29.sql`

Intent:

1. Detect duplicate report pairs where one row is canonical and the other ends with `:category-{id}`.
2. Keep the canonical row.
3. Delete duplicate line items under the category-suffixed row.
4. Delete the category-suffixed `MonthlyReports` row.
5. Delete affected `CompanyProductRevenueMixes` and `CompanyMonthlyActivityTrendSnapshots` rows
   so they can be recalculated cleanly.

## Reprocessing Guidance

Full backfill reset is not required.

- Affected months can be selectively repaired with the SQL script.
- After cleanup, re-run recalculation for the affected company/month periods.
- Current ingestion and backfill already converge on the same canonical report-key builder in the
  live code path, so selective reprocessing is sufficient unless broader historical corruption is
  discovered outside the `:category-{id}` pattern.
