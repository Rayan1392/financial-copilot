# User Story — Fund Portfolio Report Source Sync and Data Operations

## Status
`[ ]` Proposed

## Feature
Provide controlled single-file, bulk-backfill, and optional scheduled acquisition of fund portfolio workbooks, with review and reprocessing operations for incomplete or ambiguous reports.

## Story

As a FinancialCopilot data administrator,

I want to import historical and monthly fund portfolio reports, monitor their processing status, and resolve data-quality issues,

so that fund intelligence remains complete, reproducible, and operable without direct database changes.

## Business Context

Feature 100 provides the canonical report and parser foundation. A production fund-intelligence product needs an operational path for:

- importing one supplied workbook;
- backfilling many historical workbooks;
- discovering new files from a configured source when a verified adapter exists;
- reviewing unresolved fund/security mappings and parser issues;
- reprocessing reports after parser-profile or mapping changes;
- preventing duplicate or competing scheduled runs.

The external source of all monthly workbooks is not yet specified. This feature must therefore introduce a provider-neutral acquisition contract. It must not invent a Codal URL, undocumented endpoint, or scraping strategy.

## Dependencies

- Feature `100-fund-portfolio-domain-and-workbook-ingestion-foundation`.
- Feature `012-admin-data-operations`.
- Feature `018-ai-observability-and-telemetry`.
- Feature `055-frontend-data-management-console`.
- Feature `058-live-data-sync-monitor` patterns for operational status.

## In Scope

- Manual single-file upload.
- Bounded multi-file upload/backfill.
- Import from a configured local/object-storage location for development and controlled operations.
- Provider-neutral `IFundPortfolioReportSource` discovery/download contract.
- Optional scheduled discovery worker, disabled by default until a verified source adapter is configured.
- Ingestion run and item-level status.
- Duplicate, corrected revision, unsupported layout, partial parse, and failure outcomes.
- Data-quality review queue for fund identity, security identity, sheet classification, date, and reconciliation issues.
- Reprocessing after parser/mapping changes.
- Safe cancellation, retry, poison-item handling, and distributed lease.
- Admin APIs and data-management console integration.

## Out of Scope

- Implementing an unverified Codal/SEO/scraping adapter.
- Editing raw source workbooks.
- User-facing fund analytics or AI answers.
- Automatically approving ambiguous fund or security mappings through an LLM.

## Acceptance Criteria

1. An authorized administrator can upload one or many `.xlsx` files and receive an asynchronous import-run response.
2. Duplicate files are reported as duplicates, not reinserted.
3. A file with the same fund/period but a different hash follows the source-revision policy from Feature 100.
4. Bulk imports are bounded, resumable, and item-isolated; one bad file does not fail the entire run.
5. The scheduled worker cannot overlap with itself and is disabled when no source adapter is configured.
6. The system never guesses an external download URL.
7. Administrators can filter reports by fund, date, provider, status, parser version, and issue type.
8. Administrators can approve or reject governed mappings and reprocess affected reports.
9. Reprocessing is idempotent and preserves immutable raw/source revisions.
10. DataAdmin authorization, audit, observability, and retention rules are enforced.

## API Proposal

```http
POST /api/v1/admin/fund-portfolio-reports/uploads
POST /api/v1/admin/fund-portfolio-reports/bulk-import
POST /api/v1/admin/fund-portfolio-reports/discover
POST /api/v1/admin/fund-portfolio-reports/{reportId}/reprocess
GET  /api/v1/admin/fund-portfolio-reports
GET  /api/v1/admin/fund-portfolio-reports/{reportId}
GET  /api/v1/admin/fund-portfolio-reports/{reportId}/issues
GET  /api/v1/admin/fund-portfolio-import-runs/{runId}
POST /api/v1/admin/fund-portfolio-mapping-reviews/{reviewId}/resolve
```

## Data Model Proposal

```text
FundPortfolioImportRuns
- Id
- TriggerType
- ProviderName
- RequestedByActorId?
- StartedAtUtc
- CompletedAtUtc?
- Status
- DiscoveredCount
- ImportedCount
- DuplicateCount
- PartialCount
- FailedCount
- CorrelationId

FundPortfolioImportItems
- Id
- ImportRunId
- SourceObjectId?
- OriginalFileName
- FileSha256?
- ReportId?
- Status
- AttemptCount
- LastErrorCode?
- LastErrorSummary?
- StartedAtUtc?
- CompletedAtUtc?

FundPortfolioMappingReviews
- Id
- ReportId
- MappingType
- RawValue
- NormalizedValue
- CandidateJson
- Status
- ResolutionJson?
- ResolvedByActorId?
- ResolvedAtUtc?
```

## Security and Operations Rules

- All endpoints require granular DataAdmin authorization.
- File scanning and workbook limits from Feature 100 apply before queueing.
- Provider credentials and storage secrets remain in secret configuration.
- Audit every approval, rejection, reprocess, cancellation, and source-revision action.
