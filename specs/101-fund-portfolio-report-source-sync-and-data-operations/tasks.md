# Tasks — Fund Portfolio Report Source Sync and Data Operations

## 1. Source Abstraction

- [ ] Add `IFundPortfolioReportSource` with bounded discovery and stream-download contracts.
- [ ] Define source descriptors containing provider name, stable source object id, file name, observed period/fund hints, last-modified timestamp, checksum when available, and download token/reference.
- [ ] Implement `ManualUploadFundPortfolioReportSource` and a configured local/object-storage source suitable for development/backfill.
- [ ] Add a no-op/unavailable source implementation when no external source is configured.
- [ ] Explicitly document that no Codal or vendor adapter may be implemented without verified source details and fixtures.

## 2. Import Run Persistence

- [ ] Add `FundPortfolioImportRun`, `FundPortfolioImportItem`, and `FundPortfolioMappingReview` entities/configurations/migration.
- [ ] Add unique source identity and file-hash constraints to prevent duplicate run items.
- [ ] Add indexes for run status/time, provider, report, issue type, mapping status, and retry eligibility.
- [ ] Store bounded error codes/summaries; keep full sensitive exception details in protected logs only.
- [ ] Define retention for raw files, run metadata, failed items, and mapping decisions.

## 3. Queueing and Orchestration

- [ ] Implement `StartFundPortfolioImportRunUseCase`, `ImportFundPortfolioItemUseCase`, and `FinalizeFundPortfolioImportRunUseCase`.
- [ ] Queue each workbook as an isolated item and call Feature 100 ingestion.
- [ ] Enforce bounded concurrency, cancellation, retry/backoff, poison-item state, and maximum attempts.
- [ ] Add a distributed lease for scheduled discovery and item processing.
- [ ] Ensure process restarts can resume pending/retryable items without duplicates.
- [ ] Propagate correlation id from run to report, parser, normalizers, and audit events.

## 4. Manual and Bulk Upload APIs

- [ ] Add DataAdmin single/multi-file upload endpoints with streaming upload and size limits.
- [ ] Return `202 Accepted` with run id and item count.
- [ ] Reject unsupported extension/MIME and over-limit archives before persistence.
- [ ] Add bulk import from an approved configured storage prefix; do not accept arbitrary server paths from the request.
- [ ] Provide deterministic status for imported, duplicate, corrected revision, partial, needs review, failed, and cancelled items.

## 5. Optional Scheduled Discovery

- [ ] Add configuration options for enabled flag, provider, cadence, lookback period, batch size, concurrency, and lease duration.
- [ ] Keep the worker disabled by default.
- [ ] Discover only stable source descriptors newer than the provider watermark and reconcile checksum/source id before downloading.
- [ ] Advance watermark only after all eligible items are durably recorded; failed items remain retryable.
- [ ] Emit a clear operational state when no verified source adapter is available.

## 6. Mapping and Quality Review

- [ ] Create review types for ambiguous fund identity, unresolved security, unknown sheet, header-layout mismatch, invalid date, unit ambiguity, report-period conflict, and reconciliation failure.
- [ ] Generate review items from Feature 100 and downstream normalizers without duplicating their source issue records.
- [ ] Implement list/detail/resolve use cases with optimistic concurrency.
- [ ] Apply approved mappings through governed mapping tables, not source-row mutation.
- [ ] Allow targeted reprocessing of affected reports after mapping or parser-profile updates.
- [ ] Record resolver actor, timestamp, old/new decision, and affected report count.

## 7. Admin Query APIs and Console

- [ ] Add paginated/filterable endpoints for reports, runs, items, issues, and mapping reviews.
- [ ] Add report detail with sheet inventory, parser version, status timeline, normalized-section counts, reconciliation state, and source revision.
- [ ] Extend the Data Management Console with upload, run progress, issue filters, mapping review, reprocess, and source-adapter status.
- [ ] Require explicit confirmation for bulk reprocess or cancellation.
- [ ] Never render unrestricted raw workbook cells in list endpoints.

## 8. Observability and Audit

- [ ] Emit discovery count, download latency, upload size, queue lag, parse duration, duplicate/revision outcome, retry count, review count, and final status.
- [ ] Add dashboards/health indicators consistent with Feature 058 patterns.
- [ ] Audit upload, discovery, mapping decision, reprocess, cancellation, and purge.
- [ ] Trace provider source object through import item, report revision, normalized rows, and signals.

## 9. Tests and Acceptance Scenarios

- [ ] Unit-test discovery pagination, watermark policy, duplicate source identity, retry eligibility, lease behavior, and mapping resolution concurrency.
- [ ] Integration-test single upload, multi-upload, partial run, resume after restart, duplicate file, corrected revision, reprocess, and unauthorized access.
- [ ] Test scheduled worker disabled/no-adapter states and overlapping-run prevention.
- [ ] Test that one malformed workbook does not prevent valid items in the same run from completing.
- [ ] Given an approved mapping change, when reprocess runs twice, then normalized outputs are identical and no duplicate source/report rows are created.

## Completion Gate

- [ ] Keep tasks unchecked until upload/backfill, idempotency, resumability, mapping review, reprocess, authorization, and disabled-by-default scheduled discovery are verified end to end.
