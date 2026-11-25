# Monthly sales report routing and product suggestions

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/monthly-sales-report-routing-and-suggestions`

## Layers / Area

backend, frontend · AI, Conversations, FinancialData

## Priority

P2

## Complexity

M

## Depends On

075 (product revenue mix), 077 (monthly activity trend), 136 (monthly product trend), 137 (product follow-ups), and 162 (Report Discussion and analysis options).

## References

[Approved design](design-proposal.md), [BUG-048](../../../bugs.md), [V1 freeze](../../../POLICY-V1-FREEZE.md), and [analysis options](../sol-deep-analysis-web-search/design-proposal.md).

## Story

As a user reviewing a company's monthly sales, I can open another available sales report from the same conversation and explore the sales-rate history of its main revenue contributors.

## Acceptance criteria

- AC1. After «روند فروش ماهانه فجر», typing «ترکیب فروش محصولات فجر» shows the product mix from the existing route. This also holds when Deep and web search were manually selected. The request makes zero analysis, routing-model, or web-search calls and uses the report's existing billing.
- AC2. An explicit company sales trend or product sales-rate request in the same conversation reaches its deterministic route. Explicit report interpretation still uses Report Discussion with the selected settings. Authorization, clarification, and no-data handling follow the existing routes.
- AC3. The monthly trend and product mix each offer up to two eligible rate charts, ranked by positive contribution to revenue in the report's period, with a stable tie-breaker. Each candidate resolves to one product, and rates are eligible under the existing trend data rules. A report with no eligible items has zero rate suggestions.
- AC4. Given the reported Fajr mix, with usable rate data for the leading product, the first rate suggestion is exactly «روند فروش محصول گالوانیزه». Clicking it returns the sales-rate chart for «محصول گالوانیزه» for Fajr.
- AC5. Desktop and mobile use Simple with web search off on first display, navigation into, or reload of a monthly sales report, including when the stored global choice is Deep with web search. Explicit overrides work in the open report. Other report families and analysis offers continue to use their existing defaults.

## Assumptions

- The screenshot is the fixture for relative revenue contribution. Rate availability for Fajr will be verified against authorized data if available, and against a fixture with a usable rate in regression tests.
