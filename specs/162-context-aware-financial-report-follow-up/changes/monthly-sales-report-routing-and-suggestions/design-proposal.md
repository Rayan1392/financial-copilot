# Monthly sales report routing and product suggestions

Status: Gate A: Approved by the project owner on 2026-11-25

The owner approved the proposed correction and specified «روند فروش محصول گالوانیزه» as the visible suggestion. See [BUG-048](../../../bugs.md).

## Decisions

- In V2, an explicit request recognized by the existing product revenue mix, company monthly sales trend, or product sales-rate trend route takes precedence over Report Discussion. Eligibility uses the existing deterministic intent and slot rules with the normal route's capability, access, clarification, and billing checks.
- Below both company monthly sales and product revenue mix answers, offer up to two rate charts for the highest-revenue eligible items in the respective report period. A second option requires another eligible item. An eligible item has a positive revenue contribution, unambiguous product identity, and data for a usable rate. Use the existing persisted sources and trend calculation rules.
- Keep the product's complete catalog name. Use «روند فروش محصول گالوانیزه» as the visible label for the Fajr example. The action sends the canonical company-scoped sales-rate query so the existing rate chart opens.
- A conversation opens a newly shown or reopened monthly sales report with Simple selected and web search off, including when a viewer has saved a different global preference. The viewer can still choose analysis settings for explicit interpretation of that report.
- Scope includes the active V2 backend and the shared web chat logic. No migration, external feed, public API request shape, or pricing change is required.
