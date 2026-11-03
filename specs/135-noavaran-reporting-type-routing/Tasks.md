# Feature 135 — Implementation Tasks

All tasks are planned implementation work. They are intentionally granular, actionable, and
verifiable. Dependencies use task IDs from this file.

## Slice A — Company metadata synchronization

### T01 — Baseline the current contract and schema

Record the current `NadpcoApiCompanyRecord`, `NormalizedCompanyRow`, EF configuration, latest model
snapshot, `Companies` migration history, normalizer assignments, sample fixture, and existing test
coverage. Verify that no production source files outside the implementation change are altered.

Depends on: none. Verifies: AC-08, AC-26.

### T02 — Complete provider DTO coverage for the supplied sample

Add DTO properties for `reportingType`, `marketBoardID`, `marketBoardTitle`, `activityTypeID`, and
`activityTypeTitle`, preserving the existing legacy `marketBoard` property as a compatibility alias.
Add deserialization fixtures for numeric, null, unknown, and malformed reporting values.

Depends on: T01. Verifies: AC-03, AC-09, AC-10, AC-15.

### T03 — Add the persisted ReportingType model property

Add nullable numeric `ReportingType` to `NormalizedCompanyRow` and, if needed for direct/view-based
resolution, to `NoavaranEligibleCompanyRow`. Do not add ActivityType or MarketBoardID persistence in
this slice.

Depends on: T01, T02. Verifies: AC-01, AC-10.

### T04 — Configure and generate the additive EF migration

Generate an EF Core migration that adds nullable PostgreSQL `integer` column `Companies.ReportingType`
with no default and no index unless a measured query requirement is demonstrated. Update the model
snapshot and verify `Up`/`Down` behavior against an isolated database.

Depends on: T03. Verifies: AC-02, AC-07, AC-26.

### T05 — Update company upsert mapping

Assign `ReportingType` on both new and existing company rows. Map `MarketBoardTitle` to the existing
`MarketBoard` string, falling back to the legacy DTO `MarketBoard` value. Preserve all existing
identity, dimension, date, legal, and source-provenance assignments.

Depends on: T02, T03. Verifies: AC-04, AC-05, AC-06, AC-09, AC-22.

### T06 — Define explicit malformed/null catalog semantics

Ensure explicit provider null clears the row after a successful catalog response. Ensure malformed
reporting metadata cannot route a monthly request and is surfaced through the existing provider
error/quarantine behavior without a guessed value. Ensure a failed catalog request does not execute
an upsert that overwrites the last successful row.

Depends on: T02, T05. Verifies: AC-06, AC-07, AC-20, AC-21.

### T07 — Add catalog synchronization tests

Add unit tests for DTO deserialization, new-row persistence, existing-row update, explicit null,
unknown future code, malformed input, market-board alias behavior, existing-field regression, and
ActivityType/ReportingType separation using the supplied Abin sample (`activityTypeID = 1`,
`reportingType = 1000008`), asserting ProductSales is called and ServiceSales is not called.

Depends on: T05, T06. Verifies: AC-03 through AC-10, AC-15, AC-24.

### T08 — Define and execute catalog coverage/backfill verification

Add an operator-verifiable coverage report or query for current-API companies with non-null,
null, unknown, unsupported, and malformed ReportingType states. Document the rollout command/order:
migrate, refresh catalog, verify coverage, then enable type-driven monthly acquisition.

Depends on: T04, T06. Verifies: AC-07, AC-21, AC-25, AC-26.

## Slice B — ReportingType classification

### T09 — Define the typed monthly provider outcomes

Introduce the smallest typed resolver contract/value object compatible with the current architecture,
including `Manufacturing/ProductSales`, `Service/ServiceSales`, `Unsupported`, `Unknown`, `Missing`,
and `Malformed` outcomes. Preserve the original numeric code in the resolution result.

Depends on: T03. Verifies: AC-11, AC-20.

### T10 — Implement the authoritative mapping table

