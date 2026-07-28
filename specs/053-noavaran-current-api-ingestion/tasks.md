# Tasks - Noavaran Current API Ingestion

- Add configuration:
  - enabled flag
  - Shamsi start year / date boundary
  - dataset selection
  - schedule
  - batch size
  - retry policy
- Refactor existing NADPCO API sync specs to current API semantics where applicable.
- Remove assumptions that the archive source is the recurring update source.
- Add scheduled worker only for `NoavaranCurrentApi`.
- Add run history and health endpoints for current API sync.
- Add gap-fill behavior for periods missing in archive.
- Add tests for current API ingestion from 1403 onward.
