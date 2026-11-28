# Tasks: Manual Company Monthly Backfill by Shamsi Year

Status: Gate B: Approved by the owner on 2026-11-28

## Feature Folder

`043-nadpco-api-sync-orchestration/changes/manual-company-monthly-year-backfill`

## Before Starting

- [x] Record explicit Gate B approval in this file.
- [ ] Read the user story, approved design, referenced code, and the existing test baseline.

## Implementation Tasks

1. [x] Add a focused application-level calculation for the completed months of a selected Shamsi year.
   Reuse `ShamsiMonthCalculator.LatestCompletedCalendarMonth`. Return the ascending months of the
   selected year and cap the list at 12. Take the current UTC instant as an input for deterministic tests.
2. [x] Add `GET completed-months` to `NoavaranMonthlyBackfillController`. Validate the requested
   year against the existing vendor range, read the instant from `TimeProvider`, and return the year and
   month numbers using the existing controller's authorization and actor rate limiting. Preserve the
   request, response, and behavior of `POST single-company-month`.
3. [x] In `GetCompanyIdPage`, make the monthly input optional and add Persian guidance. For a blank
   month, load the server's month list once and submit the existing single-month requests in sequence
   with the submitted symbol and year. Prevent simultaneous submissions from this form. Use the API
   client's cancellation signal support to stop the sequence when the form unmounts.
4. [x] Show progress and scoped results per period. Preserve the existing run identifier, count,
   and error details where available. Detect unavailable data from the existing run outcome and include
   transport and provider failures in the relevant month row. Continue after month-level failures.
   Stop at an operation-wide rejection and show which periods remain unattempted. Show a Persian
   empty-range message and keep the explicit-month result path compatible.

## Tests

- [x] Add focused calendar tests for Azar 1405, a past year, a future year, Farvardin, and Tehran's
      UTC-to-local month boundary.
- [x] Add focused endpoint tests for authorization, validation, and response shape. Re-run existing
      direct monthly endpoint tests and architecture tests. Use existing test doubles for provider calls.
- [ ] Add focused Vitest coverage for one explicit month, an ordered blank-month sequence with a middle
      provider failure, an empty range, duplicate-submit protection, and unmount cancellation.

## Definition Of Done

- [ ] Verify the user story acceptance criteria.
- [ ] Run `dotnet test src/backend/FinancialCopilot.sln --configuration Release` and compare failures
      with `specs/test-baseline.md`.
- [ ] Run `npm run lint`, `npm run test`, `npm run build`, and `npm run typecheck` in `src/frontend`.
      Check touched files and report any pre-existing failures against `specs/test-baseline.md`.
- [x] Add the change to `specs/README.md` and document the operator flow in
      `docs/nadpco-data-ingestion.md`. Link the relevant Admin Data Operations documentation from
      `README.md` if needed.
- [ ] Coordinate the required Persian Unreleased entry with the release manager when the user-visible
      change merges to `develop`, per `AGENTS.md`.
- [ ] Record any new open decisions, defects, deferred work, or verification limitations in the
      relevant project files and implementation report.
