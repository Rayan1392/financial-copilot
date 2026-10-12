# Feature 142 Tasks

- [x] Inspect existing page and monthly backfill integration
- [x] Discover existing financial statement API contracts
- [x] Identify reusable acquisition and persistence services
- [x] Extend administrative backend API
- [x] Implement request validation
- [x] Add financial statement form to `/getCompanyId`
- [x] Integrate frontend with backend
- [x] Reuse persistence and duplicate-handling mechanisms
- [x] Add backend tests
- [x] Frontend tests where applicable (no frontend test script exists; production build passed)
- [x] Run build and regression tests (full suite executed; existing unrelated failures documented)
- [x] Verify acceptance criteria (see implementation report)
- [x] Produce implementation report

## Database Financial Statement Inventory

- [x] Inspect statement, line-item, source-catalog, derived-metric, and company-resolution persistence mappings
- [x] Document report identity, versioning, date, duration, and status mappings and unknown-field behavior
- [x] Add DataAdmin-only read-only inventory API using persisted database data
- [x] Aggregate statement rows and indicator metric rows at report granularity without merging versions
- [x] Add RTL inventory form/table with loading, error, empty, count, and unknown states
- [x] Refresh an already visible same-ticker inventory after successful persistence/idempotent backfill
- [x] Add inventory endpoint and mapping tests; run frontend and backend verification
- [x] Update feature implementation report

## Inventory server-side pagination

- [x] Extend the inventory contract with 1-based page metadata and a fixed 15-row page size
- [x] Add relational PostgreSQL report-set CTE with report-level indicator deduplication, total count, deterministic ordering, and database LIMIT/OFFSET
- [x] Reject invalid pages and return accurate metadata for empty/out-of-range pages
- [x] Add previous/next controls, page labels, total report count, ticker reset, and stale-response protection
- [x] Reset same-ticker inventory to page 1 after successful manual ingestion
- [x] Test page boundaries, no duplication, ordering, invalid/empty pages, and existing indicator/version semantics
- [x] Update pagination acceptance criteria, design, and implementation report
- [x] Run focused backend tests and frontend/backend builds
