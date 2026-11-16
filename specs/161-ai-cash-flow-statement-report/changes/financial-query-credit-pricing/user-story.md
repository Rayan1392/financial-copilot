# User Story: Financial Query Credit Pricing

Status: Gate A: Approved by the owner on 2026-11-16

Layers: Application orchestration, Infrastructure billing, tests

## Story

As a Sapio user, I want each answered monthly sales-trend, cash-flow, profit-and-loss, or balance-sheet query to deduct two credits, so these financial report conversations follow one predictable price.

## Acceptance criteria

- [x] Completed monthly company sales-trend and monthly product-trend turns cost exactly two credits.
- [x] Completed cash-flow, income-statement, and balance-sheet table or period-analysis turns cost exactly two credits.
- [x] Each user turn is charged once regardless of internal calls; persisted-message replay creates no second charge.
- [x] No-data and other configured zero-charge outcomes remain free.
- [x] Other capabilities and V1 billing keep their current behavior.
- [x] No migration or runtime configuration change is required.