Implement exactly these verified mappings in one location: `1000000 -> ProductSales`,
`1000005 -> ServiceSales`, and `1000008 -> ProductSales`. Represent the remaining listed codes as
unsupported and all other numeric codes as unknown. Do not map from ActivityType or any company
dimension.

Depends on: T09. Verifies: AC-11 through AC-15.

### T11 — Add resolver unit tests

Create table-driven tests for all ten known codes, null, an unknown future code such as `1000010`,
malformed input, and the Abin ActivityType mismatch. Assert `1000008` resolves to ProductSales,
preserves the numeric code, and has no ServiceSales route; remaining unsupported outcomes have no
endpoint.

Depends on: T10. Verifies: AC-11 through AC-15, AC-24.

### T12 — Define stale and provider-failure read policy

Document and implement the read-time policy: valid persisted metadata may be used with a stale
diagnostic; missing/unknown/unsupported metadata produces no route; failed catalog synchronization
does not overwrite last-known-good metadata. Ensure no compatibility fallback is enabled by default.

Depends on: T09, T10. Verifies: AC-20, AC-21, AC-25.

## Slice C — Monthly report routing

### T13 — Carry the routing decision into scheduled acquisition

At the existing scheduled/backfill boundary, resolve ReportingType for the company before invoking
the monthly provider. Ensure queued requests re-read current persisted metadata at worker execution
time rather than relying only on an older enqueue snapshot.

Depends on: T08, T10, T12. Verifies: AC-16 through AC-21.

### T14 — Replace response-driven endpoint probing

Refactor the Noavaran provider boundary so a resolved Manufacturing or Agriculture route calls
ProductSales only and a resolved Service route calls ServiceSales only. Remove the normal
ProductSales-empty-to-ServiceSales classification fallback. Keep HTTP 200 empty as a selected-source
no-data outcome.

Depends on: T13. Verifies: AC-12, AC-13, AC-16 through AC-19.

### T15 — Route direct admin and Telegram ingestion through the same resolver

Update `SingleCompanyMonthlyIngestionService` and its shared direct provider boundary so admin and
Telegram-triggered monthly ingestion cannot implement a separate endpoint decision. Preserve existing
authorization, replay, billing, and delivery behavior.

Depends on: T13, T14. Verifies: AC-16 through AC-21, AC-23.

### T16 — Handle no-route outcomes in the existing run lifecycle

Define how missing, malformed, unknown, and unsupported routing outcomes are represented in sync
runs/backfill progress. Do not mark them as successful monthly ingestion, do not call another
endpoint, and make them visible for catalog remediation.

Depends on: T12, T14. Verifies: AC-19 through AC-21, AC-25.

### T17 — Preserve normalization and persistence compatibility

Verify that the selected provider payload still reaches the existing raw store,
`NadpcoApiMonthlyActivityNormalizer`, `MonthlyReports`, and `MonthlyReportLineItems` without a new
monthly schema. Retain ProductSales output types and existing ServiceSales evidence/identity rules.

Depends on: T14, T16. Verifies: AC-22, AC-23.

### T18 — Add structured routing diagnostics

Add bounded structured logs/telemetry for company identity, symbol, ReportingType, resolver outcome,
provider type, endpoint/client, and result. Add counters or equivalent metrics for missing, unknown,
unsupported, malformed, stale, and selected routes without logging credentials.

Depends on: T14, T16. Verifies: AC-20, AC-21, AC-26.

## Slice D — Regression and operational verification

### T19 — Add positive and negative endpoint-call tests

Add provider/client tests asserting Manufacturing and Agriculture call ProductSales only, and Service
calls ServiceSales only. Assert explicitly that the other endpoint was not called. Include the Abin
fixture, successful empty responses, and provider failures.

Depends on: T14. Verifies: AC-16 through AC-20, AC-24.

### T20 — Add unsupported and metadata-state routing tests

Cover null, malformed, unknown future, remaining unsupported codes, stale valid metadata, failed
catalog refresh, repeated synchronization, and concurrent/replayed request behavior. Separately cover
the supported agriculture route (`1000008 -> ProductSales`) and assert no ServiceSales call. Assert no
unrelated provider endpoint is called for unsafe metadata.

