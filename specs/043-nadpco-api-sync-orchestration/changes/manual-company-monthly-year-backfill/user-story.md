# User Story: Manual Company Monthly Backfill by Shamsi Year

## Feature Folder

`043-nadpco-api-sync-orchestration/changes/manual-company-monthly-year-backfill`

## Layers / Area

Layers: backend, frontend

Area: Administration, FinancialData

## Priority

P2

## Complexity

S

## Depends On

None. This change reuses the shipped targeted monthly route and Tehran calendar calculation.

## References

- [Approved design](design-proposal.md)
- [Existing targeted recovery endpoint](../../bugs/single-month-backfill-synchronous-rabbit-fanout-2026-08-24/issue.md#targeted-synchronous-recovery-endpoint)
- [Data ingestion guide](../../../../docs/nadpco-data-ingestion.md)
- `NoavaranMonthlyBackfillController`, `ShamsiMonthCalculator`, `GetCompanyIdPage`

## Standards Source Of Truth

Follow `.claude/rules/*` and `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`.

## User Story

As an authorized operator, I want to enter a symbol and Shamsi year and leave the month blank so I
can fetch and store every completed monthly period in that year from the same form.

## Business Context And Scope

The change applies to the monthly tab of the manual company data form. The year and symbol are
required. A specified month uses the existing validation, ingestion route, and result display.
Year-wide ingestion uses the existing Noavaran current API service and normalized persistence.
Scheduled synchronization, financial statements, and the AI query paths are outside the change.
The existing ingestion schema and individual month commit boundaries support the operation.

## API

| Method | Route | Request | Response |
|---|---|---|---|
| GET | `/api/v1/admin/noavaran-current/monthly-backfill/completed-months` | `shamsiYear`, integer from 1404 to 1500 | `{ shamsiYear, months }` with month numbers in ascending order, possibly empty |
| POST | `/api/v1/admin/noavaran-current/monthly-backfill/single-company-month` | Existing company selector, year and month | Existing per-month response and HTTP error contract |

The GET uses the same authorization and actor rate-limit policies as the POST. It validates the
year and computes the period list from the server clock in Tehran. It performs no provider requests.

## UI And Reliability

Show that the month is optional and explain the empty value in Persian. For an empty month, use
the server's list to send one request at a time. Show progress and each attempted month's outcome,
including an unavailable report or a failure. Continue after a month-level failure. Stop on an
operation-wide block such as lost authorization, an unknown symbol, or rate limiting and leave
remaining months visibly unattempted. Keep completed results visible while the form remains mounted.
Leaving the form cancels the browser workflow. Use the existing per-request diagnostics and retry
behavior. The operator can refresh an individual period using the explicit month field.

## Acceptance Criteria

- [ ] During Azar 1405 in Tehran, a blank month for فماک and year 1405 sends 01 through 08 in order.
- [ ] A supported past year sends 01 through 12. A future year and the current year in Farvardin
      return an empty range, show the explanatory message, and send no ingestion requests.
- [ ] The server calculates the range correctly across Tehran's month boundary and Nowruz.
- [ ] An explicit valid month runs once. The existing validation still rejects invalid years or months.
- [ ] The range endpoint applies the existing authorization and rejects unsupported years.
- [ ] A failed or unavailable month remains visible alongside completed periods and later months run.
- [ ] A duplicate submission from the form cannot start a second sequence while the first runs.
- [ ] Leaving the form stops subsequent requests and preserves any months already committed.

## Assumptions

- A completed calendar month can lack a published report. The result comes from the existing monthly
  ingestion operation for that period.
- The browser owns the sequence and retains its results for the lifetime of the form.
