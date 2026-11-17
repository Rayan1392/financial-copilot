# Implementation Plan: Financial Query Credit Pricing

Status: Gate B: Approved by the owner on 2026-11-16

## Scope

Implement the approved behavior in the design proposal and user story. The change is limited to V2 operation selection and operation-based pricing. Do not change V1, billing capabilities, migrations, shared configuration, secrets, release state, or deployment.

## Tasks

- [x] **B01 — Add a deterministic V2 billing operation selector.** Prefer the validated semantic capability code; use existing statement (including quarterly-report table) and sales-trend intent rules only when no frame exists. Route statement table/period-analysis queries to `AiQuery.FinancialStatement`, monthly company/product trends to `AiQuery.MonthlySalesTrend`, and all other requests to the current `AiQuery.Scanner` code.
- [x] **B02 — Set the two approved operation tariffs.** Add both V2 operation codes at two credits in the existing default operation pricing policy. Preserve all current completion-status multipliers and zero-charge statuses.
- [x] **B03 — Cover selector behavior.** Unit-test cash flow, income statement, balance sheet, financial-statement analysis, company sales trend, product sales trend, unrelated metric lookup, and the no-frame phrase fallback.
- [x] **B04 — Verify endpoint billing and replay.** V2 endpoint tests assert operation codes and exact two-credit charges for monthly company/product trends and cash-flow, income-statement, and balance-sheet requests; persisted-message replay leaves one charge. Endpoint and default-policy tests confirm `NoData` stays free.
- [x] **B05 — Update the implementation report.** Record changes, verification, deviations, and unavailable PostgreSQL/live Development checks.

## Verification Record

- Feature 161, selector, and default-pricing unit tests: 100 passed, including two-credit completion and free `NoData` outcomes for both operation codes.
- Focused V2 integration set in Release: 14 passed. It covers typed cash-flow endpoint persistence/replay, NoData, provider name and company isolation; exact two-credit charges for cash-flow tables, quarterly income statements, financial-statement analysis, balance sheets, and monthly company/product trends; and Feature 161 route/V1 baselines. Replay cases retain one exact two-credit ledger entry.
- A broader endpoint filter still has fixture/assertion failures in existing monthly-sales and quarterly-table cases before the new billing assertions. See the Feature 161 implementation report for details.

## Verification

- Run focused unit tests for the selector and pricing behavior.
- Run focused V2 endpoint integration tests for sales-trend, statement, no-data, and replay billing.
- Run solution and frontend checks required by Feature 161 where the local environment allows; compare unrelated failures with `specs/test-baseline.md`.

## Definition of done

- All acceptance criteria in the change user story pass.
- Existing Feature 161 accounting, V2 route, and V1-freeze tests remain green.
- No migration, secret/configuration change, commit, push, merge, or deployment is introduced.
- Tasks and the Feature 161 implementation report distinguish verified work from remaining environment or review limitations.
