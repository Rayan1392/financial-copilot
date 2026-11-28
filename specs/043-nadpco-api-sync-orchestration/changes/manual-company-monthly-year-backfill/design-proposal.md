# Manual Company Monthly Backfill by Shamsi Year

Status: Gate A: Approved by the owner on 2026-11-28

## Scope

Make the Shamsi month optional in the manual company monthly data form. An explicit month still fetches
that single period. With the month blank, fetch every completed month in the selected Shamsi year in
ascending order. Determine completion using the Tehran calendar. For example, in Azar, selecting 1405
for فماک fetches 1405/01 through 1405/08. A past supported year includes all 12 months. For a future year
or the current year during Farvardin, show that the selected year has no completed months.

## Design

The API provides the month list using the existing Tehran calendar calculation. The form submits each
month sequentially to the existing authorized direct monthly ingestion path. Each outcome appears on
its own row. A period with unavailable data or a failed request does not prevent the next period from
running. Existing provider routing and normalizers handle fetching and persistence for each month.

The browser owns the sequence. Leaving the form stops further submissions and preserves periods already
committed. An in-flight request can have an uncertain outcome after an interruption. Each HTTP request
covers one period, keeping request duration independent of the year range. The design requires no
schema migration or new background orchestration.

## Verification

Cover a partial current year, a complete past year, Nowruz and the first month boundary in Tehran,
an empty year, explicit-month compatibility, and month-level outcomes after a failure or interruption.
