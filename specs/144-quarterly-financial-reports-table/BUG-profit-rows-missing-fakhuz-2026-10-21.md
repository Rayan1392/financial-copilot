# Bug: gross and net profit rows missing for Khafoula

**Reported:** 2026-10-21
**Feature:** 144 — Quarterly Financial Reports Table
**Status:** Fix implemented; solution build passed

## Symptom

The quarterly income statement for خفولا, report `553709`, ending 1405/06/31, did not show gross profit and net profit in the Feature 144 table.

## Evidence

- A read-only production inspection found report `553709` for company `13283` (`IncomeStatement`, six months, Gregorian period end 2026-09-22) with supplemental source items `310` (gross profit `234599`) and `313` (net profit `138451`), but no line-item rows for primary source IDs `139` and `143`.
- The attached Noavaran API response for that report contains both primary items: `139` = `234599` and `143` = `138451`. It also contains the supplemental copies `310` and `313` with the same amounts. The prior-period report `548440` likewise contains primary IDs `139` = `94347` and `143` = `64809`.
- The user later confirmed that items 139 and 143 appeared in the database after a delay. This establishes that the observations were eventually persisted; the source dataset was not missing those values.
- The Feature 144 fallback from `310`/`313` remains useful for statements where the provider omits the primary IDs.

## Root cause and fix

The normalizer processed source items one by one and awaited separate database lookups for each item's catalog row, metric mapping, and existing line item. It did not save the line items until the end of the loop. For a statement with many items, these sequential round trips delayed completion, and the statement header could be updated before its line items became visible.

Catalog rows, mappings, and existing line items are now loaded in batches. The normalizer stages all changes and saves the statement and its line items together, so a report is not exposed with a new header and partially refreshed items. Manual backfills also receive a fresh sync-run key so a repeated request re-runs normalization, updating existing values and inserting missing rows. Feature 144 reads primary IDs `139`/`143` first and falls back to supplemental `310`/`313` when a primary row is absent.

## Verification

- Production inspection was read-only; no production rows were modified.
- `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore` passed with 0 warnings and 0 errors.
- No tests were run for this change.
