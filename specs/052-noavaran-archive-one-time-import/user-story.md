# Noavaran Archive One-Time Import

## User Story

As a data administrator, I want the historical Noavaran/NADPCO archive datasource to be imported once into normalized storage so historical financial data is available while avoiding unnecessary recurring synchronization against a frozen source.

## Acceptance Criteria

1. Archive import can be triggered manually from DataAdmin only.
2. Archive import supports dry-run, import, validate, and lock/freeze states.
3. After successful import, the source is marked as frozen/imported.
4. Scheduled workers do not automatically import or refresh the archive.
5. Re-import requires explicit admin operation and records the reason.
6. Archive import persists run history, imported row counts, skipped rows, conflicts, and failures.
7. Archive import validates company/security mapping before financial data import.
8. Archive import can be limited by dataset: companies, statements, monthly activity, ratios, derived metrics.
9. Archive import exposes coverage summary by fiscal year, company, and dataset.
10. Scanner results include archive provenance when answering from historical rows.

## Out of Scope

- Normal recurring updates from the archive source.
- Direct TSETMC trading statistics ingestion.
