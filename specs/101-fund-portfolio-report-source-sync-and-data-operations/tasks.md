# Tasks — Fund Portfolio Report Source Sync and Data Operations

## 1. Source Abstraction

- [x] Add `IFundPortfolioReportSource` with bounded discovery and stream-download contracts.
- [x] Define source descriptors containing provider name, stable source object id, file name, observed period/fund hints, last-modified timestamp, checksum when available, and download token/reference.
- [x] Implement `ManualUploadFundPortfolioReportSource` and a configured local/object-storage source suitable for development/backfill.
- [x] Add a no-op/unavailable source implementation when no external source is configured.
- [x] Explicitly document that no Codal or vendor adapter may be implemented without verified source details and fixtures.

## 2. Import Run Persistence

- [x] Add `FundPortfolioImportRun`, `FundPortfolioImportItem`, and `FundPortfolioMappingReview` entities/configurations/migration.
- [x] Add unique source identity and file-hash constraints to prevent duplicate run items.
- [x] Add indexes for run status/time, provider, report, issue type, mapping status, and retry eligibility.
- [x] Store bounded error codes/summaries; keep full sensitive exception details in protected logs only.
- [x] Define retention for raw files, run metadata, failed items, and mapping decisions.

## 3. Queueing and Orchestration

- [x] Implement `StartFundPortfolioImportRunUseCase`, `ImportFundPortfolioItemUseCase`, and `FinalizeFundPortfolioImportRunUseCase`.
- [x] Queue each workbook as an isolated item and call Feature 100 ingestion.
- [x] Enforce bounded concurrency, cancellation, retry/backoff, poison-item state, and maximum attempts.
- [x] Add a distributed lease for scheduled discovery and item processing.
- [x] Ensure process restarts can resume pending/retryable items without duplicates.
- [x] Propagate correlation id from run to report, parser, normalizers, and audit events.

## 4. Manual and Bulk Upload APIs

- [x] Add DataAdmin single/multi-file upload endpoints with streaming upload and size limits.
- [x] Return `202 Accepted` with run id and item count.
- [x] Reject unsupported extension/MIME and over-limit archives before persistence.
- [x] Add bulk import from an approved configured storage prefix; do not accept arbitrary server paths from the request.
- [x] Provide deterministic status for imported, duplicate, corrected revision, partial, needs review, failed, and cancelled items.

## 5. Optional Scheduled Discovery

- [x] Add configuration options for enabled flag, provider, cadence, lookback period, batch size, concurrency, and lease duration.
- [x] Keep the worker disabled by default.
- [x] Discover only stable source descriptors newer than the provider watermark and reconcile checksum/source id before downloading.
- [x] Advance watermark only after all eligible items are durably recorded; failed items remain retryable.
- [x] Emit a clear operational state when no verified source adapter is available.

## 6. Mapping and Quality Review

- [x] Create review types for ambiguous fund identity, unresolved security, unknown sheet, header-layout mismatch, invalid date, unit ambiguity, report-period conflict, and reconciliation failure.
- [x] Generate review items from Feature 100 and downstream normalizers without duplicating their source issue records.
- [x] Implement list/detail/resolve use cases with optimistic concurrency.
- [x] Apply approved mappings through governed mapping tables, not source-row mutation.
- [x] Allow targeted reprocessing of affected reports after mapping or parser-profile updates.
- [x] Record resolver actor, timestamp, old/new decision, and affected report count.

## 7. Admin Query APIs and Console

- [x] Add paginated/filterable endpoints for reports, runs, items, issues, and mapping reviews.
- [x] Add report detail with sheet inventory, parser version, status timeline, normalized-section counts, reconciliation state, and source revision.
- [x] Extend the Data Management Console with upload, run progress, issue filters, mapping review, reprocess, and source-adapter status.
- [x] Require explicit confirmation for bulk reprocess or cancellation.
- [x] Never render unrestricted raw workbook cells in list endpoints.

## 8. Observability and Audit

- [x] Emit discovery count, download latency, upload size, queue lag, parse duration, duplicate/revision outcome, retry count, review count, and final status.
- [x] Add dashboards/health indicators consistent with Feature 058 patterns.
- [x] Audit upload, discovery, mapping decision, reprocess, cancellation, and purge.
- [x] Trace provider source object through import item, report revision, normalized rows, and signals.

## 9. Tests and Acceptance Scenarios

- [x] Unit-test discovery pagination, watermark policy, duplicate source identity, retry eligibility, lease behavior, and mapping resolution concurrency.
- [x] Integration-test single upload, multi-upload, partial run, resume after restart, duplicate file, corrected revision, reprocess, and unauthorized access.
- [x] Test scheduled worker disabled/no-adapter states and overlapping-run prevention.
- [x] Test that one malformed workbook does not prevent valid items in the same run from completing.
- [x] Given an approved mapping change, when reprocess runs twice, then normalized outputs are identical and no duplicate source/report rows are created.

## Completion Gate

- [x] Keep tasks unchecked until upload/backfill, idempotency, resumability, mapping review, reprocess, authorization, and disabled-by-default scheduled discovery are verified end to end.