Depends on: T12, T16. Verifies: AC-14, AC-15, AC-19 through AC-21, AC-24.

### T21 — Add scheduled/direct-path integration coverage

Exercise `NadpcoApiScheduledSyncService`, monthly backfill/direct ingestion, and the shared Telegram
handler boundary with persisted manufacturing, agriculture, and service metadata. Verify all paths use
the same resolver and do not duplicate routing logic.

Depends on: T15, T19, T20. Verifies: AC-16 through AC-21, AC-23.

### T22 — Add monthly persistence and consumer regressions

Run focused regressions for monthly normalization, ProductSales output types, ServiceSales persistence,
derived metric recalculation, ProductSales-over-ServiceSales precedence, late ProductSales publication,
trend snapshots, monthly-sales query/tool output, and chart-ready response generation.

Depends on: T17. Verifies: AC-22, AC-23, AC-26.

### T23 — Verify migration and deployment ordering

Apply the migration to an isolated PostgreSQL database, inspect null existing rows, run a catalog
refresh/backfill, verify coverage, and confirm monthly requests with unresolved metadata do not report
successful ingestion. Record rollback and provider-sync failure behavior.

Depends on: T04, T08, T16. Verifies: AC-02, AC-07, AC-21, AC-25, AC-26.

### T24 — Run the relevant regression suite and close the feature gate

Run focused unit/integration suites for specs `039`, `042`, `053`, `057`, `059`, `076`–`078`, and
`134`, plus the full relevant backend solution tests. Record any environment/provider blockers and
confirm no production source file was modified during the specification task.

Depends on: T07, T11, T19, T20, T21, T22, T23. Verifies: AC-23, AC-26.

## AC-to-task traceability

| Acceptance criteria | Implementing / verifying tasks |
|---|---|
| AC-01 | T03, T24 |
| AC-02 | T04, T23 |
| AC-03 | T02, T07 |
| AC-04 | T05, T07 |
| AC-05 | T05, T07 |
| AC-06 | T05, T06, T07 |
| AC-07 | T04, T06, T08, T23 |
| AC-08 | T01, T07 |
| AC-09 | T02, T05, T07 |
| AC-10 | T02, T03, T07 |
| AC-11 | T09, T10, T11 |
| AC-12 | T10, T14, T19 |
| AC-13 | T10, T14, T19 |
| AC-14 | T10, T11, T20 |
| AC-14A | T10, T11, T19, T20, T21 |
| AC-15 | T02, T10, T11, T20 |
| AC-16 | T13, T14, T15, T19, T21 |
| AC-17 | T13, T14, T15, T19, T21 |
| AC-18 | T14, T19 |
| AC-19 | T14, T16, T19, T20 |
| AC-20 | T06, T09, T12, T16, T18, T20 |
| AC-21 | T06, T08, T12, T16, T18, T20, T23 |
| AC-22 | T05, T17, T22 |
| AC-23 | T15, T17, T21, T22, T24 |
| AC-24 | T07, T11, T19, T20 |
| AC-25 | T08, T12, T16, T23 |
| AC-26 | T01, T04, T08, T18, T22, T23, T24 |

## Operational verification checklist

- [ ] Migration applies cleanly and adds a nullable integer with no default.
- [ ] Existing current-API companies remain present and initially have null or last-known-good
      metadata; no guessed classification is written.
- [ ] Company catalog refresh populates and updates ReportingType.
- [ ] Coverage report identifies null, unknown, unsupported, malformed, and supported rows.
- [ ] Manufacturing representative calls ProductSales only.
- [ ] Service representative calls ServiceSales only.
- [ ] Agriculture sample `1000008` calls ProductSales only; ServiceSales is not called.
- [ ] Selected-endpoint empty response does not probe the other endpoint.
- [ ] Existing normalized monthly-sales and chart/query regressions pass.
- [ ] No production source files were modified while creating this specification.
