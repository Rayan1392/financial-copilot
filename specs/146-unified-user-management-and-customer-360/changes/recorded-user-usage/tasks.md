# Recorded user usage implementation

Status: Gate B: Approved by the feature owner on 2026-11-21

The owner authorized implementation of the reviewed minimal plan in the same message that approved Gate A, with a 30-day default, 7-day and 90-day presets, custom Jalali dates, and a period-specific zero label.

- [x] Extend the directory contract and permission checks.
- [x] Implement PostgreSQL aggregation, plan filtering, and cursor sorting in the existing directory query. Batch gross Billing charges for authorized readers.
- [x] Reuse the Users panel, Jalali date filter, and existing pagination controls.
- [x] Verify boundary conditions, combined filters, pagination, authorization, legacy responses, and representative query performance.

## Verification

The focused checks passed with 74 .NET unit tests, 12 integration tests against isolated PostgreSQL 17 and the API test host, and 15 frontend tests. The broader admin run also exposed failures in the existing plan catalog, auth migration backfill, and subscription-member count tests. Repository-wide frontend type checking reported errors outside the changed files.

## Query measurements

Local PostgreSQL 17 synthetic data used 20,000 users, 40,000 conversations, and 750,000 messages, including 500,000 user messages. The usage aggregate and top-11 sort took 0.87 seconds for the 30-day case and 1.81 seconds for the 90-day case with a Pro filter. Both plans used the existing partial user-message time index and a top-N sort. These are SQL execution times for the aggregate and page selection with a synthetic directory join. End-to-end and production timings remain unmeasured.
