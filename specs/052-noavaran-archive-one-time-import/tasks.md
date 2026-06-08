# Tasks - Noavaran Archive One-Time Import

- Add `ArchiveImportRun` persistence with status, dataset, requestedBy, startedAt, finishedAt, counts, diagnostics, and freeze marker.
- Add DataAdmin commands:
  - dry-run archive import
  - execute archive import
  - validate imported archive coverage
  - freeze archive source
  - request controlled re-import
- Add safeguards:
  - no accidental scheduled execution
  - no destructive cleanup without explicit confirmation
  - idempotency by source keys and period keys
- Add coverage reports:
  - by company
  - by fiscal year
  - by statement type
  - by monthly activity period
  - by ratio/index type
- Add tests for one-time import and re-import behavior.
