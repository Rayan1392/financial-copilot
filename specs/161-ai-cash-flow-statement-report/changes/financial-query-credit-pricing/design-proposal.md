# Design Proposal: Financial Query Credit Pricing

Status: Gate A: Approved by the owner on 2026-11-16

## Intent

Price completed monthly sales-trend and financial-statement conversations at two credits per user turn. This keeps the existing cash-flow report price at two credits and applies the same rate to income-statement and balance-sheet reports.

## Scope

- V2 monthly company sales-trend and monthly product-trend capabilities.
- V2 financial-statement table and financial-statement period-analysis capabilities, covering cash flow, income statements, and balance sheets.
- Exactly one two-credit operation charge for each successfully answered user turn, independent of internal tool or provider call count.
- Preserve the existing zero-charge behavior for no-data, clarification, validation failure, provider failure, and cancellation outcomes.
- Keep the existing default charge for all unrelated query capabilities.
- No database migration, plan-capability change, configuration or secret edit, V1 routing or billing change, or deployment.

## Design

The V2 billing reservation step selects a billing operation code from the validated semantic capability frame. For requests without a frame, existing deterministic intent rules provide a conservative phrase-based fallback. Statement capabilities use `AiQuery.FinancialStatement`; sales-trend capabilities use `AiQuery.MonthlySalesTrend`. Both operation codes cost two credits under the existing operation-based pricing policy. All other requests keep `AiQuery.Scanner` and its current tariff.

No-data and other zero-charge outcomes continue through the existing `DefaultPricingPolicy` status policy. The charge remains attached to the one AI query reservation, so multiple database reads, internal tools, or provider calls do not create additional charges.

## Acceptance criteria

- A V2 monthly company sales-trend query is billed exactly two credits when completed.
- A V2 monthly product-trend query is billed exactly two credits when completed.
- V2 financial-statement table and financial-statement period-analysis queries are billed exactly two credits when completed, including cash flow, profit-and-loss, and balance-sheet requests.
- No-data, clarification, validation failure, provider failure, and cancellation outcomes remain free.
- Unrelated V2 query types keep their existing pricing.
- V1 operation selection and routing remain unchanged.
- Replaying persisted messages does not issue another charge.

## Risks

- The semantic frame is the authoritative route signal; phrase fallback must use existing deterministic rules and avoid treating ordinary sales or metric lookups as trend requests.
- Pricing changes can affect user balances, so endpoint tests must assert both the selected operation code and exact charged credits.
