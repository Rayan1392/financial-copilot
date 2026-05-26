# User Story — Scanner Execution Engine

## Story

As an investor,  
I want screening questions submitted in my Conversation to return matching symbols,
so that I can quickly find fundamental and valuation candidates without selecting backend services.

## Acceptance Criteria

- Scanner Use Case executes a validated scanner plan after Tool Routing selects it.
- Support AND conditions for Phase 1.
- Support operators: <, <=, >, >=, =, between.
- Support sorting and limit.
- When an AI response returns a list of stocks, return that list through a structured result table contract.
- Unless the user explicitly changes the table columns, the default displayed columns are symbol, latest price, price change percentage, market capitalization, and the metrics relevant to the user's question.
- Relevant question metrics are included as columns, for example P/E, profitability growth, or sales growth when those concepts were used in the filter or requested result.
- The table contains at most 10 displayed data columns. Explicit user overrides are validated against this limit.
- Latest price and price change use available live/low-latency market data first; when unavailable, use the latest statistics from the previous completed trading day and identify that fallback in row/source metadata.
- Return symbol, company, industry, price value and source time, daily change percentage, market capitalization, selected metric values, score, matched conditions, and source metadata as needed by the table contract.
- Handle missing data with warnings.
- Never execute raw SQL produced by AI.
- Query performance is acceptable for normalized datasets.
- Scanner results are returned inside the Explainable Answer produced by `POST /api/ai/v1/query`.
- Scanner execution is not exposed as a frontend-facing public endpoint.
- Scanner execution supplies billable execution facts and cache eligibility/outcome metadata to Billing; it does not compute charges or update balances.

## Technical Notes

- Start with EF Core expression building or predefined query handlers.
- Prefer deterministic query model over arbitrary dynamic SQL.
- `IScannerExecutionService` and `IScannerResultRanker` belong in the Application layer behind `IAiQueryOrchestrator`.
- Price, daily change, valuation, capitalization, and growth displayed in result tables must come from normalized or freshness-controlled market data, not from generated text.
- Implement column selection through an `IScannerResultColumnPolicy` and price-source selection through an `IMarketQuoteResolver` or equivalent Application interface; do not let the LLM assemble arbitrary table projections.
- Fetch price/source data in batches for the result set and enforce row/column limits before calling external live-data providers to control response latency and provider usage.
- Keep usage pricing and reservations in `FinancialCopilot.Billing`; Scanner reports deterministic execution facts only.
